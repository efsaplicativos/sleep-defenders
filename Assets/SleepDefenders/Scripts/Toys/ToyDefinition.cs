using UnityEngine;

namespace SleepDefenders
{
    /// <summary>
    /// Dados de um "card" da barra de brinquedos: prefab, ícone e tempo de recarga.
    /// O custo vem do próprio prefab (ToyBase.Cost) para ter uma única fonte de verdade.
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

        public int Cost => prefab != null ? prefab.Cost : 0;
        public string DisplayName => prefab != null ? prefab.DisplayName : name;
    }
}
