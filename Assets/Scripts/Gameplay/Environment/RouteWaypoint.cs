namespace SIRMED.Gameplay.Environment
{
    using Google.XR.ARCoreExtensions;
    using UnityEngine;

    /// <summary>
    /// RouteWaypoint — un punto de paso de una ruta de evacuación (ver
    /// EvacuationRouteController). Vive en el mismo GameObject que un
    /// ARGeospatialCreatorAnchor (Latitude/Longitude/Altitude), exactamente el
    /// mismo patrón de anclaje que un hotspot (ver HotspotController), pero sin
    /// Collider ni HotspotData: un waypoint no es interactivo, solo aporta una
    /// posición de mundo real una vez resuelto su anchor.
    /// </summary>
    public class RouteWaypoint : MonoBehaviour
    {
        /// <summary>
        /// True cuando el ARGeospatialCreatorAnchor de este GameObject ya resolvió
        /// su ARGeospatialAnchor real y reparentó este transform bajo él (igual
        /// chequeo que HotspotController.IsAnchorReady). Antes de eso,
        /// transform.position es la posición autorada en el Editor, no una
        /// ubicación real de la sesión AR en curso.
        /// </summary>
        public bool IsAnchorReady()
        {
            return transform.parent != null &&
                   transform.parent.GetComponent<ARGeospatialAnchor>() != null;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, 0.3f);
        }
    }
}
