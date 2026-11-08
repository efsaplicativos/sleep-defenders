using System;
using UnityEngine;

namespace SleepDefenders
{
    /// <summary>
    /// Ratinho de Corda — a última defesa de cada faixa. Fica parado na beira da cama
    /// (fora da grade, não ocupa célula e não custa Luz).
    ///
    /// Quando um monstro da sua faixa passa da borda esquerda do tapete, o ratinho dispara para a direita
    /// e derruba TODOS os monstros da faixa (inclusive os que voam). Funciona uma vez por noite.
    ///
    /// Não usa física: compara posições com a lista EnemyBase.Active, o que é barato
    /// (no máximo algumas dezenas de monstros) e não depende de colliders.
    /// </summary>
    public class LaneGuard : MonoBehaviour
    {
        [Tooltip("Faixa protegida (0 = fundo ... 4 = frente).")]
        [SerializeField] private int lane;
        [Tooltip("Dispara quando um monstro passa deste ponto (distância além da borda esquerda do tapete).")]
        [SerializeField] private float triggerOffset = 0.15f;
        [SerializeField, Min(1f)] private float topSpeed = 11f;
        [SerializeField, Min(0.01f)] private float accelerationTime = 0.3f;
        [Tooltip("Distância (em X) em que o ratinho derruba um monstro.")]
        [SerializeField] private float hitRadius = 0.6f;
        [SerializeField] private float exitDistance = 3f;

        private ProceduralAnimator animator;
        private bool running;
        private float runTime;

        /// <summary>Disparado quando qualquer ratinho entra em ação (o HUD mostra um aviso).</summary>
        public static event Action<LaneGuard> Triggered;

        public int Lane => lane;
        public bool IsUsed => running;

        private void Awake() => animator = GetComponent<ProceduralAnimator>();

        public void SetLane(int value) => lane = value;

        private void FixedUpdate()
        {
            GridManager grid = GridManager.Instance;
            if (grid == null) return;

            if (!running)
            {
                if (!GameManager.IsPlaying) return;
                float trigger = grid.LeftEdgeX + triggerOffset;
                var enemies = EnemyBase.Active;
                for (int i = 0; i < enemies.Count; i++)
                {
                    EnemyBase e = enemies[i];
                    if (e != null && e.IsAlive && e.Lane == lane && e.transform.position.x <= trigger)
                    {
                        Launch();
                        break;
                    }
                }
                return;
            }

            // Corrida: acelera para a direita e derruba tudo o que encontrar na faixa.
            runTime += Time.fixedDeltaTime;
            float speed = topSpeed * Mathf.Clamp01(runTime / accelerationTime);
            transform.position += Vector3.right * (speed * Time.fixedDeltaTime);
            RunOverEnemies();

            if (transform.position.x > grid.RightEdgeX + exitDistance) Destroy(gameObject);
        }

        /// <summary>Dispara o ratinho (público para testes).</summary>
        public void Launch()
        {
            if (running) return;
            running = true;
            runTime = 0f;
            if (animator != null) animator.SetMoving(true);
            Sfx.Play(SfxType.CarLaunch);
            Vfx.Spawn(VfxType.Sparkle, transform.position + Vector3.up * 0.3f, new Color(0.75f, 0.85f, 1f), 0.8f);
            RunOverEnemies(); // pega na hora o monstro que acionou (ele está logo à frente)
            Triggered?.Invoke(this);
        }

        private void RunOverEnemies()
        {
            var enemies = EnemyBase.Active;
            float x = transform.position.x;
            // De trás para frente: Kill() remove o monstro da lista.
            for (int i = enemies.Count - 1; i >= 0; i--)
            {
                EnemyBase e = enemies[i];
                if (e == null || !e.IsAlive || e.Lane != lane) continue;
                float ex = e.transform.position.x;
                // Pega quem está na frente (até hitRadius) e quem já passou por trás do ratinho.
                if (ex - x > hitRadius) continue;
                e.Kill();
                Vfx.Spawn(VfxType.Spark, e.transform.position + Vector3.up * 0.4f, new Color(0.7f, 0.85f, 1f), 1f);
            }
        }
    }
}
