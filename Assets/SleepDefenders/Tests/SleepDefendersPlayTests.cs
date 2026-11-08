using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SleepDefenders;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

/// <summary>
/// Testes de gameplay (PlayMode). Rode em Window > General > Test Runner > PlayMode > Run All.
/// Precisa da cena gerada pelo menu "Sleep Defenders > Construir Jogo".
/// Os testes guardam e restauram o progresso das noites (PlayerPrefs) de quem estiver jogando.
/// </summary>
public class SleepDefendersPlayTests
{
    const int LastNight = 4; // Noite 5: todos os brinquedos e monstros liberados

    GridManager grid;
    EnemySpawner spawner;
    ToyPlacementController placement;
    int savedUnlocked;

    [UnitySetUp]
    public IEnumerator Setup()
    {
        savedUnlocked = LevelProgress.UnlockedCount;
        yield return LoadGame(LastNight, startPlaying: true);
        // A Noite 5 é quase toda escura; os testes que não são de luz usam o tapete todo claro.
        LightField.Instance.Configure(grid.Columns);
    }

    [TearDown]
    public void TearDown()
    {
        Time.timeScale = 1f;
        LevelProgress.SetUnlockedCount(savedUnlocked);
        LevelProgress.CurrentNight = -1;
        LevelProgress.AutoStart = false;
    }

    /// <summary>Carrega a cena na noite indicada. Com startPlaying, começa sem ondas automáticas.</summary>
    IEnumerator LoadGame(int night, bool startPlaying)
    {
        Time.timeScale = 1f;
        LevelProgress.CurrentNight = night;
        LevelProgress.AutoStart = false;
#if UNITY_EDITOR
        UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode("Assets/SleepDefenders/Scenes/Game.unity", new LoadSceneParameters(LoadSceneMode.Single));
#else
        SceneManager.LoadScene("Game");
#endif
        yield return null;
        yield return null;
        grid = GridManager.Instance;
        spawner = Object.FindAnyObjectByType<EnemySpawner>();
        placement = Object.FindAnyObjectByType<ToyPlacementController>();
        Object.FindAnyObjectByType<MoonlightSpawner>().enabled = false;
        if (startPlaying) GameManager.Instance.StartGame(false);
        yield return null;
    }

    EnemyBase EnemyPrefab<T>() where T : EnemyBase =>
        GameManager.Instance.Campaign.nights.SelectMany(n => n.waves).SelectMany(w => w.enemies).Select(e => e.prefab).First(p => p is T);

    ToyBase ToyPrefab<T>() where T : ToyBase =>
        placement.Cards.Select(c => c.Definition.prefab).First(p => p is T);

    static void RemoveLaneGuards()
    {
        foreach (LaneGuard g in Object.FindObjectsByType<LaneGuard>(FindObjectsSortMode.None)) Object.Destroy(g.gameObject);
    }

    static IEnumerator WaitUntil(Func<bool> cond, float timeout)
    {
        float t = 0f;
        while (!cond() && t < timeout) { t += Time.deltaTime; yield return null; }
    }

    static IEnumerator WaitGame(float seconds)
    {
        float t = 0f;
        while (t < seconds) { t += Time.deltaTime; yield return null; }
    }

    static bool Dead(EnemyBase e) => e == null || !e.IsAlive;

    // ====================================================================== Grade, Luz e brinquedos

    [UnityTest]
    public IEnumerator Raycast_FromScreenPoint_FindsCell()
    {
        Camera cam = Camera.main;
        foreach (var (r, c) in new[] { (0, 0), (2, 4), (4, 8), (1, 7) })
        {
            Vector3 sp = cam.WorldToScreenPoint(grid.GetCellCenter(r, c));
            Assert.IsTrue(grid.TryGetCellAtScreenPoint(sp, out GridCell cell, false), $"cell {r},{c} not hit");
            Assert.AreEqual(r, cell.Row);
            Assert.AreEqual(c, cell.Column);
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator Placement_SpendsLight_RespectsOccupancyRechargeAndCost()
    {
        CurrencyManager currency = CurrencyManager.Instance;
        currency.ResetLight(150);
        int start = currency.CurrentLight;

        ToyCardUI lamp = placement.Cards[0], robot = placement.Cards[1], bear = placement.Cards[2];
        placement.ToggleCard(lamp);
        Assert.AreEqual(lamp, placement.SelectedCard);
        Assert.IsTrue(placement.TryPlaceSelected(grid.GetCell(2, 0)));
        Assert.AreEqual(start - 50, currency.CurrentLight);
        Assert.IsTrue(grid.IsOccupied(2, 0));
        Assert.IsFalse(lamp.IsReady, "lamp card should be recharging");

        placement.ToggleCard(lamp);
        Assert.IsNull(placement.SelectedCard, "recharging card can't be selected");

        placement.ToggleCard(robot);
        Assert.IsFalse(placement.TryPlaceSelected(grid.GetCell(2, 0)), "occupied cell");
        Assert.AreEqual(start - 50, currency.CurrentLight);
        Assert.IsTrue(placement.TryPlaceSelected(grid.GetCell(2, 1)));
        Assert.AreEqual(start - 150, currency.CurrentLight);

        placement.ToggleCard(bear);
        Assert.IsNull(placement.SelectedCard, "not enough light");
        yield return null;
    }

    [UnityTest]
    public IEnumerator Robot_KillsCreeper_OnlyInItsLane()
    {
        Time.timeScale = 5f;
        Assert.IsTrue(grid.TryPlaceToy(ToyPrefab<LaserRobot>(), 2, 1, out _));
        EnemyBase enemy = spawner.SpawnEnemy(EnemyPrefab<ShadowCreeper>(), 2);
        EnemyBase other = spawner.SpawnEnemy(EnemyPrefab<ShadowCreeper>(), 3);
        yield return WaitUntil(() => Dead(enemy), 30f);
        Assert.IsTrue(Dead(enemy), "creeper in robot lane should die");
        Assert.IsTrue(other != null && other.IsAlive && Mathf.Approximately(other.CurrentHP, other.MaxHP), "other lane untouched");
    }

    [UnityTest]
    public IEnumerator Bear_BlocksClosetMonster_AndTakesDamage()
    {
        Time.timeScale = 5f;
        Assert.IsTrue(grid.TryPlaceToy(ToyPrefab<TeddyBear>(), 1, 6, out ToyBase bear));
        EnemyBase monster = spawner.SpawnEnemy(EnemyPrefab<ClosetMonster>(), 1);
        yield return WaitUntil(() => monster.IsAttacking, 40f);
        Assert.IsTrue(monster.IsAttacking, "monster should engage bear");
        Assert.AreEqual(bear, monster.CurrentTarget);
        float x = monster.transform.position.x;
        float hp = bear.CurrentHP;
        yield return WaitGame(3f);
        Assert.AreEqual(x, monster.transform.position.x, 0.02f, "monster stopped");
        Assert.Less(bear.CurrentHP, hp, "bear took damage");
        Assert.Greater(monster.transform.position.x, bear.transform.position.x, "monster in front of bear");
    }

    [UnityTest]
    public IEnumerator Flyer_SkipsFirstToy_StopsAtSecond()
    {
        Time.timeScale = 4f;
        Assert.IsTrue(grid.TryPlaceToy(ToyPrefab<TeddyBear>(), 3, 6, out ToyBase first));
        Assert.IsTrue(grid.TryPlaceToy(ToyPrefab<TeddyBear>(), 3, 2, out ToyBase second));
        var flyer = (WingedNightmare)spawner.SpawnEnemy(EnemyPrefab<WingedNightmare>(), 3);
        yield return WaitUntil(() => flyer.IsAttacking, 40f);
        Assert.AreEqual(second, flyer.CurrentTarget, "should attack the second toy");
        Assert.IsFalse(flyer.IsAirborne, "landed after skipping");
        Assert.AreEqual(first.MaxHP, first.CurrentHP, "first toy untouched");
    }

    [UnityTest]
    public IEnumerator RCCar_RunsOverWholeLane_AndFreesCell()
    {
        Time.timeScale = 4f;
        Assert.IsTrue(grid.TryPlaceToy(ToyPrefab<RCCar>(), 0, 4, out _));
        EnemyBase a = spawner.SpawnEnemy(EnemyPrefab<ShadowCreeper>(), 0);
        yield return WaitGame(1f);
        EnemyBase b = spawner.SpawnEnemy(EnemyPrefab<ClosetMonster>(), 0);
        EnemyBase c = spawner.SpawnEnemy(EnemyPrefab<ShadowCreeper>(), 1);
        yield return WaitUntil(() => Dead(a), 40f);
        Assert.IsTrue(Dead(a), "first creeper run over");
        Assert.IsFalse(grid.IsOccupied(0, 4), "cell freed when car launches");
        yield return WaitUntil(() => Dead(b), 5f);
        Assert.IsTrue(Dead(b), "closet monster also run over");
        Assert.IsTrue(c != null && c.IsAlive, "other lane untouched");
    }

    [UnityTest]
    public IEnumerator Lamp_ProducesOrb_CollectAddsLight()
    {
        Time.timeScale = 5f;
        Assert.IsTrue(grid.TryPlaceToy(ToyPrefab<ToyLamp>(), 0, 0, out _));
        int before = CurrencyManager.Instance.CurrentLight;
        LightOrb orb = null;
        yield return WaitUntil(() => (orb = Object.FindAnyObjectByType<LightOrb>()) != null, 15f);
        Assert.IsNotNull(orb, "lamp should produce an orb");
        yield return WaitGame(1f);
        orb.Collect();
        yield return WaitGame(1.5f);
        Assert.AreEqual(before + 25, CurrencyManager.Instance.CurrentLight);
    }

    // ====================================================================== Vitória, derrota e última defesa

    [UnityTest]
    public IEnumerator EnemyReachingBed_LosesGame()
    {
        RemoveLaneGuards();
        Time.timeScale = 8f;
        spawner.SpawnEnemy(EnemyPrefab<WingedNightmare>(), 4);
        yield return WaitUntil(() => GameManager.Instance.State == GameState.Lost, 40f);
        Assert.AreEqual(GameState.Lost, GameManager.Instance.State);
    }

    [UnityTest]
    public IEnumerator LaneGuard_SavesLaneOnce_ThenBedIsReachable()
    {
        yield return LoadGame(0, startPlaying: true); // Noites 4 e 5 não têm Ratinhos de Corda
        Time.timeScale = 8f;
        LaneGuard guard = Object.FindObjectsByType<LaneGuard>(FindObjectsSortMode.None).Single(g => g.Lane == 0);
        int guards = Object.FindObjectsByType<LaneGuard>(FindObjectsSortMode.None).Length;
        Assert.AreEqual(grid.Rows, guards, "one guard per lane");

        // Os monstros têm Rigidbody cinemático: em vez de mover o transform, eles nascem em momentos diferentes.
        EnemyBase first = spawner.SpawnEnemy(EnemyPrefab<ShadowCreeper>(), 0);
        yield return WaitGame(4f);
        EnemyBase sameLaneLater = spawner.SpawnEnemy(EnemyPrefab<ShadowCreeper>(), 0); // ~1,3 célula atrás
        yield return WaitGame(8f);
        EnemyBase otherLane = spawner.SpawnEnemy(EnemyPrefab<ShadowCreeper>(), 1);     // longe da cama na hora do resgate

        yield return WaitUntil(() => Dead(first), 60f);
        Assert.IsTrue(Dead(first), "guard should stop the creeper");
        Assert.AreEqual(GameState.Playing, GameManager.Instance.State, "still playing after the save");
        Assert.IsTrue(guard == null || guard.IsUsed, "guard was used");
        yield return WaitUntil(() => Dead(sameLaneLater), 5f);
        Assert.IsTrue(Dead(sameLaneLater), "guard sweeps the whole lane");
        Assert.IsTrue(otherLane != null && otherLane.IsAlive, "other lane untouched");

        yield return WaitGame(2f);
        Assert.IsTrue(guard == null, "guard leaves after the run");
        spawner.SpawnEnemy(EnemyPrefab<ShadowCreeper>(), 0);
        yield return WaitUntil(() => GameManager.Instance.State == GameState.Lost, 60f);
        Assert.AreEqual(GameState.Lost, GameManager.Instance.State, "no second save in the same lane");
    }

    [UnityTest]
    public IEnumerator SmallNight_WithDefenses_IsWon()
    {
        Time.timeScale = 8f;
        for (int r = 0; r < 5; r++)
        {
            grid.TryPlaceToy(ToyPrefab<LaserRobot>(), r, 1, out _);
            grid.TryPlaceToy(ToyPrefab<LaserRobot>(), r, 2, out _);
            grid.TryPlaceToy(ToyPrefab<TeddyBear>(), r, 5, out _);
        }
        var wave = new Wave { spawnInterval = 1.5f, delayBeforeWave = 0f };
        wave.enemies.Add(new EnemySpawnEntry { prefab = EnemyPrefab<ShadowCreeper>(), count = 4 });
        wave.enemies.Add(new EnemySpawnEntry { prefab = EnemyPrefab<WingedNightmare>(), count = 2 });
        wave.enemies.Add(new EnemySpawnEntry { prefab = EnemyPrefab<ClosetMonster>(), count = 1 });
        spawner.SetWaves(new List<Wave> { wave });
        spawner.Begin();
        yield return WaitUntil(() => GameManager.Instance.State != GameState.Playing, 300f);
        Assert.AreEqual(GameState.Won, GameManager.Instance.State);
    }

    // ====================================================================== Campanha de noites

    [UnityTest]
    public IEnumerator Campaign_HasFiveNights_EachLongerThanThePrevious()
    {
        Campaign campaign = GameManager.Instance.Campaign;
        Assert.IsNotNull(campaign, "GameManager needs the campaign");
        Assert.AreEqual(5, campaign.Count);
        for (int i = 0; i < campaign.Count; i++)
        {
            NightDefinition n = campaign.nights[i];
            Assert.IsNotEmpty(n.waves, $"{n.title} has waves");
            Assert.IsTrue(n.waves.All(w => w.TotalCount > 0), $"{n.title} waves have monsters");
            if (i > 0)
                Assert.Greater(n.EstimatedDuration(), campaign.nights[i - 1].EstimatedDuration(), $"{n.title} longer than previous");
        }
        // Noite 1 é de aprendizado: só Sombras, 2 brinquedos.
        Assert.IsTrue(campaign.nights[0].EnemyTypes().All(e => e is ShadowCreeper));
        Assert.AreEqual(2, campaign.nights[0].toys.Count);
        // Cada noite seguinte traz alguma novidade até a 3ª.
        Assert.IsNotEmpty(campaign.Novelties(1));
        Assert.IsNotEmpty(campaign.Novelties(2));
        yield return null;
    }

    [UnityTest]
    public IEnumerator Night1_ShowsTwoCards_AndUsesOnlyMiddleLanes()
    {
        yield return LoadGame(0, startPlaying: false);
        Assert.AreEqual(GameState.Menu, GameManager.Instance.State, "opens on the menu");
        Assert.AreEqual(0, GameManager.Instance.NightIndex);
        Assert.AreEqual(2, placement.ActiveCards.Count, "lamp and robot only");
        Assert.IsFalse(placement.Cards[2].gameObject.activeSelf, "bear card hidden");
        Assert.AreEqual(150, CurrencyManager.Instance.CurrentLight);

        Wave firstWave = spawner.Waves[0];
        CollectionAssert.AreEqual(new[] { 2 }, firstWave.lanes, "first wave only in the middle lane");
        var lanes = new HashSet<int>();
        for (int i = 0; i < 200; i++) lanes.Add(spawner.PickLane(spawner.Waves[1]));
        CollectionAssert.AreEquivalent(new[] { 1, 2, 3 }, lanes, "later waves use the 3 middle lanes");
    }

    [UnityTest]
    public IEnumerator WinningNight_UnlocksNext_AndNextNightStartsDirectly()
    {
        LevelProgress.SetUnlockedCount(1);
        yield return LoadGame(0, startPlaying: true);
        Assert.IsFalse(LevelProgress.IsUnlocked(1));

        GameManager.Instance.Win();
        Assert.AreEqual(GameState.Won, GameManager.Instance.State);
        Assert.IsTrue(LevelProgress.IsUnlocked(1), "night 2 unlocked");
        Assert.IsTrue(LevelProgress.IsCompleted(0));

        GameManager.Instance.NextNight();
        yield return null;
        yield return null;
        yield return null;
        Assert.AreEqual(1, GameManager.Instance.NightIndex);
        Assert.AreEqual(GameState.Playing, GameManager.Instance.State, "next night skips the menu");
        Assert.AreEqual(3, Object.FindAnyObjectByType<ToyPlacementController>().ActiveCards.Count, "bear unlocked");
    }

    [UnityTest]
    public IEnumerator Menu_LockedNightsAreDisabled()
    {
        LevelProgress.SetUnlockedCount(2);
        yield return LoadGame(0, startPlaying: false);
        var buttons = Object.FindObjectsByType<NightButtonUI>(FindObjectsSortMode.None)
            .OrderBy(b => b.transform.GetSiblingIndex()).ToArray();
        Assert.AreEqual(5, buttons.Length);
        Assert.IsTrue(buttons[0].Button.interactable);
        Assert.IsTrue(buttons[1].Button.interactable);
        Assert.IsFalse(buttons[2].Button.interactable, "night 3 locked");
        Assert.IsFalse(buttons[4].Button.interactable, "night 5 locked");
    }

    // ====================================================================== Estratégia: preço, rajadas, luz e Devorador

    [UnityTest]
    public IEnumerator Lamp_PriceRises_WithEachLampOnTheRug()
    {
        CurrencyManager currency = CurrencyManager.Instance;
        currency.ResetLight(1000);
        ToyCardUI lamp = placement.Cards[0], robot = placement.Cards[1];
        Assert.AreEqual(50, lamp.CurrentCost);

        Assert.IsTrue(grid.TryPlaceToy(ToyPrefab<ToyLamp>(), 0, 0, out ToyBase first));
        Assert.AreEqual(75, lamp.CurrentCost, "second lamp costs more");
        placement.ToggleCard(lamp);
        Assert.IsTrue(placement.TryPlaceSelected(grid.GetCell(1, 0)));
        Assert.AreEqual(1000 - 75, currency.CurrentLight, "charged the raised price");
        Assert.AreEqual(100, lamp.CurrentCost);

        first.Remove();
        yield return null;
        Assert.AreEqual(75, lamp.CurrentCost, "price drops when a lamp leaves the rug");
        Assert.AreEqual(100, robot.CurrentCost, "robot price is fixed");
        Assert.AreEqual(12f, robot.Definition.rechargeTime, 0.01f, "robot recharges slower now");
    }

    [UnityTest]
    public IEnumerator Waves_ArriveInPacks_OnDifferentLanes()
    {
        var wave = new Wave { spawnInterval = 30f, delayBeforeWave = 0f, packSize = 3, lanesAtOnce = 2 };
        wave.enemies.Add(new EnemySpawnEntry { prefab = EnemyPrefab<ShadowCreeper>(), count = 12 });
        Assert.AreEqual(2, wave.Arrivals);

        spawner.Configure(new List<Wave> { wave }, 0f);
        spawner.Begin();
        yield return null;

        List<EnemyBase> spawned = EnemyBase.Active.ToList();
        Assert.AreEqual(6, spawned.Count, "one arrival = 2 lanes x 3 monsters");
        var lanes = spawned.GroupBy(e => e.Lane).ToList();
        Assert.AreEqual(2, lanes.Count, "two different lanes");
        foreach (var lane in lanes)
        {
            Assert.AreEqual(3, lane.Count());
            float[] xs = lane.Select(e => e.transform.position.x).OrderBy(x => x).ToArray();
            Assert.Greater(xs[1] - xs[0], 0.5f, "pack is lined up, not stacked");
            Assert.Greater(xs[2] - xs[1], 0.5f, "pack is lined up, not stacked");
        }
    }

    [UnityTest]
    public IEnumerator Darkness_SlowsRobot_LampLightsArea_AndSlowsShadows()
    {
        Time.timeScale = 5f;
        LightField light = LightField.Instance;
        light.Configure(2);
        Assert.IsTrue(light.IsLit(2, 1), "bedside light reaches column 1");
        Assert.IsFalse(light.IsLit(2, 4), "column 4 is dark");

        Assert.IsTrue(grid.TryPlaceToy(ToyPrefab<LaserRobot>(), 2, 4, out ToyBase robotToy));
        var robot = (LaserRobot)robotToy;
        Assert.IsTrue(robot.InDarkness);
        float darkInterval = robot.CurrentFireInterval;

        Assert.IsTrue(grid.TryPlaceToy(ToyPrefab<ToyLamp>(), 3, 5, out _)); // ilumina linhas 2-4, colunas 4-6
        Assert.IsTrue(light.IsLampLit(2, 4) && light.IsLampLit(4, 6) && light.IsLampLit(3, 5));
        Assert.IsFalse(light.IsLampLit(1, 4), "row 1 outside the lamp light");
        Assert.IsFalse(light.IsLampLit(2, 7), "column 7 outside the lamp light");
        Assert.IsFalse(robot.InDarkness);
        Assert.AreEqual(darkInterval / 2f, robot.CurrentFireInterval, 0.001f, "dark robot fires at half speed");

        // Duas sombras correm juntas: a da faixa 4 atravessa a luz do abajur (colunas 4-6), a da faixa 0 não.
        EnemyBase lit = spawner.SpawnEnemy(EnemyPrefab<ShadowCreeper>(), 4);
        EnemyBase dark = spawner.SpawnEnemy(EnemyPrefab<ShadowCreeper>(), 0);
        yield return WaitUntil(() => grid.ColumnAt(lit.transform.position.x) == 5, 30f);
        Assert.AreEqual(light.LampLightEnemySpeed, lit.EffectiveSpeed / dark.EffectiveSpeed, 0.01f, "slower inside lamp light");
        yield return WaitUntil(() => dark.transform.position.x <= grid.GetCellCenter(0, 3).x, 30f);
        Assert.Greater(lit.transform.position.x, dark.transform.position.x + 0.5f, "the lit shadow fell behind");
    }

    [UnityTest]
    public IEnumerator Blackout_FromWave_TurnsLampLightOff_ForAWhile()
    {
        LightField light = LightField.Instance;
        light.Configure(0);
        Assert.IsTrue(grid.TryPlaceToy(ToyPrefab<ToyLamp>(), 2, 2, out _));
        Assert.IsTrue(light.IsLampLit(2, 3));
        bool? lastEvent = null;
        light.BlackoutChanged += started => lastEvent = started;

        var wave = new Wave { spawnInterval = 50f, blackout = true };
        wave.enemies.Add(new EnemySpawnEntry { prefab = EnemyPrefab<ShadowCreeper>(), count = 1 });
        spawner.Configure(new List<Wave> { wave }, 0f);
        spawner.Begin();
        yield return null;

        Assert.IsTrue(light.IsBlackout, "wave with blackout starts one");
        Assert.AreEqual(true, lastEvent);
        Assert.IsFalse(light.IsLampLit(2, 3), "lamps are off");

        Time.timeScale = 5f;
        yield return WaitUntil(() => !light.IsBlackout, light.BlackoutDuration + 5f);
        Assert.IsFalse(light.IsBlackout, "blackout ends");
        Assert.AreEqual(false, lastEvent);
        Assert.IsTrue(light.IsLampLit(2, 3), "lamps are back");
    }

    [UnityTest]
    public IEnumerator LightEater_SlipsPastRobot_EatsLamp_ButBearStopsIt()
    {
        Time.timeScale = 5f;
        EnemyBase eaterPrefab = EnemyPrefab<LightEater>();
        Assert.IsTrue(grid.TryPlaceToy(ToyPrefab<LaserRobot>(), 2, 8, out ToyBase robot));
        Assert.IsTrue(grid.TryPlaceToy(ToyPrefab<ToyLamp>(), 2, 1, out ToyBase lamp));
        Assert.IsTrue(grid.TryPlaceToy(ToyPrefab<TeddyBear>(), 3, 5, out ToyBase bear));
        Assert.IsTrue(grid.TryPlaceToy(ToyPrefab<ToyLamp>(), 3, 1, out _));
        Assert.IsTrue(grid.TryPlaceToy(ToyPrefab<ToyLamp>(), 3, 0, out _));

        // Escolhe a faixa com mais abajures (faixa 3 tem 2); monstros comuns não têm preferência.
        var options = new List<int> { 0, 1, 2, 3, 4 };
        Assert.AreEqual(3, eaterPrefab.PreferredLane(options));
        Assert.AreEqual(-1, EnemyPrefab<ShadowCreeper>().PreferredLane(options));

        EnemyBase eater = spawner.SpawnEnemy(eaterPrefab, 2);
        EnemyBase blocked = spawner.SpawnEnemy(eaterPrefab, 3);

        yield return WaitUntil(() => blocked.IsAttacking || Dead(blocked), 40f);
        Assert.AreEqual(bear, blocked.CurrentTarget, "the bear stops the light eater");

        yield return WaitUntil(() => eater.IsAttacking || Dead(eater), 60f);
        Assert.AreEqual(lamp, eater.CurrentTarget, "goes straight for the lamp");
        Assert.IsTrue(robot != null && robot.IsAlive && Mathf.Approximately(robot.CurrentHP, robot.MaxHP), "slipped past the robot");

        yield return WaitUntil(() => lamp == null || !lamp.IsAlive, 10f);
        Assert.IsTrue(lamp == null || !lamp.IsAlive, "eats lamps fast");
    }

    [UnityTest]
    public IEnumerator Nights_GetDarker_LoseGuards_AndGainBlackouts()
    {
        List<NightDefinition> nights = GameManager.Instance.Campaign.nights;
        Assert.AreEqual(9, nights[0].litColumns, "night 1 fully lit");
        for (int i = 1; i < nights.Count; i++)
            Assert.LessOrEqual(nights[i].litColumns, nights[i - 1].litColumns, $"{nights[i].title} not brighter than the previous");
        Assert.IsTrue(nights[0].laneGuards && nights[1].laneGuards && nights[2].laneGuards, "guards on nights 1-3");
        Assert.IsFalse(nights[3].laneGuards || nights[4].laneGuards, "no guards on nights 4-5");
        Assert.IsFalse(nights[2].EnemyTypes().Any(e => e is LightEater));
        Assert.IsTrue(nights[3].EnemyTypes().Any(e => e is LightEater), "light eater arrives on night 4");
        Assert.AreEqual(1, nights[3].waves.Count(w => w.blackout));
        Assert.AreEqual(2, nights[4].waves.Count(w => w.blackout));
        Assert.IsTrue(nights[0].waves.Take(3).All(w => w.packSize == 1), "night 1 starts with single monsters");
        Assert.IsTrue(nights[1].waves.Any(w => w.packSize > 1), "packs from night 2");
        Assert.IsTrue(nights[4].waves.Any(w => w.packSize >= 3 && w.lanesAtOnce >= 2), "night 5 has big packs");

        yield return LoadGame(4, startPlaying: false);
        Assert.AreEqual(1, LightField.Instance.AmbientColumns, "night 5 lighting applied");
        Assert.AreEqual(0, Object.FindObjectsByType<LaneGuard>(FindObjectsSortMode.None).Length, "no guards on night 5");

        yield return LoadGame(0, startPlaying: false);
        Assert.AreEqual(9, LightField.Instance.AmbientColumns, "night 1 lighting applied");
        Assert.AreEqual(grid.Rows, Object.FindObjectsByType<LaneGuard>(FindObjectsSortMode.None).Length, "guards on night 1");
    }

    /// <summary>
    /// Teste longo (~1 min): a Noite 5 de verdade (escura, apagões, grupos de 3, Devoradores) contra uma
    /// defesa forte e bem iluminada. Garante que a noite é vencível e que nenhum monstro fica "preso".
    /// </summary>
    [UnityTest]
    public IEnumerator Night5_IsWinnable_WithAStrongLitDefense()
    {
        NightDefinition night = GameManager.Instance.Night;
        Assert.AreEqual("Noite 5", night.title);
        LightField.Instance.Configure(night.litColumns);

        // Por faixa: Abajur, Robô, Robô, Abajur, Robô, Robô, Abajur, Urso (abajures nas colunas 0, 3 e 6
        // iluminam todas as casas dos robôs; o Urso segura o Devorador de Luz na frente).
        for (int r = 0; r < grid.Rows; r++)
        {
            int[] lampCols = { 0, 3, 6 }, robotCols = { 1, 2, 4, 5 };
            foreach (int c in lampCols) Assert.IsTrue(grid.TryPlaceToy(ToyPrefab<ToyLamp>(), r, c, out _));
            foreach (int c in robotCols) Assert.IsTrue(grid.TryPlaceToy(ToyPrefab<LaserRobot>(), r, c, out _));
            Assert.IsTrue(grid.TryPlaceToy(ToyPrefab<TeddyBear>(), r, 7, out _));
        }
        foreach (LaserRobot robot in Object.FindObjectsByType<LaserRobot>(FindObjectsSortMode.None))
            Assert.IsFalse(robot.InDarkness, $"robot {robot.Lane},{robot.Column} is lit");

        Time.timeScale = 8f;
        spawner.Configure(night.waves, 5f);
        spawner.Begin();
        yield return WaitUntil(() => GameManager.Instance.State != GameState.Playing, 1200f);
        Assert.AreEqual(GameState.Won, GameManager.Instance.State, "night 5 can be won with a strong defense");
    }
}
