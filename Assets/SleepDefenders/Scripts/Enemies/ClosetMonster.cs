using UnityEngine;

namespace SleepDefenders
{
    /// <summary>
    /// Monstro do Armário — tanque inimigo. Lento e com vida altíssima (450 HP).
    ///   - Enquanto anda, ARREMESSA sapatos no primeiro brinquedo à frente na faixa (até 3 casas):
    ///     25 de dano no alvo e 10 nos vizinhos da mesma faixa. Um Urso na frente aguenta os sapatos
    ///     e protege os Robôs; Robôs na linha de frente quebram antes de o monstro chegar.
    ///   - Quando encosta num brinquedo, para de arremessar e morde.
    ///   - Com pouca vida se enfurece e acelera.
    /// </summary>
    public class ClosetMonster : EnemyBase
    {
        [Header("Fúria")]
        [SerializeField, Range(0f, 1f)] private float enrageAtHealthFraction = 0.3f;
        [SerializeField, Min(1f)] private float enragedSpeedMultiplier = 1.6f;

        [Header("Arremesso")]
        [SerializeField] private ThrownObject throwPrefab;
        [Tooltip("Alcance do arremesso em casas, à frente do monstro.")]
        [SerializeField, Min(0.5f)] private float throwRangeCells = 3f;
        [SerializeField, Min(0.2f)] private float throwInterval = 3f;
        [SerializeField, Min(0f)] private float throwDamage = 25f;
        [Tooltip("Dano nos brinquedos vizinhos (mesma faixa) do alvo.")]
        [SerializeField, Min(0f)] private float throwSplashDamage = 10f;
        [Tooltip("Tempo entre ver um alvo e o primeiro arremesso.")]
        [SerializeField, Min(0f)] private float windUp = 0.8f;
        [SerializeField] private float throwHeight = 1.1f;

        private bool enraged;
        private float throwTimer;
        private bool hadTarget;

        /// <summary>Quantos sapatos este monstro já arremessou (usado por testes).</summary>
        public int ThrowCount { get; private set; }

        protected override float CurrentSpeed => enraged ? moveSpeed * enragedSpeedMultiplier : moveSpeed;

        protected override void OnDamaged(float amount)
        {
            if (!enraged && CurrentHP <= MaxHP * enrageAtHealthFraction)
            {
                enraged = true;
                Vfx.Spawn(VfxType.Puff, transform.position + Vector3.up * 1.2f, new Color(1f, 0.3f, 0.3f, 0.8f), 0.8f);
            }
        }

        protected override void OnTick(float deltaTime)
        {
            // Encostado num brinquedo: só morde.
            if (IsAttacking)
            {
                hadTarget = false;
                return;
            }

            ToyBase target = FindThrowTarget();
            if (target == null)
            {
                hadTarget = false;
                return;
            }
            if (!hadTarget)
            {
                hadTarget = true;
                throwTimer = Mathf.Max(throwTimer, windUp);
            }

            throwTimer -= deltaTime * AttackRateFactor;
            if (throwTimer > 0f) return;
            throwTimer = throwInterval;
            Throw(target);
        }

        /// <summary>Primeiro brinquedo vivo que segura monstros, à frente e dentro do alcance, na mesma faixa.</summary>
        public ToyBase FindThrowTarget()
        {
            GridManager grid = GridManager.Instance;
            if (grid == null || Lane < 0) return null;
            float x = transform.position.x;
            float range = throwRangeCells * grid.CellSize;

            int startColumn = Mathf.Min(grid.ColumnAt(x), grid.Columns - 1);
            for (int c = startColumn; c >= 0; c--)
            {
                ToyBase toy = grid.GetOccupant(Lane, c);
                if (toy == null || !toy.IsAlive || !toy.BlocksEnemies) continue;
                float dx = x - toy.transform.position.x;
                if (dx < 0.3f) continue;       // embaixo ou atrás do monstro
                if (dx > range) return null;   // o primeiro da fila ainda está longe
                return toy;
            }
            return null;
        }

        private void Throw(ToyBase target)
        {
            ThrowCount++;
            if (animator != null) animator.PlayAttack();
            Sfx.Play(SfxType.CarLaunch, 0.35f);

            Vector3 start = transform.position + new Vector3(-0.3f, throwHeight, 0f);
            if (throwPrefab == null)
            {
                // Sem prefab: o dano chega na hora.
                target.TakeDamage(throwDamage);
                return;
            }
            ThrownObject shot = Instantiate(throwPrefab, start, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            shot.Launch(start, target, throwDamage, throwSplashDamage);
        }
    }
}
