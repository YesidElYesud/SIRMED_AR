using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using System.Text;
using System.IO;

/// <summary>
/// HierarchyExporter — Exporta la jerarquía completa de la escena a un archivo .txt.
/// Menú: Tools → Export Scene Hierarchy
/// El archivo se guarda en la raíz del proyecto como "SCENE_HIERARCHY.txt".
/// </summary>
public static class HierarchyExporter
{
    [MenuItem("Tools/Export Scene Hierarchy")]
    public static void Export()
    {
        Scene scene = SceneManager.GetActiveScene();
        var sb = new StringBuilder();

        sb.AppendLine($"ESCENA: {scene.name}");
        sb.AppendLine($"Exportado: {System.DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine(new string('=', 60));
        sb.AppendLine();

        foreach (GameObject root in scene.GetRootGameObjects())
            AppendNode(sb, root.transform, 0);

        string path = Path.Combine(Application.dataPath, "../SCENE_HIERARCHY.txt");
        File.WriteAllText(path, sb.ToString());

        AssetDatabase.Refresh();
        Debug.Log($"[HierarchyExporter] Guardado en: {path}");
        EditorUtility.DisplayDialog("Jerarquía exportada",
            $"Archivo guardado en:\n{path}", "OK");
    }

    private static void AppendNode(StringBuilder sb, Transform t, int depth)
    {
        string indent  = new string(' ', depth * 2);
        string prefix  = depth == 0 ? "├─ " : "│  ".PadRight(depth * 2 - 2) + "├─ ";
        string active  = t.gameObject.activeSelf ? "" : " [INACTIVO]";

        // Listar componentes relevantes (excluye Transform y CanvasRenderer)
        var comps = new System.Collections.Generic.List<string>();
        foreach (var c in t.GetComponents<Component>())
        {
            if (c == null) continue;
            string typeName = c.GetType().Name;
            if (typeName == "Transform" || typeName == "CanvasRenderer") continue;
            comps.Add(typeName);
        }

        string compList = comps.Count > 0 ? $"  [{string.Join(", ", comps)}]" : "";

        sb.AppendLine($"{indent}{t.name}{active}{compList}");

        foreach (Transform child in t)
            AppendNode(sb, child, depth + 1);
    }
}
