using UnityEngine;

namespace SleepDefenders
{
    /// <summary>
    /// Urso de Pelúcia — tanque/barreira. Custo 50, muita vida (padrão 400 HP).
    /// Não ataca: apenas segura os monstros. Fica "amassado" conforme perde vida para o jogador perceber.
    /// </summary>
    public class TeddyBear : ToyBase
    {
        [Header("Aparência de dano")]
        [Tooltip("Achatamento máximo do visual quando a vida está quase no fim.")]
        [SerializeField, Range(0f, 0.5f)] private float maxSquashWhenHurt = 0.18f;
        [SerializeField] private Transform visualToSquash;

        private Vector3 baseScale;

        protected override void Awake()
        {
            base.Awake();
            if (visualToSquash != null) baseScale = visualToSquash.localScale;
        }

        protected override void OnDamaged(float amount)
        {
            if (visualToSquash == null) return;
            float hurt = 1f - Mathf.Clamp01(CurrentHP / MaxHP);
            float squash = hurt * maxSquashWhenHurt;
            visualToSquash.localScale = new Vector3(baseScale.x * (1f + squash * 0.5f), baseScale.y * (1f - squash), baseScale.z * (1f + squash * 0.5f));
        }
    }
}
