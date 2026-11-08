using UnityEngine;
using UnityEngine.UI;

namespace SleepDefenders
{
    /// <summary>Botão da ferramenta "Guardar brinquedo" (remove um brinquedo do tapete, sem reembolso).</summary>
    public class RemoveToolButton : MonoBehaviour
    {
        [SerializeField] private Button button;
        [SerializeField] private Image selectionFrame;

        private ToyPlacementController controller;

        public void SetController(ToyPlacementController owner) => controller = owner;

        private void Awake()
        {
            if (button != null) button.onClick.AddListener(() => { if (controller != null) controller.ToggleRemoveMode(); });
            SetSelected(false);
        }

        public void SetSelected(bool value)
        {
            if (selectionFrame != null) selectionFrame.enabled = value;
        }
    }
}
