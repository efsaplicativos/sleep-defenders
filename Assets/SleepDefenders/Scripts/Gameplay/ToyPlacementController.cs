using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SleepDefenders
{
    /// <summary>
    /// Liga a barra de cards à grade:
    ///   1. O jogador escolhe um card (clique ou teclas 1-4) ou a ferramenta de remover (tecla R).
    ///   2. Ao passar o mouse no tapete, o GridManager destaca a célula (verde = pode, vermelho = não pode).
    ///   3. Clique: coleta orbes de Luz (prioridade) ou posiciona/remove o brinquedo na célula.
    /// Botão direito ou Esc cancelam a seleção. Funciona com mouse e toque (Pointer do Input System).
    /// </summary>
    public class ToyPlacementController : MonoBehaviour
    {
        [SerializeField] private GridManager grid;
        [SerializeField] private Camera cam;
        [SerializeField] private List<ToyCardUI> cards = new List<ToyCardUI>();
        [SerializeField] private RemoveToolButton removeTool;

        private ToyCardUI selectedCard;
        private bool removeMode;
        private readonly List<ToyCardUI> activeCards = new List<ToyCardUI>();

        public ToyCardUI SelectedCard => selectedCard;
        public bool RemoveMode => removeMode;
        /// <summary>Todos os cards da barra (inclusive os ainda não liberados na noite atual).</summary>
        public IReadOnlyList<ToyCardUI> Cards => cards;
        /// <summary>Cards visíveis na noite atual, na ordem dos atalhos 1, 2, 3...</summary>
        public IReadOnlyList<ToyCardUI> ActiveCards => activeCards;

        private void Awake()
        {
            if (grid == null) grid = FindAnyObjectByType<GridManager>();
            if (cam == null) cam = Camera.main;
            foreach (ToyCardUI card in cards)
                if (card != null) card.SetController(this);
            if (removeTool != null) removeTool.SetController(this);
            SetAvailableToys(null);
        }

        /// <summary>
        /// Mostra só os cards dos brinquedos liberados na noite (null ou vazio = todos)
        /// e renumera os atalhos de teclado na ordem em que aparecem.
        /// </summary>
        public void SetAvailableToys(IList<ToyDefinition> toys)
        {
            ClearSelection();
            activeCards.Clear();
            foreach (ToyCardUI card in cards)
            {
                if (card == null) continue;
                bool available = toys == null || toys.Count == 0 || toys.Contains(card.Definition);
                card.gameObject.SetActive(available);
                if (!available) continue;
                activeCards.Add(card);
                card.SetHotkey(activeCards.Count.ToString());
            }
        }

        // ------------------------------------------------------------------ Seleção

        public void ToggleCard(ToyCardUI card)
        {
            if (card == null || !GameManager.IsPlaying) return;
            if (selectedCard == card)
            {
                ClearSelection();
                return;
            }
            if (!card.IsReady || !card.CanAfford)
            {
                Sfx.Play(SfxType.Error);
                card.ShakeDenied();
                return;
            }
            ClearSelection();
            selectedCard = card;
            card.SetSelected(true);
            Sfx.Play(SfxType.Select);
        }

        public void ToggleRemoveMode()
        {
            if (!GameManager.IsPlaying) return;
            bool enable = !removeMode;
            ClearSelection();
            removeMode = enable;
            if (removeTool != null) removeTool.SetSelected(enable);
            Sfx.Play(SfxType.Select);
        }

        public void ClearSelection()
        {
            if (selectedCard != null) selectedCard.SetSelected(false);
            selectedCard = null;
            removeMode = false;
            if (removeTool != null) removeTool.SetSelected(false);
            if (grid != null) grid.HideHighlight();
        }

        // ------------------------------------------------------------------ Loop de entrada

        private void Update()
        {
            if (!GameManager.IsPlaying)
            {
                if (selectedCard != null || removeMode) ClearSelection();
                return;
            }

            HandleKeyboard();

            Pointer pointer = Pointer.current;
            if (pointer == null || grid == null) return;

            Vector2 screenPos = pointer.position.ReadValue();
            bool hasCell = grid.TryGetCellAtScreenPoint(screenPos, out GridCell cell);

            // Destaque da célula sob o cursor.
            if (hasCell && (selectedCard != null || removeMode))
            {
                GridManager.HighlightMode mode;
                if (removeMode)
                    mode = grid.IsOccupied(cell.Row, cell.Column) ? GridManager.HighlightMode.Remove : GridManager.HighlightMode.Invalid;
                else
                    mode = grid.CanPlace(cell) && selectedCard.CanAfford ? GridManager.HighlightMode.Valid : GridManager.HighlightMode.Invalid;
                grid.ShowHighlight(cell, mode);
            }
            else
            {
                grid.HideHighlight();
            }

            if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
            {
                ClearSelection();
                return;
            }

            if (!pointer.press.wasPressedThisFrame) return;

            // 1) Orbes de luz têm prioridade sobre a grade.
            if (TryCollectOrb(screenPos)) return;
            if (!hasCell) return;

            // 2) Remover brinquedo.
            if (removeMode)
            {
                ToyBase toy = grid.GetOccupant(cell.Row, cell.Column);
                if (toy != null)
                {
                    toy.Remove();
                    Sfx.Play(SfxType.Place, 0.6f);
                    ClearSelection();
                }
                else Sfx.Play(SfxType.Error);
                return;
            }

            // 3) Posicionar brinquedo.
            if (selectedCard != null) TryPlaceSelected(cell);
        }

        private void HandleKeyboard()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            if (kb.escapeKey.wasPressedThisFrame) ClearSelection();
            if (kb.rKey.wasPressedThisFrame) ToggleRemoveMode();

            Key[] digits = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6 };
            for (int i = 0; i < activeCards.Count && i < digits.Length; i++)
                if (kb[digits[i]].wasPressedThisFrame) ToggleCard(activeCards[i]);
        }

        private bool TryCollectOrb(Vector2 screenPos)
        {
            if (cam == null) cam = Camera.main;
            if (cam == null) return false;
            Ray ray = cam.ScreenPointToRay(screenPos);
            if (Physics.Raycast(ray, out RaycastHit hit, 200f, GameLayers.PickupMask, QueryTriggerInteraction.Collide))
            {
                LightOrb orb = hit.collider.GetComponentInParent<LightOrb>();
                if (orb != null && orb.IsCollectable)
                {
                    orb.Collect();
                    return true;
                }
            }
            return false;
        }

        /// <summary>Cobra a Luz e posiciona o brinquedo do card selecionado na célula.</summary>
        public bool TryPlaceSelected(GridCell cell)
        {
            if (selectedCard == null || cell == null) return false;
            ToyDefinition def = selectedCard.Definition;

            if (!grid.CanPlace(cell) || !selectedCard.IsReady || CurrencyManager.Instance == null
                || !CurrencyManager.Instance.TrySpendLight(def.Cost))
            {
                Sfx.Play(SfxType.Error);
                return false;
            }

            if (!grid.TryPlaceToy(def.prefab, cell.Row, cell.Column, out _))
            {
                CurrencyManager.Instance.AddLight(def.Cost); // devolve se algo deu errado
                Sfx.Play(SfxType.Error);
                return false;
            }

            Sfx.Play(SfxType.Place);
            selectedCard.StartRecharge();
            ClearSelection();
            return true;
        }
    }
}
