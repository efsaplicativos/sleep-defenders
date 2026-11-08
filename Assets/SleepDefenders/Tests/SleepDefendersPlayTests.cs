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
/// </summary>
public class SleepDefendersPlayTests
{
    GridManager grid;
    EnemySpawner spawner;
    ToyPlacementController placement;

    [UnitySetUp]
    public IEnumerator Setup()
    {
        Time.timeScale = 1f;
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
        GameManager.Instance.StartGame(false);
        yield return null;
    }

    [TearDown]
    public void TearDown() => Time.timeScale = 1f;

    EnemyBase EnemyPrefab<T>() where T : EnemyBase =>
        spawner.Waves.SelectMany(w => w.enemies).Select(e => e.prefab).First(p => p is T);

    ToyBase ToyPrefab<T>() where T : ToyBase =>
        placement.Cards.Select(c => c.Definition.prefab).First(p => p is T);

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
        yield return WaitUntil(() => monster.IsAttacking, 30f);
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
        yield return WaitUntil(() => flyer.IsAttacking, 30f);
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
        yield return WaitUntil(() => Dead(a), 30f);
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

    [UnityTest]
    public IEnumerator EnemyReachingBed_LosesGame()
    {
        Time.timeScale = 8f;
        spawner.SpawnEnemy(EnemyPrefab<WingedNightmare>(), 4);
        yield return WaitUntil(() => GameManager.Instance.State == GameState.Lost, 40f);
        Assert.AreEqual(GameState.Lost, GameManager.Instance.State);
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
        yield return WaitUntil(() => GameManager.Instance.State != GameState.Playing, 200f);
        Assert.AreEqual(GameState.Won, GameManager.Instance.State);
    }
}
