using System.Collections.Generic;
using UnityEngine;

namespace SleepDefenders
{
    /// <summary>
    /// Obstáculos do tapete, definidos por noite (ver NightDefinition):
    ///   - Blocos de montar: ocupam casas, onde não dá para pôr brinquedos (os monstros passam por cima).
    ///   - Faixa escorregadia (bolinhas de gude): os monstros que andam no chão correm 40% mais rápido nela.
    ///     Quem está voando (Pesadelo Alado antes de pousar) não escorrega.
    /// O visual é criado em tempo de execução a cada noite configurada.
    /// </summary>
    public class RugHazards : MonoBehaviour
    {
        public static RugHazards Instance { get; private set; }

        [SerializeField] private GridManager grid;

        [Header("Regras")]
        [Tooltip("Velocidade dos monstros (no chão) numa faixa escorregadia (1 = normal).")]
        [SerializeField, Min(1f)] private float slipperySpeedMultiplier = 1.4f;

        [Header("Visual")]
        [Tooltip("Prefab dos blocos de montar (só visual, sem collider).")]
        [SerializeField] private GameObject blocksPrefab;
        [Tooltip("Material transparente da faixa escorregadia.")]
        [SerializeField] private Material slipperyMaterial;
        [Tooltip("Material das bolinhas de gude.")]
        [SerializeField] private Material marbleMaterial;
        [SerializeField, Min(0)] private int marblesPerLane = 7;

        private readonly List<int> slipperyLanes = new List<int>();
        private readonly List<Vector2Int> blockedCells = new List<Vector2Int>();
        private Transform visualsRoot;

        public IReadOnlyList<int> SlipperyLanes => slipperyLanes;
        public IReadOnlyList<Vector2Int> BlockedCells => blockedCells;
        public float SlipperySpeedMultiplier => slipperySpeedMultiplier;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            if (grid == null) grid = FindAnyObjectByType<GridManager>();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public bool IsSlippery(int lane) => slipperyLanes.Contains(lane);

        /// <summary>Multiplicador de velocidade de quem anda no chão da faixa "lane".</summary>
        public float SpeedFactor(int lane) => IsSlippery(lane) ? slipperySpeedMultiplier : 1f;

        /// <summary>Aplica os obstáculos da noite (null = nenhum). Chamado pelo GameManager ao preparar a noite.</summary>
        public void Apply(IEnumerable<Vector2Int> blocked, IEnumerable<int> slippery)
        {
            blockedCells.Clear();
            slipperyLanes.Clear();
            if (grid == null) grid = GridManager.Instance;
            int rows = grid != null ? grid.Rows : 5;

            if (blocked != null)
                foreach (Vector2Int cell in blocked)
                    if ((grid == null || grid.IsInside(cell.x, cell.y)) && !blockedCells.Contains(cell)) blockedCells.Add(cell);
            if (slippery != null)
                foreach (int lane in slippery)
                    if (lane >= 0 && lane < rows && !slipperyLanes.Contains(lane)) slipperyLanes.Add(lane);

            if (grid != null) grid.SetBlockedCells(blockedCells);
            RebuildVisuals();
        }

        // ------------------------------------------------------------------ Visual

        private void RebuildVisuals()
        {
            if (visualsRoot != null) Discard(visualsRoot.gameObject);
            visualsRoot = null;
            if (grid == null || (blockedCells.Count == 0 && slipperyLanes.Count == 0)) return;

            visualsRoot = new GameObject("Obstaculos do Tapete").transform;
            visualsRoot.SetParent(transform, false);

            foreach (Vector2Int cell in blockedCells) SpawnBlocks(cell);
            foreach (int lane in slipperyLanes) SpawnSlipperyLane(lane);
        }

        /// <summary>Destroy no Play, DestroyImmediate no Editor (pré-visualização da cena).</summary>
        private static void Discard(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        private void SpawnBlocks(Vector2Int cell)
        {
            Vector3 center = grid.GetCellCenter(cell.x, cell.y);
            // Sorteio fixo por casa: o mesmo obstáculo sempre fica do mesmo jeito.
            var rng = new System.Random(cell.x * 31 + cell.y * 7 + 3);
            Quaternion yaw = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            GameObject go;
            if (blocksPrefab != null)
            {
                go = Instantiate(blocksPrefab, center, yaw, visualsRoot);
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Discard(go.GetComponent<Collider>());
                go.transform.SetParent(visualsRoot, false);
                go.transform.SetPositionAndRotation(center + Vector3.up * 0.2f, yaw);
                go.transform.localScale = new Vector3(0.6f, 0.4f, 0.6f);
            }
            go.name = $"Blocos_{cell.x}_{cell.y}";
            foreach (Collider c in go.GetComponentsInChildren<Collider>()) Discard(c);
        }

        private void SpawnSlipperyLane(int lane)
        {
            float width = grid.Columns * grid.CellSize;
            Vector3 center = new Vector3(grid.transform.position.x, grid.transform.position.y + 0.006f, grid.GetLaneZ(lane));

            if (slipperyMaterial != null)
            {
                var stripe = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Discard(stripe.GetComponent<Collider>());
                stripe.name = $"FaixaEscorregadia_{lane}";
                stripe.transform.SetParent(visualsRoot, false);
                stripe.transform.SetPositionAndRotation(center, Quaternion.Euler(90f, 0f, 0f));
                stripe.transform.localScale = new Vector3(width, grid.CellSize * 0.72f, 1f);
                var rend = stripe.GetComponent<Renderer>();
                rend.sharedMaterial = slipperyMaterial;
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            var rng = new System.Random(lane * 97 + 11);
            for (int i = 0; i < marblesPerLane; i++)
            {
                var marble = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Discard(marble.GetComponent<Collider>());
                marble.name = "BolinhaDeGude";
                marble.transform.SetParent(visualsRoot, false);
                float x = grid.LeftEdgeX + ((i + 0.5f) / marblesPerLane) * width + (float)(rng.NextDouble() - 0.5) * 0.5f;
                float z = center.z + (float)(rng.NextDouble() - 0.5) * grid.CellSize * 0.6f;
                const float size = 0.11f;
                marble.transform.position = new Vector3(x, grid.transform.position.y + size * 0.5f, z);
                marble.transform.localScale = Vector3.one * size;
                var rend = marble.GetComponent<Renderer>();
                if (marbleMaterial != null) rend.sharedMaterial = marbleMaterial;
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }
    }
}
