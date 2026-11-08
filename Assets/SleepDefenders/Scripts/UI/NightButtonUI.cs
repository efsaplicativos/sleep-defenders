using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SleepDefenders
{
    /// <summary>
    /// Card de uma noite no menu: "Noite N" + nome temático. Noites bloqueadas ficam apagadas,
    /// a selecionada ganha moldura dourada e as concluídas mostram um selo dourado.
    /// </summary>
    public class NightButtonUI : MonoBehaviour
    {
        [SerializeField] private Button button;
        [SerializeField] private CanvasGroup group;
        [SerializeField] private TMP_Text titleLabel;
        [SerializeField] private TMP_Text subtitleLabel;
        [SerializeField] private Image selectionFrame;
        [SerializeField] private Image completedBadge;

        public Button Button => button;

        public void Show(NightDefinition night, bool unlocked, bool selected, bool completed)
        {
            gameObject.SetActive(night != null);
            if (night == null) return;

            if (titleLabel != null) titleLabel.text = night.title;
            if (subtitleLabel != null) subtitleLabel.text = unlocked ? night.subtitle : "Bloqueada";
            if (button != null) button.interactable = unlocked;
            if (group != null) group.alpha = unlocked ? 1f : 0.45f;
            if (selectionFrame != null) selectionFrame.enabled = selected && unlocked;
            if (completedBadge != null) completedBadge.enabled = completed;
        }
    }
}
