// Assets/Editor/PoolTools.cs
using UnityEditor;
using UnityEngine;
using System.IO;

/// <summary>
/// Herramientas del editor para recalcular promedios de ArtPoolSO y asegurar BoxColliders en prefabs.
/// Colocar en Assets/Editor/PoolTools.cs
/// </summary>
public static class PoolTools
{
    [MenuItem("Tools/Pools/Recalculate All Pool Averages")]
    public static void RecalculateAll()
    {
        // Buscar todos los assets del tipo ArtPoolSO
        string[] guids = AssetDatabase.FindAssets("t:ArtPoolSO");
        if (guids == null || guids.Length == 0)
        {
            Debug.Log("PoolTools: No se encontraron ArtPoolSO en el proyecto.");
            return;
        }

        int processed = 0;
        foreach (var g in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            ArtPoolSO pool = AssetDatabase.LoadAssetAtPath<ArtPoolSO>(path);
            if (pool != null)
            {
                // invalidar caches y recalcular
                pool.MarkDirty();
                pool.MarkDirtyMaxSize(); // si tu clase tiene este método para el max prefab
                Vector3 avg = pool.GetAverageSize();   // esto instancia temporalmente los prefabs en Editor
                Vector3 max = pool.GetMaxPrefabSize(); // recalcula max si existe
                EditorUtility.SetDirty(pool);
                Debug.Log($"PoolTools: Recalculated pool '{pool.categoryName}' at {path} -> avg {avg}, max {max}");
                processed++;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"PoolTools: Recalculate complete. Pools processed: {processed}");
    }

   
}
