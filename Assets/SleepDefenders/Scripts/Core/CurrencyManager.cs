using System;
using System.Collections;
using TMPro;
using UnityEngine;

namespace SleepDefenders
{
    /// <summary>
    /// Controla o recurso "Luz". Abajures e raios de luar adicionam Luz; posicionar brinquedos gasta.
    /// A UI (TMP_Text) é atualizada sempre que o valor muda e também via evento OnLightChanged.
    /// </summary>
    public class CurrencyManager : MonoBehaviour
    {
        public static CurrencyManager Instance { get; private set; }

        [Header("Valores")]
        [SerializeField] private int startingLight = 150;

        [Header("UI")]
        [Tooltip("Texto que mostra a quantidade atual de Luz.")]
        [SerializeField] private TMP_Text lightLabel;
        [Tooltip("Ícone para onde os orbes de luz coletados voam.")]
        [SerializeField] private RectTransform collectTarget;
        [SerializeField] private Color normalColor = Color.white;
        [SerializeField] private Color notEnoughColor = new Color(1f, 0.35f, 0.35f);

        public int CurrentLight { get; private set; }

        /// <summary>Disparado com o novo total sempre que a Luz muda.</summary>
        public event Action<int> OnLightChanged;

        private Coroutine feedbackRoutine;
        private Vector3 labelBaseScale = Vector3.one;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            CurrentLight = startingLight;
            if (lightLabel != null) labelBaseScale = lightLabel.rectTransform.localScale;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Start() => RefreshUI();

        public bool CanAfford(int cost) => CurrentLight >= cost;

        /// <summary>Adiciona Luz (abajur, luar ou qualquer outra fonte).</summary>
        public void AddLight(int amount)
        {
            if (amount <= 0) return;
            CurrentLight += amount;
            RefreshUI();
            PlayFeedback(1.25f, normalColor);
        }

        /// <summary>Tenta gastar Luz. Retorna false (e pisca o contador em vermelho) se não houver o suficiente.</summary>
        public bool TrySpendLight(int amount)
        {
            if (amount < 0) return false;
            if (!CanAfford(amount))
            {
                PlayFeedback(1.15f, notEnoughColor);
                return false;
            }
            CurrentLight -= amount;
            RefreshUI();
            return true;
        }

        /// <summary>
        /// Ponto do mundo 3D, na frente da câmera, que corresponde ao ícone de Luz da UI.
        /// Usado pelos orbes para "voar" até o contador.
        /// </summary>
        public Vector3 GetCollectWorldPoint(Camera cam, float distanceFromCamera = 4f)
        {
            if (cam == null) return Vector3.zero;
            Vector2 screen = collectTarget != null
                ? RectTransformUtility.WorldToScreenPoint(null, collectTarget.position)
                : new Vector2(80f, Screen.height - 60f);
            return cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, distanceFromCamera));
        }

        private void RefreshUI()
        {
            if (lightLabel != null) lightLabel.text = CurrentLight.ToString();
            OnLightChanged?.Invoke(CurrentLight);
        }

        private void PlayFeedback(float punchScale, Color color)
        {
            if (lightLabel == null || !isActiveAndEnabled) return;
            if (feedbackRoutine != null) StopCoroutine(feedbackRoutine);
            feedbackRoutine = StartCoroutine(FeedbackRoutine(punchScale, color));
        }

        private IEnumerator FeedbackRoutine(float punchScale, Color color)
        {
            const float duration = 0.3f;
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                float k = Mathf.Sin(t / duration * Mathf.PI);
                lightLabel.rectTransform.localScale = labelBaseScale * Mathf.Lerp(1f, punchScale, k);
                lightLabel.color = Color.Lerp(normalColor, color, k);
                yield return null;
            }
            lightLabel.rectTransform.localScale = labelBaseScale;
            lightLabel.color = normalColor;
            feedbackRoutine = null;
        }
    }
}
