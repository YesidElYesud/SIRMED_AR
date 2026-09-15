namespace SIRMED.Gameplay.Dialogue
{
    using UnityEngine;

    /// <summary>
    /// Un slide individual de contenido educativo mid-game.
    /// Usado por HotspotData.infoSlides[]. El panel que los recorre
    /// (InfoSlidePanel) se implementa en una fase posterior; esta clase
    /// es pura data.
    /// </summary>
    [System.Serializable]
    public class InfoSlideData
    {
        [Tooltip("Título del slide.")]
        public string title;

        [TextArea(3, 8)]
        [Tooltip("Texto principal del slide.")]
        public string body;

        [Tooltip("Imagen ilustrativa opcional. Se oculta si es null.")]
        public Sprite image;
    }
}
