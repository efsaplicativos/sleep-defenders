using UnityEngine;

namespace SleepDefenders
{
    /// <summary>Mantém um quad (ex.: halo de brilho) sempre virado para a câmera.</summary>
    public class Billboard : MonoBehaviour
    {
        private Camera cam;

        private void LateUpdate()
        {
            if (cam == null) cam = Camera.main;
            if (cam == null) return;
            transform.rotation = cam.transform.rotation;
        }
    }
}
