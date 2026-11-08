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
        int start = currency.CurrentLight;
        Assert.AreEqual(150, start);

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
}
