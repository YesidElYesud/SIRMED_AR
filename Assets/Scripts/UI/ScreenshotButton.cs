namespace SIRMED.UI
{
    using System;
    using System.Collections;
    using UnityEngine;
    using UnityEngine.UI;

    /// <summary>
    /// ScreenshotButton — botón del HUD que toma una foto de la vista AR (feed de
    /// cámara + contenido 3D) y la guarda en la galería del teléfono (GDD: "captura
    /// de pantalla a galería", roadmap §7.2 fase 6).
    ///
    /// La captura se hace con ScreenCapture.CaptureScreenshotAsTexture() al final del
    /// frame (lee el backbuffer ya compuesto, así que incluye el fondo de cámara de
    /// ARCameraBackground sin tener que leer la textura de cámara aparte). El guardado
    /// en galería usa el plugin NativeGallery (com.yasirkula.nativegallery), que en
    /// Android escribe vía MediaStore (sin permiso en API 29+, nuestro minSdk) y en iOS
    /// pide el permiso de "Agregar a Fotos" por su cuenta.
    ///
    /// La foto incluye la UI tal cual se ve en ese momento (HUD, paneles abiertos,
    /// minimapa, etc.) — decisión del usuario: es una captura de pantalla literal.
    /// El destello y el toast se muestran después de capturar, así que no salen en ella.
    ///
    /// Jerarquía sugerida en escena:
    ///   ScreenshotButton        [Button, Image, este script]   ← _captureButton
    ///   ScreenshotFlash         [Image blanca stretch, CanvasGroup alpha 0, sin Raycast Target] ← _flash (opcional)
    ///   ScreenshotToast         [Image + TMP hijo]              ← _savedToast (opcional, inactivo)
    /// </summary>
    public class ScreenshotButton : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private Button _captureButton;

        [Header("Feedback (opcional)")]
        [Tooltip("Destello blanco tras la captura (CanvasGroup sobre una Image blanca a pantalla completa).")]
        [SerializeField] private CanvasGroup _flash;
        [SerializeField] private float _flashDuration = 0.35f;

        [Tooltip("Mensaje breve \"Foto guardada\" que se muestra unos segundos tras guardar.")]
        [SerializeField] private GameObject _savedToast;
        [SerializeField] private float _toastDuration = 2f;

        [Header("Galería")]
        [Tooltip("Álbum/carpeta donde quedan las fotos en la galería del teléfono.")]
        [SerializeField] private string _albumName = "SIRMED AR";

        private bool _busy;

        // ── Lifecycle ─────────────────────────────────────────────────────────────
        private void Awake()
        {
            if (_captureButton != null)
                _captureButton.onClick.AddListener(TakePhoto);

            if (_flash != null) _flash.alpha = 0f;
            if (_savedToast != null) _savedToast.SetActive(false);
        }

        // ── API pública ───────────────────────────────────────────────────────────
        public void TakePhoto()
        {
            if (_busy) return;
            StartCoroutine(CaptureRoutine());
        }

        // ── Captura ───────────────────────────────────────────────────────────────
        private IEnumerator CaptureRoutine()
        {
            _busy = true;

            // Se lee el frame al terminar de componerse (cámara + 3D + UI overlay).
            yield return new WaitForEndOfFrame();
            Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();

            string fileName = $"SIRMED_{DateTime.Now:yyyyMMdd_HHmmss}.jpg";
            // SaveImageToGallery codifica la textura a bytes de forma síncrona antes de
            // volver, así que se puede destruir justo después.
            NativeGallery.SaveImageToGallery(shot, _albumName, fileName, OnSaved);
            Destroy(shot);

            if (_flash != null) yield return FlashRoutine();

            _busy = false;
        }

        private void OnSaved(bool success, string path)
        {
            if (success)
            {
                Debug.Log($"[Screenshot] Foto guardada: {path}");
                if (_savedToast != null) StartCoroutine(ToastRoutine());
            }
            else
            {
                Debug.LogWarning("[Screenshot] No se pudo guardar la foto (¿permiso de galería denegado?).");
            }
        }

        // ── Feedback ──────────────────────────────────────────────────────────────
        private IEnumerator FlashRoutine()
        {
            for (float t = 0f; t < _flashDuration; t += Time.unscaledDeltaTime)
            {
                _flash.alpha = 1f - t / _flashDuration;
                yield return null;
            }
            _flash.alpha = 0f;
        }

        private IEnumerator ToastRoutine()
        {
            _savedToast.SetActive(true);
            yield return new WaitForSecondsRealtime(_toastDuration);
            _savedToast.SetActive(false);
        }
    }
}
