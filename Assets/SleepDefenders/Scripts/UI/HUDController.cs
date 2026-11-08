using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SleepDefenders
{
    /// <summary>
    /// Telas e avisos: título, HUD de ondas (texto + barra de progresso da noite),
    /// faixas de aviso ("Uma onda enorme se aproxima!") e painéis de vitória/derrota.
    /// </summary>
    public class HUDController : MonoBehaviour
    {
        [Header("Painéis")]
        [SerializeField] private GameObject menuPanel;
        [SerializeField] private GameObject hudRoot;
        [SerializeField] private GameObject gameOverPanel;
        [SerializeField] private GameObject victoryPanel;

        [Header("Botões")]
        [SerializeField] private Button startButton;
        [SerializeField] private Button retryButton;
        [SerializeField] private Button playAgainButton;

        [Header("Ondas")]
        [SerializeField] private TMP_Text waveLabel;
        [SerializeField] private Image nightProgressFill;

        [Header("Aviso central")]
        [SerializeField] private TMP_Text bannerText;
        [SerializeField] private CanvasGroup bannerGroup;

        private GameManager game;
        private EnemySpawner spawner;
        private Coroutine bannerRoutine;

        private void Awake()
        {
            if (startButton != null) startButton.onClick.AddListener(() => { if (GameManager.Instance != null) GameManager.Instance.StartGame(); });
            if (retryButton != null) retryButton.onClick.AddListener(() => { if (GameManager.Instance != null) GameManager.Instance.Restart(); });
            if (playAgainButton != null) playAgainButton.onClick.AddListener(() => { if (GameManager.Instance != null) GameManager.Instance.Restart(); });
            if (bannerGroup != null) bannerGroup.alpha = 0f;
        }

        private void Start()
        {
            game = GameManager.Instance;
            spawner = game != null ? game.Spawner : FindAnyObjectByType<EnemySpawner>();
            if (game != null)
            {
                game.StateChanged += OnStateChanged;
                OnStateChanged(game.State);
            }
            if (spawner != null) spawner.WaveStarted += OnWaveStarted;
        }

        private void OnDestroy()
        {
            if (game != null) game.StateChanged -= OnStateChanged;
            if (spawner != null) spawner.WaveStarted -= OnWaveStarted;
        }

        private void Update()
        {
            if (spawner == null) return;
            if (nightProgressFill != null) nightProgressFill.fillAmount = spawner.Progress;
            if (waveLabel != null)
            {
                waveLabel.text = spawner.CurrentWaveIndex < 0
                    ? "Prepare-se..."
                    : $"Onda {spawner.CurrentWaveIndex + 1}/{spawner.TotalWaves}";
            }
        }

        private void OnStateChanged(GameState state)
        {
            if (menuPanel != null) menuPanel.SetActive(state == GameState.Menu);
            if (hudRoot != null) hudRoot.SetActive(state != GameState.Menu);
            if (gameOverPanel != null) gameOverPanel.SetActive(state == GameState.Lost);
            if (victoryPanel != null) victoryPanel.SetActive(state == GameState.Won);

            if (state == GameState.Playing)
                ShowBanner("Coloque Abajures para juntar Luz!\nClique nos orbes brilhantes para coletar.", 4f);
        }

        private void OnWaveStarted(int index, Wave wave)
        {
            Sfx.Play(SfxType.WaveStart);
            if (index == spawner.TotalWaves - 1) ShowBanner("ONDA FINAL!", 3f);
            else if (wave.isBigWave) ShowBanner("Uma onda enorme de monstros se aproxima!", 3f);
            else if (index == 0) ShowBanner("Os monstros estão chegando!", 2.5f);
            else ShowBanner($"Onda {index + 1}", 2f);
        }

        public void ShowBanner(string message, float seconds)
        {
            if (bannerText == null || bannerGroup == null) return;
            if (bannerRoutine != null) StopCoroutine(bannerRoutine);
            bannerRoutine = StartCoroutine(BannerRoutine(message, seconds));
        }

        private IEnumerator BannerRoutine(string message, float seconds)
        {
            bannerText.text = message;
            RectTransform rt = bannerGroup.transform as RectTransform;
            for (float t = 0f; t < 0.25f; t += Time.unscaledDeltaTime)
            {
                float k = t / 0.25f;
                bannerGroup.alpha = k;
                if (rt != null) rt.localScale = Vector3.one * Mathf.Lerp(1.4f, 1f, k);
                yield return null;
            }
            bannerGroup.alpha = 1f;
            if (rt != null) rt.localScale = Vector3.one;
            yield return new WaitForSecondsRealtime(seconds);
            for (float t = 0f; t < 0.4f; t += Time.unscaledDeltaTime)
            {
                bannerGroup.alpha = 1f - t / 0.4f;
                yield return null;
            }
            bannerGroup.alpha = 0f;
            bannerRoutine = null;
        }
    }
}
