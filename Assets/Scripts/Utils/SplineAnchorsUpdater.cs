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

    private void Rebuild()
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
            if (!wp.IsAnchorReady())
                return false;

        return true;
    }

    [ContextMenu("BuildSpline")]
    private void BuildSpline()
    {
        positions = new(waypoints.Length);

        for (int i = 0; i < waypoints.Length; i++)
            positions.Add(waypoints[i].transform.position);

        spline.Spline = new Spline(positions, TangentMode.AutoSmooth);

        loftBehaviour.LoftAllRoads();

#if UNITY_EDITOR
        EditorUtility.SetDirty(spline);
#endif

        _built = true;
    }
}