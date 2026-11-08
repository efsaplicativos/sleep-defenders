using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SleepDefenders
{
    /// <summary>
    /// Grade 3D do tapete (5 faixas x 9 colunas).
    ///
    /// Layout no mundo (vista de cima):
    ///   - As faixas (rows) correm ao longo do eixo X. Linha 0 fica no fundo (+Z), linha 4 perto da câmera (-Z).
    ///   - A coluna 0 fica junto da cama (lado -X); os monstros entram pela direita (+X) e andam para -X.
    ///
    /// Responsabilidades:
    ///   1. Criar as células (BoxColliders na layer "Grid") — em edit mode pelo construtor ou no Awake.
    ///   2. Converter a posição do mouse/toque em célula usando Physics.Raycast.
    ///   3. Controlar a ocupação: no máximo 1 brinquedo por célula.
    ///   4. Mostrar o destaque (verde/vermelho) da célula sob o cursor.
    /// </summary>
    public class GridManager : MonoBehaviour
    {
        public static GridManager Instance { get; private set; }

        [Header("Dimensões")]
        [SerializeField, Min(1)] private int rows = 5;
        [SerializeField, Min(1)] private int columns = 9;
        [SerializeField, Min(0.1f)] private float cellSize = 1f;
        [Tooltip("Espessura do BoxCollider de cada célula.")]
        [SerializeField] private float cellColliderHeight = 0.1f;

        [Header("Visual das células (tapete quadriculado)")]
        [SerializeField] private Material cellMaterialA;
        [SerializeField] private Material cellMaterialB;
        [Tooltip("Folga entre os ladrilhos do tapete (0 = sem folga).")]
        [SerializeField, Range(0f, 0.2f)] private float cellGap = 0.03f;

        [Header("Destaque da célula sob o cursor")]
        [SerializeField] private Renderer highlight;
        [SerializeField] private Color validColor = new Color(0.4f, 1f, 0.5f, 0.45f);
        [SerializeField] private Color invalidColor = new Color(1f, 0.3f, 0.3f, 0.45f);
        [SerializeField] private Color removeColor = new Color(1f, 0.75f, 0.2f, 0.45f);

        [Header("Raycast")]
        [SerializeField] private Camera raycastCamera;
        [SerializeField] private float maxRayDistance = 200f;

        [Header("Objetivo")]
        [Tooltip("Distância à esquerda da coluna 0 em que o monstro 'alcança a cama'.")]
        [SerializeField] private float bedLineOffset = 0.4f;

        [SerializeField, HideInInspector] private List<GridCell> cells = new List<GridCell>();

        public enum HighlightMode { Valid, Invalid, Remove }

        private GridCell[,] cellLookup;
        private ToyBase[,] occupants;
        private MaterialPropertyBlock highlightBlock;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        public int Rows => rows;
        public int Columns => columns;
        public float CellSize => cellSize;
        public float LeftEdgeX => transform.position.x - columns * cellSize * 0.5f;
        public float RightEdgeX => transform.position.x + columns * cellSize * 0.5f;
        /// <summary>X a partir do qual um monstro é considerado dentro da cama (derrota).</summary>
        public float BedLineX => LeftEdgeX - bedLineOffset;

        /// <summary>Disparado quando um brinquedo é colocado ou removido de uma célula.</summary>
        public event Action<int, int, ToyBase> CellChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (raycastCamera == null) raycastCamera = Camera.main;
            occupants = new ToyBase[rows, columns];

            // Se as células não foram criadas no editor (ou a grade mudou de tamanho), cria agora.
            if (cells.Count != rows * columns || cells.Exists(c => c == null))
                BuildGrid();
            else
                RebuildLookup();

            HideHighlight();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------------------ Construção da grade

        /// <summary>Cria (ou recria) as 5x9 células como filhas deste objeto.</summary>
        [ContextMenu("Construir Grade")]
        public void BuildGrid()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child.GetComponent<GridCell>() == null) continue;
                if (Application.isPlaying) Destroy(child.gameObject);
                else DestroyImmediate(child.gameObject);
            }
            cells.Clear();

            int gridLayer = GameLayers.Grid;
            float tile = cellSize * (1f - cellGap);

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    // Um cubo achatado serve como ladrilho visível do tapete e como alvo do Raycast.
                    GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = $"Cell_{r}_{c}";
                    go.transform.SetParent(transform, false);
                    go.transform.position = GetCellCenter(r, c) - Vector3.up * (cellColliderHeight * 0.5f);
                    go.transform.localScale = new Vector3(tile, cellColliderHeight, tile);
                    if (gridLayer >= 0) go.layer = gridLayer;

                    var box = go.GetComponent<BoxCollider>();
                    // O collider cobre a célula inteira (inclusive a folga), para não haver "buracos" no Raycast.
                    box.size = new Vector3(cellSize / tile, 1f, cellSize / tile);

                    var rend = go.GetComponent<MeshRenderer>();
                    Material mat = ((r + c) % 2 == 0) ? cellMaterialA : cellMaterialB;
                    if (mat != null) rend.sharedMaterial = mat;
                    rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                    var cell = go.AddComponent<GridCell>();
                    cell.Setup(r, c);
                    cells.Add(cell);
                }
            }
            RebuildLookup();
        }

        private void RebuildLookup()
        {
            cellLookup = new GridCell[rows, columns];
            foreach (GridCell cell in cells)
            {
                if (cell == null) continue;
                cellLookup[cell.Row, cell.Column] = cell;
            }
        }

        // ------------------------------------------------------------------ Conversões de coordenadas

        public Vector3 GetCellCenter(int row, int column)
        {
            Vector3 origin = transform.position;
            float x = origin.x + (column - (columns - 1) * 0.5f) * cellSize;
            float z = origin.z + ((rows - 1) * 0.5f - row) * cellSize;
            return new Vector3(x, origin.y, z);
        }

        public float GetLaneZ(int row) => GetCellCenter(row, 0).z;

        public bool IsInside(int row, int column) => row >= 0 && row < rows && column >= 0 && column < columns;

        public GridCell GetCell(int row, int column)
        {
            if (!IsInside(row, column)) return null;
            if (cellLookup == null) RebuildLookup();
            return cellLookup[row, column];
        }

        // ------------------------------------------------------------------ Raycast do mouse

        /// <summary>
        /// Converte uma posição de tela (mouse ou toque) em célula, com Physics.Raycast
        /// restrito à layer "Grid". Retorna false se o ponteiro estiver sobre a UI ou fora do tapete.
        /// </summary>
        public bool TryGetCellAtScreenPoint(Vector2 screenPosition, out GridCell cell, bool ignoreWhenOverUI = true)
        {
            cell = null;
            if (raycastCamera == null) raycastCamera = Camera.main;
            if (raycastCamera == null) return false;

            if (ignoreWhenOverUI && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return false;

            Ray ray = raycastCamera.ScreenPointToRay(screenPosition);
            if (Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, GameLayers.GridMask, QueryTriggerInteraction.Collide))
                cell = hit.collider.GetComponent<GridCell>();
            return cell != null;
        }

        // ------------------------------------------------------------------ Ocupação

        public bool IsOccupied(int row, int column) => IsInside(row, column) && occupants[row, column] != null;

        public ToyBase GetOccupant(int row, int column) => IsInside(row, column) ? occupants[row, column] : null;

        public bool CanPlace(GridCell cell) => cell != null && !IsOccupied(cell.Row, cell.Column);

        /// <summary>
        /// Instancia o brinquedo no centro da célula e marca a célula como ocupada.
        /// Não cobra Luz — quem chama decide (ver ToyPlacementController).
        /// </summary>
        public bool TryPlaceToy(ToyBase prefab, int row, int column, out ToyBase instance)
        {
            instance = null;
            if (prefab == null || !IsInside(row, column) || IsOccupied(row, column)) return false;

            // Brinquedos olham para +X (de onde os monstros vêm).
            instance = Instantiate(prefab, GetCellCenter(row, column), Quaternion.identity);
            instance.name = $"{prefab.name}_{row}_{column}";
            occupants[row, column] = instance;
            instance.Destroyed += HandleToyDestroyed;
            instance.Initialize(row, column);
            CellChanged?.Invoke(row, column, instance);
            return true;
        }

        /// <summary>Libera a célula ocupada por este brinquedo (ex.: o carrinho de controle remoto disparou).</summary>
        public void ReleaseCell(ToyBase toy)
        {
            if (toy == null || occupants == null) return;
            int r = toy.Lane, c = toy.Column;
            if (!IsInside(r, c) || occupants[r, c] != toy) return;
            occupants[r, c] = null;
            toy.Destroyed -= HandleToyDestroyed;
            CellChanged?.Invoke(r, c, null);
        }

        private void HandleToyDestroyed(ToyBase toy) => ReleaseCell(toy);

        // ------------------------------------------------------------------ Destaque

        public void ShowHighlight(GridCell cell, HighlightMode mode)
        {
            if (highlight == null || cell == null) return;
            highlight.gameObject.SetActive(true);
            highlight.transform.position = GetCellCenter(cell.Row, cell.Column) + Vector3.up * 0.02f;

            Color color = mode == HighlightMode.Valid ? validColor : mode == HighlightMode.Remove ? removeColor : invalidColor;
            // Leve pulsação para chamar atenção.
            color.a *= 0.8f + 0.2f * Mathf.Sin(Time.unscaledTime * 6f);
            highlightBlock ??= new MaterialPropertyBlock();
            highlight.GetPropertyBlock(highlightBlock);
            highlightBlock.SetColor(BaseColorId, color);
            highlightBlock.SetColor(ColorId, color);
            highlight.SetPropertyBlock(highlightBlock);
        }

        public void HideHighlight()
        {
            if (highlight != null) highlight.gameObject.SetActive(false);
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < columns; c++)
                    Gizmos.DrawWireCube(GetCellCenter(r, c), new Vector3(cellSize, 0.02f, cellSize));

            // Linha da cama: se um monstro passar daqui, o jogador perde.
            Gizmos.color = Color.red;
            float zTop = GetLaneZ(0) + cellSize * 0.5f, zBottom = GetLaneZ(rows - 1) - cellSize * 0.5f;
            Gizmos.DrawLine(new Vector3(BedLineX, transform.position.y, zTop), new Vector3(BedLineX, transform.position.y, zBottom));
        }
#endif
    }
}
