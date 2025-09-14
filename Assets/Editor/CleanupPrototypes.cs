// Assets/Editor/CleanupPrototypes.cs
using UnityEngine;
using UnityEditor;

public static class CleanupPrototypes
{
    [MenuItem("Tools/Reveal Hidden Cubes")]
    public static void Reveal()
    {
        var all = Resources.FindObjectsOfTypeAll<GameObject>();
        int count = 0;
        foreach (var go in all)
        {
            if (go.name == "ProtoWall" || go.name == "ProtoFloor")
            {
                go.hideFlags = HideFlags.None;
                go.SetActive(true);
                Undo.RegisterFullObjectHierarchyUndo(go, "Reveal hidden object");
                count++;
            }
        }
        Debug.Log($"Revealed {count} hidden object(s). They should now appear in the Scene/Hierarchy.");
    }

    [MenuItem("Tools/Cleanup/Hard Remove ProtoWall/ProtoFloor")]
    public static void RemoveAllHiddenPrototypes()
    {
        // This will find absolutely every GameObject in memory,
        // even ones hidden from Hierarchy by HideFlags.
        var all = Resources.FindObjectsOfTypeAll<GameObject>();
        int removed = 0;
        foreach (var go in all)
        {
            // match the names your old prototypes used
            if (go.name == "ProtoWall" || go.name == "ProtoFloor")
            {
                // this will delete hidden objects too
                Object.DestroyImmediate(go);
                removed++;
            }
        }
        Debug.Log($"HardCleanupPrototypes: removed {removed} hidden prototype(s).");
    }

    [MenuItem("Tools/Cleanup/Remove Hidden Prototypes")]
    public static void RemoveHidden()
    {
        // Find *all* GameObjects (even hidden ones)
        var all = Resources.FindObjectsOfTypeAll<GameObject>();
        int count = 0;

        foreach (var go in all)
        {
            if (go.name == "ProtoWall" || go.name == "ProtoFloor")
            {
                // this will also delete hidden objects
                Object.DestroyImmediate(go);
                count++;
            }
        }

        Debug.Log($"Removed {count} hidden prototype(s).");
    }

    [MenuItem("Tools/Cleanup/Remove ProtoWall & ProtoFloor")]
    public static void DeletePrototypes()
    {
        // This returns ALL GameObjects in memory, active or not:
        var all = Resources.FindObjectsOfTypeAll<GameObject>();

        int removed = 0;
        foreach (var go in all)
        {
            // match exactly the names you gave your prototypes
            if (go.name == "ProtoWall" || go.name == "ProtoFloor")
            {
                // Undo support so you can Ctrl+Z it if you like
                Undo.DestroyObjectImmediate(go);
                removed++;
            }
        }

        Debug.Log($"CleanupPrototypes: removed {removed} hidden prototype(s).");
    }
}
