using System.Collections.Generic;
using UnityEngine;

namespace SleepDefenders
{
    /// <summary>
    /// Lanterna — dano em área curta. Custo 125.
    ///   - Ilumina um cone curto à frente: a própria casa e as 2 seguintes da faixa, mais as 2 casas
    ///     à frente nas faixas vizinhas (7 casas).
    ///   - A cada 1,5 s, se houver monstros no cone, dá um "flash" que causa 20 de dano em TODOS eles.
    ///   - O cone conta como luz para o LightField (Robôs atiram normalmente, sombras ficam lentas)
    ///     e NÃO se apaga no apagão: é a resposta para as noites de apagão (combo).
    /// </summary>
    public class ToyFlashlight : ToyBase
    {
        [Header("Cone de luz")]
        [Tooltip("Quantas casas à frente o cone alcança na própria faixa (além da casa da lanterna).")]
        [SerializeField, Min(1)] private int reachColumns = 2;
        [Tooltip("O cone também pega as faixas vizinhas (da coluna seguinte até o alcance)?")]
        [SerializeField] private bool coversSideLanes = true;

        [Header("Flash")]
        [SerializeField, Min(0.1f)] private float flashInterval = 1.5f;
        [SerializeField, Min(0f)] private float damage = 20f;
        [Tooltip("O primeiro flash sai mais rápido depois que um monstro entra no cone.")]
        [SerializeField, Range(0f, 1f)] private float firstFlashDelayFactor = 0.3f;

        [Header("Efeitos")]
        [SerializeField] private Light beam;
        [SerializeField] private float beamBaseIntensity = 2.5f;
        [SerializeField] private float beamFlashIntensity = 9f;
        [SerializeField] private Color flashColor = new Color(1f, 0.95f, 0.7f, 1f);

        private float flashTimer;
        private float scanTimer;
        private bool hadTargets;
        private readonly List<EnemyBase> targets = new List<EnemyBase>();

        /// <summary>Quantos flashes esta lanterna já deu (usado por testes).</summary>
        public int FlashCount { get; private set; }
        public float Damage => damage;
        public float FlashInterval => flashInterval;

        /// <summary>A casa (row, column) está dentro do cone desta lanterna?</summary>
        public bool CoversCell(int row, int column)
        {
            if (!IsAlive || Lane < 0) return false;
            int dc = column - Column;
            if (row == Lane) return dc >= 0 && dc <= reachColumns;
            if (coversSideLanes && Mathf.Abs(row - Lane) == 1) return dc >= 1 && dc <= reachColumns;
            return false;
        }

        /// <summary>Marca as casas do cone (chamado pelo LightField ao recalcular o mapa de luz).</summary>
        public void ForEachLitCell(System.Action<int, int> mark)
        {
            if (!IsAlive || Lane < 0 || mark == null) return;
            for (int r = Lane - 1; r <= Lane + 1; r++)
                for (int c = Column; c <= Column + reachColumns; c++)
                    if (CoversCell(r, c)) mark(r, c);
        }

        private void Update()
        {
            if (beam != null) beam.intensity = Mathf.MoveTowards(beam.intensity, beamBaseIntensity, Time.deltaTime * 20f);
            if (!GameManager.IsPlaying || Lane < 0 || !IsAlive) return;

            flashTimer = Mathf.Max(0f, flashTimer - Time.deltaTime);
            scanTimer -= Time.deltaTime;
            if (scanTimer <= 0f)
            {
                // Varredura a cada 0,1 s (não precisa ser todo frame).
                scanTimer = 0.1f;
                CollectTargets();
                bool hasTargets = targets.Count > 0;
                if (hasTargets && !hadTargets) flashTimer = Mathf.Min(flashTimer, flashInterval * firstFlashDelayFactor);
                hadTargets = hasTargets;
            }

            if (hadTargets && flashTimer <= 0f)
            {
                CollectTargets(); // lista fresca na hora do flash
                if (targets.Count == 0) return;
                flashTimer = flashInterval;
                Flash();
            }
        }

        private void CollectTargets()
        {
            targets.Clear();
            GridManager grid = GridManager.Instance;
            if (grid == null) return;
            IReadOnlyList<EnemyBase> enemies = EnemyBase.Active;
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyBase e = enemies[i];
                if (e == null || !e.IsAlive) continue;
                if (CoversCell(e.Lane, grid.ColumnAt(e.transform.position.x))) targets.Add(e);
            }
        }

        private void Flash()
        {
            FlashCount++;
            if (animator != null) animator.PlayRecoil();
            if (beam != null) beam.intensity = beamFlashIntensity;
            Sfx.Play(SfxType.Laser, 0.35f);
            // Copia: o dano pode matar e tirar monstros da lista ativa.
            var hit = new List<EnemyBase>(targets);
            foreach (EnemyBase e in hit)
            {
                if (e == null || !e.IsAlive) continue;
                Vfx.Spawn(VfxType.Sparkle, e.transform.position + Vector3.up * 0.5f, flashColor, 0.7f);
                e.TakeDamage(damage);
            }
        }
    }
}
