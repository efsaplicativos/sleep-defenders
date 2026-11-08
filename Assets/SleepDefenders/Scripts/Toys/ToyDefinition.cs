using UnityEngine;

namespace SleepDefenders
{
    /// <summary>
    /// Dados de um "card" da barra de brinquedos: prefab, ícone e tempo de recarga.
    /// O custo base vem do próprio prefab (ToyBase.Cost) para ter uma única fonte de verdade;
    /// "extraCostPerCopy" encarece o card para cada brinquedo igual que já está no tapete
    /// (o Abajur usa +25: 50, 75, 100... — mais economia sempre custa mais caro).
    /// </summary>
    [CreateAssetMenu(menuName = "Sleep Defenders/Toy Definition", fileName = "Toy_")]
    public class ToyDefinition : ScriptableObject
    {
        public ToyBase prefab;
        public Sprite icon;
        [TextArea] public string description;
        [Tooltip("Segundos até o card poder ser usado de novo.")]
        [Min(0f)] public float rechargeTime = 7.5f;
        [Tooltip("Se verdadeiro, o card começa a partida recarregando.")]
        public bool startsRecharging;

        [Tooltip("Quanto o preço sobe para cada brinquedo igual já no tapete (0 = preço fixo).")]
        [Min(0)] public int extraCostPerCopy;

        /// <summary>Custo base (sem nenhum brinquedo igual no tapete).</summary>
        public int Cost => prefab != null ? prefab.Cost : 0;

        /// <summary>Custo com "copies" brinquedos iguais já no tapete.</summary>
        public int CostWith(int copies) => Cost + extraCostPerCopy * Mathf.Max(0, copies);

        /// <summary>Custo agora, contando os brinquedos iguais que estão no tapete.</summary>
        public int CurrentCost
        {
            get
            {
                if (extraCostPerCopy <= 0 || prefab == null) return Cost;
                GridManager grid = GridManager.Instance;
                return CostWith(grid != null ? grid.CountToys(prefab.GetType()) : 0);
            }
        }
        public string DisplayName => prefab != null ? prefab.DisplayName : name;
    }
}
