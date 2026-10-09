namespace SIRMED.UI
{
    using System.Collections;
    using SIRMED.Gameplay.Director;
    using SIRMED.Gameplay.Environment;
    using TMPro;
    using UnityEngine;

    /// <summary>
    /// RouteProgressHud — feedback del avance por la ruta de evacuación y cierre de la
    /// experiencia al llegar (checklist M07 "detectar checkpoint" y M08 "detectar entrada
    /// y finalizar evacuación").
    ///
    ///   • Punto de control alcanzado → aviso breve "Punto de control 1 de 2".
    ///   • Llegada al punto de encuentro → espera a que el Director termine sus consejos
    ///     (S7 + S9) y abre EndGamePanel.
    ///
    /// Va en un GameObject ACTIVO del Canvas (EndGamePanel empieza oculto y no puede
    /// escuchar eventos por sí mismo). El aviso es no bloqueante, como el banner del
    /// Director: se camina mientras se lee.
    ///
    /// Jerarquía sugerida:
    ///   RouteProgressHud          [este script]            (activo, sin Image)
    ///   └── ToastRoot [INACTIVO]  [Image]                  ← toastRoot
    ///       └── Text (TMP)                                 ← toastText
    /// </summary>
    public class RouteProgressHud : MonoBehaviour
    {
        [Header("Aviso de punto de control")]
        [SerializeField] private GameObject toastRoot;
        [SerializeField] private TextMeshProUGUI toastText;
        [SerializeField] private float toastDuration = 2.5f;
        [Tooltip("{0} = alcanzados, {1} = total.")]
        [SerializeField] private string toastFormat = "Punto de control {0} de {1} ✔";

        [Header("Cierre al llegar al punto de encuentro")]
        [SerializeField] private bool endGameOnArrival = true;
        [Tooltip("Segundos tras la llegada antes de abrir el cierre (además de esperar al Director).")]
        [SerializeField] private float endGameDelay = 1.5f;

        private EvacuationRouteController _route;
        private Coroutine _toastRoutine;
        private bool _closing;

        private void Awake()
        {
            if (toastRoot != null) toastRoot.SetActive(false);
        }

        private void OnDestroy() => Unsubscribe();

        private void Update()
        {
            // El singleton de la ruta puede aparecer después (orden de carga de escena).
            if (_route == null && EvacuationRouteController.Instance != null)
            {
                _route = EvacuationRouteController.Instance;
                _route.OnCheckpointReached += HandleCheckpoint;
                _route.OnArrivalAtPuntoDeEncuentro += HandleArrival;
            }
        }

        private void Unsubscribe()
        {
            if (_route == null) return;
            _route.OnCheckpointReached -= HandleCheckpoint;
            _route.OnArrivalAtPuntoDeEncuentro -= HandleArrival;
            _route = null;
        }

        private void HandleCheckpoint(int reached, int total)
        {
            if (toastRoot == null) return;
            if (toastText != null) toastText.text = string.Format(toastFormat, reached, total);
            if (_toastRoutine != null) StopCoroutine(_toastRoutine);
            _toastRoutine = StartCoroutine(ToastRoutine());
        }

        private IEnumerator ToastRoutine()
        {
            toastRoot.SetActive(true);
            yield return new WaitForSeconds(toastDuration);
            toastRoot.SetActive(false);
            _toastRoutine = null;
        }

        private void HandleArrival()
        {
            if (!endGameOnArrival || _closing) return;
            StartCoroutine(CloseRoutine());
        }

        private IEnumerator CloseRoutine()
        {
            _closing = true;
            yield return new WaitForSeconds(endGameDelay);

            // Dejar que el Director diga la llegada (S7) y la espera en zona segura (S9).
            while ((DirectorAdvicePanel.Instance != null && DirectorAdvicePanel.Instance.IsShowing) ||
                   (DirectorAdviceController.Instance != null && DirectorAdviceController.Instance.HasPendingAdvice))
                yield return null;

            EndGamePanel.Instance?.Show();
        }
    }
}
