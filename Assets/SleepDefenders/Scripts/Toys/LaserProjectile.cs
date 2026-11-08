using UnityEngine;

namespace SleepDefenders
{
    /// <summary>
    /// Feixe de laser disparado pelo Robô Laser.
    /// Prefab: SphereCollider (Trigger) + Rigidbody (IsKinematic) + Tag "Projectile" + Layer "Projectile".
    /// Anda para +X até acertar um monstro da mesma faixa ou sair do tapete; depois volta ao pool.
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
    public class LaserProjectile : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float speed = 9f;
        [SerializeField] private Color impactColor = new Color(0.4f, 1f, 1f);

        private Rigidbody body;
        private TrailRenderer trail;
        private LaserProjectile sourcePrefab;
        private float damage;
        private int lane;
        private float maxX;
        private bool active;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            GetComponent<SphereCollider>().isTrigger = true;
            trail = GetComponentInChildren<TrailRenderer>();
        }

        /// <summary>Configura o tiro. O prefab de origem é usado para devolver ao pool correto.</summary>
        public void Launch(LaserProjectile prefab, float shotDamage, int shotLane, float limitX)
        {
            sourcePrefab = prefab;
            damage = shotDamage;
            lane = shotLane;
            maxX = limitX;
            active = true;
            body.position = transform.position;
            if (trail != null) trail.Clear();
        }

        private void FixedUpdate()
        {
            if (!active) return;
            Vector3 next = body.position + Vector3.right * (speed * Time.fixedDeltaTime);
            body.MovePosition(next);
            if (next.x > maxX) Release();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!active || !other.CompareTag(GameTags.Enemy)) return;
            EnemyBase enemy = other.GetComponentInParent<EnemyBase>();
            if (enemy == null || !enemy.IsAlive || enemy.Lane != lane) return;

            enemy.TakeDamage(damage);
            Vfx.Spawn(VfxType.Spark, transform.position, impactColor, 0.6f);
            Release();
        }

        private void Release()
        {
            if (!active) return;
            active = false;
            if (sourcePrefab != null) PoolRegistry.For(sourcePrefab).Release(this);
            else Destroy(gameObject);
        }
    }
}
