using UnityEngine;

namespace SleepDefenders
{
    /// <summary>
    /// Monstro do Armário — tanque inimigo. Lento e com vida altíssima (padrão 450 HP).
    /// Quando fica com pouca vida, se enfurece e acelera um pouco.
    /// </summary>
    public class ClosetMonster : EnemyBase
    {
        [Header("Fúria")]
        [SerializeField, Range(0f, 1f)] private float enrageAtHealthFraction = 0.3f;
        [SerializeField, Min(1f)] private float enragedSpeedMultiplier = 1.6f;

        private bool enraged;

        protected override float CurrentSpeed => enraged ? moveSpeed * enragedSpeedMultiplier : moveSpeed;

        protected override void OnDamaged(float amount)
        {
            if (!enraged && CurrentHP <= MaxHP * enrageAtHealthFraction)
            {
                enraged = true;
                Vfx.Spawn(VfxType.Puff, transform.position + Vector3.up * 1.2f, new Color(1f, 0.3f, 0.3f, 0.8f), 0.8f);
            }
        }
    }
}
