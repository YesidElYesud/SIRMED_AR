namespace SIRMED.Gameplay.Environment
{
    using System;
    using System.Collections;
    using SIRMED.Managers;
    using UnityEngine;

    /// <summary>
    /// StageActivator — muestra/oculta objetos según la etapa (nivel de riesgo)
    /// actual. Pensado para que efectos, partículas, sonidos o modelos se asignen a
    /// su etapa directamente en el objeto, sin tocar los arrays de StageManager ni de
    /// VisualEffectsStageController (que viven en el prefab Managers y obligan a
    /// arrastrar referencias de escena en cada escena duplicada).
    ///
    /// Actúa sobre los HIJOS, nunca sobre su propio GameObject: si se desactivara a
    /// sí mismo no volvería a recibir eventos, y además este GameObject suele ser un
    /// ARGeospatialCreatorAnchor que tiene que seguir activo para resolver su ancla.
    ///
    /// Uso típico:
    ///   Efecto en un lugar real (quebrada, sirena):
    ///     FX_Quebrada_N3      [ARGeospatialCreatorAnchor, StageActivator]
    ///     └── Agua_Particulas [ParticleSystem]
    ///   Efecto que sigue al jugador (lluvia):
    ///     Main Camera
    ///     └── FX_Lluvia       [StageActivator]
    ///         └── Lluvia      [ParticleSystem]
    ///
    /// Las partículas se apagan dejando de emitir (las gotas ya emitidas terminan su
    /// vida) y el hijo se desactiva cuando ya no queda ninguna viva.
    /// </summary>
    public class StageActivator : MonoBehaviour
    {
        [Flags]
        public enum StageMask
        {
            Intro = 1 << 0,
            N1_Etapa1 = 1 << 1,
            N2_Etapa2 = 1 << 2,
            N3_Etapa3 = 1 << 3,
            N4_Etapa4 = 1 << 4,
            Etapa5 = 1 << 5,
        }

        [Tooltip("Etapas en las que los objetos están visibles. Se pueden marcar varias.")]
        public StageMask visibleIn = StageMask.N3_Etapa3 | StageMask.N4_Etapa4;

        [Tooltip("Objetos a mostrar/ocultar. Si queda vacío se usan todos los hijos directos.")]
        public GameObject[] targets;

        [Tooltip("Si es true, las partículas dejan de emitir y se apagan al morir la última; si es false, se ocultan de golpe.")]
        public bool smoothParticles = true;

        private bool _subscribed;
        private bool? _shown;

        private void Start()
        {
            TrySubscribe();
            // Estado inicial sin transición, para que nada "aparezca" al abrir la escena.
            Apply(StageManager.Instance != null ? StageManager.Instance.CurrentStage : StageManager.Stage.Intro, instant: true);
        }

        private void Update()
        {
            // StageManager es DontDestroyOnLoad y puede crearse después que este objeto.
            if (!_subscribed) TrySubscribe();
        }

        private void OnDestroy()
        {
            if (_subscribed && StageManager.Instance != null)
                StageManager.Instance.OnStageChanged -= OnStageChanged;
        }

        private void TrySubscribe()
        {
            if (_subscribed || StageManager.Instance == null) return;
            StageManager.Instance.OnStageChanged += OnStageChanged;
            _subscribed = true;
        }

        private void OnStageChanged(StageManager.Stage previous, StageManager.Stage current)
        {
            Apply(current, instant: false);
        }

        /// <summary>True si la etapa dada está marcada en visibleIn.</summary>
        public bool IsVisibleIn(StageManager.Stage stage)
        {
            var bit = (StageMask)(1 << (int)stage);
            return (visibleIn & bit) != 0;
        }

        private void Apply(StageManager.Stage stage, bool instant)
        {
            bool show = IsVisibleIn(stage);
            if (_shown == show) return;
            _shown = show;

            StopAllCoroutines();
            if (targets != null && targets.Length > 0)
            {
                foreach (var t in targets)
                    if (t != null && t != gameObject) SetTarget(t, show, instant);
            }
            else
            {
                foreach (Transform child in transform)
                    SetTarget(child.gameObject, show, instant);
            }
        }

        private void SetTarget(GameObject target, bool show, bool instant)
        {
            if (show)
            {
                target.SetActive(true);
                foreach (var ps in target.GetComponentsInChildren<ParticleSystem>())
                    if (!ps.isPlaying) ps.Play(false);
                return;
            }

            var systems = target.GetComponentsInChildren<ParticleSystem>();
            if (instant || !smoothParticles || systems.Length == 0 || !target.activeInHierarchy)
            {
                target.SetActive(false);
                return;
            }

            foreach (var ps in systems)
                ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            StartCoroutine(DeactivateWhenDead(target, systems));
        }

        private IEnumerator DeactivateWhenDead(GameObject target, ParticleSystem[] systems)
        {
            while (true)
            {
                bool anyAlive = false;
                foreach (var ps in systems)
                    if (ps != null && ps.IsAlive(false)) { anyAlive = true; break; }
                if (!anyAlive) break;
                yield return null;
            }
            if (target != null) target.SetActive(false);
        }
    }
}
