using System.Collections.Generic;
using UnityEngine;

namespace SleepDefenders
{
    /// <summary>
    /// Carrinho de Controle Remoto — defesa de emergência de uso único. Custo 175.
    ///
    /// Prefab: BoxCollider (Trigger) + Rigidbody (IsKinematic) + Tag "Defender".
    /// Fica parado até um monstro da mesma faixa encostar (OnTriggerEnter com Tag "Enemy").
    /// Então arranca em alta velocidade para a direita, atropelando TODOS os monstros da faixa,
    /// e some ao sair do tapete. Monstros não param para atacá-lo (BlocksEnemies = false).
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
    public class RCCar : ToyBase
    {
        [Header("Arrancada")]
        [SerializeField, Min(1f)] private float topSpeed = 14f;
        [SerializeField, Min(0.01f)] private float accelerationTime = 0.35f;
        [Tooltip("Distância além da borda direita em que o carrinho desaparece.")]
        [SerializeField] private float exitDistance = 3f;
        [Tooltip("Inimigos voando ainda alto (Pesadelo Alado antes de pousar) não acionam o carrinho.")]
        [SerializeField] private bool ignoreAirborneForTrigger = true;
        [Tooltip("Transform do modelo que empina na arrancada (filho do Visual).")]
        [SerializeField] private Transform wheelieTarget;

        private Rigidbody body;
        private Quaternion wheelieBaseRotation;
        private bool racing;
        private float raceTime;
        private readonly HashSet<EnemyBase> alreadyHit = new HashSet<EnemyBase>();

        public bool IsRacing => racing;
        public override bool BlocksEnemies => false;

        protected override void Awake()
        {
            base.Awake();
            body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            GetComponent<BoxCollider>().isTrigger = true;
            if (wheelieTarget != null) wheelieBaseRotation = wheelieTarget.localRotation;
        }

        private void OnTriggerEnter(Collider other) => HandleContact(other);
        private void OnTriggerStay(Collider other)
        {
            // Cobre o caso de o monstro já estar encostado quando o carrinho é colocado.
            if (!racing) HandleContact(other);
        }

        private void HandleContact(Collider other)
        {
            if (Lane < 0 || !other.CompareTag(GameTags.Enemy)) return;
            EnemyBase enemy = other.GetComponentInParent<EnemyBase>();
            if (enemy == null || !enemy.IsAlive || enemy.Lane != Lane) return;

            if (!racing)
            {
                if (ignoreAirborneForTrigger && enemy.IsAirborne) return;
                Launch();
            }
            RunOver(enemy);
        }

        /// <summary>Dispara o carrinho. Pode ser chamado por script (ex.: testes).</summary>
        public void Launch()
        {
            if (racing) return;
            racing = true;
            raceTime = 0f;
            // A célula fica livre na hora: o jogador já pode colocar outro brinquedo ali.
            DetachFromGrid();
            Sfx.Play(SfxType.CarLaunch);
            if (animator != null) animator.SetMoving(true);
            Vfx.Spawn(VfxType.Puff, transform.position, new Color(0.8f, 0.8f, 0.8f, 0.8f), 0.6f);
        }

        private void RunOver(EnemyBase enemy)
        {
            if (!alreadyHit.Add(enemy)) return;
            enemy.Kill();
            Vfx.Spawn(VfxType.Spark, enemy.transform.position + Vector3.up * 0.4f, new Color(1f, 0.7f, 0.2f), 1f);
        }

        private void FixedUpdate()
        {
            if (!racing) return;
            raceTime += Time.fixedDeltaTime;
            float speed = topSpeed * Mathf.Clamp01(raceTime / accelerationTime);
            body.MovePosition(body.position + Vector3.right * (speed * Time.fixedDeltaTime));

            float limit = GridManager.Instance != null ? GridManager.Instance.RightEdgeX + exitDistance : 20f;
            if (body.position.x > limit) Remove();
        }

        private void LateUpdate()
        {
            // Empinadinha de arrancada: o nariz (+X) sobe e volta ao normal em ~0,6 s.
            if (racing && wheelieTarget != null)
            {
                float wheelie = Mathf.Sin(Mathf.Clamp01(raceTime / 0.6f) * Mathf.PI) * 18f;
                wheelieTarget.localRotation = Quaternion.Euler(0f, 0f, wheelie) * wheelieBaseRotation;
            }
        }

        // O carrinho não leva dano: monstros não o atacam e ele dispara ao primeiro contato.
        public override void TakeDamage(float amount) { }
    }
}
