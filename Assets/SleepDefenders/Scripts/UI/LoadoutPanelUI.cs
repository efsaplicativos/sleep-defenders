using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SleepDefenders
{
    /// <summary>
    /// Tela de preparo da noite (antes de começar):
    ///   - Mostra os monstros que vêm nesta noite, com uma dica de como lidar com cada um.
    ///   - O jogador escolhe quais brinquedos levar na bolsa (ex.: 4 de 6). Só os escolhidos viram cards.
    /// A escolha fica salva por noite e volta em "Tentar de Novo".
    /// </summary>
    public class LoadoutPanelUI : MonoBehaviour
    {
        [Header("Textos")]
        [SerializeField] private TMP_Text titleLabel;
        [SerializeField] private TMP_Text counterLabel;
        [SerializeField] private TMP_Text infoLabel;

        [Header("Monstros da noite")]
        [SerializeField] private List<GameObject> monsterSlots = new List<GameObject>();
        [SerializeField] private List<Image> monsterIcons = new List<Image>();
        [SerializeField] private List<TMP_Text> monsterNames = new List<TMP_Text>();
        [SerializeField] private List<TMP_Text> monsterHints = new List<TMP_Text>();

        [Header("Brinquedos")]
        [SerializeField] private List<LoadoutToyButton> toyButtons = new List<LoadoutToyButton>();

        [Header("Botões")]
        [SerializeField] private Button backButton;
        [SerializeField] private Button startButton;
        [SerializeField] private TMP_Text startLabel;

        private readonly List<ToyDefinition> selection = new List<ToyDefinition>();
        private int slots;
        private string defaultInfo = "";

        public bool IsOpen => gameObject.activeSelf;
        /// <summary>Brinquedos marcados agora (ainda não aplicados).</summary>
        public IReadOnlyList<ToyDefinition> Selection => selection;
        public int Slots => slots;
        public IReadOnlyList<LoadoutToyButton> ToyButtons => toyButtons;
        /// <summary>Monstros mostrados na prévia (na ordem em que aparecem na noite).</summary>
        public List<EnemyBase> PreviewedMonsters { get; } = new List<EnemyBase>();

        public event Action<bool> OpenChanged;

        private void Awake()
        {
            if (backButton != null) backButton.onClick.AddListener(() => { Sfx.Play(SfxType.Select); Close(); });
            if (startButton != null) startButton.onClick.AddListener(() => Confirm());
            foreach (LoadoutToyButton b in toyButtons)
                if (b != null) b.Clicked += OnToyClicked;
        }

        // ------------------------------------------------------------------ Abrir / fechar

        /// <summary>Abre a tela para a noite preparada no GameManager, com a bolsa atual já marcada.</summary>
        public void Open()
        {
            GameManager game = GameManager.Instance;
            NightDefinition night = game != null ? game.Night : null;
            if (night == null) return;
            Show(night, game.Campaign, game.NightIndex, game.Loadout);
            OpenChanged?.Invoke(true);
        }

        /// <summary>Preenche a tela com a noite e a bolsa indicadas (também serve para pré-visualizar no Editor).</summary>
        public void Show(NightDefinition night, Campaign campaign, int nightIndex, IEnumerable<ToyDefinition> bag)
        {
            if (night == null) return;
            gameObject.SetActive(true);
            slots = night.ToySlots;
            selection.Clear();
            if (bag != null)
                foreach (ToyDefinition t in bag)
                    if (t != null && night.toys.Contains(t) && !selection.Contains(t) && selection.Count < slots) selection.Add(t);

            List<string> news = campaign != null ? campaign.Novelties(nightIndex) : new List<string>();
            if (titleLabel != null) titleLabel.text = $"{night.title}: {night.subtitle}";

            ShowMonsters(night, news, nightIndex > 0);
            for (int i = 0; i < toyButtons.Count; i++)
            {
                if (toyButtons[i] == null) continue;
                ToyDefinition def = i < night.toys.Count ? night.toys[i] : null;
                toyButtons[i].Show(def, def != null && selection.Contains(def), def != null && nightIndex > 0 && news.Contains(def.DisplayName));
            }

            defaultInfo = night.tip;
            SetInfo(defaultInfo);
            if (startLabel != null) startLabel.text = "Começar " + night.title;
            Refresh();
        }

        public void Close()
        {
            if (!gameObject.activeSelf) return;
            gameObject.SetActive(false);
            OpenChanged?.Invoke(false);
        }

        private void ShowMonsters(NightDefinition night, List<string> news, bool markNew)
        {
            PreviewedMonsters.Clear();
            foreach (Wave w in night.waves)
                foreach (EnemySpawnEntry e in w.enemies)
                    if (e != null && e.prefab != null && !PreviewedMonsters.Contains(e.prefab)) PreviewedMonsters.Add(e.prefab);

            for (int i = 0; i < monsterSlots.Count; i++)
            {
                EnemyBase m = i < PreviewedMonsters.Count ? PreviewedMonsters[i] : null;
                if (monsterSlots[i] != null) monsterSlots[i].SetActive(m != null);
                if (m == null) continue;
                if (i < monsterIcons.Count && monsterIcons[i] != null)
                {
                    monsterIcons[i].sprite = m.Icon;
                    monsterIcons[i].enabled = m.Icon != null;
                }
                if (i < monsterNames.Count && monsterNames[i] != null)
                    monsterNames[i].text = markNew && news.Contains(m.DisplayName)
                        ? $"{m.DisplayName} <color=#FF8FB1>NOVO!</color>"
                        : m.DisplayName;
                if (i < monsterHints.Count && monsterHints[i] != null) monsterHints[i].text = m.Hint;
            }
        }

        // ------------------------------------------------------------------ Escolha

        private void OnToyClicked(LoadoutToyButton button)
        {
            if (button == null || button.Definition == null) return;
            Toggle(button.Definition);
            SetInfo($"<color=#FFD54A>{button.Definition.DisplayName}</color>: {button.Definition.description}");
        }

        /// <summary>Coloca ou tira um brinquedo da bolsa. Falso se a bolsa já estiver cheia.</summary>
        public bool Toggle(ToyDefinition def)
        {
            if (def == null) return false;
            if (selection.Contains(def))
            {
                selection.Remove(def);
            }
            else if (selection.Count < slots)
            {
                selection.Add(def);
            }
            else
            {
                Sfx.Play(SfxType.Error);
                SetInfo("<color=#FFB3B3>A bolsa está cheia!</color> Tire um brinquedo antes de pegar outro.");
                return false;
            }

            Sfx.Play(SfxType.Select);
            foreach (LoadoutToyButton b in toyButtons)
                if (b != null && b.Definition == def) b.SetSelected(selection.Contains(def));
            Refresh();
            return true;
        }

        /// <summary>Aplica a bolsa e começa a noite. Falso se a bolsa não estiver completa.</summary>
        public bool Confirm()
        {
            GameManager game = GameManager.Instance;
            if (game == null || selection.Count != slots || !game.SetLoadout(selection))
            {
                Sfx.Play(SfxType.Error);
                return false;
            }
            Close();
            game.StartGame();
            return true;
        }

        private void Refresh()
        {
            bool full = selection.Count == slots;
            if (counterLabel != null)
                counterLabel.text = full
                    ? $"Bolsa pronta: {selection.Count}/{slots}"
                    : $"Escolha {slots} brinquedos: {selection.Count}/{slots}";
            if (startButton != null) startButton.interactable = full;
        }

        private void SetInfo(string text)
        {
            if (infoLabel != null) infoLabel.text = text;
        }
    }
}
