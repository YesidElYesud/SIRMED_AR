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
    [SerializeField] private bool forceReady;
    private SplineContainer spline;
    private RouteWaypoint[] waypoints;
    private LoftRoadBehaviour loftBehaviour;
    private bool _built;
    private List<float3> positions;

    [ContextMenu("Awake")]
    private void Awake()
    {
        spline = GetComponent<SplineContainer>();
        waypoints = GetComponentsInChildren<RouteWaypoint>();
        loftBehaviour = GetComponent<LoftRoadBehaviour>();
    }

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
        if (forceReady) return true;

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
        _built = true;

#if UNITY_EDITOR
        EditorUtility.SetDirty(spline);
#endif

        if (loftBehaviour != null)
        {
            loftBehaviour.LoftAllRoads();
        }
    }
}