namespace SIRMED.Gameplay.Dialogue
{
    using UnityEngine;

    /// <summary>
    /// Tarjeta informativa extra que se muestra entre el contenido principal de un
    /// hotspot y su trivia (p. ej. Punto de encuentro / Ruta de evacuación).
    /// Usado por HotspotData.infoCard; la muestra InfoCardPanel. Pura data.
    /// </summary>
    [System.Serializable]
    public class InfoCardData
    {
        [System.Serializable]
        public class Row
        {
            [Tooltip("Ícono de la fila. Se oculta si es null.")]
            public Sprite icon;

            [TextArea(2, 5)]
            public string text;
        }

        [Tooltip("Título de la tarjeta (p. ej. \"PUNTO DE ENCUENTRO\").")]
        public string title;

        [TextArea(2, 6)]
        [Tooltip("Texto introductorio bajo el título.")]
        public string intro;

        [Tooltip("Foto de fondo opcional. Si es null se deja la que tenga el panel.")]
        public Sprite background;

        [Tooltip("Filas ícono + texto. El panel tiene un número fijo de espacios; " +
                 "los que sobren se ocultan y las filas de más se ignoran.")]
        public Row[] rows;
    }
}
