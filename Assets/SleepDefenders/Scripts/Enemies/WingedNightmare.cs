using UnityEngine;

namespace SleepDefenders
{
    /// <summary>
    /// Pesadelo Alado — velocista voador.
    /// Voa rápido e IGNORA a primeira defesa terrestre que encontrar: sobe, passa por cima dela
    /// e pousa logo depois. A partir daí anda mais devagar e para em brinquedos normalmente.
    /// O collider continua na altura do chão, então o Robô Laser consegue acertá-lo mesmo voando.
    /// </summary>
    public class WingedNightmare : EnemyBase
    {
        [Header("Voo")]
        [Tooltip("Velocidade depois de pousar.")]
        [SerializeField, Min(0f)] private float landedSpeed = 0.55f;
        [Tooltip("Altura extra do visual ao sobrevoar o brinquedo.")]
        [SerializeField] private float swoopHeight = 0.9f;
        [Tooltip("Altura do visual enquanto ainda está voando alto.")]
        [SerializeField] private float cruiseHeight = 0.45f;
        [Tooltip("Altura do visual depois de pousar (pairando baixo).")]
        [SerializeField] private float landedHeight = 0.1f;

        private bool hasSkipped;
        private bool airborne = true;
        private ToyBase skippedToy;
        private float visualHeight;

        public override bool IsAirborne => airborne;
        protected override float CurrentSpeed => airborne ? moveSpeed : landedSpeed;

        protected override void Awake()
        {
            base.Awake();
            visualHeight = cruiseHeight;
        }

        protected override bool ShouldEngage(ToyBase toy)
        {
            if (!hasSkipped)
            {
                hasSkipped = true;
                skippedToy = toy;
                return false; // passa por cima do primeiro brinquedo
            }
            return toy != skippedToy;
        }

        protected override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!IsAlive) return;

            float targetHeight = cruiseHeight;
            if (hasSkipped && IsAttacking)
            {
                // Encontrou o segundo brinquedo ainda no ar: pousa para atacar.
                airborne = false;
            }
            else if (skippedToy != null && airborne)
            {
                // Sobrevoando: sobe enquanto está em cima do brinquedo e pousa quando já passou.
                float dx = transform.position.x - skippedToy.transform.position.x;
                if (dx < -0.6f) airborne = false;
                else targetHeight = swoopHeight;
            }
            else if (hasSkipped && airborne)
            {
                // O brinquedo sobrevoado sumiu no meio do caminho: pousa mesmo assim.
                airborne = false;
            }

            if (!airborne) targetHeight = landedHeight;
            visualHeight = Mathf.MoveTowards(visualHeight, targetHeight, Time.fixedDeltaTime * 2.5f);
            if (animator != null) animator.ExtraHeight = visualHeight;
        }
    }
}
