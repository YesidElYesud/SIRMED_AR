using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Ventana de editor: Window → Asset Report
/// Lista todos los assets con tamaño en disco, tipo Unity y configuración de compresión.
/// Incluye overrides de plataforma WebGL cuando están disponibles.
/// </summary>
public class AssetReportWindow : EditorWindow
{
    // ── Datos ─────────────────────────────────────────────────────────────────

    private struct AssetInfo
    {
        public string path;
        public string name;
        public string type;
        public long   sizeBytes;
        public string compression;
        public string compressionDetail;
    }

    private List<AssetInfo> _allAssets    = new();
    private List<AssetInfo> _filtered     = new();
    private Vector2         _scroll;
    private string          _filterType   = "Todos";
    private string          _filterSearch = "";
    private bool            _sortAscending = false;
    private bool            _dirty         = true;

    private static readonly string[] TYPE_OPTIONS =
        { "Todos", "Texture2D", "AudioClip", "Mesh", "VideoClip", "Font", "ScriptableObject", "Otro" };

    // ── Apertura ──────────────────────────────────────────────────────────────

    [MenuItem("Window/Asset Report")]
    public static void Open() => GetWindow<AssetReportWindow>("Asset Report");

    // ── GUI ───────────────────────────────────────────────────────────────────

    private void OnGUI()
    {
        DrawToolbar();

        if (_dirty)
        {
            ScanAssets();
            ApplyFilter();
            _dirty = false;
        }

        DrawTable();
        DrawFooter();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        if (GUILayout.Button("Escanear", EditorStyles.toolbarButton, GUILayout.Width(80)))
        {
            _dirty = true;
            GUI.FocusControl(null);
        }

        GUILayout.Space(6);

        // Filtro por tipo
        EditorGUI.BeginChangeCheck();
        int idx = System.Array.IndexOf(TYPE_OPTIONS, _filterType);
        idx = EditorGUILayout.Popup(idx < 0 ? 0 : idx, TYPE_OPTIONS,
            EditorStyles.toolbarPopup, GUILayout.Width(120));
        if (EditorGUI.EndChangeCheck())
        {
            _filterType = TYPE_OPTIONS[idx];
            ApplyFilter();
        }

        GUILayout.Space(6);

        // Búsqueda por nombre
        EditorGUI.BeginChangeCheck();
        _filterSearch = EditorGUILayout.TextField(_filterSearch, EditorStyles.toolbarSearchField);
        if (EditorGUI.EndChangeCheck()) ApplyFilter();

        if (GUILayout.Button("✕", EditorStyles.toolbarButton, GUILayout.Width(20)))
        {
            _filterSearch = "";
            ApplyFilter();
            GUI.FocusControl(null);
        }

        GUILayout.FlexibleSpace();

        if (GUILayout.Button("Exportar CSV", EditorStyles.toolbarButton, GUILayout.Width(90)))
            ExportCSV();

        EditorGUILayout.EndHorizontal();

        // Encabezados de tabla
        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
        ColHeader("Nombre",        250);
        ColHeader("Tipo",          100);
        ColHeader("Compresión",    130);
        ColHeader("Detalle",       200);

        // Encabezado de tamaño clicable para ordenar
        if (GUILayout.Button(_sortAscending ? "Tamaño ▲" : "Tamaño ▼",
            EditorStyles.miniButtonLeft, GUILayout.Width(80)))
        {
            _sortAscending = !_sortAscending;
            _filtered = _sortAscending
                ? _filtered.OrderBy(a => a.sizeBytes).ToList()
                : _filtered.OrderByDescending(a => a.sizeBytes).ToList();
        }
        EditorGUILayout.EndHorizontal();
    }

    private static void ColHeader(string label, float width)
    {
        GUILayout.Label(label, EditorStyles.boldLabel, GUILayout.Width(width));
    }

    private void DrawTable()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        foreach (var a in _filtered)
        {
            EditorGUILayout.BeginHorizontal();

            // Nombre — clicable para seleccionar el asset
            if (GUILayout.Button(a.name, EditorStyles.linkLabel, GUILayout.Width(248)))
            {
                var obj = AssetDatabase.LoadMainAssetAtPath(a.path);
                Selection.activeObject = obj;
                EditorGUIUtility.PingObject(obj);
            }

            GUILayout.Label(a.type,              GUILayout.Width(100));
            GUILayout.Label(a.compression,       GUILayout.Width(130));
            GUILayout.Label(a.compressionDetail, GUILayout.Width(200));
            GUILayout.Label(FormatSize(a.sizeBytes), GUILayout.Width(80));

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawFooter()
    {
        long total = _filtered.Sum(a => a.sizeBytes);
        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
        GUILayout.Label($"{_filtered.Count} assets  |  Total en disco: {FormatSize(total)}",
            EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();
    }

    // ── Escaneo ───────────────────────────────────────────────────────────────

    private void ScanAssets()
    {
        _allAssets.Clear();

        string[] paths = AssetDatabase.GetAllAssetPaths()
            .Where(p => p.StartsWith("Assets/") && !p.EndsWith(".meta"))
            .ToArray();

        int total = paths.Length;
        for (int i = 0; i < total; i++)
        {
            string path = paths[i];

            if (i % 50 == 0)
                EditorUtility.DisplayProgressBar("Asset Report", path, (float)i / total);

            var info = BuildInfo(path);
            if (info.HasValue) _allAssets.Add(info.Value);
        }

        EditorUtility.ClearProgressBar();
        _allAssets = _allAssets.OrderByDescending(a => a.sizeBytes).ToList();
    }

    private static AssetInfo? BuildInfo(string path)
    {
        // Ignorar directorios y scripts de editor que no son assets de runtime
        if (AssetDatabase.IsValidFolder(path)) return null;

        var absPath = Path.Combine(
            Directory.GetParent(Application.dataPath).FullName,
            path.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(absPath)) return null;

        long size = new FileInfo(absPath).Length;

        System.Type assetType = AssetDatabase.GetMainAssetTypeAtPath(path);
        if (assetType == null) return null;

        string typeName;
        string compression;
        string detail;

        GetCompressionInfo(path, assetType, out typeName, out compression, out detail);

        return new AssetInfo
        {
            path              = path,
            name              = Path.GetFileName(path),
            type              = typeName,
            sizeBytes         = size,
            compression       = compression,
            compressionDetail = detail,
        };
    }

    // ── Compresión por tipo ───────────────────────────────────────────────────

    private static void GetCompressionInfo(string path, System.Type assetType,
        out string typeName, out string compression, out string detail)
    {
        compression = "—";
        detail      = "";

        // ── Textura ───────────────────────────────────────────────────────────
        if (assetType == typeof(Texture2D) || assetType == typeof(Cubemap))
        {
            typeName = "Texture2D";
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) { compression = "N/A"; return; }

            // Intentar obtener override de WebGL primero
            var webglSettings = imp.GetPlatformTextureSettings("WebGL");
            if (webglSettings.overridden)
            {
                compression = webglSettings.format.ToString();
                detail      = $"WebGL override | Q:{webglSettings.compressionQuality}";
                if (webglSettings.crunchedCompression) detail += " | Crunch";
            }
            else
            {
                compression = imp.textureCompression.ToString();
                detail      = $"Default | MaxSize:{imp.maxTextureSize}";
                if (imp.crunchedCompression) detail += " | Crunch";
            }
            return;
        }

        // ── Audio ─────────────────────────────────────────────────────────────
        if (assetType == typeof(AudioClip))
        {
            typeName = "AudioClip";
            var imp = AssetImporter.GetAtPath(path) as AudioImporter;
            if (imp == null) { compression = "N/A"; return; }

            // Override WebGL si existe, si no, default
            AudioImporterSampleSettings settings;
            bool hasOverride = imp.ContainsSampleSettingsOverride("WebGL");
            settings    = hasOverride
                ? imp.GetOverrideSampleSettings("WebGL")
                : imp.defaultSampleSettings;

            compression = settings.compressionFormat.ToString();
            detail      = hasOverride ? "WebGL override" : "Default";
            detail     += $" | Q:{settings.quality:F2} | {settings.sampleRateSetting}";
            return;
        }

        // ── Mesh / Modelo 3D ──────────────────────────────────────────────────
        if (assetType == typeof(Mesh))
        {
            typeName = "Mesh";
            var imp = AssetImporter.GetAtPath(path) as ModelImporter;
            if (imp == null) { compression = "N/A"; return; }
            compression = imp.meshCompression.ToString();
            detail      = $"R/W:{imp.isReadable} | OptMesh:{imp.optimizeMeshPolygons}";
            return;
        }

        // ── Video ─────────────────────────────────────────────────────────────
        if (assetType == typeof(UnityEngine.Video.VideoClip))
        {
            typeName = "VideoClip";
            var imp = AssetImporter.GetAtPath(path) as VideoClipImporter;
            if (imp == null) { compression = "N/A"; return; }
            compression = imp.defaultTargetSettings.codec.ToString();
            detail      = $"{imp.defaultTargetSettings.bitrateMode} | "
                        + $"Res:{imp.defaultTargetSettings.resizeMode}";
            return;
        }

        // ── Font ──────────────────────────────────────────────────────────────
        if (assetType == typeof(Font))
        {
            typeName    = "Font";
            compression = "N/A";
            return;
        }

        // ── ScriptableObject ──────────────────────────────────────────────────
        if (typeof(ScriptableObject).IsAssignableFrom(assetType))
        {
            typeName    = "ScriptableObject";
            compression = "N/A";
            detail      = assetType.Name;
            return;
        }

        // ── Genérico ──────────────────────────────────────────────────────────
        typeName    = assetType.Name;
        compression = "N/A";
    }

    // ── Filtrado ──────────────────────────────────────────────────────────────

    private void ApplyFilter()
    {
        _filtered = _allAssets
            .Where(a =>
            {
                bool typeMatch = _filterType == "Todos"
                    || (_filterType == "Otro"  && !TYPE_OPTIONS.Skip(1).SkipLast(1).Contains(a.type))
                    || a.type == _filterType;

                bool nameMatch = string.IsNullOrEmpty(_filterSearch)
                    || a.name.IndexOf(_filterSearch, System.StringComparison.OrdinalIgnoreCase) >= 0;

                return typeMatch && nameMatch;
            })
            .ToList();
    }

    // ── Exportar CSV ──────────────────────────────────────────────────────────

    private void ExportCSV()
    {
        string filePath = EditorUtility.SaveFilePanel(
            "Guardar Asset Report", "", "asset_report", "csv");
        if (string.IsNullOrEmpty(filePath)) return;

        var sb = new StringBuilder();
        sb.AppendLine("Nombre,Tipo,Compresión,Detalle,Tamaño (bytes),Tamaño legible,Ruta");

        foreach (var a in _filtered)
        {
            sb.AppendLine(
                $"\"{a.name}\"," +
                $"\"{a.type}\"," +
                $"\"{a.compression}\"," +
                $"\"{a.compressionDetail}\"," +
                $"{a.sizeBytes}," +
                $"\"{FormatSize(a.sizeBytes)}\"," +
                $"\"{a.path}\"");
        }

        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        Debug.Log($"[AssetReport] Exportado: {filePath}");
        EditorUtility.RevealInFinder(filePath);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1024 * 1024) return $"{bytes / (1024f * 1024f):F1} MB";
        if (bytes >= 1024)        return $"{bytes / 1024f:F1} KB";
        return $"{bytes} B";
    }
}
