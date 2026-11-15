using System.Collections.Generic;
using SIRMED.Gameplay.Environment;
using SIRMED.Managers;
using SIRMED.UI;
using UnityEngine;

namespace SIRMED.Gameplay.NPCs
{
    /// <summary>
    /// Camino de un NPC de ayuda comunitaria hasta el punto de encuentro, siguiendo los
    /// waypoints de la ruta de evacuación (EvacuationRouteController.TryGetPathToMeetingPoint).
    /// Lo comparten Don Aurelio, Samuel y el perro.
    ///
    /// Los puntos están a la altura de las anclas (teléfono); el modelo conserva su desfase
    /// vertical respecto a su hotspot (groundOffset ≈ −1,4 m, el piso).
    ///
    /// Si durante la caminata se recalibra o se reancla (GeoAnchorManager), las anclas se
    /// recrean y el camino guardado queda corrido: se vuelve a pedir desde la posición actual.
    /// </summary>
    public class MeetingPointPath
    {
        private const float RepathDelay = 1.5f;   // s tras recrear anclas, para que tomen su pose
        private const float ArriveSqr = 0.0025f;   // 5 cm

        private readonly List<Vector3> _points = new List<Vector3>();
        private readonly List<Vector3> _scratch = new List<Vector3>();
        private float _groundOffset;
        private int _index;
        private float _repathAt = -1f;
        private bool _subscribed;

        public int Count => _points.Count;
        public int Index => _index;
        public bool Finished => _index >= _points.Count;

        /// <summary>Se dispara cuando el camino se rehízo tras una recalibración (Index vuelve a 0).</summary>
        public System.Action Repathed;

        /// <summary>
        /// Arma el camino desde 'from'. Si la escena no tiene ruta anclada usa 'fallback'
        /// (posiciones de mundo). false si no quedó ningún punto.
        /// </summary>
        public bool Build(Vector3 from, float groundOffset, IEnumerable<Vector3> fallback = null)
        {
            _groundOffset = groundOffset;
            _index = 0;
            _points.Clear();

            EvacuationRouteController route = EvacuationRouteController.Instance;
            if (route == null || !route.TryGetPathToMeetingPoint(from, _points))
            {
                _points.Clear();
                if (fallback != null) _points.AddRange(fallback);
            }

            Subscribe();
            return _points.Count > 0;
        }

        /// <summary>
        /// Avanza 'model' hacia el punto actual. Devuelve true el frame en que llega a un punto
        /// (Index ya apunta al siguiente).
        /// </summary>
        public bool Step(Transform model, float speed, float turnSpeed)
        {
            TryRepath(model.position);
            if (Finished) return false;

            Vector3 target = _points[_index] + Vector3.up * _groundOffset;
            model.position = Vector3.MoveTowards(model.position, target, speed * Time.deltaTime);
            Turn(model, target - model.position, turnSpeed);

            if ((model.position - target).sqrMagnitude > ArriveSqr) return false;
            _index++;
            return true;
        }

        /// <summary>Deja de escuchar a GeoAnchorManager (al terminar o en OnDisable).</summary>
        public void Unsubscribe()
        {
            if (!_subscribed) return;
            _subscribed = false;
            GeoAnchorManager gam = GeoAnchorManager.Instance;
            if (gam == null) return;
            gam.RecalibrationFinished -= OnRecalibrated;
            gam.AutoReanchored -= OnAnchorsRecreated;
        }

        private void Subscribe()
        {
            if (_subscribed) return;
            GeoAnchorManager gam = GeoAnchorManager.Instance;
            if (gam == null) return;
            _subscribed = true;
            gam.RecalibrationFinished += OnRecalibrated;
            gam.AutoReanchored += OnAnchorsRecreated;
        }

        private void OnRecalibrated(bool _) => OnAnchorsRecreated();
        private void OnAnchorsRecreated() => _repathAt = Time.time + RepathDelay;

        private void TryRepath(Vector3 from)
        {
            if (_repathAt < 0f || Time.time < _repathAt || Finished) return;

            EvacuationRouteController route = EvacuationRouteController.Instance;
            if (route == null || !route.TryGetPathToMeetingPoint(from, _scratch))
            {
                _repathAt = Time.time + RepathDelay; // anclas aún sin resolver: reintentar
                return;
            }

            _repathAt = -1f;
            _points.Clear();
            _points.AddRange(_scratch);
            _index = 0;
            Debug.Log($"[NPC] Anclas recreadas: camino al punto de encuentro rehecho ({_points.Count} puntos).");
            Repathed?.Invoke();
        }

        public static void Turn(Transform t, Vector3 dir, float turnSpeed)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;
            t.rotation = Quaternion.RotateTowards(t.rotation, Quaternion.LookRotation(dir), turnSpeed * Time.deltaTime);
        }

        /// <summary>
        /// true si un NPC puede abrir su diálogo ahora: el panel existe, está cerrado y el
        /// Director no está hablando (evita dos voces a la vez, p. ej. S7 al llegar al PE).
        /// </summary>
        public static bool CanTalkNow(NpcDialoguePanel panel) =>
            panel != null && !panel.IsOpen &&
            (DirectorAdvicePanel.Instance == null || !DirectorAdvicePanel.Instance.IsShowing);
    }
}
