
public static class UTMConverter
{
    // WGS84
    private const double A = 6378137.0;
    private const double E2 = 0.00669437999014;
    private const double K0 = 0.9996;

    // UTM Zone 18N
    private const double CentralMeridian = -75.0;
    private const double FalseEasting = 500000.0;
    private const double FalseNorthing = 0.0;

    public struct GPSCoordinate
    {
        public double latitude;
        public double longitude;

        public GPSCoordinate(double lat, double lon)
        {
            latitude = lat;
            longitude = lon;
        }

        public override readonly string ToString()
        {
            return $"Lat: {latitude:F10}, Lon: {longitude:F10}";
        }
    }

    public static GPSCoordinate UTMToGPS(double x, double y)
    {
        double e1 = (1.0 - System.Math.Sqrt(1.0 - E2)) /
                    (1.0 + System.Math.Sqrt(1.0 - E2));

        double ePrime2 = E2 / (1.0 - E2);

        double m = (y - FalseNorthing) / K0;

        double mu = m / (A * (
            1.0 - E2 / 4.0
            - 3.0 * E2 * E2 / 64.0
            - 5.0 * E2 * E2 * E2 / 256.0
        ));

        double phi1 =
            mu
            + (3.0 * e1 / 2.0 - 27.0 * System.Math.Pow(e1, 3) / 32.0)
                * System.Math.Sin(2.0 * mu)
            + (21.0 * e1 * e1 / 16.0 - 55.0 * System.Math.Pow(e1, 4) / 32.0)
                * System.Math.Sin(4.0 * mu)
            + (151.0 * System.Math.Pow(e1, 3) / 96.0)
                * System.Math.Sin(6.0 * mu)
            + (1097.0 * System.Math.Pow(e1, 4) / 512.0)
                * System.Math.Sin(8.0 * mu);

        double n1 = A / System.Math.Sqrt(
            1.0 - E2 * System.Math.Pow(System.Math.Sin(phi1), 2.0)
        );

        double t1 = System.Math.Pow(System.Math.Tan(phi1), 2.0);
        double c1 = ePrime2 * System.Math.Pow(System.Math.Cos(phi1), 2.0);

        double r1 = A * (1.0 - E2) /
            System.Math.Pow(
                1.0 - E2 * System.Math.Pow(System.Math.Sin(phi1), 2.0),
                1.5
            );

        double d = (x - FalseEasting) / (n1 * K0);

        double latitude =
            phi1 - (n1 * System.Math.Tan(phi1) / r1) *
            (
                d * d / 2.0
                - (5.0 + 3.0 * t1 + 10.0 * c1
                   - 4.0 * c1 * c1 - 9.0 * ePrime2)
                    * System.Math.Pow(d, 4.0) / 24.0
                + (61.0 + 90.0 * t1 + 298.0 * c1
                   + 45.0 * t1 * t1 - 252.0 * ePrime2
                   - 3.0 * c1 * c1)
                    * System.Math.Pow(d, 6.0) / 720.0
            );

        double longitude =
            (
                d
                - (1.0 + 2.0 * t1 + c1) * System.Math.Pow(d, 3.0) / 6.0
                + (5.0 - 2.0 * c1 + 28.0 * t1
                   - 3.0 * c1 * c1 + 8.0 * ePrime2
                   + 24.0 * t1 * t1)
                    * System.Math.Pow(d, 5.0) / 120.0
            ) / System.Math.Cos(phi1);

        latitude *= 180.0 / System.Math.PI;

        longitude = CentralMeridian +
                    longitude * 180.0 / System.Math.PI;

        return new GPSCoordinate(latitude, longitude);
    }
}