using Google.XR.ARCoreExtensions;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARSubsystems;

public class AccuracySignalPanel : MonoBehaviour
{
    [SerializeField] private AREarthManager earthManager;
    [SerializeField] private Slider slider;

    private GeospatialPose pose;
    private int horizontal;
    private int vertical;
    private int yaw;
    private int overall;

    private readonly float[] horizontalThresholds = { 3, 5, 8, 15 };
    private readonly float[] verticalThresholds = { 1, 2, 3, 5 };
    private readonly float[] yawThresholds = { 5, 10, 15, 25 };

    private void Update()
    {
        if (earthManager.EarthTrackingState != TrackingState.Tracking)
        {
            SetSignal(0);
            return;
        }

        pose = earthManager.CameraGeospatialPose;

        horizontal = GetAccuracyLevel(pose.HorizontalAccuracy, horizontalThresholds);
        vertical = GetAccuracyLevel(pose.VerticalAccuracy, verticalThresholds);
        yaw = GetAccuracyLevel(pose.OrientationYawAccuracy, yawThresholds);

        overall = Mathf.Min(horizontal, vertical, yaw);

        SetSignal(overall);
    }

    private int GetAccuracyLevel(double accuracy, float[] thresholds)
    {
        if (double.IsNaN(accuracy) || double.IsInfinity(accuracy))
            return 0;

        for (int i = 0; i < thresholds.Length; i++)
        {
            if (accuracy <= thresholds[i])
                return 5 - i;
        }

        return 1;
    }

    private void SetSignal(int level)
    {
        slider.value = level;
    }
}