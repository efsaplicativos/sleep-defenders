using UnityEngine;

namespace SleepDefenders
{
    public enum VfxType { Puff, Spark, Sparkle }

    /// <summary>
    /// Ponto único para disparar partículas (fumaça ao morrer, faísca de laser, brilho de luz).
    /// Os prefabs são ParticleSystems "one-shot" que voltam ao pool quando terminam.
    /// </summary>
    public class Vfx : MonoBehaviour
    {
        [SerializeField] private ParticleSystem puffPrefab;
        [SerializeField] private ParticleSystem sparkPrefab;
        [SerializeField] private ParticleSystem sparklePrefab;

        private static Vfx instance;

        private void Awake() => instance = this;

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        public static void Spawn(VfxType type, Vector3 position, Color? tint = null, float scale = 1f)
        {
            if (instance == null) return;
            ParticleSystem prefab = type switch
            {
                VfxType.Puff => instance.puffPrefab,
                VfxType.Spark => instance.sparkPrefab,
                _ => instance.sparklePrefab,
            };
            if (prefab == null) return;

            ParticleSystem ps = PoolRegistry.For(prefab).Get(position, Quaternion.identity);
            ps.transform.localScale = Vector3.one * scale;
            var main = ps.main;
            main.startColor = tint.HasValue ? new ParticleSystem.MinMaxGradient(tint.Value) : prefab.main.startColor;
            ps.Clear(true);
            ps.Play(true);

            var returner = ps.GetComponent<VfxReturnToPool>();
            if (returner == null) returner = ps.gameObject.AddComponent<VfxReturnToPool>();
            returner.Arm(prefab);
        }
    }

    /// <summary>Devolve a partícula ao pool assim que ela termina de tocar.</summary>
    public class VfxReturnToPool : MonoBehaviour
    {
        private ParticleSystem system;
        private ParticleSystem prefab;

        public void Arm(ParticleSystem sourcePrefab)
        {
            prefab = sourcePrefab;
            system = GetComponent<ParticleSystem>();
        }

        private void Update()
        {
            if (system != null && !system.IsAlive(true))
                PoolRegistry.For(prefab).Release(system);
        }
    }
}
