using UnityEngine;

namespace SleepDefenders
{
    /// <summary>
    /// Uma célula do tapete. Cada célula tem um BoxCollider fino na layer "Grid",
    /// que é o alvo do Raycast do mouse feito pelo GridManager.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class GridCell : MonoBehaviour
    {
        [SerializeField] private int row;
        [SerializeField] private int column;

        /// <summary>Faixa (lane) da célula: 0 = faixa do fundo, 4 = faixa mais perto da câmera.</summary>
        public int Row => row;
        public int Column => column;

        public void Setup(int newRow, int newColumn)
        {
            row = newRow;
            column = newColumn;
        }

        public override string ToString() => $"Célula ({row},{column})";
    }
}
