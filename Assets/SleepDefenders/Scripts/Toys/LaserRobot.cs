using UnityEngine;

namespace SleepDefenders
{
    /// <summary>
    /// Robô Laser — atacante de linha. Custo 100.
    /// Só dispara quando um Physics.Raycast (ou BoxCast) ao longo da PRÓPRIA faixa encontra um monstro
    /// na layer "Enemy" dentro do tapete. A varredura roda em intervalos curtos em vez de todo frame.
    /// </summary>
    public class LaserRobot : ToyBase
    {
        public enum DetectionMode { Raycast, BoxCast }

        [Header("Ataque")]
        [SerializeField] private LaserProjectile projectilePrefab;
        [SerializeField] private Transform muzzle;
        [SerializeField, Min(0.1f)] private float fireInterval = 1.4f;
        [SerializeField, Min(0f)] private float damage = 20f;
        [Tooltip("Primeiro tiro sai mais rápido após detectar um alvo.")]
        [SerializeField, Range(0f, 1f)] private float firstShotDelayFactor = 0.35f;

        [Header("Detecção")]
        [SerializeField] private DetectionMode detection = DetectionMode.Raycast;
        [Tooltip("Intervalo entre varreduras (otimização: não precisa ser todo frame).")]
        [SerializeField, Min(0.02f)] private float scanInterval = 0.1f;
        [Tooltip("Altura do raio em relação ao chão.")]
        [SerializeField] private float rayHeight = 0.45f;
        [Tooltip("Meia-extensão da caixa (modo BoxCast).")]
        [SerializeField] private Vector3 boxHalfExtents = new Vector3(0.05f, 0.3f, 0.25f);
        [Tooltip("Distância além da borda direita do tapete em que o robô já enxerga monstros.")]
        [SerializeField] private float extraRangeBeyondBoard = 0.2f;

        [Header("Efeitos")]
        [SerializeField] private Light muzzleLight;

        private float fireTimer;
        private float scanTimer;
        private bool hasTarget;

        private void Update()
        {
            if (!GameManager.IsPlaying || Lane < 0) return;

            scanTimer -= Time.deltaTime;
            if (scanTimer <= 0f)
            {
                scanTimer = scanInterval;
                bool hadTarget = hasTarget;
                hasTarget = CheckForEnemies();
                if (hasTarget && !hadTarget)
                    fireTimer = Mathf.Min(fireTimer, fireInterval * firstShotDelayFactor);
            }

            fireTimer -= Time.deltaTime;
            if (hasTarget && fireTimer <= 0f)
            {
                fireTimer = fireInterval;
                ShootLaser();
            }

            if (muzzleLight != null)
                muzzleLight.intensity = Mathf.MoveTowards(muzzleLight.intensity, 0f, Time.deltaTime * 30f);
        }

        /// <summary>Procura um monstro vivo à frente (+X), na mesma faixa, até a borda do tapete.</summary>
        private bool CheckForEnemies()
        {
            GridManager grid = GridManager.Instance;
            Vector3 origin = new Vector3(transform.position.x, transform.position.y + rayHeight, transform.position.z);
            float range = grid != null ? grid.RightEdgeX + extraRangeBeyondBoard - origin.x : 12f;
            if (range <= 0f) return false;

            RaycastHit hit;
            bool found = detection == DetectionMode.BoxCast
                ? Physics.BoxCast(origin, boxHalfExtents, Vector3.right, out hit, Quaternion.identity, range, GameLayers.EnemyMask, QueryTriggerInteraction.Collide)
                : Physics.Raycast(origin, Vector3.right, out hit, range, GameLayers.EnemyMask, QueryTriggerInteraction.Collide);

            if (!found || !hit.collider.CompareTag(GameTags.Enemy)) return false;
            EnemyBase enemy = hit.collider.GetComponentInParent<EnemyBase>();
            return enemy != null && enemy.IsAlive && enemy.Lane == Lane;
        }

        private void ShootLaser()
        {
            if (projectilePrefab == null) return;
            Vector3 spawn = muzzle != null ? muzzle.position : transform.position + new Vector3(0.35f, rayHeight, 0f);
            // Mantém o projétil exatamente no Z da faixa para não acertar faixas vizinhas.
            spawn.z = transform.position.z;

            LaserProjectile shot = PoolRegistry.For(projectilePrefab, 8).Get(spawn, Quaternion.identity);
            float maxX = GridManager.Instance != null ? GridManager.Instance.RightEdgeX + 2f : spawn.x + 12f;
            shot.Launch(projectilePrefab, damage, Lane, maxX);

            if (animator != null) animator.PlayRecoil();
            if (muzzleLight != null) muzzleLight.intensity = 3f;
            Sfx.Play(SfxType.Laser, 0.6f);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Vector3 origin = transform.position + Vector3.up * rayHeight;
            Gizmos.DrawLine(origin, origin + Vector3.right * 10f);
        }
#endif
    }
}
