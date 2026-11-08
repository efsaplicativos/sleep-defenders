using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SleepDefenders
{
    /// <summary>
    /// Classe base dos Monstros do Escuro.
    ///
    /// Configuração esperada do prefab:
    ///   - Tag "Enemy", Layer "Enemy".
    ///   - CapsuleCollider (IsTrigger) + Rigidbody (IsKinematic = true).
    ///     O Rigidbody cinemático é o que faz os eventos OnTrigger* dispararem contra os brinquedos,
    ///     que são colliders estáticos (sem Rigidbody).
    ///
    /// Comportamento: anda em linha reta para -X (em direção à cama). Ao encostar num brinquedo
    /// da mesma faixa (OnTriggerEnter/Stay com Tag "Defender"), para e ataca em intervalos fixos.
    /// Quando o brinquedo é destruído, volta a andar.
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
    public class EnemyBase : MonoBehaviour, IDamageable
    {
        [Header("Monstro")]
        [SerializeField] private string displayName = "Monstro";
        [SerializeField, Min(1f)] private float maxHP = 100f;
        [Tooltip("Unidades por segundo (1 unidade = 1 célula).")]
        [SerializeField, Min(0f)] protected float moveSpeed = 0.45f;
        [SerializeField, Min(0f)] private float attackDamage = 20f;
        [SerializeField, Min(0.1f)] private float attackInterval = 1f;

        [Header("Efeitos")]
        [SerializeField] private Color deathPuffColor = new Color(0.35f, 0.2f, 0.55f, 0.9f);
        [SerializeField] private float deathDuration = 0.45f;

        private static readonly List<EnemyBase> active = new List<EnemyBase>();
        /// <summary>Monstros vivos na cena (usado para checar vitória e por testes).</summary>
        public static IReadOnlyList<EnemyBase> Active => active;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => active.Clear();

        protected Rigidbody body;
        protected ProceduralAnimator animator;
        private HitFlash hitFlash;
        private Collider mainCollider;
        private float attackTimer;
        private float bedLineX = float.NegativeInfinity;
        private bool reachedBed;
        private bool dead;

        public string DisplayName => displayName;
        public int Lane { get; private set; } = -1;
        public float MaxHP => maxHP;
        public float CurrentHP { get; private set; }
        public bool IsAlive => !dead && CurrentHP > 0f;
        /// <summary>Verdadeiro enquanto o monstro voa alto (ver WingedNightmare).</summary>
        public virtual bool IsAirborne => false;
        public ToyBase CurrentTarget { get; private set; }
        public bool IsAttacking => CurrentTarget != null;
        public float AttackDamage => attackDamage;

        public event Action<EnemyBase> Died;

        protected virtual void Awake()
        {
            CurrentHP = maxHP;
            body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            mainCollider = GetComponent<CapsuleCollider>();
            mainCollider.isTrigger = true;
            animator = GetComponent<ProceduralAnimator>();
            hitFlash = GetComponent<HitFlash>();
            if (animator != null) animator.SetForward(Vector3.left);
        }

        protected virtual void OnEnable()
        {
            if (!dead && !active.Contains(this)) active.Add(this);
        }

        protected virtual void OnDisable() => active.Remove(this);

        /// <summary>Chamado pelo EnemySpawner logo após instanciar.</summary>
        public virtual void Initialize(int lane, float bedLine)
        {
            Lane = lane;
            bedLineX = bedLine;
            if (animator != null) animator.PlaySpawn();
        }

        // ------------------------------------------------------------------ Movimento

        protected virtual float CurrentSpeed => moveSpeed;

        /// <summary>Sombras andam mais devagar na luz de um abajur (ver LightField). O Devorador de Luz não.</summary>
        protected virtual bool SlowedByLight => true;

        /// <summary>Velocidade de agora, já com o efeito da luz.</summary>
        public float EffectiveSpeed
        {
            get
            {
                float speed = CurrentSpeed;
                LightField light = LightField.Instance;
                if (SlowedByLight && light != null) speed *= light.EnemySpeedFactorAt(Lane, transform.position);
                return speed;
            }
        }

        /// <summary>
        /// Faixa preferida deste tipo de monstro entre as opções da onda (-1 = sem preferência, sorteia).
        /// Chamado no PREFAB pelo EnemySpawner antes de criar o monstro.
        /// </summary>
        public virtual int PreferredLane(IReadOnlyList<int> options) => -1;

        protected virtual void FixedUpdate()
        {
            if (!IsAlive) return;

            if (!GameManager.IsPlaying)
            {
                if (animator != null) animator.SetMoving(false);
                return;
            }

            if (CurrentTarget != null && !CurrentTarget.IsAlive) CurrentTarget = null;

            bool walking = CurrentTarget == null;
            if (walking) Move();
            if (animator != null) animator.SetMoving(walking);

            if (!reachedBed && body.position.x <= bedLineX)
            {
                reachedBed = true;
                if (GameManager.Instance != null) GameManager.Instance.EnemyReachedBed(this);
            }
        }

        /// <summary>Movimento linear para a esquerda (rumo à cama).</summary>
        protected virtual void Move()
        {
            body.MovePosition(body.position + Vector3.left * (EffectiveSpeed * Time.fixedDeltaTime));
        }

        // ------------------------------------------------------------------ Ataque

        private void Update()
        {
            if (!IsAlive || CurrentTarget == null || !GameManager.IsPlaying) return;

            attackTimer -= Time.deltaTime;
            if (attackTimer > 0f) return;
            attackTimer = attackInterval;

            if (!CurrentTarget.IsAlive)
            {
                CurrentTarget = null;
                return;
            }
            if (animator != null) animator.PlayAttack();
            Sfx.Play(SfxType.Chomp, 0.5f);
            CurrentTarget.TakeDamage(attackDamage);
        }

        private void OnTriggerEnter(Collider other) => TryEngage(other);

        private void OnTriggerStay(Collider other)
        {
            if (CurrentTarget == null) TryEngage(other);
        }

        /// <summary>Detecção de colisão com brinquedos: decide se o monstro para e ataca.</summary>
        private void TryEngage(Collider other)
        {
            if (!IsAlive || CurrentTarget != null || !other.CompareTag(GameTags.Defender)) return;

            ToyBase toy = other.GetComponentInParent<ToyBase>();
            if (toy == null || !toy.IsAlive || !toy.BlocksEnemies || toy.Lane != Lane) return;
            // Só ataca o que está à frente (à esquerda); ignora brinquedos que ficaram para trás.
            if (toy.transform.position.x > transform.position.x + 0.05f) return;
            if (!ShouldEngage(toy)) return;

            CurrentTarget = toy;
            attackTimer = attackInterval * 0.5f;
            OnStartAttack(toy);
        }

        /// <summary>Permite às subclasses ignorar certos brinquedos (ex.: Pesadelo Alado sobrevoa o primeiro).</summary>
        protected virtual bool ShouldEngage(ToyBase toy) => true;

        protected virtual void OnStartAttack(ToyBase toy) { }

        // ------------------------------------------------------------------ Dano e morte

        public virtual void TakeDamage(float amount)
        {
            if (!IsAlive || amount <= 0f) return;
            CurrentHP -= amount;
            if (hitFlash != null) hitFlash.Flash();
            if (animator != null) animator.PlayHit();
            Sfx.Play(SfxType.Hit, 0.5f);
            OnDamaged(amount);
            if (CurrentHP <= 0f) Die();
        }

        protected virtual void OnDamaged(float amount) { }

        /// <summary>Morte instantânea (atropelado pelo carrinho RC).</summary>
        public void Kill()
        {
            if (!IsAlive) return;
            CurrentHP = 0f;
            Die();
        }

        private void Die()
        {
            if (dead) return;
            dead = true;
            CurrentHP = 0f;
            CurrentTarget = null;
            active.Remove(this);
            mainCollider.enabled = false;
            Sfx.Play(SfxType.MonsterDie, 0.7f);
            Vfx.Spawn(VfxType.Puff, transform.position + Vector3.up * 0.5f, deathPuffColor, 1.2f);
            Died?.Invoke(this);
            StartCoroutine(DeathRoutine());
        }

        private IEnumerator DeathRoutine()
        {
            Transform visual = animator != null && animator.Visual != null ? animator.Visual : transform;
            if (animator != null) animator.enabled = false;
            Vector3 startScale = visual.localScale;
            Vector3 startPos = visual.localPosition;
            for (float t = 0f; t < deathDuration; t += Time.deltaTime)
            {
                float k = t / deathDuration;
                visual.localScale = startScale * (1f - k * k);
                visual.localPosition = startPos + Vector3.up * (k * 0.4f);
                visual.localRotation *= Quaternion.Euler(0f, 540f * Time.deltaTime, 0f);
                yield return null;
            }
            Destroy(gameObject);
        }
    }
}
