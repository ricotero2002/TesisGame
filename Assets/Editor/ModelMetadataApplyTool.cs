// Assets/Editor/ModelMetadataApplyTool.cs
using UnityEditor;
using UnityEngine;
using System.IO;
using System.Linq;

/// <summary>
/// Herramienta editor para aplicar manualmente un ModelMetadata a su prefab correspondiente.
/// Seleccioná el asset ModelMetadata en Project y usá el menú: Tools/Metadata/Apply Selected Metadata To Prefab
/// </summary>
public static class ModelMetadataApplyTool
{
    [MenuItem("Tools/Metadata/Apply Selected Metadata To Prefab")]
    public static void ApplySelectedMetadata()
    {
        // Obtener selección
        var selections = Selection.objects;
        if (selections == null || selections.Length == 0)
        {
            EditorUtility.DisplayDialog("Apply Metadata", "Seleccioná un ModelMetadata en el Project window.", "OK");
            return;
        }

        int applied = 0;
        foreach (var obj in selections)
        {
            var meta = obj as ModelMetadata;
            if (meta == null) continue;
            if (ApplyMetadataToPrefab(meta)) applied++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Apply Metadata", $"Aplicados: {applied}", "OK");
    }

    public static bool ApplyMetadataToPrefab(ModelMetadata meta)
    {
        if (meta == null) return false;

        // Determinar nombre base del prefab: si el metadata tiene sufijo "Meta", lo removemos.
        string metaName = meta.name;
        string baseName = metaName.EndsWith("Meta") ? metaName.Substring(0, metaName.Length - 4) : metaName;

        // Intentar encontrar el prefab por varias rutas posibles
        string[] candidatePaths = new string[]
        {
            $"Assets/Prefabs/{baseName}.prefab",
            $"Assets/Prefabs/{metaName}.prefab",         // en caso raro
            $"Assets/Prefabs/{baseName.ToLower()}.prefab",
            $"Assets/Prefabs/{baseName.Replace(' ', '_')}.prefab"
        };

        string foundPrefabPath = null;
        foreach (var p in candidatePaths)
        {
            if (File.Exists(p))
            {
                foundPrefabPath = p;
                break;
            }
        }

        if (foundPrefabPath == null)
        {
            Debug.LogWarning($"ApplyMetadata: No existe prefab en {string.Join(" , ", candidatePaths)} para metadata {metaName} (base={baseName})");
            return false;
        }

        // Cargar prefab editable
        var root = PrefabUtility.LoadPrefabContents(foundPrefabPath);
        if (root == null)
        {
            Debug.LogError($"ApplyMetadata: No se pudo cargar prefab contents {foundPrefabPath}");
            return false;
        }

        try
        {
            // Si referencia de altura existe, aplicar escala uniforme para que altura (y) coincida
            if (meta.referenceHeightProvided > 0.001f)
            {
                var rends = root.GetComponentsInChildren<Renderer>(true);
                if (rends != null && rends.Length > 0)
                {
                    Bounds b = rends[0].bounds;
                    for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                    float currentHeight = b.size.y;
                    if (currentHeight > 0.0001f)
                    {
                        float scale = meta.referenceHeightProvided / currentHeight;
                        root.transform.localScale = root.transform.localScale * scale;
                    }
                }
            }

            // Recalcular bounds y actualizar/crear BoxCollider en root
            var renderers2 = root.GetComponentsInChildren<Renderer>(true);
            if (renderers2 != null && renderers2.Length > 0)
            {
                Bounds combined = renderers2[0].bounds;
                for (int i = 1; i < renderers2.Length; i++) combined.Encapsulate(renderers2[i].bounds);
                Vector3 worldCenter = combined.center;
                Vector3 centerLocal = root.transform.InverseTransformPoint(worldCenter);
                Vector3 lossy = root.transform.lossyScale;
                Vector3 localSize = new Vector3(
                    lossy.x != 0f ? combined.size.x / lossy.x : combined.size.x,
                    lossy.y != 0f ? combined.size.y / lossy.y : combined.size.y,
                    lossy.z != 0f ? combined.size.z / lossy.z : combined.size.z
                );

                var col = root.GetComponent<BoxCollider>();
                if (col == null) col = root.AddComponent<BoxCollider>();
                col.center = centerLocal;
                col.size = localSize;
            }

            // Guardar cambios en prefab
            PrefabUtility.SaveAsPrefabAsset(root, foundPrefabPath);

            // Actualizar ModelMetadata.size (recalcular sobre prefab final)
            Bounds finalBounds = GetBoundsFromPrefab(foundPrefabPath);
            meta.size = finalBounds.size;
            // Recalcular tris y materiales
            GameObject prefabObj = AssetDatabase.LoadAssetAtPath<GameObject>(foundPrefabPath);
            meta.triangleCount = prefabObj != null ? CountTriangles(prefabObj) : meta.triangleCount;
            meta.materialNames = prefabObj != null ? prefabObj.GetComponentsInChildren<MeshRenderer>().SelectMany(r => r.sharedMaterials).Where(m => m != null).Select(m => m.name).Distinct().ToArray() : meta.materialNames;

            // Si cambió category: mover prefab entre pools
            if (!string.IsNullOrEmpty(meta.category))
            {
                MovePrefabBetweenPools(baseName, meta.category);
            }

            EditorUtility.SetDirty(meta);
            Debug.Log($"ApplyMetadata: Applied metadata to prefab {foundPrefabPath}");
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static Bounds GetBoundsFromPrefab(string prefabPath)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null) return new Bounds(Vector3.zero, Vector3.zero);
        var inst = GameObject.Instantiate(prefab);
        inst.hideFlags = HideFlags.HideAndDontSave;
        var rends = inst.GetComponentsInChildren<Renderer>(true);
        if (rends == null || rends.Length == 0) { GameObject.DestroyImmediate(inst); return new Bounds(Vector3.zero, Vector3.zero); }
        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        GameObject.DestroyImmediate(inst);
        return b;
    }

    private static int CountTriangles(GameObject go)
    {
        int tris = 0;
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>()) if (mf.sharedMesh != null) tris += mf.sharedMesh.triangles.Length / 3;
        return tris;
    }

    private static void MovePrefabBetweenPools(string prefabBaseName, string targetCategory)
    {
        // cargar prefab referencia
        string prefabPath = $"Assets/Prefabs/{prefabBaseName}.prefab";
        var prefabObj = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefabObj == null) return;

        // quitar de todas las pools
        string[] guids = AssetDatabase.FindAssets("t:ArtPoolSO");
        foreach (var g in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            var pool = AssetDatabase.LoadAssetAtPath<ArtPoolSO>(path);
            if (pool == null) continue;
            if (pool.artPrefabs.Contains(prefabObj))
            {
                pool.artPrefabs.Remove(prefabObj);
                pool.MarkDirty();
                EditorUtility.SetDirty(pool);
            }
        }

        // añadir a la pool target (crear si no existe)
        string targetPoolPath = $"Assets/Pools/{targetCategory}Pool.asset";
        var targetPool = AssetDatabase.LoadAssetAtPath<ArtPoolSO>(targetPoolPath);
        if (targetPool == null)
        {
            targetPool = ScriptableObject.CreateInstance<ArtPoolSO>();
            targetPool.categoryName = targetCategory;
            if (!AssetDatabase.IsValidFolder("Assets/Pools")) AssetDatabase.CreateFolder("Assets", "Pools");
            AssetDatabase.CreateAsset(targetPool, targetPoolPath);
        }
        if (!targetPool.artPrefabs.Contains(prefabObj))
        {
            targetPool.artPrefabs.Add(prefabObj);
            targetPool.MarkDirty();
            EditorUtility.SetDirty(targetPool);
        }
    }
}
