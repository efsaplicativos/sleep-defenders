using System;
using System.Collections.Generic;
using UnityEngine;

namespace SleepDefenders
{
    /// <summary>
    /// Uma "fase" do jogo: uma noite com suas ondas, os brinquedos liberados e a Luz inicial.
    /// </summary>
    [Serializable]
    public class NightDefinition
    {
        [Tooltip("Nome curto mostrado no menu e no HUD (ex.: \"Noite 1\").")]
        public string title = "Noite 1";
        [Tooltip("Nome temático da noite (ex.: \"Asas no Escuro\").")]
        public string subtitle = "";
        [Tooltip("Dica mostrada no menu e no começo da noite.")]
        [TextArea(2, 4)] public string tip = "";

        [Min(0)] public int startingLight = 150;
        [Tooltip("Tempo de preparo antes da primeira onda.")]
        [Min(0f)] public float initialDelay = 25f;
        [Tooltip("Raios de luar caem do teto nesta noite?")]
        public bool moonlight = true;

        [Tooltip("Cards disponíveis nesta noite (vazio = todos).")]
        public List<ToyDefinition> toys = new List<ToyDefinition>();
        public List<Wave> waves = new List<Wave>();

        /// <summary>Monstros que aparecem nas ondas desta noite (sem repetição).</summary>
        public HashSet<EnemyBase> EnemyTypes()
        {
            var set = new HashSet<EnemyBase>();
            foreach (Wave w in waves)
                foreach (EnemySpawnEntry e in w.enemies)
                    if (e != null && e.prefab != null) set.Add(e.prefab);
            return set;
        }

        /// <summary>Tempo aproximado da noite em segundos (preparo + ondas), sem contar a travessia.</summary>
        public float EstimatedDuration()
        {
            float t = initialDelay;
            for (int i = 0; i < waves.Count; i++)
            {
                if (i > 0) t += waves[i].delayBeforeWave;
                t += waves[i].TotalCount * waves[i].spawnInterval;
            }
            return t;
        }
    }

    /// <summary>
    /// Campanha: a sequência de noites do jogo. Fica em Generated/Data/Campanha.asset
    /// (criada pelo construtor) e é lida pelo GameManager.
    /// </summary>
    [CreateAssetMenu(menuName = "Sleep Defenders/Campanha", fileName = "Campanha")]
    public class Campaign : ScriptableObject
    {
        public List<NightDefinition> nights = new List<NightDefinition>();

        public int Count => nights.Count;

        public NightDefinition Get(int index) =>
            nights.Count == 0 ? null : nights[Mathf.Clamp(index, 0, nights.Count - 1)];

        /// <summary>
        /// Brinquedos e monstros que aparecem pela primeira vez na noite "index"
        /// (usado no menu e no aviso de início da noite).
        /// </summary>
        public List<string> Novelties(int index)
        {
            var result = new List<string>();
            NightDefinition night = Get(index);
            if (night == null) return result;

            var seenToys = new HashSet<ToyDefinition>();
            var seenEnemies = new HashSet<EnemyBase>();
            for (int i = 0; i < index && i < nights.Count; i++)
            {
                foreach (ToyDefinition t in nights[i].toys) if (t != null) seenToys.Add(t);
                seenEnemies.UnionWith(nights[i].EnemyTypes());
            }

            foreach (ToyDefinition t in night.toys)
                if (t != null && !seenToys.Contains(t)) result.Add(t.DisplayName);
            foreach (EnemyBase e in night.EnemyTypes())
                if (!seenEnemies.Contains(e)) result.Add(e.DisplayName);
            return result;
        }
    }
}
