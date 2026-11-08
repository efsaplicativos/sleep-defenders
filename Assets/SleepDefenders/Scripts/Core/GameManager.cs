using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SleepDefenders
{
    public enum GameState { Menu, Playing, Won, Lost }

    /// <summary>
    /// Estado geral da partida: menu inicial, jogando, vitória (todas as ondas derrotadas)
    /// e derrota (um monstro alcançou a cama).
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }
        public static bool IsPlaying => Instance != null && Instance.State == GameState.Playing;

        [SerializeField] private EnemySpawner spawner;
        [Tooltip("Começa a partida direto, sem a tela de título (útil para testes).")]
        [SerializeField] private bool skipMenu;

        public GameState State { get; private set; } = GameState.Menu;
        public EnemySpawner Spawner => spawner;

        public event Action<GameState> StateChanged;

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
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            if (skipMenu) StartGame();
            else SetState(GameState.Menu);
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
            SetState(GameState.Won);
            Sfx.Play(SfxType.Win);
        }

        public void Restart()
        {
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
