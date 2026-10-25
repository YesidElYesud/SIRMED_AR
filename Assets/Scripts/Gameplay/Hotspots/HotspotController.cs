namespace SIRMED.Gameplay.Hotspots
{
    using System;
    using System.Collections.Generic;
    using Google.XR.ARCoreExtensions;
    using SIRMED.Gameplay.Dialogue;
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
    /// RiskLevelIndicator, EndGamePanel y EvacuationRouteController ya están
    /// conectados (ClosePanel invoca a este último cuando
    /// data.activatesEvacuationRoute es true; ver SIRMED.Gameplay.Environment).
    /// </summary>
    /// <summary>Tipo de hotspot (uno por ícono 3D). Hoy solo lo usa el minimapa para el color.</summary>
    public enum HotspotCategory
    {
        Auto,
        Ninguna,
        Comunitaria,
        Lider,
        RutaEvacuacion,
        Clima,
        Documentos,
        Trivia,
        PuntoEncuentro,
        Sirena
    }

    [RequireComponent(typeof(Collider))]
    public class HotspotController : MonoBehaviour, IHotspotInteractable
    {
        // ── Registro global ───────────────────────────────────────────────────────
        /// <summary>
        /// Todos los HotspotController actualmente habilitados en escena (respeta
        /// RefreshStageVisibility: un hotspot fuera de su etapa se desregistra al
        /// desactivarse). Usado por MinimapController para dibujar los POIs sin que
        /// cada hotspot necesite conocer al minimapa.
        /// </summary>
        private static readonly List<HotspotController> _active = new List<HotspotController>();
        public static IReadOnlyList<HotspotController> ActiveHotspots => _active;

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

        [Header("Minimapa")]
        [Tooltip("Tipo de hotspot para el color del punto en el minimapa (MinimapController.categoryStyles).\n" +
                 "Auto = se deduce del nombre del ícono hijo (COMUNITARIA_ICONO, LIDER-ICONO, spot CLIMA…).")]
        public HotspotCategory minimapCategory = HotspotCategory.Auto;

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

        [Header("Secuencia (opcional — p. ej. Líder: parada 1 → 2 → 3)")]
        [Tooltip("Hotspots con el mismo id forman una secuencia; vacío = sin secuencia.")]
        public string sequenceId = "";
        [Tooltip("Orden dentro de la secuencia (0 = primera parada). Solo aparece cuando las anteriores ya se completaron.")]
        public int sequenceStep = 0;

        [Header("Etapa (opcional)")]
        [Tooltip("Etapa en la que se ve ESTE hotspot (1 = N1 … 4 = N4); sobrescribe data.requiredStage.\n" +
                 "-1 = usar data.requiredStage. Útil cuando varios hotspots comparten el mismo HotspotData\n" +
                 "(p. ej. las paradas del Líder, una por nivel).")]
        public int requiredStageOverride = -1;

        // ── Internos ──────────────────────────────────────────────────────────────
        private Transform _playerCamera;
        private bool _isNearby = false;
        private bool _isPanelOpen = false;
        private bool _isInfoPanelOpen = false;
        private bool _hasBeenVisited = false;
        private bool _triviaShown = false;
        private bool _infoCardShown = false;
        private Renderer _meshRenderer;
        private ARGeospatialAnchor geospatialAnchor;

        public Action correctNpcDialogue;
        public Action incorrectNpcDialogue;

        /// <summary>
        /// Se dispara al activar el hotspot (el jugador pulsó el botón del HUD), antes de abrir
        /// su panel. Lo usa p. ej. LeaderNpcController para que la líder aparezca de inmediato.
        /// </summary>
        public event Action<HotspotController> Activated;

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
            {
                // Preferir el panel genérico; las copias por hotspot (isDefaultPanel = false)
                // solo se usan si el hotspot las tiene asignadas.
                foreach (HotspotUIPanel p in FindObjectsByType<HotspotUIPanel>(FindObjectsSortMode.None))
                {
                    if (p.isDefaultPanel) { uiPanel = p; break; }
                }
            }

            if (data == null)
            {
                Debug.LogWarning($"[Hotspot] '{gameObject.name}' no tiene HotspotData asignado.", this);
                return;
            }

            if (StageManager.Instance != null)
                StageManager.Instance.OnStageChanged += OnStageChanged;
            HotspotSequence.Changed += RefreshStageVisibility;

            RefreshStageVisibility();
        }

        private void OnDestroy()
        {
            if (StageManager.Instance != null)
                StageManager.Instance.OnStageChanged -= OnStageChanged;
            HotspotSequence.Changed -= RefreshStageVisibility;

            HotspotPromptButton.Instance?.UnregisterHotspot(this);
        }

        private void OnEnable()
        {
            if (!_active.Contains(this))
                _active.Add(this);
        }

        private void OnDisable()
        {
            _active.Remove(this);

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
        public bool IsAnchorReady()
        {
            if (Application.isEditor)
            {
                return true;
            }
            else
            {
                if (geospatialAnchor == null && transform.parent != null)
                    geospatialAnchor = transform.parent.GetComponent<ARGeospatialAnchor>();

                return geospatialAnchor != null;
            }
        }

        /// <summary>
        /// Tipo efectivo del hotspot: minimapCategory si se eligió a mano; si está en
        /// Auto, se deduce del nombre de los íconos hijos (los FBX de
        /// Assets/Resources/ICONOS 3D). Ninguna si no coincide con ninguno.
        /// </summary>
        public HotspotCategory ResolvedCategory
        {
            get
            {
                if (minimapCategory != HotspotCategory.Auto) return minimapCategory;
                if (_resolvedCategory == null)
                {
                    _resolvedCategory = HotspotCategory.Ninguna;
                    foreach (Transform child in transform)
                    {
                        HotspotCategory c = CategoryFromName(child.name);
                        if (c != HotspotCategory.Ninguna) { _resolvedCategory = c; break; }
                    }
                }
                return _resolvedCategory.Value;
            }
        }

        private HotspotCategory? _resolvedCategory;

        /// <summary>True si el jugador ya abrió y cerró este hotspot (lo usa el Director, situación 4).</summary>
        public bool HasBeenVisited => _hasBeenVisited;

        private static HotspotCategory CategoryFromName(string name)
        {
            string n = name.ToUpperInvariant();
            if (n.Contains("COMUNIT")) return HotspotCategory.Comunitaria;
            if (n.Contains("LIDER") || n.Contains("LÍDER")) return HotspotCategory.Lider;
            if (n.Contains("ENCUENTRO")) return HotspotCategory.PuntoEncuentro;
            if (n.Contains("RUTA") || n.Contains("EVACUA")) return HotspotCategory.RutaEvacuacion;
            if (n.Contains("CLIMA")) return HotspotCategory.Clima;
            if (n.Contains("DOCUMENT")) return HotspotCategory.Documentos;
            if (n.Contains("TRIVIA")) return HotspotCategory.Trivia;
            if (n.Contains("SIRENA")) return HotspotCategory.Sirena;
            return HotspotCategory.Ninguna;
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
            bool hasSequence = !string.IsNullOrEmpty(sequenceId);
            int requiredStage = requiredStageOverride >= 0 ? requiredStageOverride : data.requiredStage;
            if (requiredStage < 0 && !hasSequence) return;

            bool stageMatch = requiredStage < 0 ||
                              (StageManager.Instance != null &&
                               (int)StageManager.Instance.CurrentStage == requiredStage);
            bool unlocked = HotspotSequence.IsUnlocked(sequenceId, sequenceStep);

            gameObject.SetActive(stageMatch && unlocked);
            // OnDisable se encarga de limpiar el prompt button si queda oculto
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
                Debug.Log($"[Hotspot] Entrando en rango de: {data.title}", gameObject);
                if (!_isPanelOpen && !(_interactOnce && _hasBeenVisited))
                    HotspotPromptButton.Instance?.RegisterHotspot(this);
            }
            else if (!nowNearby && _isNearby)
            {
                _isNearby = false;
                Debug.Log($"[Hotspot] Saliendo del rango de: {data.title}", gameObject);
                HotspotPromptButton.Instance?.UnregisterHotspot(this);
                // Solo se corta el InfoPanel. Los modales (trivia, NPC, SIATA, slides) siguen
                // hasta que el jugador los cierra y llaman ClosePanel() ellos mismos; cerrarlos
                // aquí hacía que el cierre corriera dos veces (doble avance de etapa).
                if (_isInfoPanelOpen) ClosePanel(allowTrivia: false); // no encadenar trivia al alejarse
            }
        }

        // ── Dispatch por tipo de acción ───────────────────────────────────────────
        /// <summary>
        /// Punto central de activación. Llamado por HotspotPromptButton al pulsar el botón HUD.
        /// </summary>
        public void DispatchAction()
        {
            if (data == null) return;

            // La tarjeta informativa es parte del contenido: sale cada vez que se abre
            // (a diferencia de la trivia, que solo sale una vez).
            _infoCardShown = false;

            Activated?.Invoke(this);

            // Ocultar el botón de prompt mientras el panel está abierto
            HotspotPromptButton.Instance?.UnregisterHotspot(this);

            if (data.riskLevel != RiskLevel.None && RiskLevelIndicator.Instance != null)
                RiskLevelIndicator.Instance.SetLevel(data.riskLevel);

            // Si tiene trivia, entra directo a ella (sin pasar por InfoPanel/NPC/etc.).
            // ClosePanel() se encarga de lo que sigue (visitado, ruta, avance de etapa)
            // cuando la trivia termine y llame de vuelta.
            if (!data.triviaAfterContent && ShouldShowTrivia() && ShowTrivia())
                return;

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
                        RiskLevel level = RiskLevelIndicator.Instance != null
                            ? RiskLevelIndicator.Instance.CurrentLevel
                            : RiskLevel.None;
                        NpcDialogueData dialogue = data.GetDialogueFor(level);
                        Action onCorrect = null;
                        Action onIncorrect = null;
                        if (dialogue != null && dialogue.isCommunityHelp)
                        {
                            onCorrect = () =>
                            {
                                Director.DirectorAdviceController.Instance?.NotifyCommunityHelpCorrect();
                                correctNpcDialogue?.Invoke();
                            };
                            onIncorrect = () =>
                            {
                                incorrectNpcDialogue?.Invoke();
                            };
                        }

                        NpcDialoguePanel.Instance.Show(dialogue, this, onCorrect, onIncorrect);
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
            _isInfoPanelOpen = true;
            uiPanel.Show(data, this);
        }

        public void ClosePanel() => ClosePanel(allowTrivia: true);

        private void ClosePanel(bool allowTrivia)
        {
            // Tarjeta informativa entre el contenido y la trivia: al pulsar "Siguiente",
            // InfoCardPanel vuelve a llamar ClosePanel() y, como _infoCardShown ya es
            // true, sigue con la trivia / cierre normal.
            if (allowTrivia && ShouldShowInfoCard())
            {
                if (uiPanel != null) uiPanel.Hide();
                _isInfoPanelOpen = false;
                _infoCardShown = true;
                _isPanelOpen = true;
                InfoCardPanel.Instance.Show(data.infoCard, this);
                return;
            }

            // Trivia después del contenido: al cerrar el panel principal se encadena la
            // trivia; cuando termine, TriviaPanel vuelve a llamar ClosePanel() y, como
            // _triviaShown ya es true, sigue el cierre normal (visitado, ruta, etapa...).
            if (allowTrivia && data != null && data.triviaAfterContent && ShouldShowTrivia())
            {
                if (uiPanel != null) uiPanel.Hide();
                _isInfoPanelOpen = false;
                if (ShowTrivia()) return;
            }

            _isPanelOpen = false;
            _isInfoPanelOpen = false;

            if (!_hasBeenVisited)
            {
                if (enableVisitedEffect) MarkAsVisited(); // MarkAsVisited también pone _hasBeenVisited = true
                else _hasBeenVisited = true;
            }

            // Al salir del rango no cuenta como parada completada (se cortó el contenido).
            if (allowTrivia) HotspotSequence.Complete(sequenceId, sequenceStep);

            if (uiPanel != null) uiPanel.Hide();

            if (data != null && data.activatesEvacuationRoute)
                SIRMED.Gameplay.Environment.EvacuationRouteController.Instance?.Show();

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

        // ── Marcador visual ───────────────────────────────────────────────────────
        /// <summary>
        /// Muestra/oculta el marcador (ícono 3D hijo) sin desactivar el hotspot: sigue
        /// detectando proximidad y abriendo su panel. Lo usa LeaderNpcController para
        /// cambiar la moneda por el personaje.
        /// </summary>
        public void SetMarkerVisible(bool visible)
        {
            if (meshTransform == null && transform.childCount > 0)
                meshTransform = transform.GetChild(0);
            if (meshTransform != null)
                meshTransform.gameObject.SetActive(visible);
        }

        // ── Trivia ────────────────────────────────────────────────────────────────
        /// <summary>
        /// True si este hotspot tiene trivia asignada, todavía no se mostró y el
        /// nivel de riesgo activo no es N4 (GDD: la trivia se limita a momentos
        /// seguros y nunca debe aparecer durante el desplazamiento de evacuación).
        /// </summary>
        private bool ShouldShowInfoCard()
        {
            return data != null && data.useInfoCard && data.infoCard != null &&
                   !_infoCardShown && InfoCardPanel.Instance != null;
        }

        private bool ShouldShowTrivia()
        {
            if (data == null || !data.HasTrivia || _triviaShown) return false;
            if (TriviaPanel.Instance == null) return false;

            bool isEvacuating = RiskLevelIndicator.Instance != null &&
                                 RiskLevelIndicator.Instance.CurrentLevel == RiskLevel.N4;
            return !isEvacuating || data.allowTriviaInN4;
        }

        /// <summary>
        /// False si el panel no la pudo mostrar (trivia null en el pool, o ya hay otra
        /// abierta): el que llama sigue con el contenido/cierre normal en vez de dejar el
        /// hotspot trabado con _isPanelOpen = true.
        /// </summary>
        private bool ShowTrivia()
        {
            _triviaShown = true;
            if (!TriviaPanel.Instance.Show(data.GetTriviaToShow(), this)) return false;
            _isPanelOpen = true;
            return true;
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
