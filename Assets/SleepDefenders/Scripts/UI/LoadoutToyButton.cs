using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SleepDefenders
{
    /// <summary>
    /// Card de brinquedo na tela de preparo: clicar coloca ou tira o brinquedo da bolsa.
    /// Os escolhidos ganham moldura dourada; os outros ficam apagados.
    /// </summary>
    public class LoadoutToyButton : MonoBehaviour
    {
        [SerializeField] private Button button;
        [SerializeField] private CanvasGroup group;
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text nameLabel;
        [SerializeField] private TMP_Text costLabel;
        [SerializeField] private Image selectionFrame;
        [Tooltip("Etiqueta \"NOVO!\" para brinquedos que estreiam na noite.")]
        [SerializeField] private GameObject newTag;

        private Vector3 baseScale = Vector3.one;
        private float punch;

        public ToyDefinition Definition { get; private set; }
        public bool Selected { get; private set; }

        public event Action<LoadoutToyButton> Clicked;

        private void Awake()
        {
            baseScale = transform.localScale;
            if (button != null) button.onClick.AddListener(() => Clicked?.Invoke(this));
        }

        public void Show(ToyDefinition def, bool selected, bool isNew)
        {
            Definition = def;
            gameObject.SetActive(def != null);
            if (def == null) return;
            if (icon != null) icon.sprite = def.icon;
            if (nameLabel != null) nameLabel.text = def.DisplayName;
            if (costLabel != null) costLabel.text = def.Cost.ToString();
            if (newTag != null) newTag.SetActive(isNew);
            SetSelected(selected);
        }

        public void SetSelected(bool selected)
        {
            Selected = selected;
            if (selectionFrame != null) selectionFrame.enabled = selected;
            if (group != null) group.alpha = selected ? 1f : 0.55f;
            punch = 1f;
        }

        private void Update()
        {
            if (punch <= 0f) return;
            punch = Mathf.Max(0f, punch - Time.unscaledDeltaTime * 5f);
            transform.localScale = baseScale * (1f + Mathf.Sin(punch * Mathf.PI) * 0.06f);
        }
    }
}
