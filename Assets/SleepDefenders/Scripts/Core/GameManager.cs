using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SleepDefenders
{
    public enum GameState { Menu, Playing, Won, Lost }

    /// <summary>
    /// Estado geral da partida: menu inicial, jogando, vitória (todas as ondas da noite derrotadas)
    /// e derrota (um monstro alcançou a cama).
    ///
    /// Também aplica a noite escolhida da Campanha: ondas do spawner, Luz inicial, bolsa de brinquedos,
    /// luar, escuridão e obstáculos do tapete. Vencer uma noite libera a próxima e dá de 1 a 3 estrelas
    /// (LevelProgress):
    ///   1 estrela  vencer a noite;
    ///   2 estrelas nenhum Abajur quebrado;
    ///   3 estrelas nenhum monstro chegou perto da cama (nas 2 colunas junto dela).
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }
        public static bool IsPlaying => Instance != null && Instance.State == GameState.Playing;

        [SerializeField] private EnemySpawner spawner;
        [Tooltip("Sequência de noites do jogo.")]
        [SerializeField] private Campaign campaign;
        [SerializeField] private CurrencyManager currency;
        [SerializeField] private ToyPlacementController placement;
        [SerializeField] private MoonlightSpawner moonlight;
        [SerializeField] private LightField lightField;
        [Tooltip("Objeto que agrupa os Ratinhos de Corda (ligado/desligado conforme a noite).")]
        [SerializeField] private GameObject laneGuards;
        [SerializeField] private RugHazards rugHazards;
        [SerializeField] private GridManager grid;
        [Tooltip("Começa a partida direto, sem a tela de título (útil para testes).")]
        [SerializeField] private bool skipMenu;

        public GameState State { get; private set; } = GameState.Menu;
        public EnemySpawner Spawner => spawner;
        public Campaign Campaign => campaign;
        /// <summary>Índice (0-based) da noite preparada.</summary>
        public int NightIndex { get; private set; }
        public NightDefinition Night => campaign != null ? campaign.Get(NightIndex) : null;
        public int NightCount => campaign != null ? campaign.Count : 1;
        public bool HasNextNight => NightIndex + 1 < NightCount;

        public event Action<GameState> StateChanged;
        /// <summary>Disparado quando uma noite é preparada (menu ou início da cena).</summary>
        public event Action<int, NightDefinition> NightConfigured;
        /// <summary>Disparado quando a bolsa de brinquedos muda.</summary>
        public event Action<IReadOnlyList<ToyDefinition>> LoadoutChanged;

        // ------------------------------------------------------------------ Bolsa e estrelas

        private readonly List<ToyDefinition> loadout = new List<ToyDefinition>();

        /// <summary>Brinquedos levados para a noite (os cards que aparecem na barra).</summary>
        public IReadOnlyList<ToyDefinition> Loadout => loadout;

        /// <summary>A noite preparada pede para escolher os brinquedos antes de começar?</summary>
        public bool NeedsLoadoutChoice => Night != null && Night.NeedsLoadoutChoice;

        /// <summary>Colunas junto da cama que contam como "perto da cama" para a 3ª estrela.</summary>
        public const int BedZoneColumns = 2;

        /// <summary>Algum Abajur foi quebrado nesta noite (perde a 2ª estrela)?</summary>
        public bool LampLost { get; private set; }
        /// <summary>Algum monstro entrou nas colunas junto da cama (perde a 3ª estrela)?</summary>
        public bool BedZoneBreached { get; private set; }
        /// <summary>Estrelas da última vitória (0 enquanto não venceu).</summary>
        public int StarsEarned { get; private set; }
        /// <summary>A última vitória bateu o recorde de estrelas da noite?</summary>
        public bool NewStarRecord { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            Time.timeScale = 1f;
            GameLayers.ConfigureCollisionMatrix();
            if (spawner == null) spawner = FindAnyObjectByType<EnemySpawner>();
            if (currency == null) currency = FindAnyObjectByType<CurrencyManager>();
            if (placement == null) placement = FindAnyObjectByType<ToyPlacementController>();
            if (moonlight == null) moonlight = FindAnyObjectByType<MoonlightSpawner>();
            if (lightField == null) lightField = FindAnyObjectByType<LightField>();
            if (rugHazards == null) rugHazards = FindAnyObjectByType<RugHazards>();
            if (grid == null) grid = FindAnyObjectByType<GridManager>();
            ToyBase.Broken += OnToyBroken;
        }

        private void OnDestroy()
        {
            ToyBase.Broken -= OnToyBroken;
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            int index = LevelProgress.CurrentNight >= 0 ? LevelProgress.CurrentNight : LevelProgress.UnlockedCount - 1;
            ConfigureNight(index);

            if (skipMenu || LevelProgress.AutoStart)
            {
                LevelProgress.AutoStart = false;
                StartGame();
            }
            else SetState(GameState.Menu);
        }

        // ------------------------------------------------------------------ Noites

        /// <summary>
        /// Prepara a noite "index": ondas, Luz inicial, cards, luar, escuridão do tapete e Ratinhos de Corda.
        /// Só funciona antes da partida começar.
        /// </summary>
        public void ConfigureNight(int index)
        {
            if (State == GameState.Playing) return;
            NightIndex = Mathf.Clamp(index, 0, Mathf.Max(0, NightCount - 1));
            LevelProgress.CurrentNight = NightIndex;

            NightDefinition night = Night;
            if (night != null)
            {
                if (spawner != null) spawner.Configure(night.waves, night.initialDelay);
                if (currency != null) currency.ResetLight(night.startingLight);
                ApplyLoadout(SavedOrDefaultLoadout(night), false);
                if (moonlight != null) moonlight.enabled = night.moonlight;
                if (lightField != null) lightField.Configure(night.litColumns);
                if (laneGuards != null) laneGuards.SetActive(night.laneGuards);
                if (rugHazards != null) rugHazards.Apply(night.blockedCells, night.slipperyLanes);
                else if (grid != null) grid.SetBlockedCells(night.blockedCells);
            }
            NightConfigured?.Invoke(NightIndex, night);
        }

        // ------------------------------------------------------------------ Bolsa de brinquedos

        /// <summary>
        /// Escolhe os brinquedos da noite (tela de preparo). Respeita o número de espaços e a ordem da noite.
        /// A escolha fica salva e volta em "Tentar de Novo". Retorna false se nada válido foi escolhido.
        /// </summary>
        public bool SetLoadout(IEnumerable<ToyDefinition> toys)
        {
            NightDefinition night = Night;
            if (State == GameState.Playing || night == null || toys == null) return false;
            var wanted = new HashSet<ToyDefinition>(toys);
            var chosen = new List<ToyDefinition>();
            foreach (ToyDefinition t in night.toys)
                if (t != null && wanted.Contains(t) && chosen.Count < night.ToySlots) chosen.Add(t);
            if (chosen.Count == 0) return false;

            ApplyLoadout(chosen, true);
            return true;
        }

        private void ApplyLoadout(List<ToyDefinition> toys, bool save)
        {
            loadout.Clear();
            if (toys != null) loadout.AddRange(toys);
            if (placement != null) placement.SetAvailableToys(loadout);

            NightDefinition night = Night;
            if (save && night != null)
            {
                var indexes = new List<int>();
                foreach (ToyDefinition t in loadout) indexes.Add(night.toys.IndexOf(t));
                LevelProgress.SaveLoadout(NightIndex, indexes);
            }
            LoadoutChanged?.Invoke(loadout);
        }

        /// <summary>
        /// A bolsa salva da noite (se ainda válida) ou a sugestão padrão: primeiro os brinquedos que estreiam
        /// nesta noite, depois os outros na ordem dos cards, até encher os espaços.
        /// </summary>
        private List<ToyDefinition> SavedOrDefaultLoadout(NightDefinition night)
        {
            var result = new List<ToyDefinition>();
            int slots = night.ToySlots;

            List<int> saved = LevelProgress.GetLoadout(NightIndex);
            if (saved != null)
            {
                foreach (int i in saved)
                    if (i >= 0 && i < night.toys.Count && night.toys[i] != null && !result.Contains(night.toys[i]) && result.Count < slots)
                        result.Add(night.toys[i]);
                if (result.Count == slots) return SortLikeNight(night, result);
                result.Clear();
            }

            if (campaign != null)
            {
                var seen = new HashSet<ToyDefinition>();
                for (int n = 0; n < NightIndex && n < campaign.Count; n++)
                    foreach (ToyDefinition t in campaign.nights[n].toys) if (t != null) seen.Add(t);
                foreach (ToyDefinition t in night.toys)
                    if (t != null && !seen.Contains(t) && result.Count < slots) result.Add(t);
            }
            foreach (ToyDefinition t in night.toys)
                if (t != null && !result.Contains(t) && result.Count < slots) result.Add(t);
            return SortLikeNight(night, result);
        }

        private static List<ToyDefinition> SortLikeNight(NightDefinition night, List<ToyDefinition> toys)
        {
            toys.Sort((a, b) => night.toys.IndexOf(a).CompareTo(night.toys.IndexOf(b)));
            return toys;
        }

        /// <summary>Começa a noite. Com runWaves = false as ondas não rodam (testes / modo livre).</summary>
        public void StartGame(bool runWaves = true)
        {
            if (State == GameState.Playing) return;
            LampLost = false;
            BedZoneBreached = false;
            StarsEarned = 0;
            NewStarRecord = false;
            SetState(GameState.Playing);
            if (runWaves && spawner != null) spawner.Begin();
        }

        private void Update()
        {
            if (State != GameState.Playing) return;
            if (!BedZoneBreached) CheckBedZone();
            if (spawner == null) return;
            if (spawner.AllWavesSpawned && EnemyBase.Active.Count == 0)
                Win();
        }

        /// <summary>3ª estrela: algum monstro vivo entrou nas colunas junto da cama?</summary>
        private void CheckBedZone()
        {
            GridManager g = grid != null ? grid : GridManager.Instance;
            if (g == null) return;
            IReadOnlyList<EnemyBase> enemies = EnemyBase.Active;
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyBase e = enemies[i];
                if (e != null && e.IsAlive && g.ColumnAt(e.transform.position.x) < BedZoneColumns)
                {
                    BedZoneBreached = true;
                    return;
                }
            }
        }

        private void OnToyBroken(ToyBase toy)
        {
            if (State == GameState.Playing && toy is ToyLamp) LampLost = true;
        }

        /// <summary>Estrelas que a noite daria agora (1 por vencer + as condições ainda de pé).</summary>
        public int CountStars() => 1 + (LampLost ? 0 : 1) + (BedZoneBreached ? 0 : 1);

        public void EnemyReachedBed(EnemyBase enemy)
        {
            if (State != GameState.Playing) return;
            Debug.Log($"[SleepDefenders] {enemy.DisplayName} alcançou a cama na faixa {enemy.Lane}.");
            SetState(GameState.Lost);
            if (spawner != null) spawner.Stop();
            Sfx.Play(SfxType.Lose);
        }

        public void Win()
        {
            if (State != GameState.Playing) return;
            // Vencer a noite N libera a noite N + 1 (o contador guarda quantas estão liberadas).
            LevelProgress.UnlockUpTo(NightIndex + 2);
            CheckBedZone();
            StarsEarned = CountStars();
            NewStarRecord = LevelProgress.RecordStars(NightIndex, StarsEarned);
            SetState(GameState.Won);
            Sfx.Play(SfxType.Win);
        }

        // ------------------------------------------------------------------ Navegação (recarrega a cena)

        /// <summary>Joga a mesma noite de novo, sem passar pelo menu.</summary>
        public void Restart() => Reload(NightIndex, true);

        /// <summary>
        /// Vai para a próxima noite. Se ela pede para escolher os brinquedos, abre direto a tela de preparo;
        /// senão começa sem passar pelo menu.
        /// </summary>
        public void NextNight()
        {
            int next = HasNextNight ? NightIndex + 1 : NightIndex;
            NightDefinition night = campaign != null ? campaign.Get(next) : null;
            bool choose = night != null && night.NeedsLoadoutChoice;
            LevelProgress.OpenLoadout = choose;
            Reload(next, !choose);
        }

        /// <summary>Volta ao menu de noites.</summary>
        public void BackToMenu() => Reload(NightIndex, false);

        private static void Reload(int nightIndex, bool autoStart)
        {
            LevelProgress.CurrentNight = nightIndex;
            LevelProgress.AutoStart = autoStart;
            Time.timeScale = 1f;

            Scene scene = SceneManager.GetActiveScene();
            if (scene.buildIndex >= 0)
            {
                SceneManager.LoadScene(scene.buildIndex);
                return;
            }
#if UNITY_EDITOR
            // A cena não está na lista do Build Profile: no Editor dá para recarregar pelo caminho.
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(scene.path, new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene(0);
#endif
        }

        private void SetState(GameState newState)
        {
            State = newState;
            StateChanged?.Invoke(newState);
        }
    }
}
