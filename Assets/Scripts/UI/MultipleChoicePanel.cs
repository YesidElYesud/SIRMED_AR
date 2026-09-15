namespace SIRMED.UI
{
    using System;
    using System.Collections.Generic;
    using SIRMED.Gameplay.Dialogue;
    using TMPro;
    using UnityEngine;
    using UnityEngine.UI;

    /// <summary>
    /// MultipleChoicePanel — Componente embebible de selección múltiple.
    ///
    /// Responsabilidades:
    ///   - Generar botones con prefijo A) B) C) desde DialogueOption[].
    ///   - Deshabilitar todos los botones tras la primera selección.
    ///   - Mostrar feedback con color correcto/incorrecto.
    ///   - Opcionalmente resaltar la respuesta correcta tras un error.
    ///   - Exponer eventos OnCorrect / OnWrong para que el panel padre reaccione.
    ///
    /// NO es singleton: se embebe dentro de SiataCallPanel.
    ///
    /// Prefab del botón requerido (OptionButton):
    ///   • Raíz      → Button + Image (para el color de fondo)
    ///   • Hijo      → TextMeshProUGUI (para el texto "A) opción...")
    /// </summary>
    public class MultipleChoicePanel : MonoBehaviour
    {
        // ── Eventos públicos ──────────────────────────────────────────────────────
        /// <summary>Disparado cuando el jugador selecciona la opción correcta.</summary>
        public event Action OnCorrect;

        /// <summary>Disparado cuando el jugador selecciona una opción incorrecta.</summary>
        public event Action OnWrong;

        /// <summary>
        /// Disparado cuando el jugador pulsa "Intentar de nuevo".
        /// El panel padre puede suscribirse para cancelar su propia lógica de auto-reintento.
        /// </summary>
        public event Action OnRetry;

        // ── Inspector: Opciones ───────────────────────────────────────────────────
        [Header("Generación de opciones")]
        [Tooltip("Transform con Layout Group donde se instancian los botones.")]
        public Transform optionsContainer;

        [Tooltip("Prefab del botón. Requiere Button + Image en raíz y TextMeshProUGUI hijo.")]
        public GameObject optionButtonPrefab;

        // ── Inspector: Feedback ───────────────────────────────────────────────────
        [Header("Sección de feedback")]
        [Tooltip("GameObject que engloba el feedback. Se activa tras responder.")]
        public GameObject feedbackSection;

        [Tooltip("Texto pedagógico de la opción elegida.")]
        public TextMeshProUGUI feedbackText;

        [Tooltip("Fondo del área de feedback (cambia de color).")]
        public Image feedbackBackground;

        [Tooltip("Botón 'Intentar de nuevo'. Solo se muestra tras respuesta incorrecta.")]
        public Button retryButton;

        // ── Inspector: Colores ────────────────────────────────────────────────────
        [Header("Colores")]
        public Color correctFeedbackColor = new Color(0.13f, 0.69f, 0.30f, 0.95f);
        public Color incorrectFeedbackColor = new Color(0.78f, 0.18f, 0.18f, 0.95f);
        public Color selectedButtonColor = new Color(0.25f, 0.47f, 0.78f, 1f);
        public Color correctHighlightColor = new Color(0.13f, 0.69f, 0.30f, 1f);

        // ── Inspector: Comportamiento ─────────────────────────────────────────────
        [Header("Comportamiento")]
        [Tooltip("Si true, tras una respuesta incorrecta se resalta el botón correcto en verde.")]
        public bool showCorrectAfterWrong = true;

        // ── Internos ──────────────────────────────────────────────────────────────
        private static readonly string[] _letters = { "A", "B", "C", "D", "E", "F" };

        private List<GameObject> _spawnedButtons = new List<GameObject>();
        private List<Image> _buttonImages = new List<Image>();
        private DialogueOption[] _currentOptions;
        private bool _answered = false;

        // ── Lifecycle ─────────────────────────────────────────────────────────────
        private void Awake()
        {
            // Si retryButton no fue asignado en Inspector, intentar encontrarlo
            // automáticamente como hijo del feedbackSection.
            if (retryButton == null && feedbackSection != null)
                retryButton = feedbackSection.GetComponentInChildren<Button>(true);

            if (retryButton != null)
                retryButton.onClick.AddListener(OnRetryClicked);
        }

        // ── API pública ───────────────────────────────────────────────────────────

        /// <summary>
        /// Genera los botones de opción desde el array dado y muestra la sección de opciones.
        /// Llama esto cada vez que quieras mostrar (o reiniciar) el panel de elección.
        /// </summary>
        public void SetOptions(DialogueOption[] options)
        {
            _currentOptions = options;
            _answered = false;

            ClearButtons();

            if (feedbackSection != null) feedbackSection.SetActive(false);
            if (optionsContainer != null) optionsContainer.gameObject.SetActive(true);

            if (options == null || options.Length == 0)
            {
                Debug.LogWarning("[MultipleChoicePanel] El array de opciones está vacío.");
                return;
            }

            for (int i = 0; i < options.Length; i++)
            {
                if (optionButtonPrefab == null || optionsContainer == null) break;

                GameObject btnGo = Instantiate(optionButtonPrefab, optionsContainer);
                _spawnedButtons.Add(btnGo);

                // Guardar referencia a la Image para cambiar colores luego
                Image btnImage = btnGo.GetComponent<Image>();
                _buttonImages.Add(btnImage);

                // Prefijo de letra solo cuando hay más de una opción (A) B) C)…)
                // Una sola opción (Continuar / Cerrar) no necesita prefijo
                TextMeshProUGUI label = btnGo.GetComponentInChildren<TextMeshProUGUI>();
                string prefix = (options.Length > 1 && i < _letters.Length) ? $"{_letters[i]})  " : "";
                if (label != null) label.text = prefix + options[i].optionText;

                // Closure seguro
                int capturedIndex = i;
                DialogueOption captured = options[i];
                Button btn = btnGo.GetComponent<Button>();
                if (btn != null)
                    btn.onClick.AddListener(() => OnButtonClicked(captured, capturedIndex));
            }
        }

        /// <summary>Destruye todos los botones y reinicia el estado.</summary>
        public void Clear()
        {
            ClearButtons();
            if (feedbackSection != null) feedbackSection.SetActive(false);
            _currentOptions = null;
            _answered = false;
        }

        // ── Selección ─────────────────────────────────────────────────────────────
        private void OnButtonClicked(DialogueOption option, int selectedIndex)
        {
            if (_answered) return; // doble-clic protegido
            _answered = true;

            DisableAllButtons();
            HighlightSelectedButton(selectedIndex, option.isCorrect);

            ShowFeedback(option);

            if (option.isCorrect)
                OnCorrect?.Invoke();
            else
                OnWrong?.Invoke();
        }

        private void DisableAllButtons()
        {
            foreach (GameObject go in _spawnedButtons)
            {
                if (go == null) continue;
                Button btn = go.GetComponent<Button>();
                if (btn != null) btn.interactable = false;
            }
        }

        private void HighlightSelectedButton(int selectedIndex, bool wasCorrect)
        {
            // Resaltar el botón que el usuario presionó
            if (selectedIndex < _buttonImages.Count && _buttonImages[selectedIndex] != null)
                _buttonImages[selectedIndex].color = wasCorrect ? correctHighlightColor : selectedButtonColor;

            // Si la respuesta fue incorrecta y showCorrectAfterWrong está activo,
            // buscar y resaltar el botón correcto en verde
            if (!wasCorrect && showCorrectAfterWrong && _currentOptions != null)
            {
                for (int i = 0; i < _currentOptions.Length; i++)
                {
                    if (_currentOptions[i].isCorrect && i < _buttonImages.Count && _buttonImages[i] != null)
                    {
                        _buttonImages[i].color = correctHighlightColor;
                        break;
                    }
                }
            }
        }

        private void ShowFeedback(DialogueOption option)
        {
            // Ocultar la sección de feedback si no hay texto (ej: botón "Continuar" de pasos Info)
            bool hasFeedback = !string.IsNullOrEmpty(option.feedbackText);
            if (feedbackSection != null) feedbackSection.SetActive(hasFeedback);
            if (!hasFeedback) return;

            if (feedbackText != null)
                feedbackText.text = option.feedbackText;

            if (feedbackBackground != null)
                feedbackBackground.color = option.isCorrect ? correctFeedbackColor : incorrectFeedbackColor;

            // Botón de reintento solo si fue incorrecto
            if (retryButton != null)
                retryButton.gameObject.SetActive(!option.isCorrect);
        }

        // ── Reintento ─────────────────────────────────────────────────────────────
        private void OnRetryClicked()
        {
            OnRetry?.Invoke();
            // Regenerar las mismas opciones en el mismo panel (sin cerrarlo)
            if (_currentOptions != null)
                SetOptions(_currentOptions);
        }

        // ── Limpieza interna ──────────────────────────────────────────────────────
        private void ClearButtons()
        {
            foreach (GameObject go in _spawnedButtons)
                if (go != null) Destroy(go);

            _spawnedButtons.Clear();
            _buttonImages.Clear();
        }
    }
}
