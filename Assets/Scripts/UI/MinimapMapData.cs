namespace SIRMED.UI
{
    using UnityEngine;

    /// <summary>
    /// MinimapMapData — fondo de mapa georreferenciado de un sitio SAT para el
    /// minimapa (calles/edificios de OpenStreetMap renderizados offline, norte arriba).
    ///
    /// Se genera con <c>Tools/minimap/render_osm_minimap.py</c>, que escribe el PNG y
    /// un .json con los tres valores a copiar aquí (centerLatitude, centerLongitude,
    /// metersPerPixel). El centro geográfico corresponde al centro exacto del PNG.
    ///
    /// Proyección: equirectangular local alrededor del centro (la misma del script
    /// Python). A la escala de un barrio (&lt; 1 km) el error frente a una proyección
    /// exacta es de centímetros.
    ///
    /// Datos © OpenStreetMap contributors (ODbL) — mostrar la atribución en la app.
    /// </summary>
    [CreateAssetMenu(fileName = "MinimapMapData", menuName = "AR/Minimap Map Data")]
    public class MinimapMapData : ScriptableObject
    {
        [Tooltip("Textura del mapa (norte arriba). Importar como Sprite/Default sin mipmaps no es necesario; Default sirve.")]
        public Texture2D texture;

        [Tooltip("Latitud del centro del PNG (del .json generado).")]
        public double centerLatitude;

        [Tooltip("Longitud del centro del PNG (del .json generado).")]
        public double centerLongitude;

        [Tooltip("Metros que representa un píxel del PNG (del .json generado).")]
        public float metersPerPixel = 0.293f;

        [Tooltip("Texto de atribución obligatorio de OSM.")]
        public string attribution = "© OpenStreetMap contributors";

        private const double EarthRadius = 6378137.0;

        /// <summary>Lado del mapa en metros (ancho, alto).</summary>
        public Vector2 SizeMeters => texture == null
            ? Vector2.zero
            : new Vector2(texture.width * metersPerPixel, texture.height * metersPerPixel);

        /// <summary>
        /// Lat/lon → metros respecto al centro del mapa: x = este, y = norte.
        /// </summary>
        public Vector2 LatLonToMeters(double latitude, double longitude) =>
            LatLonToMeters(latitude, longitude, centerLatitude, centerLongitude);

        /// <summary>
        /// Lat/lon → metros (x = este, y = norte) respecto a un origen arbitrario,
        /// con la misma proyección equirectangular local del script de render.
        /// </summary>
        public static Vector2 LatLonToMeters(double latitude, double longitude,
                                             double originLatitude, double originLongitude)
        {
            double k = System.Math.PI / 180.0 * EarthRadius;
            double east = (longitude - originLongitude) * k * System.Math.Cos(originLatitude * System.Math.PI / 180.0);
            double north = (latitude - originLatitude) * k;
            return new Vector2((float)east, (float)north);
        }
    }
}
