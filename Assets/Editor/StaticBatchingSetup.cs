using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// Marks all suitable GameObjects in the open scene as Static so Unity's
/// Static Batching can combine them into fewer draw calls for WebGL.
///
/// Objects that should NOT be static (they animate or are dynamic) are
/// identified by partial name match and excluded automatically.
public static class StaticBatchingSetup
{
    // Objects whose names contain any of these strings are skipped.
    private static readonly string[] s_DynamicKeywords =
    {
        "agua", "water", "rio", "river",
        "lluvia", "rain", "splash", "chispa",
        "escombro", "debris",
        "camera", "cam",
        "light", "luz",
        "npc", "persona", "player",
        "fog", "niebla",
        "viento", "wind",
        "animat",
    };

    [MenuItem("Tools/WebGL Performance/Mark Static Objects")]
    public static void MarkStaticObjects()
    {
        var roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
        int marked = 0, skipped = 0;
        var toMark = new List<GameObject>();

        foreach (var root in roots)
            CollectCandidates(root, toMark);

        foreach (var go in toMark)
        {
            // All static flags: Contribute GI, Occluder, Occludee, Batching, Navigation, Reflection Probe
            GameObjectUtility.SetStaticEditorFlags(go,
                StaticEditorFlags.ContributeGI |
                StaticEditorFlags.OccluderStatic |
                StaticEditorFlags.OccludeeStatic |
                StaticEditorFlags.BatchingStatic |
                StaticEditorFlags.ReflectionProbeStatic);
            marked++;
        }

        Debug.Log($"[StaticBatching] Marked {marked} objects as static, skipped {skipped}.");
        EditorUtility.DisplayDialog("Static Batching Setup",
            $"Marcados como Static: {marked} objetos.\n" +
            $"Excluidos (dinamicos): {skipped} objetos.\n\n" +
            "Recuerda hacer un nuevo Build WebGL para que el batching tome efecto.",
            "OK");
    }

    [MenuItem("Tools/WebGL Performance/Clear Static Flags (revert)")]
    public static void ClearStaticFlags()
    {
        var roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
        int cleared = 0;
        foreach (var root in roots)
        {
            foreach (var go in root.GetComponentsInChildren<Transform>(true))
            {
                if (GameObjectUtility.GetStaticEditorFlags(go.gameObject) != 0)
                {
                    GameObjectUtility.SetStaticEditorFlags(go.gameObject, 0);
                    cleared++;
                }
            }
        }
        Debug.Log($"[StaticBatching] Cleared static flags from {cleared} objects.");
    }

    private static void CollectCandidates(GameObject go, List<GameObject> result)
    {
        if (ShouldSkip(go))
            return;

        // Skip objects that have particle systems, cameras, lights, or animators
        if (go.GetComponent<ParticleSystem>() != null) return;
        if (go.GetComponent<Camera>() != null) return;
        if (go.GetComponent<Light>() != null) return;
        if (go.GetComponent<Animator>() != null) return;
        if (go.GetComponent<Animation>() != null) return;

        // Mark only objects that have a renderer (visual contribution)
        if (go.GetComponent<Renderer>() != null)
            result.Add(go);

        foreach (Transform child in go.transform)
            CollectCandidates(child.gameObject, result);
    }

    private static bool ShouldSkip(GameObject go)
    {
        string nameLower = go.name.ToLowerInvariant();
        foreach (var kw in s_DynamicKeywords)
            if (nameLower.Contains(kw))
                return true;
        return false;
    }
}
