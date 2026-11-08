using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SleepDefenders
{
    /// <summary>
    /// Card de uma noite no menu: "Noite N" + nome temático + as estrelas conquistadas (até 3).
    /// Noites bloqueadas ficam apagadas e sem estrelas; a selecionada ganha moldura dourada.
    /// </summary>
    public class NightButtonUI : MonoBehaviour
    {
        [SerializeField] private Button button;
        [SerializeField] private CanvasGroup group;
        [SerializeField] private TMP_Text titleLabel;
        [SerializeField] private TMP_Text subtitleLabel;
        [SerializeField] private Image selectionFrame;
        [Tooltip("Selo de noite concluída (opcional, usado quando não há estrelas).")]
        [SerializeField] private Image completedBadge;
        [SerializeField] private List<Image> stars = new List<Image>();
        [SerializeField] private Color starEarnedColor = new Color(1f, 0.83f, 0.3f, 1f);
        [SerializeField] private Color starMissingColor = new Color(1f, 1f, 1f, 0.22f);

        public Button Button => button;
        /// <summary>Estrelas mostradas agora (para testes).</summary>
        public int ShownStars { get; private set; }

        public void Show(NightDefinition night, bool unlocked, bool selected, bool completed, int earnedStars = 0)
        {
            gameObject.SetActive(night != null);
            if (night == null) return;

            if (titleLabel != null) titleLabel.text = night.title;
            if (subtitleLabel != null) subtitleLabel.text = unlocked ? night.subtitle : "Bloqueada";
            if (button != null) button.interactable = unlocked;
            if (group != null) group.alpha = unlocked ? 1f : 0.45f;
            if (selectionFrame != null) selectionFrame.enabled = selected && unlocked;
            if (completedBadge != null) completedBadge.enabled = completed && stars.Count == 0;

            ShownStars = unlocked ? Mathf.Clamp(earnedStars, 0, stars.Count) : 0;
            for (int i = 0; i < stars.Count; i++)
            {
                if (stars[i] == null) continue;
                stars[i].enabled = unlocked;
                stars[i].color = i < ShownStars ? starEarnedColor : starMissingColor;
            }
        }
    }
}
