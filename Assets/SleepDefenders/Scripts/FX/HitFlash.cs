using System.Collections;
using UnityEngine;

namespace SleepDefenders
{
    /// <summary>
    /// Pisca o modelo com uma cor ao levar dano, usando MaterialPropertyBlock
    /// (não cria cópias de material). Funciona tanto com o shader URP/Lit quanto
    /// com os shaders do glTFast usados pelos modelos do Tripo.
    /// </summary>
    public class HitFlash : MonoBehaviour
    {
        [SerializeField] private Color flashColor = new Color(1f, 0.45f, 0.45f, 1f);
        [SerializeField] private float duration = 0.1f;

        private static readonly int[] ColorIds =
        {
            Shader.PropertyToID("_BaseColor"),        // URP Lit/Unlit
            Shader.PropertyToID("baseColorFactor"),   // glTFast (Shader Graph)
            Shader.PropertyToID("_Color"),            // shaders legados
        };

        private Renderer[] renderers;
        private MaterialPropertyBlock block;
        private Coroutine routine;

        private void Awake()
        {
            renderers = GetComponentsInChildren<Renderer>(true);
            block = new MaterialPropertyBlock();
        }

        public void Flash() => Flash(flashColor);

        public void Flash(Color color)
        {
            if (!isActiveAndEnabled) return;
            if (routine != null) StopCoroutine(routine);
            routine = StartCoroutine(FlashRoutine(color));
        }

        private IEnumerator FlashRoutine(Color color)
        {
            Apply(color);
            yield return new WaitForSeconds(duration);
            Clear();
            routine = null;
        }

        private void Apply(Color color)
        {
            block.Clear();
            foreach (int id in ColorIds) block.SetColor(id, color);
            foreach (Renderer r in renderers)
                if (r != null && !(r is ParticleSystemRenderer) && !(r is TrailRenderer))
                    r.SetPropertyBlock(block);
        }

        private void Clear()
        {
            foreach (Renderer r in renderers)
                if (r != null) r.SetPropertyBlock(null);
        }

        private void OnDisable() => Clear();
    }
}
