using UnityEngine;

namespace SleepDefenders
{
    /// <summary>
    /// Orbe de Luz coletável. Nasce de um Abajur (pulinho em arco) ou cai do teto como raio de luar.
    /// O jogador clica nele (Raycast na layer "Pickup") para coletar; ele voa até o contador da UI.
    /// Se ninguém coletar, some depois de alguns segundos.
    /// </summary>
    [RequireComponent(typeof(SphereCollider))]
    public class LightOrb : MonoBehaviour
    {
        private enum State { Arcing, Falling, Idle, Collecting }

        [SerializeField] private float lifetime = 12f;
        [SerializeField] private float fallSpeed = 1.6f;
        [SerializeField] private float arcDuration = 0.6f;
        [SerializeField] private float arcHeight = 0.8f;
        [SerializeField] private float collectDuration = 0.45f;
        [SerializeField] private Transform visual;
        [SerializeField] private Light orbLight;

        private State state = State.Idle;
        private int value = 25;
        private Vector3 start, target;
        private float timer;
        private float idleTime;
        private Vector3 baseScale;
        private float baseLightIntensity = 1f;
        private Camera cam;
        private SphereCollider sphere;

        public int Value => value;
        public bool IsCollectable => state != State.Collecting;

        private void Awake()
        {
            sphere = GetComponent<SphereCollider>();
            sphere.isTrigger = true;
            if (visual == null) visual = transform;
            baseScale = visual.localScale;
            if (orbLight != null) baseLightIntensity = orbLight.intensity;
            cam = Camera.main;
        }

        /// <summary>Sai do abajur em arco até "to".</summary>
        public void LaunchFrom(Vector3 from, Vector3 to, int lightValue)
        {
            value = lightValue;
            start = from;
            target = to;
            transform.position = from;
            timer = 0f;
            state = State.Arcing;
        }

        /// <summary>Cai do teto em linha reta até "to" (raio de luar).</summary>
        public void FallTo(Vector3 to, float startHeight, int lightValue)
        {
            value = lightValue;
            target = to;
            transform.position = to + Vector3.up * startHeight;
            state = State.Falling;
        }

        /// <summary>Coleta o orbe: voa até o contador e soma a Luz ao chegar.</summary>
        public void Collect()
        {
            if (state == State.Collecting) return;
            state = State.Collecting;
            sphere.enabled = false;
            start = transform.position;
            timer = 0f;
            Sfx.Play(SfxType.Collect);
            Vfx.Spawn(VfxType.Sparkle, transform.position, new Color(1f, 0.95f, 0.6f), 0.8f);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            switch (state)
            {
                case State.Arcing:
                {
                    timer += dt;
                    float k = Mathf.Clamp01(timer / arcDuration);
                    Vector3 p = Vector3.Lerp(start, target, k);
                    p.y += Mathf.Sin(k * Mathf.PI) * arcHeight;
                    transform.position = p;
                    if (k >= 1f) BeginIdle();
                    break;
                }
                case State.Falling:
                {
                    transform.position = Vector3.MoveTowards(transform.position, target, fallSpeed * dt);
                    if ((transform.position - target).sqrMagnitude < 0.0001f) BeginIdle();
                    break;
                }
                case State.Idle:
                {
                    if (!GameManager.IsPlaying) break; // não expira com o jogo pausado/terminado
                    idleTime += dt;
                    float remaining = lifetime - idleTime;
                    // Pisca nos últimos 3 segundos para avisar que vai sumir.
                    bool visible = remaining > 3f || Mathf.Repeat(remaining * 6f, 1f) > 0.35f;
                    visual.gameObject.SetActive(visible);
                    if (remaining <= 0f) Destroy(gameObject);
                    break;
                }
                case State.Collecting:
                {
                    timer += dt;
                    float k = Mathf.Clamp01(timer / collectDuration);
                    if (cam == null) cam = Camera.main;
                    Vector3 dest = CurrencyManager.Instance != null && cam != null
                        ? CurrencyManager.Instance.GetCollectWorldPoint(cam)
                        : start + Vector3.up * 3f;
                    float eased = k * k * (3f - 2f * k);
                    transform.position = Vector3.Lerp(start, dest, eased);
                    visual.gameObject.SetActive(true);
                    visual.localScale = baseScale * Mathf.Lerp(1f, 0.35f, eased);
                    if (k >= 1f)
                    {
                        if (CurrencyManager.Instance != null) CurrencyManager.Instance.AddLight(value);
                        Destroy(gameObject);
                    }
                    break;
                }
            }

            // Gira e pulsa suavemente.
            if (state != State.Collecting)
            {
                visual.Rotate(0f, 90f * dt, 0f, Space.Self);
                float pulse = 1f + Mathf.Sin(Time.time * 4f) * 0.08f;
                visual.localScale = baseScale * pulse;
            }
            if (orbLight != null) orbLight.intensity = baseLightIntensity * (1f + Mathf.Sin(Time.time * 4f) * 0.2f);
        }

        private void BeginIdle()
        {
            state = State.Idle;
            idleTime = 0f;
        }
    }
}
