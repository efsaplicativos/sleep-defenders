using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SleepDefenders
{
    /// <summary>
    /// Card de um brinquedo na barra superior: ícone, nome, custo, recarga e estado (selecionado / sem Luz).
    /// </summary>
    public class ToyCardUI : MonoBehaviour
    {
        [SerializeField] private ToyDefinition definition;

        [Header("Referências de UI")]
        [SerializeField] private Button button;
        [SerializeField] private Image icon;
        [SerializeField] private Image cooldownOverlay;
        [SerializeField] private Image selectionFrame;
        [SerializeField] private TMP_Text costLabel;
        [SerializeField] private TMP_Text nameLabel;
        [SerializeField] private TMP_Text hotkeyLabel;

        [Header("Cores")]
        [SerializeField] private Color unaffordableTint = new Color(0.45f, 0.45f, 0.55f, 1f);

        private ToyPlacementController controller;
        private float rechargeRemaining;
        private float rechargeTotal = 1f;
        private bool selected;
        private Vector3 baseScale = Vector3.one;
        private Coroutine shake;

        public ToyDefinition Definition => definition;
        public bool IsReady => rechargeRemaining <= 0f;
        public bool CanAfford => CurrencyManager.Instance != null && definition != null && CurrencyManager.Instance.CanAfford(definition.CurrentCost);
        /// <summary>Preço mostrado no card agora (sobe com cada Abajur no tapete).</summary>
        public int CurrentCost => definition != null ? definition.CurrentCost : 0;
        private int shownCost = -1;
        public float RechargeRemaining => rechargeRemaining;

        public void SetController(ToyPlacementController owner) => controller = owner;

        private void Awake()
        {
            baseScale = transform.localScale;
            if (button != null) button.onClick.AddListener(() => { if (controller != null) controller.ToggleCard(this); });
        }

        private void Start()
        {
            ApplyDefinition();
            if (definition != null && definition.startsRecharging) StartRecharge();
            SetSelected(false);
        }

        public void ApplyDefinition()
        {
            if (definition == null) return;
            if (icon != null && definition.icon != null) icon.sprite = definition.icon;
            RefreshCost();
            if (nameLabel != null) nameLabel.text = definition.DisplayName;
        }

        /// <summary>Atualiza o preço do card só quando ele muda (evita criar strings todo frame).</summary>
        private void RefreshCost()
        {
            int cost = CurrentCost;
            if (cost == shownCost || costLabel == null) return;
            shownCost = cost;
            costLabel.text = cost.ToString();
        }

        public void SetHotkey(string key)
        {
            if (hotkeyLabel != null) hotkeyLabel.text = key;
        }

        public void StartRecharge()
        {
            if (definition == null) return;
            rechargeTotal = Mathf.Max(0.01f, definition.rechargeTime);
            rechargeRemaining = definition.rechargeTime;
        }

        public void SetSelected(bool value)
        {
            selected = value;
            if (selectionFrame != null) selectionFrame.enabled = value;
        }

        public void ShakeDenied()
        {
            if (!isActiveAndEnabled) return;
            if (shake != null) StopCoroutine(shake);
            shake = StartCoroutine(ShakeRoutine());
        }

        private void Update()
        {
            if (rechargeRemaining > 0f && GameManager.IsPlaying)
                rechargeRemaining = Mathf.Max(0f, rechargeRemaining - Time.deltaTime);

            if (cooldownOverlay != null)
                cooldownOverlay.fillAmount = rechargeRemaining > 0f ? rechargeRemaining / rechargeTotal : 0f;

            RefreshCost();
            bool usable = IsReady && CanAfford;
            if (icon != null) icon.color = usable ? Color.white : unaffordableTint;
            if (costLabel != null) costLabel.color = CanAfford ? Color.white : new Color(1f, 0.55f, 0.55f);

            // O card selecionado fica levemente maior.
            float target = selected ? 1.08f : 1f;
            if (shake == null)
                transform.localScale = Vector3.Lerp(transform.localScale, baseScale * target, Time.unscaledDeltaTime * 12f);
        }

        private IEnumerator ShakeRoutine()
        {
            Vector3 start = transform.localPosition;
            for (float t = 0f; t < 0.25f; t += Time.unscaledDeltaTime)
            {
                transform.localPosition = start + Vector3.right * (Mathf.Sin(t * 70f) * 6f * (1f - t / 0.25f));
                yield return null;
            }
            transform.localPosition = start;
            shake = null;
        }
    }
}
