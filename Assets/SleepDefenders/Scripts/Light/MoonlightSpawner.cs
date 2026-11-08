using UnityEngine;

namespace SleepDefenders
{
    /// <summary>
    /// "Faixas de luar": de tempos em tempos um orbe de Luz desce do teto sobre uma célula aleatória.
    /// É a fonte de Luz garantida enquanto o jogador ainda não tem abajures.
    /// </summary>
    public class MoonlightSpawner : MonoBehaviour
    {
        [SerializeField] private LightOrb orbPrefab;
        [SerializeField, Min(1)] private int lightValue = 25;
        [SerializeField] private Vector2 intervalRange = new Vector2(8f, 12f);
        [SerializeField] private float firstDropDelay = 4f;
        [SerializeField] private float dropHeight = 6f;
        [Tooltip("Feixe de luz decorativo que acompanha a queda (opcional).")]
        [SerializeField] private GameObject moonbeamPrefab;

        private float timer;

        private void Start() => timer = firstDropDelay;

        private void Update()
        {
            if (!GameManager.IsPlaying || orbPrefab == null || GridManager.Instance == null) return;

            timer -= Time.deltaTime;
            if (timer > 0f) return;
            timer = Random.Range(intervalRange.x, intervalRange.y);
            Drop();
        }

        public LightOrb Drop()
        {
            GridManager grid = GridManager.Instance;
            int row = Random.Range(0, grid.Rows);
            int col = Random.Range(0, grid.Columns);
            Vector3 target = grid.GetCellCenter(row, col) + new Vector3(Random.Range(-0.2f, 0.2f), 0.35f, Random.Range(-0.2f, 0.2f));

            LightOrb orb = Instantiate(orbPrefab, target + Vector3.up * dropHeight, Quaternion.identity);
            orb.FallTo(target, dropHeight, lightValue);

            if (moonbeamPrefab != null)
            {
                GameObject beam = Instantiate(moonbeamPrefab, new Vector3(target.x, 0f, target.z), Quaternion.identity);
                var fx = beam.GetComponent<MoonbeamFx>();
                if (fx == null) fx = beam.AddComponent<MoonbeamFx>();
                fx.Play(4f);
            }
            return orb;
        }
    }

    /// <summary>Feixe de luar: abre, fica um pouco e fecha (anima a largura) antes de se destruir.</summary>
    public class MoonbeamFx : MonoBehaviour
    {
        private float duration = 4f;
        private float time;
        private Vector3 baseScale;

        public void Play(float seconds)
        {
            duration = Mathf.Max(0.5f, seconds);
            time = 0f;
            baseScale = transform.localScale;
            Apply();
        }

        private void Update()
        {
            time += Time.deltaTime;
            Apply();
            if (time >= duration) Destroy(gameObject);
        }

        private void Apply()
        {
            float k = Mathf.Clamp01(time / duration);
            float width = Mathf.Min(1f, Mathf.Min(k / 0.15f, (1f - k) / 0.25f));
            transform.localScale = new Vector3(baseScale.x * width, baseScale.y, baseScale.z * width);
        }
    }
}
