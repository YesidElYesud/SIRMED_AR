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

        private readonly CultureInfo culture = CultureInfo.InvariantCulture;

        public string ToCsvRow()
        {
            return string.Join(
                ",",
                (Label ?? string.Empty).Replace(",", " "),
                Latitude.ToString(culture),
                Longitude.ToString(culture),
                Altitude.ToString(culture),
                HorizontalAccuracy.ToString(culture),
                VerticalAccuracy.ToString(culture),
                OrientationYawAccuracy.ToString(culture),
                CapturedAtUtc);
        }

        public string ToReadableBlock(int index)
        {
            return
                $"--- Hotspot {index:00} ---\n" +
                $"Nombre: {Label}\n" +
                $"Latitud: {Latitude.ToString(culture)}°\n" +
                $"Longitud: {Longitude.ToString(culture)}°\n" +
                $"Altitud: {Altitude.ToString(culture)} m\n" +
                $"Precisión horizontal: {HorizontalAccuracy.ToString(culture)} m\n" +
                $"Precisión vertical: {VerticalAccuracy.ToString(culture)} m\n" +
                $"Precisión de orientación (yaw): {OrientationYawAccuracy.ToString(culture)}°\n" +
                $"Capturado (UTC): {CapturedAtUtc}";
        }
    }
}