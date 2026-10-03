namespace SIRMED.Gameplay.Environment
{
    using System.Collections.Generic;
    using Google.XR.ARCoreExtensions;
    using UnityEngine;

    /// <summary>
    /// QuebradaZone — círculo de peligro anclado sobre el cauce de la quebrada.
    /// Mismo patrón de anclaje que RouteWaypoint: vive en un GameObject con
    /// ARGeospatialCreatorAnchor (Latitude/Longitude/Altitude), sin Collider ni
    /// HotspotData. La quebrada es una línea, así que se colocan varias zonas a lo
    /// largo del cauce (cada una con su radio) en vez de una sola.
    ///
    /// La consume DirectorAdviceController (situaciones 3 y 6 del documento de
    /// validación del DAGRD) vía IsInsideAny(). La distancia se mide en el plano
    /// horizontal: la altitud Geospatial tiene más error que lat/lon y el usuario
    /// camina por calles con pendiente.
    /// </summary>
    public class QuebradaZone : MonoBehaviour
    {
        private static readonly List<QuebradaZone> _active = new List<QuebradaZone>();

        [Tooltip("Radio (m) alrededor de este punto que se considera 'demasiado cerca del cauce'.")]
        public float radius = 15f;

        private void OnEnable() => _active.Add(this);
        private void OnDisable() => _active.Remove(this);

        /// <summary>Igual chequeo que RouteWaypoint/HotspotController: antes de resolver, la posición es la del Editor.</summary>
        public bool IsAnchorReady()
        {
            return transform.parent != null &&
                   transform.parent.GetComponent<ARGeospatialAnchor>() != null;
        }

        /// <summary>True si 'position' está dentro del radio de alguna zona ya anclada.</summary>
        public static bool IsInsideAny(Vector3 position)
        {
            foreach (QuebradaZone zone in _active)
            {
                if (zone == null || !zone.IsAnchorReady()) continue;
                Vector3 d = position - zone.transform.position;
                d.y = 0f;
                if (d.sqrMagnitude <= zone.radius * zone.radius) return true;
            }
            return false;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.5f, 1f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
