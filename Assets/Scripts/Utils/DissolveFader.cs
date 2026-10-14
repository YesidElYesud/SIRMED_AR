namespace SIRMED.Utils
{
    using System;
    using System.Collections;
    using UnityEngine;

    /// <summary>
    /// DissolveFader — aparición/desaparición suave de un personaje con el parámetro
    /// _Dissolve de SIRMED_Character.shadergraph (1 = visible, 0 = invisible; dither +
    /// alpha clip), en vez de SetActive instantáneo ("popping").
    ///
    /// Usa MaterialPropertyBlock por renderer: no crea copias de los materiales, así que
    /// varios personajes con el mismo material se desvanecen de forma independiente.
    ///
    /// SETUP: agregar en la raíz del modelo (el mismo GameObject que se muestra/oculta).
    /// FadeOut desactiva el GameObject al terminar; FadeIn lo reactiva antes de empezar.
    /// </summary>
    public class DissolveFader : MonoBehaviour
    {
        [Tooltip("Nombre de referencia de la propiedad en el shader.")]
        [SerializeField] private string _propertyName = "_Dissolve";

        [Tooltip("Duración (s) de la aparición y de la desaparición.")]
        [SerializeField] private float _duration = 0.8f;

        private Renderer[] _renderers;
        private MaterialPropertyBlock _block;
        private int _propertyId;
        private float _value = 1f;
        private Coroutine _fadeRoutine;

        /// <summary>Valor actual de _Dissolve (0 = invisible, 1 = visible).</summary>
        public float Value => _value;

        // Caché perezosa: otros scripts pueden llamar SetVisibleImmediate desde su Awake,
        // antes de que corra el de este componente.
        private void EnsureInit()
        {
            if (_renderers != null) return;
            _renderers = GetComponentsInChildren<Renderer>(true);
            _block = new MaterialPropertyBlock();
            _propertyId = Shader.PropertyToID(_propertyName);
        }

        /// <summary>Aparece suavemente (activa el GameObject si estaba oculto).</summary>
        public void FadeIn(Action onDone = null)
        {
            EnsureInit();
            if (!gameObject.activeSelf)
            {
                Apply(0f);
                gameObject.SetActive(true);
            }
            StartFade(1f, onDone);
        }

        /// <summary>Desaparece suavemente y desactiva el GameObject al terminar.</summary>
        public void FadeOut(Action onDone = null)
        {
            EnsureInit();
            if (!gameObject.activeInHierarchy)
            {
                Apply(0f);
                gameObject.SetActive(false);
                onDone?.Invoke();
                return;
            }
            StartFade(0f, () =>
            {
                gameObject.SetActive(false);
                onDone?.Invoke();
            });
        }

        /// <summary>Muestra u oculta sin transición.</summary>
        public void SetVisibleImmediate(bool visible)
        {
            EnsureInit();
            if (_fadeRoutine != null) { StopCoroutine(_fadeRoutine); _fadeRoutine = null; }
            Apply(visible ? 1f : 0f);
            gameObject.SetActive(visible);
        }

        private void StartFade(float target, Action onDone)
        {
            if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
            _fadeRoutine = StartCoroutine(FadeRoutine(target, onDone));
        }

        private IEnumerator FadeRoutine(float target, Action onDone)
        {
            float speed = _duration > 0f ? 1f / _duration : float.MaxValue;
            while (!Mathf.Approximately(_value, target))
            {
                Apply(Mathf.MoveTowards(_value, target, speed * Time.deltaTime));
                yield return null;
            }
            _fadeRoutine = null;
            onDone?.Invoke();
        }

        private void Apply(float value)
        {
            _value = value;
            foreach (Renderer r in _renderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(_block);
                _block.SetFloat(_propertyId, value);
                r.SetPropertyBlock(_block);
            }
        }
    }
}
