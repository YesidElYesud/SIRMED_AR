namespace SIRMED.Gameplay.Hotspots
{
    using Google.XR.ARCoreExtensions;
    using SIRMED.Managers;
    using SIRMED.UI;
    using UnityEngine;

    /// <summary>
    /// HotspotController — Interacción mediada por HUD, anclada al mundo real.
    ///
    /// Al entrar en el radio del jugador muestra el HotspotPromptButton en el HUD.
    /// El panel se abre SOLO cuando el jugador pulsa ese botón, no automáticamente.
    /// Al salir del radio el botón se oculta; si había un panel abierto se cierra.
    ///
    /// Convención de escena: este componente vive en el MISMO GameObject que un
    /// ARGeospatialCreatorAnchor (con Latitude/Longitude/Altitude configurados desde
    /// el CSV de campo, o colocados a mano en el Editor contra el Cesium3DTileset).
    /// Ese componente resuelve el anchor real en runtime y reparenta este GameObject
    /// bajo el ARGeospatialAnchor resultante — a partir de ese momento
    /// transform.position ya es la posición real del hotspot en el mundo, y este
    /// script puede operar exactamente igual que si estuviera en una posición fija
    /// de escena. Antes de que eso ocurra, la proximidad se mantiene desactivada
    /// (ver IsAnchorReady) para no comparar contra una posición todavía sin resolver.
    ///
    /// Nota de portado (SATCS → SIRMED_AR): se eliminó todo lo relacionado con
    /// CameraSequence/CinematicSequencer (cameraSequencer, linkedWalker,
    /// replayButton, ActivateReplayButton, ReplaySequence) — en AR real no existe
    /// una cámara virtual que se pueda tomar prestada del jugador. También se
    /// eliminó allowClick/hotspotMaterial (dependían de una malla fija en escena).
    /// RiskLevelIndicator y EndGamePanel ya están conectados (fase de diálogos/UI).
    /// La referencia a EvacuationRouteController queda como TODO explícito: ese
    /// tipo se agrega en la fase de ambiente sin necesidad de tocar esta clase más
    /// que para añadir la línea de invocación.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class HotspotController : MonoBehaviour, IHotspotInteractable
    {
        [Header("Datos del Hotspot")]
        [Tooltip("ScriptableObject con el contenido de este hotspot")]
        public HotspotData data;

        [Header("Referencias (auto-detectadas si están en escena)")]
        [Tooltip("Panel UI de información. Se busca automáticamente si está vacío.")]
        public HotspotUIPanel uiPanel;

        [Header("Malla / marcador visual (rotación)")]
        [Tooltip("Transform hijo con la malla del marcador. Se detecta automáticamente si está vacío.")]
        public Transform meshTransform;

        [Tooltip("Grados por segundo de rotación sobre el eje Y del marcador visual.")]
        public float rotationSpeed = 60f;

        [Header("Efecto Visitado")]
        [Tooltip("Activa el efecto translúcido en el marcador al interactuar.")]
        [SerializeField] private bool enableVisitedEffect = true;
        [Tooltip("Alpha que tendrán los materiales de la malla tras la primera interacción (0 = invisible, 1 = opaco).")]
        [Range(0f, 1f)]
        [SerializeField] private float visitedAlpha = 0.35f;

        [Tooltip("Si true, solo puede activarse UNA VEZ por sesión.\n" +
                 "Tras la primera interacción el botón de prompt no vuelve a aparecer aunque el jugador\n" +
                 "salga y vuelva a entrar en rango.")]
        [SerializeField] private bool _interactOnce = false;

        // ── Internos ──────────────────────────────────────────────────────────────
        private Transform _playerCamera;
        private bool _isNearby = false;
        private bool _isPanelOpen = false;
        private bool _hasBeenVisited = false;
        private bool _triviaShown = false;
        private Renderer _meshRenderer;

        // ── Gizmos en editor ──────────────────────────────────────────────────────
        private void OnDrawGizmosSelected()
        {
            if (data == null) return;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, data.triggerRadius);
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────────
        private void Start()
        {
            if (meshTransform == null && transform.childCount > 0)
                meshTransform = transform.GetChild(0);

            if (meshTransform != null)
                _meshRenderer = meshTransform.GetComponent<Renderer>();

            if (Camera.main != null)
                _playerCamera = Camera.main.transform;

            if (uiPanel == null)
                uiPanel = FindAnyObjectByType<HotspotUIPanel>();

            if (data == null)
            {
                Debug.LogWarning($"[Hotspot] '{gameObject.name}' no tiene HotspotData asignado.", this);
                return;
            }

            if (StageManager.Instance != null)
                StageManager.Instance.OnStageChanged += OnStageChanged;

            RefreshStageVisibility();
        }

        private void OnDestroy()
        {
            if (StageManager.Instance != null)
                StageManager.Instance.OnStageChanged -= OnStageChanged;

            HotspotPromptButton.Instance?.UnregisterHotspot(this);
        }

        private void OnDisable()
        {
            // Al desactivarse (p.ej. StageManager lo oculta) limpiar el botón de prompt
            HotspotPromptButton.Instance?.UnregisterHotspot(this);
            _isNearby = false;
        }

        private void Update()
        {
            if (data == null || _playerCamera == null) return;
            if (!IsAnchorReady()) return;

            ApplyRotationEffect();
            CheckProximity();
        }

        // ── Anclaje geoespacial ───────────────────────────────────────────────────
        /// <summary>
        /// True cuando el ARGeospatialCreatorAnchor de este mismo GameObject ya
        /// resolvió su ARGeospatialAnchor real (se reparenta bajo él al resolver,
        /// ver ARGeospatialCreatorAnchor.FinishAnchor). Antes de eso, transform
        /// todavía tiene la posición autorada en el Editor, que no corresponde a
        /// ninguna ubicación real de la sesión AR en curso.
        /// </summary>
        private bool IsAnchorReady()
        {
            return transform.parent != null &&
                   transform.parent.GetComponent<ARGeospatialAnchor>() != null;
        }

        // ── Filtro de etapa ───────────────────────────────────────────────────────
        private void OnStageChanged(StageManager.Stage previous, StageManager.Stage current)
        {
            RefreshStageVisibility();
        }

        /// <summary>
        /// requiredStage = -1 → siempre visible.
        /// requiredStage >= 0 → visible solo en esa etapa exacta.
        /// </summary>
        private void RefreshStageVisibility()
        {
            if (data == null) return;
            if (data.requiredStage < 0) return;

            bool stageMatch = StageManager.Instance != null &&
                              (int)StageManager.Instance.CurrentStage == data.requiredStage;

            gameObject.SetActive(stageMatch);
            // OnDisable se encarga de limpiar el prompt button si stageMatch es false
        }

        // ── Rotación del marcador ─────────────────────────────────────────────────
        private void ApplyRotationEffect()
        {
            if (meshTransform == null) return;
            if (_isNearby || _isPanelOpen) return;

            meshTransform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.Self);
        }

        // ── Proximidad ────────────────────────────────────────────────────────────
        private void CheckProximity()
        {
            float dist = Vector3.Distance(_playerCamera.position, transform.position);
            bool nowNearby = dist <= data.triggerRadius;

            if (nowNearby && !_isNearby)
            {
                _isNearby = true;
                Debug.Log($"[Hotspot] Entrando en rango de: {data.title}");
                if (!_isPanelOpen && !(_interactOnce && _hasBeenVisited))
                    HotspotPromptButton.Instance?.RegisterHotspot(this);
            }
            else if (!nowNearby && _isNearby)
            {
                _isNearby = false;
                Debug.Log($"[Hotspot] Saliendo del rango de: {data.title}");
                HotspotPromptButton.Instance?.UnregisterHotspot(this);
                if (_isPanelOpen) ClosePanel();
            }
        }

        // ── Dispatch por tipo de acción ───────────────────────────────────────────
        /// <summary>
        /// Punto central de activación. Llamado por HotspotPromptButton al pulsar el botón HUD.
        /// </summary>
        public void DispatchAction()
        {
            if (data == null) return;

            // Ocultar el botón de prompt mientras el panel está abierto
            HotspotPromptButton.Instance?.UnregisterHotspot(this);

            if (data.riskLevel != RiskLevel.None && RiskLevelIndicator.Instance != null)
                RiskLevelIndicator.Instance.SetLevel(data.riskLevel);

            switch (data.actionType)
            {
                case HotspotActionType.InfoPanel:
                    OpenInfoPanel();
                    break;

                case HotspotActionType.Cinematic:
                    if (CinematicManager.Instance != null)
                    {
                        _isPanelOpen = true;
                        CinematicManager.Instance.Play(data, this);
                    }
                    else
                    {
                        Debug.LogWarning($"[Hotspot] '{data.title}' → CinematicManager no encontrado. Usando InfoPanel.");
                        OpenInfoPanel();
                    }
                    break;

                case HotspotActionType.NpcConversation:
                    if (NpcDialoguePanel.Instance != null)
                    {
                        _isPanelOpen = true;
                        NpcDialoguePanel.Instance.Show(data.dialogueData, this);
                    }
                    else
                    {
                        Debug.LogWarning($"[Hotspot] '{data.title}' → NpcDialoguePanel no encontrado. Usando InfoPanel.");
                        OpenInfoPanel();
                    }
                    break;

                case HotspotActionType.SiataCall:
                    if (SiataCallPanel.Instance != null)
                    {
                        _isPanelOpen = true;
                        if (data.siataSequence != null)
                            SiataCallPanel.Instance.Show(data.siataSequence, this);
                        else
                            SiataCallPanel.Instance.Show(data.dialogueData, this);
                    }
                    else if (NpcDialoguePanel.Instance != null)
                    {
                        Debug.LogWarning($"[Hotspot] '{data.title}' → SiataCallPanel no encontrado. Usando NpcDialoguePanel.");
                        _isPanelOpen = true;
                        NpcDialoguePanel.Instance.Show(data.dialogueData, this);
                    }
                    else
                    {
                        Debug.LogWarning($"[Hotspot] '{data.title}' → SiataCallPanel y NpcDialoguePanel no encontrados. Usando InfoPanel.");
                        OpenInfoPanel();
                    }
                    break;

                case HotspotActionType.InfoSlidePanel:
                    if (InfoSlidePanel.Instance != null)
                    {
                        _isPanelOpen = true;
                        InfoSlidePanel.Instance.Show(data.infoSlides, this, data.infoSlideAdvancesStage);
                    }
                    else
                    {
                        Debug.LogWarning($"[Hotspot] '{data.title}' → InfoSlidePanel no encontrado. Usando InfoPanel.");
                        OpenInfoPanel();
                    }
                    break;

                case HotspotActionType.RiskLevelOnly:
                    // El nivel de riesgo ya se aplicó arriba; solo cerrar para marcar como visitado.
                    ClosePanel();
                    break;
            }
        }

        // ── Panel informativo ─────────────────────────────────────────────────────
        private void OpenInfoPanel()
        {
            if (uiPanel == null) return;
            _isPanelOpen = true;
            uiPanel.Show(data, this);
        }

        public void ClosePanel()
        {
            _isPanelOpen = false;

            if (!_hasBeenVisited)
            {
                if (enableVisitedEffect) MarkAsVisited(); // MarkAsVisited también pone _hasBeenVisited = true
                else _hasBeenVisited = true;
            }

            if (uiPanel != null) uiPanel.Hide();

            if (ShouldShowTrivia())
            {
                _triviaShown = true;
                _isPanelOpen = true; // TriviaPanel llama de vuelta a ClosePanel() al terminar
                TriviaPanel.Instance.Show(data.trivia, this);
                return;
            }

            // TODO Fase 4 (ambiente): cuando exista EvacuationRouteController, si
            // data.activatesEvacuationRoute es true, mostrar la ruta aquí.

            if (data != null && data.advancesStageOnClose)
                StageManager.Instance?.NextStage();

            if (data != null && data.activatesEndGame)
            {
                bool stageOk = data.endGameRequiredStage < 0 ||
                               (StageManager.Instance != null &&
                                (int)StageManager.Instance.CurrentStage == data.endGameRequiredStage);
                if (stageOk)
                {
                    EndGamePanel.Instance?.Show();
                    return;
                }
            }

            // Si el jugador sigue en rango y el hotspot permite re-activación, mostrar el botón
            if (_isNearby && !(_interactOnce && _hasBeenVisited))
                HotspotPromptButton.Instance?.RegisterHotspot(this);
        }

        // ── Trivia ────────────────────────────────────────────────────────────────
        /// <summary>
        /// True si este hotspot tiene trivia asignada, todavía no se mostró y el
        /// nivel de riesgo activo no es N4 (GDD: la trivia se limita a momentos
        /// seguros y nunca debe aparecer durante el desplazamiento de evacuación).
        /// </summary>
        private bool ShouldShowTrivia()
        {
            if (data == null || data.trivia == null || _triviaShown) return false;
            if (TriviaPanel.Instance == null) return false;

            bool isEvacuating = RiskLevelIndicator.Instance != null &&
                                 RiskLevelIndicator.Instance.CurrentLevel == RiskLevel.N4;
            return !isEvacuating;
        }

        // ── Efecto visitado ───────────────────────────────────────────────────────
        private void MarkAsVisited()
        {
            _hasBeenVisited = true;
            if (_meshRenderer == null) return;

            // Instanciar los materiales para no modificar los assets compartidos
            Material[] mats = _meshRenderer.materials;
            foreach (Material mat in mats)
                ApplyVisitedTransparency(mat, visitedAlpha);
            _meshRenderer.materials = mats;
        }

        private static void ApplyVisitedTransparency(Material mat, float alpha)
        {
            if (mat == null) return;

            // Cambiar a modo Transparent del Standard shader
            if (mat.HasProperty("_Mode"))
            {
                mat.SetFloat("_Mode", 3f);
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.EnableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = 3000;
            }

            if (mat.HasProperty("_Color"))
            {
                Color c = mat.GetColor("_Color");
                c.a = alpha;
                mat.SetColor("_Color", c);
            }
        }
    }
}
