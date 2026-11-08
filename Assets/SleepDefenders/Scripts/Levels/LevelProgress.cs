using System.Collections.Generic;
using UnityEngine;

namespace SleepDefenders
{
    /// <summary>
    /// Progresso da campanha.
    ///   - Quantas noites estão liberadas, as estrelas de cada noite e a última bolsa de brinquedos
    ///     escolhida em cada noite ficam salvas em PlayerPrefs (sobrevivem entre sessões).
    ///   - Qual noite jogar, se ela começa direto (sem menu) e se a tela de preparo abre sozinha
    ///     ficam em campos estáticos, que sobrevivem ao recarregamento da cena.
    /// </summary>
    public static class LevelProgress
    {
        private const string UnlockedKey = "SleepDefenders.NoitesLiberadas";
        private const string StarsKeyPrefix = "SleepDefenders.Estrelas.";
        private const string LoadoutKeyPrefix = "SleepDefenders.Bolsa.";
        /// <summary>Quantas noites os comandos de limpar/copiar o progresso cobrem.</summary>
        private const int MaxNights = 20;

        public const int MaxStars = 3;

        /// <summary>Noite escolhida (0-based). -1 = a última liberada.</summary>
        public static int CurrentNight { get; set; } = -1;

        /// <summary>Se verdadeiro, a próxima cena carregada começa a noite direto, sem o menu.</summary>
        public static bool AutoStart { get; set; }

        /// <summary>Se verdadeiro, a próxima cena carregada abre a tela de preparo (escolha dos brinquedos).</summary>
        public static bool OpenLoadout { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            CurrentNight = -1;
            AutoStart = false;
            OpenLoadout = false;
        }

        // ------------------------------------------------------------------ Noites liberadas

        /// <summary>Quantas noites já podem ser jogadas (sempre pelo menos 1).</summary>
        public static int UnlockedCount => Mathf.Max(1, PlayerPrefs.GetInt(UnlockedKey, 1));

        public static bool IsUnlocked(int index) => index >= 0 && index < UnlockedCount;

        /// <summary>Uma noite está concluída quando a seguinte já foi liberada (ou quando já ganhou estrelas).</summary>
        public static bool IsCompleted(int index) => index >= 0 && (index < UnlockedCount - 1 || GetStars(index) > 0);

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

        // ------------------------------------------------------------------ Estrelas

        /// <summary>Melhor resultado da noite: 0 (nunca venceu) a 3 estrelas.</summary>
        public static int GetStars(int index) =>
            index < 0 ? 0 : Mathf.Clamp(PlayerPrefs.GetInt(StarsKeyPrefix + index, 0), 0, MaxStars);

        /// <summary>Guarda as estrelas se forem um recorde. Retorna true quando melhorou.</summary>
        public static bool RecordStars(int index, int stars)
        {
            stars = Mathf.Clamp(stars, 0, MaxStars);
            if (index < 0 || stars <= GetStars(index)) return false;
            PlayerPrefs.SetInt(StarsKeyPrefix + index, stars);
            PlayerPrefs.Save();
            return true;
        }

        // ------------------------------------------------------------------ Bolsa de brinquedos

        /// <summary>
        /// Última bolsa escolhida na noite: posições na lista de brinquedos da noite (NightDefinition.toys).
        /// null se nunca escolheu.
        /// </summary>
        public static List<int> GetLoadout(int index)
        {
            if (index < 0) return null;
            string raw = PlayerPrefs.GetString(LoadoutKeyPrefix + index, "");
            if (string.IsNullOrEmpty(raw)) return null;
            var result = new List<int>();
            foreach (string part in raw.Split(','))
                if (int.TryParse(part, out int i) && i >= 0 && !result.Contains(i)) result.Add(i);
            return result.Count > 0 ? result : null;
        }

        public static void SaveLoadout(int index, IEnumerable<int> toyIndexes)
        {
            if (index < 0) return;
            string raw = toyIndexes != null ? string.Join(",", toyIndexes) : "";
            if (string.IsNullOrEmpty(raw)) PlayerPrefs.DeleteKey(LoadoutKeyPrefix + index);
            else PlayerPrefs.SetString(LoadoutKeyPrefix + index, raw);
            PlayerPrefs.Save();
        }

        // ------------------------------------------------------------------ Limpar / copiar

        public static void ResetProgress()
        {
            PlayerPrefs.DeleteKey(UnlockedKey);
            for (int i = 0; i < MaxNights; i++)
            {
                PlayerPrefs.DeleteKey(StarsKeyPrefix + i);
                PlayerPrefs.DeleteKey(LoadoutKeyPrefix + i);
            }
            PlayerPrefs.Save();
            CurrentNight = -1;
        }

        /// <summary>Cópia de todo o progresso salvo (os testes usam para devolver o save do jogador intacto).</summary>
        public sealed class Snapshot
        {
            internal bool hasUnlocked;
            internal int unlocked;
            internal readonly int[] stars = new int[MaxNights];
            internal readonly string[] loadouts = new string[MaxNights];
        }

        public static Snapshot TakeSnapshot()
        {
            var s = new Snapshot { hasUnlocked = PlayerPrefs.HasKey(UnlockedKey), unlocked = PlayerPrefs.GetInt(UnlockedKey, 1) };
            for (int i = 0; i < MaxNights; i++)
            {
                s.stars[i] = PlayerPrefs.GetInt(StarsKeyPrefix + i, 0);
                s.loadouts[i] = PlayerPrefs.GetString(LoadoutKeyPrefix + i, "");
            }
            return s;
        }

        public static void RestoreSnapshot(Snapshot s)
        {
            if (s == null) return;
            if (s.hasUnlocked) PlayerPrefs.SetInt(UnlockedKey, s.unlocked);
            else PlayerPrefs.DeleteKey(UnlockedKey);
            for (int i = 0; i < MaxNights; i++)
            {
                if (s.stars[i] > 0) PlayerPrefs.SetInt(StarsKeyPrefix + i, s.stars[i]);
                else PlayerPrefs.DeleteKey(StarsKeyPrefix + i);
                if (!string.IsNullOrEmpty(s.loadouts[i])) PlayerPrefs.SetString(LoadoutKeyPrefix + i, s.loadouts[i]);
                else PlayerPrefs.DeleteKey(LoadoutKeyPrefix + i);
            }
            PlayerPrefs.Save();
        }
    }
}
