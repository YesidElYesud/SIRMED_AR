using UnityEditor;

/// <summary>
/// UISpriteImportSettings — Configura como Sprite UI toda textura nueva bajo Assets/UI/Sprites.
/// Solo actúa en la primera importación (sin .meta previo), así que los ajustes que se hagan
/// a mano después en el Inspector se respetan.
/// - Iconos/: iconos pequeños del HUD, tamaño máximo 512.
/// - Alta/:   ilustraciones en alta resolución (~1500 px), limitadas a 1024 para no
///            gastar memoria en el teléfono; subir a 2048 por asset si se ve borroso.
/// </summary>
public class UISpriteImportSettings : AssetPostprocessor
{
    const string Root = "Assets/UI/Sprites/";

    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(Root) || !assetImporter.importSettingsMissing)
            return;

        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.wrapMode = UnityEngine.TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Compressed;
        importer.maxTextureSize = assetPath.StartsWith(Root + "Alta/") ? 1024 : 512;
    }
}
