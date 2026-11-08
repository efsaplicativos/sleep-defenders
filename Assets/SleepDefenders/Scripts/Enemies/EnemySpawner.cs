using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SleepDefenders
{
    [Serializable]
    public class EnemySpawnEntry
    {
        public EnemyBase prefab;
        [Min(1)] public int count = 1;
    }

    [Serializable]
    public class Wave
    {
        public string name = "Onda";
        public List<EnemySpawnEntry> enemies = new List<EnemySpawnEntry>();
        [Tooltip("Segundos entre um monstro e o próximo dentro da onda.")]
        [Min(0.1f)] public float spawnInterval = 4f;
        [Tooltip("Espera antes da onda começar (encurtada se o tapete estiver vazio de monstros).")]
        [Min(0f)] public float delayBeforeWave = 12f;
        [Tooltip("Ondas grandes mostram um aviso especial e soltam um 'bando' inicial.")]
        public bool isBigWave;
        [Tooltip("Faixas por onde os monstros desta onda podem vir (0 = fundo ... 4 = frente). Vazio = todas.")]
        public List<int> lanes = new List<int>();

        public int TotalCount
        {
            get
            {
                int total = 0;
                foreach (EnemySpawnEntry e in enemies) if (e != null && e.prefab != null) total += e.count;
                return total;
            }
        }
    }

    /// <summary>
    /// Sistema de ondas. Cada onda é uma lista de monstros que nascem em faixas aleatórias
    /// (entre as faixas permitidas da onda, evitando repetir a mesma faixa duas vezes seguidas),
    /// à direita do tapete. As ondas de cada noite vêm da Campanha (ver GameManager.ConfigureNight).
    /// </summary>
    public class EnemySpawner : MonoBehaviour
    {
        [SerializeField] private List<Wave> waves = new List<Wave>();
        [Tooltip("Tempo de preparo antes da primeira onda (para colocar os primeiros abajures).")]
        [SerializeField, Min(0f)] private float initialDelay = 20f;
        [Tooltip("Distância à direita da última coluna em que os monstros nascem.")]
        [SerializeField] private float spawnOffsetX = 1.2f;
        [SerializeField, Range(0f, 0.9f)] private float intervalJitter = 0.3f;
        [Tooltip("Espera mínima entre ondas quando não há monstros vivos.")]
        [SerializeField, Min(0f)] private float minGapWhenClear = 4f;
        [SerializeField] private Transform enemiesParent;

        private int lastLane = -1;
        private Coroutine routine;

        public IReadOnlyList<Wave> Waves => waves;
        public int TotalWaves => waves.Count;
        /// <summary>Índice da onda atual (0-based). -1 antes da primeira.</summary>
        public int CurrentWaveIndex { get; private set; } = -1;
        public bool AllWavesSpawned { get; private set; }
        public bool IsRunning => routine != null;
        /// <summary>Progresso total da noite (0..1), em monstros já criados.</summary>
        public float Progress { get; private set; }

        /// <summary>Disparado quando uma onda começa: (índice, onda).</summary>
        public event Action<int, Wave> WaveStarted;
        public event Action<EnemyBase> EnemySpawned;

        public float InitialDelay => initialDelay;

        public void SetWaves(List<Wave> newWaves) => waves = newWaves ?? new List<Wave>();

        /// <summary>Prepara as ondas de uma noite (só tem efeito antes de Begin).</summary>
        public void Configure(List<Wave> newWaves, float newInitialDelay)
        {
            if (IsRunning) return;
            SetWaves(newWaves);
            initialDelay = Mathf.Max(0f, newInitialDelay);
            CurrentWaveIndex = -1;
            Progress = 0f;
            AllWavesSpawned = false;
        }

        public void Begin()
        {
            if (routine != null) StopCoroutine(routine);
            AllWavesSpawned = false;
            CurrentWaveIndex = -1;
            routine = StartCoroutine(RunWaves());
        }

        public void Stop()
        {
            if (routine != null) StopCoroutine(routine);
            routine = null;
        }

        private IEnumerator RunWaves()
        {
            int total = 0, spawned = 0;
            foreach (Wave w in waves) total += w.TotalCount;

            yield return WaitWhilePlaying(initialDelay, false);

            for (int i = 0; i < waves.Count; i++)
            {
                Wave wave = waves[i];
                if (i > 0) yield return WaitWhilePlaying(wave.delayBeforeWave, true);

                CurrentWaveIndex = i;
                WaveStarted?.Invoke(i, wave);

                List<EnemyBase> queue = BuildShuffledQueue(wave);
                for (int n = 0; n < queue.Count; n++)
                {
                    SpawnEnemy(queue[n], PickLane(wave));
                    spawned++;
                    Progress = total > 0 ? spawned / (float)total : 1f;

                    // Na onda grande os 3 primeiros chegam juntos, como um "bando".
                    bool burst = wave.isBigWave && n < 2;
                    float wait = burst ? 0.4f : wave.spawnInterval * (1f + UnityEngine.Random.Range(-intervalJitter, intervalJitter));
                    yield return WaitWhilePlaying(wait, false);
                }
            }

            AllWavesSpawned = true;
            routine = null;
        }

        /// <summary>Espera "seconds" de jogo (pausa se o jogo não estiver rodando).</summary>
        private IEnumerator WaitWhilePlaying(float seconds, bool shortenWhenClear)
        {
            float t = 0f;
            while (t < seconds)
            {
                if (shortenWhenClear && t >= minGapWhenClear && EnemyBase.Active.Count == 0) yield break;
                if (GameManager.IsPlaying) t += Time.deltaTime;
                yield return null;
            }
        }

        private static List<EnemyBase> BuildShuffledQueue(Wave wave)
        {
            var list = new List<EnemyBase>();
            foreach (EnemySpawnEntry e in wave.enemies)
                if (e != null && e.prefab != null)
                    for (int k = 0; k < e.count; k++) list.Add(e.prefab);

            // Embaralha (Fisher-Yates) para a ordem variar a cada partida.
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
            return list;
        }

        private readonly List<int> laneOptions = new List<int>();

        /// <summary>Sorteia uma faixa entre as permitidas pela onda, evitando repetir a anterior.</summary>
        public int PickLane(Wave wave)
        {
            GridManager grid = GridManager.Instance;
            int rows = grid != null ? grid.Rows : 5;

            laneOptions.Clear();
            if (wave != null)
                foreach (int l in wave.lanes)
                    if (l >= 0 && l < rows && !laneOptions.Contains(l)) laneOptions.Add(l);
            if (laneOptions.Count == 0)
                for (int l = 0; l < rows; l++) laneOptions.Add(l);

            int lane = laneOptions[UnityEngine.Random.Range(0, laneOptions.Count)];
            if (laneOptions.Count > 1 && lane == lastLane)
            {
                int i = (laneOptions.IndexOf(lane) + UnityEngine.Random.Range(1, laneOptions.Count)) % laneOptions.Count;
                lane = laneOptions[i];
            }
            lastLane = lane;
            return lane;
        }

        /// <summary>Cria um monstro na faixa indicada (público para testes e eventos de script).</summary>
        public EnemyBase SpawnEnemy(EnemyBase prefab, int lane)
        {
            GridManager grid = GridManager.Instance;
            if (prefab == null || grid == null) return null;

            var position = new Vector3(grid.RightEdgeX + spawnOffsetX, grid.transform.position.y, grid.GetLaneZ(lane));
            EnemyBase enemy = Instantiate(prefab, position, Quaternion.identity, enemiesParent);
            enemy.Initialize(lane, grid.BedLineX);
            EnemySpawned?.Invoke(enemy);
            return enemy;
        }
    }
}
