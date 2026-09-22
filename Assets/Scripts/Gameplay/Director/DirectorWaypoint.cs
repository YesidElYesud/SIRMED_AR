namespace SIRMED.Gameplay.Director
{
    using Google.XR.ARCoreExtensions;
    using UnityEngine;

    /// <summary>
    /// DirectorWaypoint — un punto real donde el Director DAGRD puede pararse o
    /// caminar hacia (ver DirectorController). Mismo patrón de anclaje que
    /// RouteWaypoint/hotspots: vive en el mismo GameObject que un
    /// ARGeospatialCreatorAnchor y solo aporta una posición de mundo real una vez
    /// que ese anchor resuelve y reparenta este transform.
    /// </summary>
    public class DirectorWaypoint : MonoBehaviour
    {
        [Tooltip("Identificador usado por DirectorAdviceData.moveToPointId para pedirle al Director que camine hasta aquí. Debe ser único entre los waypoints asignados a DirectorController.waypoints.")]
        public string pointId;

        [Tooltip("Consejo opcional que el Director reproduce automáticamente en cuanto termina de caminar hasta este punto (GDD: \"verificar si el punto tiene una ronda de consejos o animaciones en especial\").")]
        public DirectorAdviceData arrivalAdvice;

        /// <summary>
        /// True cuando el ARGeospatialCreatorAnchor de este GameObject ya resolvió
        /// su ARGeospatialAnchor real y reparentó este transform bajo él (igual
        /// chequeo que HotspotController.IsAnchorReady / RouteWaypoint.IsAnchorReady).
        /// </summary>
        public bool IsAnchorReady()
        {
            return transform.parent != null &&
                   transform.parent.GetComponent<ARGeospatialAnchor>() != null;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, 0.3f);
        }
    }
}
