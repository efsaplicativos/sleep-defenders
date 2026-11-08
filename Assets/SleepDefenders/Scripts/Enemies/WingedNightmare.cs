using UnityEngine;

namespace SleepDefenders
{
    /// <summary>
    /// Pesadelo Alado — velocista voador que caça Abajures.
    ///   - Se há um Abajur vivo à frente na faixa, continua voando por cima de tudo (Robôs, Ursos...)
    ///     e mergulha no Abajur. Proteja os Abajures colocando-os em faixas com Robôs, ou atrás dos Ursos
    ///     num lugar onde o Alado morra antes de chegar.
    ///   - Sem Abajur à frente, faz o voo antigo: passa por cima da PRIMEIRA defesa, pousa logo depois
    ///     e, a partir daí, anda devagar e para no próximo brinquedo.
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

        /// <summary>Voando atrás de um Abajur que está mais à frente na faixa?</summary>
        public bool IsHuntingLamp => airborne && FindLampAhead() != null;

        protected override void Awake()
        {
            base.Awake();
            visualHeight = cruiseHeight;
        }

        protected override bool ShouldEngage(ToyBase toy)
        {
            if (airborne)
            {
                if (toy is ToyLamp) return true;            // mergulha no abajur
                if (FindLampAhead() != null) return false;  // caçando: sobrevoa o resto sem gastar o "pulo"
            }
            if (!hasSkipped)
            {
                hasSkipped = true;
                skippedToy = toy;
                return false; // passa por cima do primeiro brinquedo
            }
            return toy != skippedToy;
        }

        protected override void OnStartAttack(ToyBase toy)
        {
            // Mergulhou no abajur (ou achou o segundo brinquedo): pousa na hora de atacar.
            // Já no chão, não pula mais nenhum brinquedo.
            airborne = false;
            hasSkipped = true;
        }

        /// <summary>Abajur vivo mais próximo à frente (à esquerda) na faixa, ou null.</summary>
        public ToyLamp FindLampAhead()
        {
            GridManager grid = GridManager.Instance;
            if (grid == null || Lane < 0) return null;
            float x = transform.position.x;
            int start = Mathf.Min(grid.ColumnAt(x), grid.Columns - 1);
            for (int c = start; c >= 0; c--)
                if (grid.GetOccupant(Lane, c) is ToyLamp lamp && lamp.IsAlive && lamp.transform.position.x <= x + 0.05f)
                    return lamp;
            return null;
        }

        private bool IsAboveToy()
        {
            GridManager grid = GridManager.Instance;
            if (grid == null) return false;
            ToyBase toy = grid.GetOccupant(Lane, grid.ColumnAt(transform.position.x));
            return toy != null && toy.IsAlive;
        }

        protected override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!IsAlive) return;

            float targetHeight = cruiseHeight;
            if (IsAttacking)
            {
                // Mergulhou no abajur (ou achou o segundo brinquedo): pousa para atacar.
                // Já no chão, não pula mais nenhum brinquedo.
                airborne = false;
                hasSkipped = true;
            }
            else if (airborne && FindLampAhead() != null)
            {
                // Caçando um abajur: sobe ao passar por cima de cada brinquedo.
                if (IsAboveToy()) targetHeight = swoopHeight;
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
