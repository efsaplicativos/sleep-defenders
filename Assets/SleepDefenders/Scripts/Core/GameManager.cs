using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SleepDefenders
{
    public enum GameState { Menu, Playing, Won, Lost }

    /// <summary>
    /// Estado geral da partida: menu inicial, jogando, vitória (todas as ondas da noite derrotadas)
    /// e derrota (um monstro alcançou a cama).
    ///
    /// Também aplica a noite escolhida da Campanha: ondas do spawner, Luz inicial, cards liberados
    /// e luar. Vencer uma noite libera a próxima (LevelProgress).
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
        }

        private void OnDestroy()
        {
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
                if (placement != null) placement.SetAvailableToys(night.toys);
                if (moonlight != null) moonlight.enabled = night.moonlight;
                if (lightField != null) lightField.Configure(night.litColumns);
                if (laneGuards != null) laneGuards.SetActive(night.laneGuards);
            }
            NightConfigured?.Invoke(NightIndex, night);
        }

        /// <summary>Começa a noite. Com runWaves = false as ondas não rodam (testes / modo livre).</summary>
        public void StartGame(bool runWaves = true)
        {
            if (State == GameState.Playing) return;
            SetState(GameState.Playing);
            if (runWaves && spawner != null) spawner.Begin();
        }

        private void Update()
        {
            if (State != GameState.Playing || spawner == null) return;
            if (spawner.AllWavesSpawned && EnemyBase.Active.Count == 0)
                Win();
        }

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
            SetState(GameState.Won);
            Sfx.Play(SfxType.Win);
        }

        // ------------------------------------------------------------------ Navegação (recarrega a cena)

        /// <summary>Joga a mesma noite de novo, sem passar pelo menu.</summary>
        public void Restart() => Reload(NightIndex, true);

        /// <summary>Vai para a próxima noite, sem passar pelo menu.</summary>
        public void NextNight() => Reload(HasNextNight ? NightIndex + 1 : NightIndex, true);

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
