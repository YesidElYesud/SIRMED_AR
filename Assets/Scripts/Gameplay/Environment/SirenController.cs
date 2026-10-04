namespace SIRMED.Gameplay.Environment
{
    using System;
    using SIRMED.Managers;
    using SIRMED.UI;
    using UnityEngine;
    using UnityEngine.Events;

    /// <summary>
    /// SirenController — la sirena del SAT según el nivel de alerta (checklist M03/M04).
    ///
    /// Significado validado en los diálogos del Líder:
    ///   N1/N2 → en silencio.
    ///   N3    → sonidos intermitentes: permanecer alerta ante una posible evacuación.
    ///   N4    → sonido continuo: evacuar.
    ///
    /// Solo usa assets (clips), sin referencias de escena, así que puede vivir en el
    /// prefab Managers y sirve igual para todos los SAT. Los UnityEvent son el "hook"
    /// que pide el checklist para enganchar luces/animación de la sirena sin código.
    /// La sirena baja de volumen mientras habla un personaje (banner del Director o
    /// panel modal abierto) para que la voz se entienda.
    /// </summary>
    public class SirenController : MonoBehaviour
    {
        public static SirenController Instance { get; private set; }

        [Header("Clips (provisionales en Assets/Audio/SFX)")]
        [Tooltip("N3: toque de alerta, se repite cada 'alertInterval' segundos.")]
        public AudioClip alertClip;
        [Tooltip("N4: sirena de evacuación en loop continuo.")]
        public AudioClip evacuationClip;

        [Header("Comportamiento")]
        [Tooltip("Segundos entre el inicio de un toque de alerta y el siguiente en N3.")]
        public float alertInterval = 25f;
        [Range(0f, 1f)] public float volume = 0.8f;
        [Tooltip("Factor de volumen mientras habla un personaje (Director o diálogo).")]
        [Range(0f, 1f)] public float duckFactor = 0.25f;
        [Tooltip("0 = 2D (se oye igual en todo el recorrido). Subirlo solo si se ubica en la sirena real.")]
        [Range(0f, 1f)] public float spatialBlend = 0f;

        [Header("Hooks (luces, animación, etc.)")]
        public UnityEvent onAlertStarted;       // entra a N3
        public UnityEvent onEvacuationStarted;  // entra a N4
        public UnityEvent onSilenced;           // vuelve a N1/N2

        public enum SirenState { Silent, Alert, Evacuation }
        public SirenState State { get; private set; } = SirenState.Silent;
        public event Action<SirenState> StateChanged;

        private AudioSource _source;
        private float _nextAlertTime;
        private bool _subscribed;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = spatialBlend;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_subscribed && StageManager.Instance != null)
                StageManager.Instance.OnStageChanged -= HandleStageChanged;
        }

        private void Update()
        {
            if (!_subscribed && StageManager.Instance != null)
            {
                StageManager.Instance.OnStageChanged += HandleStageChanged;
                _subscribed = true;
                Apply(StateFor(StageManager.Instance.CurrentStage));
            }

            bool voiceActive = (DirectorAdvicePanel.Instance != null && DirectorAdvicePanel.Instance.IsShowing) ||
                               (StageManager.Instance != null && StageManager.Instance.IsPlayerInputBlocked);
            _source.volume = volume * (voiceActive ? duckFactor : 1f);

            if (State == SirenState.Alert && alertClip != null && Time.time >= _nextAlertTime)
            {
                _source.PlayOneShot(alertClip);
                _nextAlertTime = Time.time + Mathf.Max(alertClip.length, alertInterval);
            }
        }

        private void HandleStageChanged(StageManager.Stage previous, StageManager.Stage current) =>
            Apply(StateFor(current));

        private static SirenState StateFor(StageManager.Stage stage) => stage switch
        {
            StageManager.Stage.Etapa3 => SirenState.Alert,
            StageManager.Stage.Etapa4 => SirenState.Evacuation,
            _ => SirenState.Silent,
        };

        private void Apply(SirenState state)
        {
            if (state == State) return;
            State = state;

            _source.Stop();
            _source.loop = false;

            switch (state)
            {
                case SirenState.Alert:
                    _nextAlertTime = Time.time; // primer toque inmediato
                    onAlertStarted?.Invoke();
                    break;
                case SirenState.Evacuation:
                    if (evacuationClip != null)
                    {
                        _source.clip = evacuationClip;
                        _source.loop = true;
                        _source.Play();
                    }
                    onEvacuationStarted?.Invoke();
                    break;
                default:
                    onSilenced?.Invoke();
                    break;
            }
            StateChanged?.Invoke(state);
        }
    }
}
