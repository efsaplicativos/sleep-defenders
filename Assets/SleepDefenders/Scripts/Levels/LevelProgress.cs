using UnityEngine;

namespace SleepDefenders
{
    /// <summary>
    /// Progresso da campanha.
    ///   - Quantas noites estão liberadas fica salvo em PlayerPrefs (sobrevive entre sessões).
    ///   - Qual noite jogar e se ela começa direto (sem menu) ficam em campos estáticos,
    ///     que sobrevivem ao recarregamento da cena ("Tentar de Novo" / "Próxima Noite").
    /// </summary>
    public static class LevelProgress
    {
        private const string UnlockedKey = "SleepDefenders.NoitesLiberadas";

        /// <summary>Noite escolhida (0-based). -1 = a última liberada.</summary>
        public static int CurrentNight { get; set; } = -1;

        /// <summary>Se verdadeiro, a próxima cena carregada começa a noite direto, sem o menu.</summary>
        public static bool AutoStart { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            CurrentNight = -1;
            AutoStart = false;
        }

        /// <summary>Quantas noites já podem ser jogadas (sempre pelo menos 1).</summary>
        public static int UnlockedCount => Mathf.Max(1, PlayerPrefs.GetInt(UnlockedKey, 1));

        public static bool IsUnlocked(int index) => index >= 0 && index < UnlockedCount;

        /// <summary>Uma noite está concluída quando a seguinte já foi liberada.</summary>
        public static bool IsCompleted(int index) => index >= 0 && index < UnlockedCount - 1;

        /// <summary>Libera as noites até "count" (nunca diminui o progresso).</summary>
        public static void UnlockUpTo(int count)
        {
            if (count <= UnlockedCount) return;
            SetUnlockedCount(count);
        }

        /// <summary>Define o progresso diretamente (menu do Editor e testes).</summary>
        public static void SetUnlockedCount(int count)
        {
            PlayerPrefs.SetInt(UnlockedKey, Mathf.Max(1, count));
            PlayerPrefs.Save();
        }

        public static void ResetProgress()
        {
            PlayerPrefs.DeleteKey(UnlockedKey);
            PlayerPrefs.Save();
            CurrentNight = -1;
        }
    }
}
