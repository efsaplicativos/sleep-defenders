using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SleepDefenders
{
    /// <summary>
    /// Telas e avisos:
    ///   - Menu inicial com a seleção de noites (cards "Noite 1" ... "Noite 5", com estrelas) e a descrição da escolhida.
    ///   - Tela de preparo (bolsa de brinquedos + monstros da noite) quando a noite pede escolha.
    ///   - HUD de ondas ("Noite 2 • Onda 3/5" + barra de progresso da noite).
    ///   - Faixas de aviso ("Uma onda enorme se aproxima!", ratinho de corda, novidades da noite).
    ///   - Painéis de vitória (estrelas + Próxima Noite / Menu) e derrota (Tentar de Novo / Menu).
    /// </summary>
    public class HUDController : MonoBehaviour
    {
        [Header("Painéis")]
        [SerializeField] private GameObject menuPanel;
        [SerializeField] private GameObject hudRoot;
        [SerializeField] private GameObject gameOverPanel;
        [SerializeField] private GameObject victoryPanel;

        [Header("Menu de noites")]
        [SerializeField] private List<NightButtonUI> nightButtons = new List<NightButtonUI>();
        [SerializeField] private TMP_Text nightDescription;
        [SerializeField] private Button startButton;
        [SerializeField] private TMP_Text startButtonLabel;
        [Tooltip("Tela de preparo (escolha dos brinquedos). Sem ela, a noite começa com a bolsa padrão.")]
        [SerializeField] private LoadoutPanelUI loadoutPanel;

        [Header("Resultado")]
        [SerializeField] private Button retryButton;
        [SerializeField] private Button loseMenuButton;
        [SerializeField] private TMP_Text victoryTitle;
        [SerializeField] private TMP_Text victorySubtitle;
        [SerializeField] private Button nextNightButton;
        [SerializeField] private Button winMenuButton;
        [SerializeField] private List<Image> victoryStars = new List<Image>();
        [SerializeField] private TMP_Text victoryCriteria;
        [SerializeField] private Color starEarnedColor = new Color(1f, 0.83f, 0.3f, 1f);
        [SerializeField] private Color starMissingColor = new Color(1f, 1f, 1f, 0.18f);

        [Header("Ondas")]
        [SerializeField] private TMP_Text waveLabel;
        [SerializeField] private Image nightProgressFill;

        [Header("Aviso central")]
        [SerializeField] private TMP_Text bannerText;
        [SerializeField] private CanvasGroup bannerGroup;

        private GameManager game;
        private EnemySpawner spawner;
        private LightField lightField;
        private Coroutine bannerRoutine;

        private void Awake()
        {
            if (startButton != null) startButton.onClick.AddListener(OnStartPressed);
            if (retryButton != null) retryButton.onClick.AddListener(() => { if (GameManager.Instance != null) GameManager.Instance.Restart(); });
            if (nextNightButton != null) nextNightButton.onClick.AddListener(() => { if (GameManager.Instance != null) GameManager.Instance.NextNight(); });
            if (loseMenuButton != null) loseMenuButton.onClick.AddListener(() => { if (GameManager.Instance != null) GameManager.Instance.BackToMenu(); });
            if (winMenuButton != null) winMenuButton.onClick.AddListener(() => { if (GameManager.Instance != null) GameManager.Instance.BackToMenu(); });

            for (int i = 0; i < nightButtons.Count; i++)
            {
                int index = i; // captura para o lambda
                if (nightButtons[i] != null && nightButtons[i].Button != null)
                    nightButtons[i].Button.onClick.AddListener(() => SelectNight(index));
            }
            if (bannerGroup != null) bannerGroup.alpha = 0f;
        }

        private void Start()
        {
            game = GameManager.Instance;
            spawner = game != null ? game.Spawner : FindAnyObjectByType<EnemySpawner>();
            if (game != null)
            {
                game.StateChanged += OnStateChanged;
                game.NightConfigured += OnNightConfigured;
                RefreshMenu();
                OnStateChanged(game.State);
            }
            if (spawner != null) spawner.WaveStarted += OnWaveStarted;
            LaneGuard.Triggered += OnLaneGuardTriggered;
            lightField = LightField.Instance;
            if (lightField != null) lightField.BlackoutChanged += OnBlackoutChanged;
        }

        private void OnDestroy()
        {
            if (game != null)
            {
                game.StateChanged -= OnStateChanged;
                game.NightConfigured -= OnNightConfigured;
            }
            if (spawner != null) spawner.WaveStarted -= OnWaveStarted;
            LaneGuard.Triggered -= OnLaneGuardTriggered;
            if (lightField != null) lightField.BlackoutChanged -= OnBlackoutChanged;
        }

        private void Update()
        {
            if (spawner == null) return;
            if (nightProgressFill != null) nightProgressFill.fillAmount = spawner.Progress;
            if (waveLabel != null)
            {
                string night = game != null && game.Night != null ? game.Night.title + "  •  " : "";
                waveLabel.text = spawner.CurrentWaveIndex < 0
                    ? night + "Prepare-se..."
                    : $"{night}Onda {spawner.CurrentWaveIndex + 1}/{spawner.TotalWaves}";
            }
        }

        // ------------------------------------------------------------------ Menu

        /// <summary>Botão "Começar": abre a tela de preparo se a noite pede escolha de brinquedos.</summary>
        public void OnStartPressed()
        {
            if (game == null) game = GameManager.Instance;
            if (game == null) return;
            if (game.NeedsLoadoutChoice && loadoutPanel != null)
            {
                Sfx.Play(SfxType.Select);
                loadoutPanel.Open();
            }
            else game.StartGame();
        }

        public LoadoutPanelUI LoadoutPanel => loadoutPanel;

        private void SelectNight(int index)
        {
            if (game == null || !LevelProgress.IsUnlocked(index)) return;
            Sfx.Play(SfxType.Select);
            game.ConfigureNight(index);
        }

        private void OnNightConfigured(int index, NightDefinition night) => RefreshMenu();

        private void RefreshMenu()
        {
            if (game == null) return;
            Campaign campaign = game.Campaign;
            for (int i = 0; i < nightButtons.Count; i++)
            {
                if (nightButtons[i] == null) continue;
                NightDefinition night = campaign != null && i < campaign.Count ? campaign.nights[i] : null;
                nightButtons[i].Show(night, LevelProgress.IsUnlocked(i), i == game.NightIndex, LevelProgress.IsCompleted(i), LevelProgress.GetStars(i));
            }

            NightDefinition current = game.Night;
            if (startButtonLabel != null)
                startButtonLabel.text = current == null ? "Começar a Noite"
                    : (current.NeedsLoadoutChoice && loadoutPanel != null ? "Preparar " : "Começar ") + current.title;
            if (nightDescription != null)
                nightDescription.text = current != null ? DescribeNight(campaign, game.NightIndex, current) : "";
        }

        private static string DescribeNight(Campaign campaign, int index, NightDefinition night)
        {
            string text = $"<color=#FFD54A>{night.title}: {night.subtitle}</color>\n{night.tip}";
            List<string> news = campaign != null ? campaign.Novelties(index) : null;
            if (news != null && news.Count > 0 && index > 0)
                text += $"\n<color=#B9A6FF>Novidades: {string.Join(", ", news)}</color>";
            if (night.NeedsLoadoutChoice)
                text += $"\n<color=#9EE6FF>Bolsa: leve {night.ToySlots} de {night.toys.Count} brinquedos.</color>";
            int stars = LevelProgress.GetStars(index);
            if (stars > 0) text += $"  <color=#FFD54A>Recorde: {stars} de {LevelProgress.MaxStars} estrelas</color>";
            return text;
        }

        // ------------------------------------------------------------------ Estado do jogo

        private void OnStateChanged(GameState state)
        {
            if (menuPanel != null) menuPanel.SetActive(state == GameState.Menu);
            if (hudRoot != null) hudRoot.SetActive(state != GameState.Menu);
            if (gameOverPanel != null) gameOverPanel.SetActive(state == GameState.Lost);
            if (victoryPanel != null) victoryPanel.SetActive(state == GameState.Won);

            if (state != GameState.Menu && loadoutPanel != null) loadoutPanel.Close();

            switch (state)
            {
                case GameState.Menu:
                    RefreshMenu();
                    // "Próxima Noite" numa noite com escolha: a tela de preparo já abre sozinha.
                    if (LevelProgress.OpenLoadout)
                    {
                        LevelProgress.OpenLoadout = false;
                        if (loadoutPanel != null && game != null && game.NeedsLoadoutChoice) loadoutPanel.Open();
                    }
                    break;
                case GameState.Playing:
                    NightDefinition night = game != null ? game.Night : null;
                    if (night != null)
                        ShowBanner($"{night.title}: {night.subtitle}\n<size=70%><color=#FFFFFF>{night.tip}</color></size>", 5f);
                    else
                        ShowBanner("Coloque Abajures para juntar Luz!\nClique nos orbes brilhantes para coletar.", 4f);
                    break;
                case GameState.Won:
                    ShowVictory();
                    break;
            }
        }

        private void ShowVictory()
        {
            if (game == null) return;
            bool last = !game.HasNextNight;
            NightDefinition night = game.Night;
            string title = night != null ? night.title : "A noite";

            if (victoryTitle != null)
                victoryTitle.text = last ? "Amanheceu! Você venceu todas as noites!" : $"Amanheceu! {title} concluída!";
            if (victorySubtitle != null)
                victorySubtitle.text = last
                    ? "Os brinquedos protegeram o sono da criança a semana inteira."
                    : $"A criança dormiu tranquila. {game.Campaign.Get(game.NightIndex + 1).title} liberada!";
            if (nextNightButton != null) nextNightButton.gameObject.SetActive(!last);
            ShowStars();
        }

        private void ShowStars()
        {
            if (game == null) return;
            int earned = game.StarsEarned;
            for (int i = 0; i < victoryStars.Count; i++)
                if (victoryStars[i] != null) victoryStars[i].color = i < earned ? starEarnedColor : starMissingColor;
            if (victoryStars.Count > 0) StartCoroutine(PopStars(earned));

            if (victoryCriteria != null)
            {
                string Line(bool ok, string text) =>
                    ok ? $"<color=#FFD54A>{text}</color>" : $"<color=#8F87AA>{text}  (faltou)</color>";
                string record = game.NewStarRecord && game.StarsEarned > 0 ? "\n<color=#9EE6FF>Novo recorde!</color>" : "";
                victoryCriteria.text =
                    Line(true, "1. Venceu a noite") + "\n" +
                    Line(!game.LampLost, "2. Nenhum Abajur quebrado") + "\n" +
                    Line(!game.BedZoneBreached, "3. Nenhum monstro chegou perto da cama") + record;
            }
        }

        private IEnumerator PopStars(int earned)
        {
            foreach (Image star in victoryStars)
                if (star != null) star.transform.localScale = Vector3.zero;
            for (int i = 0; i < victoryStars.Count; i++)
            {
                Image star = victoryStars[i];
                if (star == null) continue;
                yield return new WaitForSecondsRealtime(0.25f);
                if (i < earned) Sfx.Play(SfxType.Collect, 0.8f);
                for (float t = 0f; t < 0.3f; t += Time.unscaledDeltaTime)
                {
                    float k = t / 0.3f;
                    star.transform.localScale = Vector3.one * (1f + Mathf.Sin(k * Mathf.PI) * 0.35f) * k;
                    yield return null;
                }
                star.transform.localScale = Vector3.one;
            }
        }

        private void OnWaveStarted(int index, Wave wave)
        {
            Sfx.Play(SfxType.WaveStart);
            string message;
            float seconds;
            if (index == spawner.TotalWaves - 1) { message = "ONDA FINAL!"; seconds = 3f; }
            else if (wave.isBigWave) { message = "Uma onda enorme de monstros se aproxima!"; seconds = 3f; }
            else if (index == 0) { message = "Os monstros estão chegando!"; seconds = 2.5f; }
            else { message = $"Onda {index + 1}"; seconds = 2f; }
            // O apagão desta onda já disparou (LightField): junta os dois avisos num só.
            if (wave.blackout) message += "\n<size=70%><color=#FFB3B3>APAGÃO! Os abajures se apagaram.</color></size>";
            ShowBanner(message, seconds);
        }

        private void OnBlackoutChanged(bool started)
        {
            if (started)
                ShowBanner("APAGÃO!\n<size=70%>Os abajures se apagaram por alguns segundos.</size>", 2.5f);
            else
                ShowBanner("As luzes voltaram!", 1.5f);
        }

        private void OnLaneGuardTriggered(LaneGuard guard) =>
            ShowBanner("O Ratinho de Corda salvou a faixa!\n<size=70%>Ele só funciona uma vez por noite.</size>", 2.5f);

        // ------------------------------------------------------------------ Aviso central

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
