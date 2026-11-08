using UnityEngine;

namespace SleepDefenders
{
    /// <summary>
    /// Nomes de Tags usados pelo jogo. Precisam existir no Tag Manager
    /// (o menu "Sleep Defenders > Construir Jogo" cria todas automaticamente).
    /// </summary>
    public static class GameTags
    {
        public const string Enemy = "Enemy";
        public const string Defender = "Defender";
        public const string Projectile = "Projectile";
        public const string LightOrb = "LightOrb";
    }

    /// <summary>
    /// Layers de física. Centralizar aqui evita "strings mágicas" espalhadas
    /// e permite montar LayerMasks em um único lugar.
    /// </summary>
    public static class GameLayers
    {
        public const string GridName = "Grid";
        public const string EnemyName = "Enemy";
        public const string DefenderName = "Defender";
        public const string ProjectileName = "Projectile";
        public const string PickupName = "Pickup";

        public static int Grid => LayerMask.NameToLayer(GridName);
        public static int Enemy => LayerMask.NameToLayer(EnemyName);
        public static int Defender => LayerMask.NameToLayer(DefenderName);
        public static int Projectile => LayerMask.NameToLayer(ProjectileName);
        public static int Pickup => LayerMask.NameToLayer(PickupName);

        public static int GridMask => LayerMask.GetMask(GridName);
        public static int EnemyMask => LayerMask.GetMask(EnemyName);
        public static int DefenderMask => LayerMask.GetMask(DefenderName);
        public static int PickupMask => LayerMask.GetMask(PickupName);

        /// <summary>
        /// Desliga pares de colisão que nunca interessam ao jogo. Menos pares = menos
        /// callbacks de trigger e menos trabalho para o PhysX.
        /// </summary>
        public static void ConfigureCollisionMatrix()
        {
            int grid = Grid, enemy = Enemy, defender = Defender, projectile = Projectile, pickup = Pickup;
            if (grid < 0 || enemy < 0 || defender < 0 || projectile < 0 || pickup < 0)
            {
                Debug.LogWarning("[SleepDefenders] Layers não configuradas. Rode 'Sleep Defenders > Construir Jogo'.");
                return;
            }

            // Inimigos: só interessam brinquedos e projéteis.
            Physics.IgnoreLayerCollision(enemy, enemy, true);
            Physics.IgnoreLayerCollision(enemy, grid, true);
            Physics.IgnoreLayerCollision(enemy, pickup, true);

            // Projéteis: só interessam inimigos.
            Physics.IgnoreLayerCollision(projectile, projectile, true);
            Physics.IgnoreLayerCollision(projectile, defender, true);
            Physics.IgnoreLayerCollision(projectile, grid, true);
            Physics.IgnoreLayerCollision(projectile, pickup, true);

            // Brinquedos e orbes de luz não precisam de contatos entre si nem com a grade
            // (a grade e os orbes são usados apenas por Raycasts do mouse).
            Physics.IgnoreLayerCollision(defender, defender, true);
            Physics.IgnoreLayerCollision(defender, grid, true);
            Physics.IgnoreLayerCollision(defender, pickup, true);
            Physics.IgnoreLayerCollision(pickup, pickup, true);
            Physics.IgnoreLayerCollision(pickup, grid, true);
        }
    }

    /// <summary>Qualquer coisa que pode levar dano (brinquedos e monstros).</summary>
    public interface IDamageable
    {
        bool IsAlive { get; }
        void TakeDamage(float amount);
    }
}
