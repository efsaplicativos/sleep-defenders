using UnityEngine;

namespace SleepDefenders
{
    /// <summary>
    /// Animação procedural leve aplicada ao filho "Visual" de brinquedos e monstros.
    /// Os modelos gerados pelo Tripo AI são estáticos (sem rig), então damos vida a eles com
    /// respiração (idle), gingado ao andar, bote ao atacar, recuo ao disparar e "squash" ao levar dano.
    /// Tudo é calculado como offset sobre a pose base, então nunca "deriva" com o tempo.
    /// </summary>
    public class ProceduralAnimator : MonoBehaviour
    {
        [SerializeField] private Transform visual;

        [Header("Idle (respiração)")]
        [SerializeField] private float breatheAmount = 0.03f;
        [SerializeField] private float breatheSpeed = 2.2f;

        [Header("Andar")]
        [SerializeField] private float walkBobHeight = 0.06f;
        [SerializeField] private float walkWaddleDegrees = 7f;
        [SerializeField] private float walkFrequency = 5f;

        [Header("Voo")]
        [SerializeField] private bool flying;
        [SerializeField] private float hoverAmplitude = 0.12f;
        [SerializeField] private float hoverFrequency = 3.2f;

        [Header("Reações")]
        [SerializeField] private float attackLungeDistance = 0.18f;
        [SerializeField] private float recoilDistance = 0.12f;
        [SerializeField] private float hitSquash = 0.15f;

        private Vector3 basePosition;
        private Quaternion baseRotation;
        private Vector3 baseScale;
        private float phase;
        private bool moving;
        private float lungeTimer, recoilTimer, hitTimer, spawnTimer = -1f;
        private float extraHeight;
        private Vector3 forwardDir = Vector3.right;

        private const float LungeDuration = 0.25f, RecoilDuration = 0.18f, HitDuration = 0.18f, SpawnDuration = 0.35f;

        public Transform Visual => visual;

        /// <summary>Altura extra (usada pelo Pesadelo Alado ao sobrevoar um brinquedo).</summary>
        public float ExtraHeight { get => extraHeight; set => extraHeight = value; }

        private void Awake()
        {
            if (visual == null && transform.childCount > 0) visual = transform.GetChild(0);
            if (visual == null) visual = transform;
            basePosition = visual.localPosition;
            baseRotation = visual.localRotation;
            baseScale = visual.localScale;
            phase = Random.value * 10f; // dessincroniza unidades iguais
        }

        public void Configure(Transform visualTransform, bool isFlying)
        {
            visual = visualTransform;
            flying = isFlying;
        }

        public void SetMoving(bool value) => moving = value;
        /// <summary>Direção "para frente" em espaço local do objeto raiz (brinquedos: +X, monstros: -X).</summary>
        public void SetForward(Vector3 localForward) => forwardDir = localForward.normalized;
        public void PlayAttack() => lungeTimer = LungeDuration;
        public void PlayRecoil() => recoilTimer = RecoilDuration;
        public void PlayHit() => hitTimer = HitDuration;
        public void PlaySpawn() => spawnTimer = SpawnDuration;

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            phase += dt;

            Vector3 pos = basePosition;
            Quaternion rot = baseRotation;
            Vector3 scale = baseScale;

            if (flying)
            {
                pos.y += Mathf.Sin(phase * hoverFrequency) * hoverAmplitude;
                rot = Quaternion.AngleAxis(Mathf.Sin(phase * hoverFrequency * 0.5f) * 8f, forwardDir) * rot;
            }
            else if (moving)
            {
                float s = phase * walkFrequency;
                pos.y += Mathf.Abs(Mathf.Sin(s)) * walkBobHeight;
                rot = Quaternion.AngleAxis(Mathf.Sin(s) * walkWaddleDegrees, forwardDir) * rot;
            }
            else
            {
                float b = Mathf.Sin(phase * breatheSpeed) * breatheAmount;
                scale = new Vector3(scale.x * (1f - b * 0.5f), scale.y * (1f + b), scale.z * (1f - b * 0.5f));
            }

            pos.y += extraHeight;

            if (lungeTimer > 0f)
            {
                lungeTimer -= dt;
                float k = Mathf.Sin((1f - lungeTimer / LungeDuration) * Mathf.PI);
                pos += forwardDir * (attackLungeDistance * k);
                rot = Quaternion.AngleAxis(12f * k, Vector3.Cross(Vector3.up, forwardDir)) * rot;
            }

            if (recoilTimer > 0f)
            {
                recoilTimer -= dt;
                float k = Mathf.Sin((1f - recoilTimer / RecoilDuration) * Mathf.PI);
                pos -= forwardDir * (recoilDistance * k);
            }

            if (hitTimer > 0f)
            {
                hitTimer -= dt;
                float k = Mathf.Sin((1f - hitTimer / HitDuration) * Mathf.PI) * hitSquash;
                scale = new Vector3(scale.x * (1f + k), scale.y * (1f - k), scale.z * (1f + k));
            }

            if (spawnTimer >= 0f)
            {
                spawnTimer -= dt;
                float t = Mathf.Clamp01(1f - spawnTimer / SpawnDuration);
                // Overshoot elástico: cresce, passa um pouco do tamanho e volta.
                float e = 1f + 2.70158f * Mathf.Pow(t - 1f, 3f) + 1.70158f * Mathf.Pow(t - 1f, 2f);
                scale *= Mathf.Max(0.01f, e);
            }

            visual.localPosition = pos;
            visual.localRotation = rot;
            visual.localScale = scale;
        }
    }
}
