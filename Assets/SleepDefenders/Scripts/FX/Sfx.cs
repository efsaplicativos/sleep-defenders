using System.Collections.Generic;
using UnityEngine;

namespace SleepDefenders
{
    public enum SfxType { Laser, Collect, Place, Hit, MonsterDie, ToyBreak, CarLaunch, Error, Select, WaveStart, Win, Lose, Chomp }

    /// <summary>
    /// Efeitos sonoros sintetizados em tempo de execução (nenhum arquivo de áudio necessário).
    /// Para usar áudios próprios, basta preencher a lista "overrides" no Inspector.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class Sfx : MonoBehaviour
    {
        [System.Serializable]
        public struct Override
        {
            public SfxType type;
            public AudioClip clip;
        }

        [SerializeField, Range(0f, 1f)] private float masterVolume = 0.55f;
        [SerializeField] private List<Override> overrides = new List<Override>();

        private static Sfx instance;
        private AudioSource source;
        private readonly Dictionary<SfxType, AudioClip> clips = new Dictionary<SfxType, AudioClip>();
        private readonly Dictionary<SfxType, float> lastPlayed = new Dictionary<SfxType, float>();
        private const int SampleRate = 44100;

        private void Awake()
        {
            instance = this;
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            Generate();
            foreach (Override o in overrides)
                if (o.clip != null) clips[o.type] = o.clip;
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        /// <summary>Toca um efeito. Chamadas repetidas no mesmo instante são agrupadas para não estourar o volume.</summary>
        public static void Play(SfxType type, float volume = 1f)
        {
            if (instance == null) return;
            instance.PlayInternal(type, volume);
        }

        private void PlayInternal(SfxType type, float volume)
        {
            if (!clips.TryGetValue(type, out AudioClip clip) || clip == null) return;
            float now = Time.unscaledTime;
            if (lastPlayed.TryGetValue(type, out float last) && now - last < 0.04f) return;
            lastPlayed[type] = now;
            source.pitch = 1f;
            source.PlayOneShot(clip, masterVolume * volume);
        }

        // ------------------------------------------------------------------ Síntese

        private void Generate()
        {
            clips[SfxType.Laser] = Make("laser", 0.16f, (t, d) => Square(Mathf.Lerp(1400f, 500f, t / d), t) * Env(t, d, 0.005f) * 0.35f);
            clips[SfxType.Collect] = Make("collect", 0.32f, (t, d) =>
            {
                float f = t < 0.08f ? 880f : t < 0.16f ? 1108.7f : 1318.5f; // arpejo Lá maior
                return Mathf.Sin(2f * Mathf.PI * f * t) * Env(t, d, 0.005f) * 0.5f;
            });
            clips[SfxType.Place] = Make("place", 0.18f, (t, d) => Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(220f, 90f, t / d) * t) * Env(t, d, 0.002f) * 0.8f);
            clips[SfxType.Hit] = Make("hit", 0.08f, (t, d) => Noise() * Env(t, d, 0.001f) * 0.35f);
            clips[SfxType.Chomp] = Make("chomp", 0.12f, (t, d) => (Noise() * 0.5f + Square(120f, t) * 0.5f) * Env(t, d, 0.002f) * 0.35f);
            clips[SfxType.MonsterDie] = Make("die", 0.45f, (t, d) =>
                Mathf.Sin(2f * Mathf.PI * (Mathf.Lerp(420f, 80f, t / d) + Mathf.Sin(t * 60f) * 30f) * t) * Env(t, d, 0.01f) * 0.5f);
            clips[SfxType.ToyBreak] = Make("break", 0.3f, (t, d) => (Noise() * 0.6f + Square(Mathf.Lerp(300f, 60f, t / d), t) * 0.4f) * Env(t, d, 0.002f) * 0.45f);
            clips[SfxType.CarLaunch] = Make("car", 0.7f, (t, d) => Saw(Mathf.Lerp(90f, 420f, t / d), t) * Env(t, d, 0.02f) * 0.3f);
            clips[SfxType.Error] = Make("error", 0.2f, (t, d) => Square(110f, t) * Env(t, d, 0.005f) * 0.25f);
            clips[SfxType.Select] = Make("select", 0.06f, (t, d) => Mathf.Sin(2f * Mathf.PI * 1500f * t) * Env(t, d, 0.002f) * 0.3f);
            clips[SfxType.WaveStart] = Make("wave", 1.1f, (t, d) =>
                (Saw(55f + Mathf.Sin(t * 5f) * 4f, t) * 0.5f + Mathf.Sin(2f * Mathf.PI * 110f * t) * 0.5f) * Env(t, d, 0.2f) * 0.35f);
            clips[SfxType.Win] = Make("win", 1.2f, (t, d) =>
            {
                float[] notes = { 523.3f, 659.3f, 784f, 1046.5f };
                int i = Mathf.Min(notes.Length - 1, (int)(t / 0.18f));
                return Mathf.Sin(2f * Mathf.PI * notes[i] * t) * Env(t, d, 0.01f) * 0.45f;
            });
            clips[SfxType.Lose] = Make("lose", 1.3f, (t, d) =>
            {
                float[] notes = { 392f, 370f, 349.2f, 261.6f };
                int i = Mathf.Min(notes.Length - 1, (int)(t / 0.3f));
                return Square(notes[i], t) * Env(t, d, 0.01f) * 0.22f;
            });
        }

        private delegate float Wave(float t, float duration);

        private static AudioClip Make(string name, float duration, Wave wave)
        {
            int count = Mathf.CeilToInt(duration * SampleRate);
            var data = new float[count];
            for (int i = 0; i < count; i++)
                data[i] = Mathf.Clamp(wave(i / (float)SampleRate, duration), -1f, 1f);
            AudioClip clip = AudioClip.Create("sfx_" + name, count, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static float Env(float t, float d, float attack) =>
            Mathf.Clamp01(t / attack) * Mathf.Clamp01(1f - t / d) * Mathf.Clamp01(1f - t / d);

        private static float Square(float freq, float t) => Mathf.Sin(2f * Mathf.PI * freq * t) >= 0f ? 1f : -1f;
        private static float Saw(float freq, float t) => 2f * (t * freq - Mathf.Floor(0.5f + t * freq));
        private static float Noise() => Random.value * 2f - 1f;
    }
}
