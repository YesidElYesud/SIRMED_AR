namespace SIRMED.Geospatial
{
    using System;
    using System.Globalization;

    /// <summary>
    /// A single point captured during a site survey: a Geospatial pose plus the metadata
    /// needed to later recreate a hotspot/3D element at the same real-world location.
    /// </summary>
    [Serializable]
    public class HotspotRecord
    {
        public string Label;
        public double Latitude;
        public double Longitude;
        public double Altitude;
        public double HorizontalAccuracy;
        public double VerticalAccuracy;
        public double OrientationYawAccuracy;
        public string CapturedAtUtc;

        public static string CsvHeader =>
            "label,latitude,longitude,altitude,horizontal_accuracy_m,vertical_accuracy_m," +
            "orientation_yaw_accuracy_deg,captured_at_utc";

        public string ToCsvRow()
        {
            var culture = CultureInfo.InvariantCulture;
            var safeLabel = (Label ?? string.Empty).Replace(",", " ");
            return string.Join(
                ",",
                safeLabel,
                Latitude.ToString("F6", culture),
                Longitude.ToString("F6", culture),
                Altitude.ToString("F2", culture),
                HorizontalAccuracy.ToString("F2", culture),
                VerticalAccuracy.ToString("F2", culture),
                OrientationYawAccuracy.ToString("F1", culture),
                CapturedAtUtc);
        }

        public string ToReadableBlock(int index)
        {
            return
                $"--- Hotspot {index:00} ---\n" +
                $"Nombre: {Label}\n" +
                $"Latitud: {Latitude.ToString("F6", CultureInfo.InvariantCulture)}°\n" +
                $"Longitud: {Longitude.ToString("F6", CultureInfo.InvariantCulture)}°\n" +
                $"Altitud: {Altitude.ToString("F2", CultureInfo.InvariantCulture)} m\n" +
                $"Precisión horizontal: {HorizontalAccuracy.ToString("F2", CultureInfo.InvariantCulture)} m\n" +
                $"Precisión vertical: {VerticalAccuracy.ToString("F2", CultureInfo.InvariantCulture)} m\n" +
                $"Precisión de orientación (yaw): {OrientationYawAccuracy.ToString("F1", CultureInfo.InvariantCulture)}°\n" +
                $"Capturado (UTC): {CapturedAtUtc}";
        }
    }
}
