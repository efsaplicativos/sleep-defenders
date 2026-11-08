using UnityEngine;

namespace SleepDefenders
{
    /// <summary>
    /// Sapato (ou meia) arremessado pelo Monstro do Armário. Voa em arco até o brinquedo-alvo
    /// e causa dano ao chegar. Respinga um pouco nos brinquedos vizinhos da mesma faixa,
    /// por isso Robôs grudados uns nos outros sofrem juntos.
    /// A trajetória é calculada (sem física e sem collider).
    /// </summary>
    public class ThrownObject : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float flightTime = 0.7f;
        [SerializeField, Min(0f)] private float arcHeight = 1.1f;
        [SerializeField] private float spinDegreesPerSecond = 620f;
        [SerializeField] private Color impactColor = new Color(0.72f, 0.58f, 1f, 0.9f);

        private ToyBase target;
        private Vector3 from, to;
        private float damage, splashDamage;
        private float elapsed;
        private bool launched;

        public ToyBase Target => target;

        /// <summary>Começa o voo de "start" até o brinquedo "toy".</summary>
        public void Launch(Vector3 start, ToyBase toy, float damageAmount, float splashAmount)
        {
            target = toy;
            from = start;
            to = toy != null ? toy.transform.position + Vector3.up * 0.45f : start;
            damage = damageAmount;
            splashDamage = splashAmount;
            elapsed = 0f;
            launched = true;
            transform.position = start;
        }

        private void Update()
        {
            if (!launched) return;
            if (GameManager.Instance != null && !GameManager.IsPlaying)
            {
                // A noite acabou com o sapato no ar: some sem causar dano.
                if (GameManager.Instance.State != GameState.Menu) Destroy(gameObject);
                return;
            }

            elapsed += Time.deltaTime;
            float k = Mathf.Clamp01(elapsed / flightTime);
            Vector3 p = Vector3.Lerp(from, to, k);
            p.y += Mathf.Sin(k * Mathf.PI) * arcHeight;
            transform.position = p;
            transform.Rotate(0f, 0f, spinDegreesPerSecond * Time.deltaTime, Space.Self);

            if (k >= 1f) Land();
        }

        private void Land()
        {
            launched = false;
            Vfx.Spawn(VfxType.Puff, to, impactColor, 0.6f);
            Sfx.Play(SfxType.Hit, 0.5f);

            if (target != null && target.IsAlive)
            {
                target.TakeDamage(damage);
                if (splashDamage > 0f) Splash(target.Lane, target.Column);
            }
            Destroy(gameObject);
        }

        private void Splash(int lane, int column)
        {
            GridManager grid = GridManager.Instance;
            if (grid == null) return;
            for (int c = column - 1; c <= column + 1; c += 2)
            {
                ToyBase neighbor = grid.GetOccupant(lane, c);
                if (neighbor != null && neighbor != target && neighbor.IsAlive && neighbor.BlocksEnemies)
                    neighbor.TakeDamage(splashDamage);
            }
        }
    }
}
