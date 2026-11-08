using System;
using UnityEngine;

namespace SleepDefenders
{
    /// <summary>
    /// Classe base de todos os brinquedos defensores.
    ///
    /// Configuração esperada do prefab:
    ///   - Tag "Defender", Layer "Defender".
    ///   - Um Collider (BoxCollider) do tamanho aproximado de uma célula.
    ///   - Filho "Visual" com o modelo 3D (animado pelo ProceduralAnimator).
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class ToyBase : MonoBehaviour, IDamageable
    {
        [Header("Brinquedo")]
        [SerializeField] private string displayName = "Brinquedo";
        [Tooltip("Custo em Luz para posicionar.")]
        [SerializeField, Min(0)] private int cost = 50;
        [SerializeField, Min(1f)] private float maxHP = 100f;
        [Tooltip("Se falso, os monstros passam por ele sem parar para atacar (ex.: carrinho RC).")]
        [SerializeField] private bool blocksEnemies = true;

        [Header("Efeitos")]
        [SerializeField] private Color deathPuffColor = new Color(1f, 0.9f, 0.7f);

        protected ProceduralAnimator animator;
        protected HitFlash hitFlash;
        private bool isDestroyed;

        public string DisplayName => displayName;
        public int Cost => cost;
        public float MaxHP => maxHP;
        public float CurrentHP { get; private set; }
        /// <summary>Faixa (linha da grade) em que o brinquedo está.</summary>
        public int Lane { get; private set; } = -1;
        public int Column { get; private set; } = -1;
        public bool IsAlive => !isDestroyed && CurrentHP > 0f;
        public virtual bool BlocksEnemies => blocksEnemies;

        /// <summary>Disparado uma única vez quando o brinquedo é destruído ou removido.</summary>
        public event Action<ToyBase> Destroyed;

        /// <summary>
        /// Disparado quando QUALQUER brinquedo é quebrado pelos monstros (vida zerada).
        /// Guardar o brinquedo ou o Carrinho RC partir não contam. Usado nas estrelas da noite.
        /// </summary>
        public static event Action<ToyBase> Broken;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Broken = null;

        protected virtual void Awake()
        {
            CurrentHP = maxHP;
            animator = GetComponent<ProceduralAnimator>();
            hitFlash = GetComponent<HitFlash>();
            if (animator != null) animator.SetForward(Vector3.right);
        }

        /// <summary>Chamado pelo GridManager logo após instanciar o brinquedo na célula.</summary>
        public void Initialize(int lane, int column)
        {
            Lane = lane;
            Column = column;
            if (animator != null) animator.PlaySpawn();
            Vfx.Spawn(VfxType.Puff, transform.position + Vector3.up * 0.1f, new Color(1f, 1f, 1f, 0.8f), 0.7f);
            OnPlaced();
        }

        /// <summary>Gancho para as subclasses reagirem ao serem posicionadas.</summary>
        protected virtual void OnPlaced() { }

        public virtual void TakeDamage(float amount)
        {
            if (!IsAlive || amount <= 0f) return;
            CurrentHP -= amount;
            if (hitFlash != null) hitFlash.Flash();
            if (animator != null) animator.PlayHit();
            OnDamaged(amount);
            if (CurrentHP <= 0f) Die();
        }

        protected virtual void OnDamaged(float amount) { }

        /// <summary>Recupera vida (sem passar do máximo). Ex.: Urso perto da Caixinha de Música.</summary>
        public void Heal(float amount)
        {
            if (!IsAlive || amount <= 0f || CurrentHP >= MaxHP) return;
            CurrentHP = Mathf.Min(MaxHP, CurrentHP + amount);
            OnHealed(amount);
        }

        protected virtual void OnHealed(float amount) { }

        /// <summary>Destrói o brinquedo (vida zerada).</summary>
        public void Die()
        {
            if (isDestroyed) return;
            CurrentHP = 0f;
            Sfx.Play(SfxType.ToyBreak);
            Vfx.Spawn(VfxType.Puff, transform.position + Vector3.up * 0.4f, deathPuffColor);
            Broken?.Invoke(this);
            Despawn();
        }

        /// <summary>Remove o brinquedo sem efeito de "quebra" (usado pela ferramenta de remover).</summary>
        public void Remove()
        {
            if (isDestroyed) return;
            Vfx.Spawn(VfxType.Puff, transform.position + Vector3.up * 0.2f, new Color(1f, 1f, 1f, 0.7f), 0.6f);
            Despawn();
        }

        private void Despawn()
        {
            isDestroyed = true;
            OnDeath();
            Destroyed?.Invoke(this);
            Destroyed = null;
            Destroy(gameObject);
        }

        protected virtual void OnDeath() { }

        /// <summary>Notifica a grade e os ouvintes como se o brinquedo tivesse saído da célula, sem destruí-lo.</summary>
        protected void DetachFromGrid()
        {
            if (GridManager.Instance != null) GridManager.Instance.ReleaseCell(this);
        }
    }
}
