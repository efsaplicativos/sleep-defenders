using UnityEngine;

namespace SleepDefenders
{
    /// <summary>
    /// Sombra do Tapete — não vem pela borda: EMERGE do meio do tapete (colunas 3 a 5 da faixa),
    /// atrás da linha de frente. Os brinquedos da frente não a enxergam (Robôs só atiram para a frente),
    /// então só quem está mais perto da cama consegue pará-la. Castiga quem põe toda a defesa na frente.
    /// Depois de emergir, anda como uma Sombra Rastejante. Valores no prefab: HP 90, velocidade 0,34.
    /// </summary>
    public class RugShadow : EnemyBase
    {
        [Header("Emergir do tapete")]
        [Tooltip("Colunas possíveis (inclusive) onde a sombra aparece. Prefere casas vazias.")]
        [SerializeField] private Vector2Int emergeColumns = new Vector2Int(3, 5);
        [SerializeField, Min(0.05f)] private float emergeDuration = 0.9f;
        [Tooltip("Profundidade inicial do visual (abaixo do tapete).")]
        [SerializeField] private float buriedDepth = -0.7f;
        [SerializeField] private Color emergeColor = new Color(0.3f, 0.12f, 0.45f, 0.9f);

        private float emergeRemaining;

        public bool IsEmerging => emergeRemaining > 0f;
        /// <summary>Coluna em que a sombra saiu do tapete (-1 se nasceu fora da grade).</summary>
        public int EmergeColumn { get; private set; } = -1;

        public override void Initialize(int lane, float bedLine)
        {
            base.Initialize(lane, bedLine);
            GridManager grid = GridManager.Instance;
            if (grid == null) return;

            EmergeColumn = PickColumn(grid, lane);
            Vector3 p = grid.GetCellCenter(lane, EmergeColumn) + Vector3.right * (grid.CellSize * 0.35f);
            p.y = transform.position.y;
            // O Rigidbody cinemático com interpolação manda na posição: ajusta os dois.
            transform.position = p;
            body.position = p;

            emergeRemaining = emergeDuration;
            if (animator != null) animator.ExtraHeight = buriedDepth;
            Vfx.Spawn(VfxType.Puff, p + Vector3.up * 0.1f, emergeColor, 1.2f);
        }

        private int PickColumn(GridManager grid, int lane)
        {
            int min = Mathf.Clamp(Mathf.Min(emergeColumns.x, emergeColumns.y), 0, grid.Columns - 1);
            int max = Mathf.Clamp(Mathf.Max(emergeColumns.x, emergeColumns.y), 0, grid.Columns - 1);

            int chosen = -1, empties = 0;
            for (int c = min; c <= max; c++)
            {
                if (grid.IsOccupied(lane, c)) continue;
                if (Random.Range(0, ++empties) == 0) chosen = c; // sorteio justo entre as vazias
            }
            return chosen >= 0 ? chosen : Random.Range(min, max + 1);
        }

        protected override void OnTick(float deltaTime)
        {
            if (emergeRemaining <= 0f) return;
            emergeRemaining -= deltaTime;
            float k = 1f - Mathf.Clamp01(emergeRemaining / emergeDuration);
            if (animator != null) animator.ExtraHeight = Mathf.Lerp(buriedDepth, 0f, 1f - (1f - k) * (1f - k));
        }

        protected override void Move()
        {
            if (IsEmerging) return; // ainda saindo do tapete
            base.Move();
        }

        protected override bool ShouldEngage(ToyBase toy) => !IsEmerging;
    }
}
