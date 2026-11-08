using UnityEngine;

namespace SleepDefenders
{
    /// <summary>
    /// Abajur de Brinquedo — gerador de recurso.
    /// Custo 50. A cada 10 segundos produz um orbe de 25 de Luz que o jogador clica para coletar.
    /// </summary>
    public class ToyLamp : ToyBase
    {
        [Header("Geração de Luz")]
        [SerializeField, Min(1)] private int lightPerCycle = 25;
        [SerializeField, Min(0.5f)] private float interval = 10f;
        [Tooltip("Atraso aleatório da primeira produção (para os abajures não ficarem sincronizados).")]
        [SerializeField] private Vector2 firstDelayRange = new Vector2(5f, 8f);
        [Tooltip("Se vazio, a Luz é somada direto ao contador.")]
        [SerializeField] private LightOrb orbPrefab;

        [Header("Brilho")]
        [SerializeField] private Light glowLight;
        [SerializeField] private float glowBaseIntensity = 1.2f;
        [SerializeField] private float glowPulseIntensity = 4f;

        private float timer;
        private float glowBoost;

        protected override void OnPlaced()
        {
            timer = Random.Range(firstDelayRange.x, firstDelayRange.y);
        }

        private void Update()
        {
            if (!GameManager.IsPlaying || Lane < 0) return;

            timer -= Time.deltaTime;
            if (timer <= 0f)
            {
                timer = interval;
                Produce();
            }

            if (glowLight != null)
            {
                glowBoost = Mathf.MoveTowards(glowBoost, 0f, Time.deltaTime * 2f);
                float flicker = 1f + Mathf.Sin(Time.time * 3f + Column) * 0.08f;
                glowLight.intensity = (glowBaseIntensity + glowBoost * glowPulseIntensity) * flicker;
            }
        }

        private void Produce()
        {
            glowBoost = 1f;
            if (animator != null) animator.PlayRecoil();

            if (orbPrefab == null)
            {
                if (CurrencyManager.Instance != null) CurrencyManager.Instance.AddLight(lightPerCycle);
                return;
            }

            // O orbe "pula" do abajur e cai num ponto próximo, dentro da mesma célula.
            Vector3 from = transform.position + Vector3.up * 0.9f;
            Vector2 jitter = Random.insideUnitCircle * 0.25f;
            Vector3 to = transform.position + new Vector3(jitter.x - 0.15f, 0.35f, jitter.y - 0.2f);
            LightOrb orb = Instantiate(orbPrefab, from, Quaternion.identity);
            orb.LaunchFrom(from, to, lightPerCycle);
        }
    }
}
