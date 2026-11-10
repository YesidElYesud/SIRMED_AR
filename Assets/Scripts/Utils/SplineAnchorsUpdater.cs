using Google.XR.ARCoreExtensions;
using SIRMED.Gameplay.Environment;
using SIRMED.Managers;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Splines.Examples;

#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.Splines;
using UnityEngine.XR.ARSubsystems;

[RequireComponent(typeof(SplineContainer))]
public class SplineAnchorsUpdater : MonoBehaviour
{
    [SerializeField] private SplineContainer spline;
    [SerializeField] private LoftRoadBehaviour loftBehaviour;

    [Space]
    [SerializeField] private RouteWaypoint[] waypoints;

    private bool _built;
    private List<float3> positions;

    private void OnEnable()
    {
        if (GeoAnchorManager.Instance != null)
            GeoAnchorManager.Instance.RecalibrationStart += Rebuild;
    }

    private void OnDisable()
    {
        if (GeoAnchorManager.Instance != null)
            GeoAnchorManager.Instance.RecalibrationStart -= Rebuild;
    }

    public void Rebuild()
    {
        _built = false;
    }

    private void Update()
    {
        if (!_built && AllWaypointsReady())
            BuildSpline();
    }

    private bool AllWaypointsReady()
    {
        foreach (RouteWaypoint wp in waypoints)
        {
            if (wp == null || !wp.IsAnchorReady())
                return false;

            if (!Application.isEditor)
            {
                ARGeospatialAnchor anchor = wp.GetComponentInParent<ARGeospatialAnchor>();
                return anchor != null && anchor.trackingState == TrackingState.Tracking;
            }
        }

        return true;
    }

    [ContextMenu("BuildSpline")]
    private void BuildSpline()
    {
        positions = new(waypoints.Length);

        for (int i = 0; i < waypoints.Length; i++)
        {
            positions.Add(waypoints[i].transform.position);
        }

        spline.Spline = new Spline(positions, TangentMode.AutoSmooth);

        loftBehaviour.LoftAllRoads();

#if UNITY_EDITOR
        EditorUtility.SetDirty(spline);
#endif

        if (spline.Spline.GetLength() > 1)
            _built = true;
    }
}