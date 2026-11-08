using System;
using UnityEngine;

namespace SleepDefenders
{
    /// <summary>
    /// Luz como território. Cada casa do tapete está em um de três estados:
    ///   - Escuro: Robôs Laser atiram na metade da velocidade.
    ///   - Luz de cabeceira (ambiente): as primeiras "ambientColumns" colunas a partir da cama.
    ///     Normal. Cada noite define quantas colunas a luz de cabeceira alcança (Noite 1 = tapete todo).
    ///   - Luz de abajur: as casas em volta de cada Abajur (quadrado de lado 2 * lampRadius + 1).
    ///     Robôs atiram normalmente E as sombras andam mais devagar.
    ///
    /// Apagão: por alguns segundos os abajures param de iluminar (continuam gerando Luz).
    /// Ondas com "blackout" disparam um apagão quando começam.
    ///
    /// O visual é uma camada fina sobre cada casa (escura ou dourada), que aparece e some suavemente.
    /// O mapa de luz só é recalculado quando algo muda (brinquedo colocado/removido, apagão, noite nova).
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class LightField : MonoBehaviour
    {
        public static LightField Instance { get; private set; }

        [SerializeField] private GridManager grid;
        [SerializeField] private EnemySpawner spawner;

        [Header("Regras")]
        [Tooltip("Colunas iluminadas pela luz de cabeceira, a partir da cama. O resto do tapete fica no escuro.")]
        [SerializeField, Min(0)] private int ambientColumns = 9;
        [Tooltip("Alcance da luz do abajur em casas (1 = quadrado 3x3 em volta dele).")]
        [SerializeField, Min(0)] private int lampRadius = 1;
        [Tooltip("No escuro, o intervalo entre os tiros do Robô é multiplicado por este valor.")]
        [SerializeField, Min(1f)] private float darkFireIntervalMultiplier = 2f;
        [Tooltip("Velocidade das sombras dentro da luz de um abajur (1 = normal).")]
        [SerializeField, Range(0.1f, 1f)] private float lampLightEnemySpeed = 0.65f;
        [Tooltip("Duração de cada apagão (segundos de jogo).")]
        [SerializeField, Min(0.5f)] private float blackoutDuration = 12f;

        [Header("Visual")]
        [Tooltip("Material transparente (URP Unlit) das camadas de luz/escuridão.")]
        [SerializeField] private Material overlayMaterial;
        [SerializeField] private Color darkColor = new Color(0.02f, 0.01f, 0.08f, 0.62f);
        [SerializeField] private Color lampColor = new Color(1f, 0.8f, 0.35f, 0.17f);
        [SerializeField] private float overlayHeight = 0.012f;
        [SerializeField, Min(0.1f)] private float fadeSpeed = 3f;

        private bool[,] lampLit;
        private bool dirty = true;
        private float blackoutRemaining;

        private Renderer[,] overlays;
        private Color[,] overlayColors;
        private MaterialPropertyBlock block;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        public int AmbientColumns => ambientColumns;
        public int LampRadius => lampRadius;
        public bool IsBlackout => blackoutRemaining > 0f;
        public float BlackoutRemaining => blackoutRemaining;
        public float BlackoutDuration => blackoutDuration;
        public float LampLightEnemySpeed => lampLightEnemySpeed;
        public float DarkFireIntervalMultiplier => darkFireIntervalMultiplier;

        /// <summary>true quando um apagão começa, false quando as luzes voltam.</summary>
        public event Action<bool> BlackoutChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            if (grid == null) grid = FindAnyObjectByType<GridManager>();
            if (spawner == null) spawner = FindAnyObjectByType<EnemySpawner>();
        }

        private void Start()
        {
            if (grid != null) grid.CellChanged += OnCellChanged;
            if (spawner != null) spawner.WaveStarted += OnWaveStarted;
            EnsureOverlays();
            RefreshVisuals(true);
        }

        private void OnDestroy()
        {
            if (grid != null) grid.CellChanged -= OnCellChanged;
            if (spawner != null) spawner.WaveStarted -= OnWaveStarted;
            if (Instance == this) Instance = null;
        }

        private void OnCellChanged(int row, int column, ToyBase toy) => dirty = true;

        private void OnWaveStarted(int index, Wave wave)
        {
            if (wave != null && wave.blackout) TriggerBlackout(blackoutDuration);
        }

        // ------------------------------------------------------------------ Configuração

        /// <summary>Aplica a luz de cabeceira da noite (chamado pelo GameManager ao preparar a noite).</summary>
        public void Configure(int litColumns)
        {
            int max = grid != null ? grid.Columns : 9;
            ambientColumns = Mathf.Clamp(litColumns, 0, max);
            blackoutRemaining = 0f;
            dirty = true;
            if (overlays != null) RefreshVisuals(true);
        }

        /// <summary>Apaga os abajures por "seconds" segundos de jogo (soma com um apagão em andamento).</summary>
        public void TriggerBlackout(float seconds)
        {
            if (seconds <= 0f) return;
            bool wasBlackout = IsBlackout;
            blackoutRemaining = Mathf.Max(blackoutRemaining, seconds);
            dirty = true;
            if (!wasBlackout)
            {
                Sfx.Play(SfxType.Error, 0.8f);
                BlackoutChanged?.Invoke(true);
            }
        }

        private void Update()
        {
            if (blackoutRemaining > 0f && GameManager.IsPlaying)
            {
                blackoutRemaining -= Time.deltaTime;
                if (blackoutRemaining <= 0f)
                {
                    blackoutRemaining = 0f;
                    dirty = true;
                    BlackoutChanged?.Invoke(false);
                }
            }
            RefreshVisuals(false);
        }

        // ------------------------------------------------------------------ Consultas

        public bool IsAmbientLit(int row, int column) =>
            grid != null && grid.IsInside(row, column) && column < ambientColumns;

        public bool IsLampLit(int row, int column)
        {
            if (grid == null || !grid.IsInside(row, column)) return false;
            Recompute();
            return lampLit[row, column];
        }

        /// <summary>Casa iluminada (luz de cabeceira ou de abajur)? Sem LightField na cena, tudo é claro.</summary>
        public bool IsLit(int row, int column) => IsAmbientLit(row, column) || IsLampLit(row, column);

        /// <summary>Multiplicador do intervalo de tiro de um brinquedo na casa (1 = normal, maior = mais lento).</summary>
        public float FireIntervalMultiplier(int row, int column) =>
            grid == null || !grid.IsInside(row, column) || IsLit(row, column) ? 1f : darkFireIntervalMultiplier;

        /// <summary>Multiplicador de velocidade de uma sombra na faixa "lane" e posição "world" (só a coluna importa).</summary>
        public float EnemySpeedFactorAt(int lane, Vector3 world)
        {
            if (grid == null) return 1f;
            int column = grid.ColumnAt(world.x);
            return IsLampLit(lane, column) ? lampLightEnemySpeed : 1f;
        }

        // ------------------------------------------------------------------ Mapa de luz

        private void Recompute()
        {
            if (grid == null) return;
            if (lampLit == null || lampLit.GetLength(0) != grid.Rows || lampLit.GetLength(1) != grid.Columns)
            {
                lampLit = new bool[grid.Rows, grid.Columns];
                dirty = true;
            }
            if (!dirty) return;
            dirty = false;
            Array.Clear(lampLit, 0, lampLit.Length);
            if (IsBlackout) return;

            if (Application.isPlaying)
            {
                for (int r = 0; r < grid.Rows; r++)
                    for (int c = 0; c < grid.Columns; c++)
                        if (grid.GetOccupant(r, c) is ToyLamp lamp && lamp.IsAlive) MarkLamp(r, c);
            }
            else
            {
                // Pré-visualização no Editor (cena parada): acha os abajures pela posição.
                foreach (ToyLamp lamp in FindObjectsByType<ToyLamp>(FindObjectsSortMode.None))
                    if (grid.WorldToCell(lamp.transform.position, out int r, out int c)) MarkLamp(r, c);
            }
        }

        private void MarkLamp(int row, int column)
        {
            for (int r = row - lampRadius; r <= row + lampRadius; r++)
                for (int c = column - lampRadius; c <= column + lampRadius; c++)
                    if (grid.IsInside(r, c)) lampLit[r, c] = true;
        }

        // ------------------------------------------------------------------ Visual

        /// <summary>Cria uma camada fina (sem collider, para não atrapalhar o Raycast da grade) sobre cada casa.</summary>
        public void EnsureOverlays()
        {
            if (grid == null || overlayMaterial == null) return;
            if (overlays != null && overlays.GetLength(0) == grid.Rows && overlays.GetLength(1) == grid.Columns) return;

            Transform root = new GameObject("Camada de Luz").transform;
            root.SetParent(transform, false);
            Mesh quad = BuildQuad(grid.CellSize);
            overlays = new Renderer[grid.Rows, grid.Columns];
            overlayColors = new Color[grid.Rows, grid.Columns];
            for (int r = 0; r < grid.Rows; r++)
            {
                for (int c = 0; c < grid.Columns; c++)
                {
                    var go = new GameObject($"Luz_{r}_{c}");
                    go.transform.SetParent(root, false);
                    go.transform.position = grid.GetCellCenter(r, c) + Vector3.up * overlayHeight;
                    go.AddComponent<MeshFilter>().sharedMesh = quad;
                    var rend = go.AddComponent<MeshRenderer>();
                    rend.sharedMaterial = overlayMaterial;
                    rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    rend.receiveShadows = false;
                    overlays[r, c] = rend;
                    overlayColors[r, c] = new Color(0f, 0f, 0f, 0f);
                }
            }
        }

        /// <summary>Atualiza as cores das camadas. "instant" pula a transição suave (troca de noite / Editor).</summary>
        public void RefreshVisuals(bool instant)
        {
            if (overlays == null || grid == null) return;
            block ??= new MaterialPropertyBlock();
            float step = instant ? 1f : Time.deltaTime * fadeSpeed;
            for (int r = 0; r < grid.Rows; r++)
            {
                for (int c = 0; c < grid.Columns; c++)
                {
                    Color target = IsLampLit(r, c) ? lampColor : IsAmbientLit(r, c) ? new Color(darkColor.r, darkColor.g, darkColor.b, 0f) : darkColor;
                    Color current = overlayColors[r, c];
                    if (current == target && !instant) continue;
                    current = instant ? target : Color.Lerp(current, target, Mathf.Clamp01(step * 2f));
                    if (!instant && Mathf.Abs(current.a - target.a) < 0.004f) current = target;
                    overlayColors[r, c] = current;

                    Renderer rend = overlays[r, c];
                    if (rend == null) continue;
                    rend.enabled = current.a > 0.003f;
                    rend.GetPropertyBlock(block);
                    block.SetColor(BaseColorId, current);
                    block.SetColor(ColorId, current);
                    rend.SetPropertyBlock(block);
                }
            }
        }

        private static Mesh BuildQuad(float size)
        {
            float h = size * 0.5f;
            var mesh = new Mesh { name = "LightCell" };
            mesh.vertices = new[] { new Vector3(-h, 0f, -h), new Vector3(-h, 0f, h), new Vector3(h, 0f, h), new Vector3(h, 0f, -h) };
            mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
