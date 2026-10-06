namespace SIRMED.UI
{
    using UnityEngine;
    using UnityEngine.UI;

    /// <summary>
    /// WelcomeSlideButton — Botón de navegación propio de un slide del WelcomePanel.
    ///
    /// Se agrega a un Button que esté DENTRO de un slide (Modo B) o de la plantilla (Modo A).
    /// Si el slide visible contiene al menos uno de estos botones, el WelcomePanel oculta
    /// sus botones globales (anterior / siguiente / comenzar) y la navegación la hacen
    /// los botones del slide.
    ///
    /// Se identifica por componente y no por nombre del GameObject: renombrar o duplicar
    /// el botón no lo rompe. Lo ideal es tener el par Volver/Siguiente como prefab y
    /// colocarlo en cada slide para que se vea igual en todos.
    ///
    /// Comportamiento automático:
    ///   · Previous se oculta en el primer slide.
    ///   · Next en el último slide actúa como "Comenzar" (salvo que el slide tenga un
    ///     botón con rol Start; en ese caso Next se oculta).
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class WelcomeSlideButton : MonoBehaviour
    {
        public enum Role { Previous, Next, Start }

        [Tooltip("Qué hace este botón dentro del slide.")]
        public Role role = Role.Next;

        private WelcomePanel _panel;

        private void Awake()
        {
            _panel = GetComponentInParent<WelcomePanel>(true);
            if (_panel == null)
            {
                Debug.LogWarning($"[WelcomeSlideButton] '{name}' no está dentro de un WelcomePanel.", this);
                return;
            }

            GetComponent<Button>().onClick.AddListener(() => _panel.Navigate(role));
        }
    }
}
