using System.Collections.Generic;
using UnityEngine;

namespace SleepDefenders
{
    /// <summary>
    /// Caixinha de Música — suporte. Custo 75. Não causa dano.
    ///   - A melodia deixa os monstros por perto (a própria faixa e as vizinhas, até 1,5 casa de distância)
    ///     com METADE da velocidade e atacando na metade do ritmo.
    ///   - Combo: Ursos de Pelúcia em volta dela (quadrado 3x3) recuperam vida aos poucos.
    /// Várias caixinhas não somam: vale a mais forte.
    /// </summary>
    public class MusicBox : ToyBase
    {
        [Header("Melodia")]
        [Tooltip("Velocidade e ritmo de ataque dos monstros dentro do alcance (0,5 = metade).")]
        [SerializeField, Range(0.1f, 1f)] private float slowFactor = 0.5f;
        [Tooltip("Faixas vizinhas alcançadas (1 = a de cima e a de baixo).")]
        [SerializeField, Min(0)] private int laneReach = 1;
        [Tooltip("Distância máxima (em casas, ao longo da faixa) até o monstro.")]
        [SerializeField, Min(0.5f)] private float reachCells = 1.5f;

        [Header("Combo com o Urso")]
        [SerializeField, Min(0f)] private float bearHealPerSecond = 8f;

        [Header("Efeitos")]
        [SerializeField] private Color noteColor = new Color(1f, 0.6f, 0.9f, 1f);
        [SerializeField, Min(0.1f)] private float noteInterval = 0.9f;
        [Tooltip("Parte do modelo que gira devagar (bailarina/tampa). Opcional.")]
        [SerializeField] private Transform spinner;

        private static readonly List<MusicBox> active = new List<MusicBox>();
        /// <summary>Caixinhas tocando no tapete.</summary>
        public static IReadOnlyList<MusicBox> Active => active;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetActive() => active.Clear();

        private float noteTimer;

        public float SlowFactor => slowFactor;
        public float BearHealPerSecond => bearHealPerSecond;

        protected override void OnPlaced()
        {
            if (!active.Contains(this)) active.Add(this);
            noteTimer = Random.Range(0f, noteInterval);
        }

        protected override void OnDeath() => active.Remove(this);

        private void OnDisable() => active.Remove(this);

        /// <summary>A melodia alcança um monstro na faixa "lane" e posição "world"?</summary>
        public bool Covers(int lane, Vector3 world)
        {
            if (!IsAlive || Lane < 0 || lane < 0 || Mathf.Abs(lane - Lane) > laneReach) return false;
            float cell = GridManager.Instance != null ? GridManager.Instance.CellSize : 1f;
            return Mathf.Abs(world.x - transform.position.x) <= reachCells * cell;
        }

        /// <summary>Multiplicador de velocidade/ritmo de um monstro (1 = sem caixinha por perto).</summary>
        public static float SlowFactorFor(EnemyBase enemy)
        {
            if (enemy == null || active.Count == 0) return 1f;
            float factor = 1f;
            Vector3 position = enemy.transform.position;
            for (int i = 0; i < active.Count; i++)
            {
                MusicBox box = active[i];
                if (box != null && box.Covers(enemy.Lane, position)) factor = Mathf.Min(factor, box.slowFactor);
            }
            return factor;
        }

        private void Update()
        {
            if (!GameManager.IsPlaying || Lane < 0 || !IsAlive) return;
            float dt = Time.deltaTime;

            HealBears(dt);

            if (spinner != null) spinner.Rotate(0f, 40f * dt, 0f, Space.Self);
            noteTimer -= dt;
            if (noteTimer <= 0f)
            {
                noteTimer = noteInterval;
                Vector3 jitter = new Vector3(Random.Range(-0.2f, 0.2f), 0f, Random.Range(-0.2f, 0.2f));
                Vfx.Spawn(VfxType.Sparkle, transform.position + Vector3.up * 0.75f + jitter, noteColor, 0.45f);
            }
        }

        private void HealBears(float dt)
        {
            GridManager grid = GridManager.Instance;
            if (grid == null || bearHealPerSecond <= 0f) return;
            for (int r = Lane - 1; r <= Lane + 1; r++)
                for (int c = Column - 1; c <= Column + 1; c++)
                    if (grid.GetOccupant(r, c) is TeddyBear bear && bear.IsAlive) bear.Heal(bearHealPerSecond * dt);
        }
    }
}
