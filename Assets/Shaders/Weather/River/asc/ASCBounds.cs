using UnityEngine;

public class ASCBounds : MonoBehaviour
{
    [Header("ASC Header")]
    public int columns = 1575;
    public int rows = 546;

    public double x = 429030;
    public double y = 693173;

    public double cellSize = 2;

    public bool originIsCenter = true;

    [ContextMenu("Calculate GPS Corners")]
    public void CalculateCorners()
    {
        double xmin = x;
        double ymin = y;

        if (originIsCenter)
        {
            xmin -= cellSize * 0.5;
            ymin -= cellSize * 0.5;
        }

        double xmax = xmin + columns * cellSize;
        double ymax = ymin + rows * cellSize;

        PrintCorner("Inferior izquierda", xmin, ymin);
        PrintCorner("Inferior derecha", xmax, ymin);
        PrintCorner("Superior izquierda", xmin, ymax);
        PrintCorner("Superior derecha", xmax, ymax);
    }

    private void PrintCorner(string name, double x, double y)
    {
        UTMConverter.GPSCoordinate gps = UTMConverter.UTMToGPS(x, y);

        Debug.Log(
            $"{name} | UTM: ({x}, {y}) | GPS: {gps}"
        );
    }
}