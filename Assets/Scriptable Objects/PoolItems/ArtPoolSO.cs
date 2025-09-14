using System.IO;
using System;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using UnityEditor;
using System.Reflection;

[CreateAssetMenu(fileName = "ArtPool", menuName = "Scriptable Objects/Art Pool")]
public class ArtPoolSO : ScriptableObject
{
    [Tooltip("Aquí arrastra todos los prefabs de tu pool (por ejemplo, distintos modelos de cápsulas, estatuas, pinturas, etc.).")]
    public List<GameObject> artPrefabs = new List<GameObject>();

    [System.NonSerialized] private List<int> availableIndices;
    [Header("Subpools (auto-managed)")]
    public List<SubPool> subpools = new List<SubPool>();

    [Header("Optional Environment Overrides")]
    public MaterialPoolSO floorMatOverride;
    public MaterialPoolSO wallMatOverride;
    public MaterialPoolSO ceilingMatOverride;
    public MaterialPoolSO tileMatOverride;
    public ArtPoolSO DoorOverride;
    public string categoryName;

    [HideInInspector] public int themeIndex = -1;

    [Header("Snapshot JSON (auto-managed)")]
    public string snapshotJsonPath;

    [Tooltip("(Opcional) Asigna aquí un TextAsset con el JSON si quieres que se cargue en runtime sin depender de archivos en disco.")]
    public TextAsset snapshotJsonAsset;

    // Runtime chosen subpool tracking
    [NonSerialized] private string _lastChosenSubpoolId = null;
    [NonSerialized] private SubPool _cachedChosenSubpool = null;

    [Serializable]
    public class SubpoolJson
    {
        public string subpoolId;
        public string displayName;
        public List<string> memberObjectIds = new List<string>();
        public FeatureVector5 center5 = FeatureVector5.zero;
    }

    [Serializable]
    public class PoolJson
    {
        public string poolName;
        public List<SubpoolJson> subpools = new List<SubpoolJson>();
    }

    public string GetDefaultSnapshotPath()
    {
        string safeName = string.IsNullOrEmpty(categoryName) ? "pool" : categoryName;
        return $"Assets/PoolsSnapshots/{safeName}_pool.json";
    }

    public void InitMaterialTheme()
    {
        var pool = tileMatOverride ?? floorMatOverride ?? wallMatOverride ?? ceilingMatOverride;
        if (pool != null && pool.artPrefabs.Count > 0)
        {
            themeIndex = UnityEngine.Random.Range(0, pool.artPrefabs.Count);
        }
        else
        {
            themeIndex = -1;
        }
    }

    public GameObject GetThemeDoor()
    {
        if (DoorOverride != null
         && themeIndex >= 0
         && themeIndex < DoorOverride.artPrefabs.Count)
            return DoorOverride.artPrefabs[themeIndex];
        return null;
    }

    public Material GetThemeTileMat()
    {
        if (tileMatOverride != null
         && themeIndex >= 0
         && themeIndex < tileMatOverride.artPrefabs.Count)
            return tileMatOverride.artPrefabs[themeIndex];
        return null;
    }

    public Material GetThemeFloorMat()
    {
        if (floorMatOverride != null
         && themeIndex >= 0
         && themeIndex < floorMatOverride.artPrefabs.Count)
            return floorMatOverride.artPrefabs[themeIndex];
        return null;
    }

    public Material GetThemeWallMat()
    {
        if (wallMatOverride != null
         && themeIndex >= 0
         && themeIndex < wallMatOverride.artPrefabs.Count)
            return wallMatOverride.artPrefabs[themeIndex];
        return null;
    }

    public Material GetThemeCeilingMat()
    {
        if (ceilingMatOverride != null
         && themeIndex >= 0
         && themeIndex < ceilingMatOverride.artPrefabs.Count)
            return ceilingMatOverride.artPrefabs[themeIndex];
        return null;
    }

    public void ResetPool()
    {
        availableIndices = new List<int>(artPrefabs.Count);
        for (int i = 0; i < artPrefabs.Count; i++)
        {
            availableIndices.Add(i);
        }
    }

    public GameObject GetRandomPrefab()
    {
        if (artPrefabs == null || artPrefabs.Count == 0)
        {
            Debug.LogWarning("ArtPoolSO.GetRandomPrefab: no hay prefabs cargados en artPrefabs.");
            return null;
        }

        if (availableIndices == null || availableIndices.Count == 0)
        {
            ResetPool();
        }

        int randomListIndex = UnityEngine.Random.Range(0, availableIndices.Count);
        int prefabIndex = availableIndices[randomListIndex];
        availableIndices.RemoveAt(randomListIndex);
        return artPrefabs[prefabIndex];
    }

    // --- Tamaños ---
    private Vector3 averageSize = Vector3.zero;
    private bool dirty = true;
    private Vector3 cachedMaxPrefabSize = Vector3.zero;
    [HideInInspector] private bool cachedMaxDirty = true;

    public Vector3 GetAverageSize()
    {
        if (!dirty) return averageSize;
        if (artPrefabs.Count == 0) return Vector3.one;

        Vector3 sum = Vector3.zero;
        int count = 0;
        foreach (var prefab in artPrefabs)
        {
            if (prefab == null) continue;
            var temp = Instantiate(prefab);
            temp.hideFlags = HideFlags.HideAndDontSave;
            BoxCollider col = temp.GetComponentInChildren<BoxCollider>();
            Vector3 sz;
            if (col != null) sz = Vector3.Scale(col.size, temp.transform.lossyScale);
            else
            {
                var rends = temp.GetComponentsInChildren<Renderer>();
                if (rends.Length == 0) { DestroyImmediate(temp); continue; }
                Bounds b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                sz = b.size;
            }
            sum += sz;
            count++;
            DestroyImmediate(temp);
        }
        averageSize = (count > 0) ? (sum / count) : Vector3.one;
        dirty = false;
        return averageSize;
    }

    public Vector3 GetMaxPrefabSize()
    {
        if (!cachedMaxDirty && cachedMaxPrefabSize != Vector3.zero) return cachedMaxPrefabSize;

        Vector3 maxSize = Vector3.zero;
        if (artPrefabs == null || artPrefabs.Count == 0) return Vector3.one;

        foreach (var prefab in artPrefabs)
        {
            if (prefab == null) continue;
            var temp = Instantiate(prefab);
            temp.hideFlags = HideFlags.HideAndDontSave;
            var rends = temp.GetComponentsInChildren<Renderer>();
            if (rends != null && rends.Length > 0)
            {
                Bounds b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                Vector3 size = b.size;
                if (size.x > maxSize.x) maxSize.x = size.x;
                if (size.y > maxSize.y) maxSize.y = size.y;
                if (size.z > maxSize.z) maxSize.z = size.z;
            }
            else
            {
                var col = temp.GetComponentInChildren<Collider>();
                if (col != null)
                {
                    var b = col.bounds;
                    if (b.size.x > maxSize.x) maxSize.x = b.size.x;
                    if (b.size.y > maxSize.y) maxSize.y = b.size.y;
                    if (b.size.z > maxSize.z) maxSize.z = b.size.z;
                }
            }
            DestroyImmediate(temp);
        }

        cachedMaxPrefabSize = (maxSize == Vector3.zero) ? Vector3.one : maxSize;
        cachedMaxDirty = false;
        return cachedMaxPrefabSize;
    }

    public SubPool FindBestSubpool(FeatureVector5 feature, float[] featureScales, float threshold)
    {
        if (subpools == null || subpools.Count == 0) return null;
        float best = float.MaxValue;
        SubPool bestPool = null;
        for (int i = 0; i < subpools.Count; i++)
        {
            var sp = subpools[i];
            float dist = feature.DistanceL2(sp.center5, featureScales);
            if (dist < best) { best = dist; bestPool = sp; }
        }
        if (best <= threshold) return bestPool;
        return null;
    }

    public string GetSubpoolForPrefab(GameObject prefab)
    {
        foreach (var sub in subpools)
        {
            if (sub.members != null && sub.members.Contains(prefab))
                return sub.getSubName();
        }
        return null;
    }

    private static string GetObjectIdFromPrefab(GameObject prefab)
    {
        if (prefab == null) return null;
        string baseName = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(prefab));
        string metaPath = $"Assets/Metadata/{baseName}Meta.asset";
        var mm = AssetDatabase.LoadAssetAtPath<ModelMetadata>(metaPath);
        if (mm != null && !string.IsNullOrEmpty(mm.objectId))
            return mm.objectId;
        return null;
    }

    public SubPool AddPrefabToSubpool(GameObject prefab, Func<GameObject, FeatureVector5> featureGetter, float[] featureScales, float threshold)
    {
        if (prefab == null) return null;
        if (!artPrefabs.Contains(prefab)) artPrefabs.Add(prefab);
        FeatureVector5 f = featureGetter(prefab);
        SubPool found = FindBestSubpool(f, featureScales, threshold);
        string oid = GetObjectIdFromPrefab(prefab) ?? $"{(string.IsNullOrEmpty(categoryName) ? "Uncategorized" : categoryName)}/{Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(prefab))}";
        if (found != null)
        {
            if (!found.memberObjectIds.Contains(oid)) found.memberObjectIds.Add(oid);
            if (found.members == null) found.members = new List<GameObject>();
            if (!found.members.Contains(prefab)) found.members.Add(prefab);
            // recalcular center usando el resolver de esta instancia
            found.RecalculateCenter(o => this.TryComputeFeatureVectorFromObjectId(o));
            dirty = true;
            return found;
        }
        SubPool sp = new SubPool();
        sp.subpoolId = Guid.NewGuid().ToString("N").Substring(0, 8);
        sp.displayName = $"{categoryName}_sub_{subpools.Count + 1}";
        sp.memberObjectIds = new List<string>() { oid };
        sp.center5 = f;
        sp.members = new List<GameObject>() { prefab };
        subpools.Add(sp);
        dirty = true;
        cachedMaxDirty = true;
        return sp;
    }

    public void RecalculateAllSubpools(Func<string, FeatureVector5> featureGetter)
    {
        foreach (var sp in subpools)
        {
            sp.RecalculateCenter(featureGetter);
        }
        dirty = false;
    }

    private Vector3 MeasurePrefabSize(GameObject prefab)
    {
        if (prefab == null) return Vector3.one;
        var temp = GameObject.Instantiate(prefab);
        temp.hideFlags = HideFlags.HideAndDontSave;
        temp.transform.position = Vector3.zero;
        temp.transform.rotation = Quaternion.identity;
        temp.transform.localScale = Vector3.one;

        Vector3 size = Vector3.one;
        var col = temp.GetComponentInChildren<BoxCollider>();
        if (col != null)
        {
            size = Vector3.Scale(col.size, temp.transform.lossyScale);
        }
        else
        {
            var rends = temp.GetComponentsInChildren<Renderer>();
            if (rends != null && rends.Length > 0)
            {
                Bounds b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                size = b.size;
            }
            else
            {
                size = Vector3.one;
            }
        }

        GameObject.DestroyImmediate(temp);
        return size;
    }

    public void MarkDirtyMaxSize() { cachedMaxDirty = true; cachedMaxPrefabSize = Vector3.zero; }
    public void MarkDirty() => dirty = true;

    public GameObject GetRandomPrefabFromChosenSubpool()
    {
        if (_cachedChosenSubpool == null)
        {
            if (!string.IsNullOrEmpty(_lastChosenSubpoolId))
            {
                _cachedChosenSubpool = subpools?.Find(x => x.subpoolId == _lastChosenSubpoolId);
            }
            if (_cachedChosenSubpool == null)
            {
                _cachedChosenSubpool = ChooseRandomSubpool();
                if (_cachedChosenSubpool == null) return null;
            }
        }


        if (_cachedChosenSubpool.members == null || _cachedChosenSubpool.members.Count == 0)
        {
            if (_cachedChosenSubpool.memberObjectIds != null && _cachedChosenSubpool.memberObjectIds.Count > 0)
            {
                _cachedChosenSubpool.members = new List<GameObject>();
                foreach (var oid in _cachedChosenSubpool.memberObjectIds)
                {
                    var go = ResolvePrefabForObjectId(oid, Application.isEditor);
                    if (go != null) _cachedChosenSubpool.members.Add(go);
                }
            }


            if ((_cachedChosenSubpool.members == null || _cachedChosenSubpool.members.Count == 0) && artPrefabs != null)
            {
                _cachedChosenSubpool.members = new List<GameObject>(artPrefabs);
            }
        }


        if (_cachedChosenSubpool.members == null || _cachedChosenSubpool.members.Count == 0)
        {
            Debug.LogWarning("GetRandomPrefabFromChosenSubpool: no pude obtener prefabs para la subpool elegida");
            return null;
        }


        var list = _cachedChosenSubpool.members;
        return list[UnityEngine.Random.Range(0, list.Count)];
    }

    // <summary>
    /// Busca MaterialPoolSO en Assets/PoolMateriales/{categoryName} y asigna floor/wall/ceiling/tiles
    /// basada en heurísticas de nombre y (si existe) campos string dentro del MaterialPoolSO.
    /// Llamar desde editor (no en runtime).
    /// </summary>
    public void AutoAssignEnvironmentOverridesFromFolder(string baseFolder = "Assets/PoolMateriales")
    {
        if (string.IsNullOrEmpty(categoryName))
        {
            Debug.LogWarning($"ArtPoolSO.AutoAssign: pool without categoryName cannot auto-assign.");
            return;
        }

        string folder = $"{baseFolder}/{categoryName}";
        if (!AssetDatabase.IsValidFolder(folder))
        {
            Debug.LogWarning($"ArtPoolSO.AutoAssign: folder not found: {folder}");
            return;
        }

        // find all MaterialPoolSO assets in folder
        string[] guids = AssetDatabase.FindAssets("t:MaterialPoolSO", new[] { folder });
        if (guids == null || guids.Length == 0)
        {
            Debug.LogWarning($"ArtPoolSO.AutoAssign: no MaterialPoolSO found in {folder}");
            return;
        }

        // candidate lists
        List<MaterialPoolSO> candidates = new List<MaterialPoolSO>();
        foreach (var g in guids)
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            var m = AssetDatabase.LoadAssetAtPath<MaterialPoolSO>(p);
            if (m != null) candidates.Add(m);
        }

        // helper to score candidate for a role
        Func<MaterialPoolSO, string, int> scoreByName = (m, role) =>
        {
            if (m == null || string.IsNullOrEmpty(role)) return 0;
            string nm = m.name.ToLowerInvariant();
            if (nm == role) return 100;
            if (nm.Contains(role)) return 50;
            return 0;
        };

        // try read potential descriptor fields inside the ScriptableObject (reflection)
        Func<MaterialPoolSO, string> extractDescriptor = (m) =>
        {
            if (m == null) return null;
            // try common field/property names
            var t = m.GetType();
            var names = new[] { "purpose", "poolType", "role", "category", "tag", "name" };
            foreach (var n in names)
            {
                var f = t.GetField(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (f != null && f.FieldType == typeof(string))
                {
                    var v = f.GetValue(m) as string;
                    if (!string.IsNullOrEmpty(v)) return v.ToLowerInvariant();
                }
                var prop = t.GetProperty(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (prop != null && prop.PropertyType == typeof(string))
                {
                    var v = prop.GetValue(m) as string;
                    if (!string.IsNullOrEmpty(v)) return v.ToLowerInvariant();
                }
            }
            return null;
        };

        // keywords mapping role -> keywords
        var roleKeywords = new Dictionary<string, string[]>() {
            { "floor", new[]{ "piso","floor","flooring" } },
            { "wall", new[]{ "pared","wall","walls" } },
            { "ceiling", new[]{ "techo","ceiling","ceilos" } },
            { "tile", new[]{ "tile","tiles","baldosa","tileset" } }
        };

        // scoring per candidate
        MaterialPoolSO bestFloor = null, bestWall = null, bestCeil = null, bestTile = null;
        int bestFloorScore = 0, bestWallScore = 0, bestCeilScore = 0, bestTileScore = 0;

        foreach (var cand in candidates)
        {
            string desc = extractDescriptor(cand); // puede ser null
            string nm = cand.name.ToLowerInvariant();

            // check each role
            foreach (var kv in roleKeywords)
            {
                string role = kv.Key;
                int score = 0;
                // name exact/contains
                foreach (var kw in kv.Value)
                {
                    if (nm == kw) score += 150;
                    else if (nm.Contains(kw)) score += 80;
                }
                // descriptor match
                if (!string.IsNullOrEmpty(desc))
                {
                    foreach (var kw in kv.Value)
                    {
                        if (desc == kw) score += 120;
                        else if (desc.Contains(kw)) score += 60;
                    }
                }
                // small bonus if asset path contains role
                string path = AssetDatabase.GetAssetPath(cand).ToLowerInvariant();
                if (path.Contains("/" + role + "/") || path.Contains("/" + role + "_")) score += 10;

                // assign to best
                if (role == "floor" && score > bestFloorScore) { bestFloorScore = score; bestFloor = cand; }
                if (role == "wall" && score > bestWallScore) { bestWallScore = score; bestWall = cand; }
                if (role == "ceiling" && score > bestCeilScore) { bestCeilScore = score; bestCeil = cand; }
                if (role == "tile" && score > bestTileScore) { bestTileScore = score; bestTile = cand; }
            }
        }

        // apply results (only overwrite if found)
        bool changed = false;
        if (bestFloor != null && floorMatOverride != bestFloor) { floorMatOverride = bestFloor; changed = true; }
        if (bestWall != null && wallMatOverride != bestWall) { wallMatOverride = bestWall; changed = true; }
        if (bestCeil != null && ceilingMatOverride != bestCeil) { ceilingMatOverride = bestCeil; changed = true; }
        if (bestTile != null && tileMatOverride != bestTile) { tileMatOverride = bestTile; changed = true; }

        if (changed)
        {
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
            Debug.Log($"ArtPoolSO.AutoAssign: Assigned env overrides for pool '{categoryName}' from {folder}.");
        }
        else
        {
            Debug.Log($"ArtPoolSO.AutoAssign: No clear matches found in {folder} for pool '{categoryName}'.");
        }
    }

    /// <summary>
    /// Resolve a prefab GameObject for a given objectId. In editorMode=true it uses AssetDatabase lookups; in runtime
    /// it tries Resources.Load with different candidate paths (you must have prefabs in Resources for runtime auto-resolve).
    /// </summary>
    public GameObject ResolvePrefabForObjectId(string objectId, bool editorMode)
    {
        if (string.IsNullOrEmpty(objectId)) return null;


        string prefabName = objectId.Split('/').Length > 0 ? objectId.Split('/')[^1] : objectId;
        string sanitized = SanitizeName(prefabName);


#if UNITY_EDITOR
        if (editorMode)
        {
            string[] tryPaths = new string[] {
$"Assets/Prefabs/{sanitized}.prefab",
$"Assets/Prefabs/{prefabName}.prefab",
$"Assets/Prefabs/{sanitized}/{sanitized}.prefab"
};
            foreach (var p in tryPaths)
            {
                var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(p);
                if (go != null) return go;
            }


            string metaPath = $"Assets/Metadata/{prefabName}Meta.asset";
            var mm = UnityEditor.AssetDatabase.LoadAssetAtPath<ModelMetadata>(metaPath);
            if (mm != null)
            {
                var guids = UnityEditor.AssetDatabase.FindAssets($"{prefabName} t:Prefab");
                foreach (var g in guids)
                {
                    var path = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
                    var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (go != null) return go;
                }
            }


            var all = UnityEditor.AssetDatabase.FindAssets($"t:Prefab");
            foreach (var g in all)
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
                var nameOnly = Path.GetFileNameWithoutExtension(path);
                if (string.Equals(nameOnly, prefabName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(SanitizeName(nameOnly), SanitizeName(prefabName), StringComparison.OrdinalIgnoreCase))
                {
                    var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (go != null) return go;
                }
            }


            return null;
        }
#endif


        string[] runtimeCandidates = new string[] {
sanitized,
$"Prefabs/{sanitized}",
$"Prefabs/{prefabName}",
$"{prefabName}"
};
        foreach (var rc in runtimeCandidates)
        {
            var go = Resources.Load<GameObject>(rc);
            if (go != null) return go;
        }


        if (artPrefabs != null)
        {
            foreach (var g in artPrefabs)
            {
                if (g == null) continue;
                var nameOnly = Path.GetFileNameWithoutExtension(g.name);
                if (string.Equals(nameOnly, prefabName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(SanitizeName(nameOnly), SanitizeName(prefabName), StringComparison.OrdinalIgnoreCase))
                {
                    return g;
                }
            }
        }
        return null;
    }
    // --- Subpool selection & prefab fetching API (public runtime-friendly methods) ---


    public SubPool ChooseRandomSubpool(bool forceReloadFromJson = false)
    {
        if (subpools == null || subpools.Count == 0 || forceReloadFromJson)
        {
            PopulateMembersFromSnapshot(editorMode: Application.isEditor);
        }


        if (subpools == null || subpools.Count == 0)
        {
            Debug.LogWarning("ChooseRandomSubpool: no hay subpools disponibles");
            return null;
        }


        var candidates = new List<SubPool>();
        foreach (var sp in subpools)
        {
            if (sp == null) continue;
            bool has = (sp.members != null && sp.members.Count > 0) || (sp.memberObjectIds != null && sp.memberObjectIds.Count > 0);
            if (has) candidates.Add(sp);
        }


        if (candidates.Count == 0)
        {
            Debug.LogWarning("ChooseRandomSubpool: ninguna subpool tiene miembros");
            return null;
        }


        var chosen = candidates[UnityEngine.Random.Range(0, candidates.Count)];
        _lastChosenSubpoolId = chosen.subpoolId;
        _cachedChosenSubpool = chosen;
        return chosen;
    }

    // ----------------- Generar las pools desde json -----------------

    // Añadir en ArtPoolSO (campo para inspeccionar el último reporte)
    [NonSerialized] public string lastPopulateReport = string.Empty;

    // Reemplaza PopulateMembersFromSnapshot con esta versión (mejor debug)
    public void PopulateMembersFromSnapshot(bool editorMode = false)
    {
        lastPopulateReport = string.Empty;
        System.Text.StringBuilder report = new System.Text.StringBuilder();
        int totalMemberIds = 0;
        int resolvedCount = 0;
        List<string> unresolved = new List<string>();

        report.AppendLine($"== ArtPoolSO.PopulateMembersFromSnapshot (pool='{name}') ==");
        report.AppendLine($"editorMode={editorMode}, snapshotJsonAsset assigned={(snapshotJsonAsset != null)}");
        string json = null;

        if (snapshotJsonAsset != null)
        {
            report.AppendLine("Usando snapshotJsonAsset (TextAsset) asignado en inspector.");
            json = snapshotJsonAsset.text;
        }

#if UNITY_EDITOR
        // Intentar leer snapshotJsonPath en Editor si no hay TextAsset
        if (string.IsNullOrEmpty(json) && !string.IsNullOrEmpty(snapshotJsonPath) && editorMode)
        {
            report.AppendLine($"Intentando leer snapshotJsonPath (editor): '{snapshotJsonPath}'");
            try
            {
                string full = Path.GetFullPath(snapshotJsonPath);
                report.AppendLine($" -> intentando Path.GetFullPath -> '{full}' (exists={File.Exists(full)})");
                if (File.Exists(full)) json = File.ReadAllText(full);
                else
                {
                    report.AppendLine($" -> Full path no existe, intentando ruta tal cual.");
                    report.AppendLine($" -> exists={File.Exists(snapshotJsonPath)}");
                    if (File.Exists(snapshotJsonPath)) json = File.ReadAllText(snapshotJsonPath);
                }
            }
            catch (Exception ex)
            {
                report.AppendLine($" -> lectura snapshotJsonPath lanzó excepción: {ex.Message}");
            }
        }
#endif

        // Si aún vacio, intentar default path (editor) o Resources (runtime)
        if (string.IsNullOrEmpty(json))
        {
#if UNITY_EDITOR
            if (editorMode)
            {
                string defaultPath = string.IsNullOrEmpty(snapshotJsonPath) ? GetDefaultSnapshotPath() : snapshotJsonPath;
                report.AppendLine($"Intentando defaultPath en editor: '{defaultPath}'");
                try
                {
                    string full = Path.GetFullPath(defaultPath);
                    report.AppendLine($" -> full: '{full}' exists={File.Exists(full)}");
                    if (File.Exists(full)) json = File.ReadAllText(full);
                    else if (File.Exists(defaultPath)) json = File.ReadAllText(defaultPath);
                }
                catch (Exception ex)
                {
                    report.AppendLine($" -> lectura defaultPath excepción: {ex.Message}");
                }
            }
#endif
        }

        // Runtime: intentar Resources si se indicó snapshotJsonPath que contiene 'Resources/'
        if (string.IsNullOrEmpty(json))
        {
            if (snapshotJsonAsset == null && !string.IsNullOrEmpty(snapshotJsonPath))
            {
                int idx = snapshotJsonPath.IndexOf("Resources/", StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    string resourcesRelative = snapshotJsonPath.Substring(idx + "Resources/".Length);
                    resourcesRelative = resourcesRelative.Replace(".json", "");
                    report.AppendLine($"Intentando Resources.Load con ruta relativa: '{resourcesRelative}'");
                    var ta = Resources.Load<TextAsset>(resourcesRelative);
                    if (ta != null) json = ta.text;
                    else report.AppendLine(" -> Resources.Load devolvió null.");
                }
                else
                {
                    report.AppendLine("snapshotJsonPath no contiene 'Resources/', no intentar Resources.");
                }
            }
        }

        if (string.IsNullOrEmpty(json))
        {
            report.AppendLine("NO se pudo cargar JSON (ni TextAsset, ni snapshotJsonPath, ni default/Resources).");
            Debug.LogWarning(report.ToString());
            lastPopulateReport = report.ToString();
            return;
        }

        // Log excerpt (limit)
        report.AppendLine("JSON cargado correctamente. Extracto:");
        report.AppendLine(json.Length > 1024 ? json.Substring(0, 1024) + "...[truncated]" : json);

        PoolJson pj = null;
        try { pj = JsonUtility.FromJson<PoolJson>(json); }
        catch (Exception ex)
        {
            report.AppendLine($"Error parseando JSON: {ex.Message}");
            Debug.LogError(report.ToString());
            lastPopulateReport = report.ToString();
            return;
        }

        if (pj == null || pj.subpools == null || pj.subpools.Count == 0)
        {
            report.AppendLine("JSON parseado correctamente, pero no hay subpools en el JSON.");
            Debug.LogWarning(report.ToString());
            lastPopulateReport = report.ToString();
            return;
        }

        report.AppendLine($"JSON contiene {pj.subpools.Count} subpools.");
        if (subpools == null) subpools = new List<SubPool>();

        // construir diccionario byId
        var byId = new Dictionary<string, SubPool>();
        foreach (var sp in subpools)
            if (sp != null && !string.IsNullOrEmpty(sp.subpoolId)) byId[sp.subpoolId] = sp;

        // iterar subpools en JSON
        foreach (var spj in pj.subpools)
        {
            report.AppendLine($"-- Subpool JSON: id='{spj.subpoolId}', name='{spj.displayName}', memberObjectIds={(spj.memberObjectIds?.Count ?? 0)}");
            SubPool target = null;
            if (!string.IsNullOrEmpty(spj.subpoolId) && byId.TryGetValue(spj.subpoolId, out target))
            {
                report.AppendLine($"   Reutilizando SubPool existente con id '{spj.subpoolId}'");
            }
            else
            {
                target = subpools.Find(x => x.displayName == spj.displayName);
                if (target != null) report.AppendLine($"   Reutilizando SubPool existente por displayName '{spj.displayName}'");
            }

            if (target == null)
            {
                target = new SubPool();
                target.subpoolId = string.IsNullOrEmpty(spj.subpoolId) ? Guid.NewGuid().ToString("N").Substring(0, 8) : spj.subpoolId;
                target.displayName = string.IsNullOrEmpty(spj.displayName) ? target.subpoolId : spj.displayName;
                subpools.Add(target);
                report.AppendLine($"   Creada nueva SubPool id='{target.subpoolId}', name='{target.displayName}'");
            }

            // asignar memberObjectIds y center
            target.memberObjectIds = spj.memberObjectIds != null ? new List<string>(spj.memberObjectIds) : new List<string>();
            target.center5 = spj.center5;

            // poblar members runtime
            target.members = new List<GameObject>();
            if (target.memberObjectIds != null)
            {
                foreach (var oid in target.memberObjectIds)
                {
                    totalMemberIds++;
                    var res = ResolvePrefabForObjectIdWithDebug(oid, editorMode);
                    if (res.go != null)
                    {
                        target.members.Add(res.go);
                        resolvedCount++;
                        report.AppendLine($"   Resuelto: '{oid}' -> prefab '{res.go.name}' via: {res.debug}");
                    }
                    else
                    {
                        unresolved.Add(oid);
                        report.AppendLine($"   NO resuelto: '{oid}' ; intentos: {res.debug}");
                    }
                }
            }
        }

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            UnityEditor.EditorUtility.SetDirty(this);
            UnityEditor.AssetDatabase.SaveAssets();
            UnityEditor.AssetDatabase.Refresh();
        }
#endif

        report.AppendLine($"== Resumen: subpools={subpools.Count}, totalMemberIds={totalMemberIds}, resolved={resolvedCount}, unresolved={unresolved.Count} ==");
        if (unresolved.Count > 0)
        {
            report.AppendLine("Unresolved objectIds (lista):");
            foreach (var u in unresolved) report.AppendLine(" - " + u);
        }

        string final = report.ToString();
        Debug.Log(final);
        lastPopulateReport = final;
    }

    // Helper: devuelve tanto GameObject como string con detalle de pasos intentados
    private (GameObject go, string debug) ResolvePrefabForObjectIdWithDebug(string objectId, bool editorMode)
    {
        System.Text.StringBuilder debug = new System.Text.StringBuilder();
        if (string.IsNullOrEmpty(objectId)) { debug.Append("objectId vacío"); return (null, debug.ToString()); }

        string prefabName = objectId.Split('/').Length > 0 ? objectId.Split('/')[^1] : objectId;
        string sanitized = SanitizeName(prefabName);
        debug.AppendLine($"Resolver '{objectId}' -> basename='{prefabName}', sanitized='{sanitized}'");

#if UNITY_EDITOR
        if (editorMode)
        {
            debug.AppendLine("Editor mode: probando rutas comunes en Assets/Prefabs...");
            string[] tryPaths = new string[] {
            $"Assets/Prefabs/{sanitized}.prefab",
            $"Assets/Prefabs/{prefabName}.prefab",
            $"Assets/Prefabs/{sanitized}/{sanitized}.prefab"
        };
            foreach (var p in tryPaths)
            {
                debug.AppendLine($" - intentando AssetDatabase.LoadAssetAtPath('{p}') (exists={File.Exists(Path.GetFullPath(p))})");
                var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(p);
                if (go != null) { debug.AppendLine($"   -> cargado desde '{p}'"); return (go, debug.ToString()); }
            }

            debug.AppendLine("Probando ModelMetadata lookup (Assets/Metadata)...");
            string metaPath = $"Assets/Metadata/{prefabName}Meta.asset";
            var mm = UnityEditor.AssetDatabase.LoadAssetAtPath<ModelMetadata>(metaPath);
            debug.AppendLine($" - metadataPath='{metaPath}', mm={(mm != null ? "found" : "null")}");
            if (mm != null && !string.IsNullOrEmpty(mm.objectId))
            {
                debug.AppendLine($" - ModelMetadata.objectId='{mm.objectId}'");
                var guids = UnityEditor.AssetDatabase.FindAssets($"{prefabName} t:Prefab");
                debug.AppendLine($" - FindAssets returned {guids.Length} prefabs for name '{prefabName}'");
                foreach (var g in guids)
                {
                    var path = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
                    var go2 = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (go2 != null) { debug.AppendLine($"   -> cargado '{path}' (por metadata match)"); return (go2, debug.ToString()); }
                }
            }

            debug.AppendLine("Búsqueda amplia por nombre (AssetDatabase.FindAssets)...");
            string[] found = UnityEditor.AssetDatabase.FindAssets($"{prefabName} t:Prefab");
            debug.AppendLine($" - encontrados {found.Length} candidatos");
            List<string> candidates = new List<string>();
            foreach (var g in found)
            {
                var p = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
                var nameOnly = Path.GetFileNameWithoutExtension(p);
                if (string.Equals(nameOnly, prefabName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(SanitizeName(nameOnly), SanitizeName(prefabName), StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(p);
                }
            }
            debug.AppendLine($" - candidatos filtrados exact-match: {candidates.Count}");
            if (candidates.Count > 0)
            {
                debug.AppendLine(" - seleccionando primer candidato:");
                debug.AppendLine("   " + candidates[0]);
                var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(candidates[0]);
                if (go != null) return (go, debug.ToString());
            }

            debug.AppendLine("No encontrado en editor.");
            return (null, debug.ToString());
        }
#endif

        // Runtime attempts
        debug.AppendLine("Runtime mode: probando Resources y artPrefabs...");

        string[] runtimeCandidates = new string[] {
        sanitized,
        $"Prefabs/{sanitized}",
        $"Prefabs/{prefabName}",
        $"{prefabName}"
    };
        foreach (var rc in runtimeCandidates)
        {
            debug.AppendLine($" - Resources.Load('{rc}')");
            var go = Resources.Load<GameObject>(rc);
            if (go != null) { debug.AppendLine($"   -> cargado desde Resources('{rc}')"); return (go, debug.ToString()); }
        }

        debug.AppendLine("Chequeando artPrefabs (inspector references) por nombre...");
        if (artPrefabs != null)
        {
            foreach (var g in artPrefabs)
            {
                if (g == null) continue;
                var nameOnly = Path.GetFileNameWithoutExtension(g.name);
                if (string.Equals(nameOnly, prefabName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(SanitizeName(nameOnly), SanitizeName(prefabName), StringComparison.OrdinalIgnoreCase))
                {
                    debug.AppendLine($" -> encontrado en artPrefabs como '{g.name}'");
                    return (g, debug.ToString());
                }
            }
        }

        debug.AppendLine("No encontrado en runtime.");
        return (null, debug.ToString());
    }

    // ----------------- Métodos añadidos / fixes -----------------

    // Intenta obtener FeatureVector5 a partir de un objectId.
    // Usa ModelMetadata (si existe) o mide el prefab (Assets/Prefabs) como fallback.
    private FeatureVector5 TryComputeFeatureVectorFromObjectId(string objectId)
    {
        if (string.IsNullOrEmpty(objectId)) return FeatureVector5.zero;
        try
        {
            string prefabName = objectId.Split('/').Last();
            // 1) intentar metadata
            string metaPath = $"Assets/Metadata/{prefabName}Meta.asset";
            var mm = AssetDatabase.LoadAssetAtPath<ModelMetadata>(metaPath);
            if (mm != null && mm.size != Vector3.zero)
            {
                float height = mm.size.y;
                float footprint = Mathf.Max(mm.size.x, mm.size.z);
                float depth = Mathf.Min(mm.size.x, mm.size.z);
                float aspect = (footprint > 0f) ? height / footprint : height;
                float rawVol = Mathf.Max(0.0001f, mm.size.x * mm.size.y * mm.size.z);
                float volLog = Mathf.Log(rawVol + 1f);
                return new FeatureVector5(height, footprint, depth, aspect, volLog);
            }

            // 2) fallback: intentar cargar prefab desde Assets/Prefabs
            string prefabPath = $"Assets/Prefabs/{SanitizeName(prefabName)}.prefab";
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (pf != null)
            {
                Vector3 size = MeasurePrefabSize(pf);
                float height = size.y;
                float footprint = Mathf.Max(size.x, size.z);
                float depth = Mathf.Min(size.x, size.z);
                float aspect = (footprint > 0f) ? height / footprint : height;
                float rawVol = Mathf.Max(0.0001f, size.x * size.y * size.z);
                float volLog = Mathf.Log(rawVol + 1f);
                return new FeatureVector5(height, footprint, depth, aspect, volLog);
            }

            // 3) búsqueda más amplia por nombre (AssetDatabase.FindAssets)
            string search = prefabName;
            var guids = AssetDatabase.FindAssets($"{search} t:Prefab");
            foreach (var g in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                if (string.IsNullOrEmpty(path)) continue;
                var nameOnly = Path.GetFileNameWithoutExtension(path);
                if (string.Equals(nameOnly, prefabName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(SanitizeName(nameOnly), SanitizeName(prefabName), StringComparison.OrdinalIgnoreCase))
                {
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (go != null)
                    {
                        Vector3 s = MeasurePrefabSize(go);
                        float height = s.y;
                        float footprint = Mathf.Max(s.x, s.z);
                        float depth = Mathf.Min(s.x, s.z);
                        float aspect = (footprint > 0f) ? height / footprint : height;
                        float rawVol = Mathf.Max(0.0001f, s.x * s.y * s.z);
                        float volLog = Mathf.Log(rawVol + 1f);
                        return new FeatureVector5(height, footprint, depth, aspect, volLog);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"TryComputeFeatureVectorFromObjectId error for '{objectId}': {ex.Message}");
        }

        return FeatureVector5.zero;
    }

    // Helper interno para sanear nombres de archivo
    private string SanitizeName(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }
} // end ArtPoolSO

//Vector para subpooling
[Serializable]
public struct FeatureVector5
{
    public float a; // height
    public float b; // footprint (max x,z)
    public float c; // depth (min x,z)
    public float d; // aspect = height / footprint
    public float e; // volume = x*y*z (can be large)

    public FeatureVector5(float a, float b, float c, float d, float e)
    {
        this.a = a; this.b = b; this.c = c; this.d = d; this.e = e;
    }

    public static FeatureVector5 zero => new FeatureVector5(0, 0, 0, 0, 0);

    public float DistanceL2(FeatureVector5 other, float[] scales)
    {
        // Defensive: if scales is null/short, treat missing as 1
        float s0 = (scales != null && scales.Length > 0) ? scales[0] : 1f;
        float s1 = (scales != null && scales.Length > 1) ? scales[1] : 1f;
        float s2 = (scales != null && scales.Length > 2) ? scales[2] : 1f;
        float s3 = (scales != null && scales.Length > 3) ? scales[3] : 1f;
        float s4 = (scales != null && scales.Length > 4) ? scales[4] : 1f;

        float dx = (this.a - other.a) / s0;
        float dy = (this.b - other.b) / s1;
        float dz = (this.c - other.c) / s2;
        float dw = (this.d - other.d) / s3;
        float dv = (this.e - other.e) / s4;
        return Mathf.Sqrt(dx * dx + dy * dy + dz * dz + dw * dw + dv * dv);
    }

    public static FeatureVector5 operator +(FeatureVector5 x, FeatureVector5 y)
        => new FeatureVector5(x.a + y.a, x.b + y.b, x.c + y.c, x.d + y.d, x.e + y.e);

    public static FeatureVector5 operator /(FeatureVector5 x, float s)
        => new FeatureVector5(x.a / s, x.b / s, x.c / s, x.d / s, x.e / s);
}

// SubPool definition
[Serializable]
public class SubPool
{
    public string subpoolId;
    public string displayName;
    public List<string> memberObjectIds = new List<string>(); // persistente
    public FeatureVector5 center5 = FeatureVector5.zero;

    [NonSerialized] public List<GameObject> members = new List<GameObject>(); // runtime, no persistir

    public string getSubName() => displayName;

    public void AddMemberByObjectId(string objectId)
    {
        if (string.IsNullOrEmpty(objectId)) return;
        if (memberObjectIds == null) memberObjectIds = new List<string>();
        if (!memberObjectIds.Contains(objectId)) memberObjectIds.Add(objectId);
    }

    public void RemoveMemberByObjectId(string objectId)
    {
        if (memberObjectIds == null) return;
        memberObjectIds.Remove(objectId);
        if (members != null && members.Count > 0)
            members.RemoveAll(m => m == null || m.name == objectId.Split('/').Last());
    }

    public void SyncMembersFromObjectIds(Func<string, GameObject> resolver)
    {
        members = new List<GameObject>();
        if (memberObjectIds == null) return;
        foreach (var oid in memberObjectIds)
        {
            var go = resolver?.Invoke(oid);
            if (go != null) members.Add(go);
        }
    }

    public void RecalculateCenter(Func<string, FeatureVector5> featureResolver)
    {
        if (memberObjectIds == null || memberObjectIds.Count == 0)
        {
            center5 = FeatureVector5.zero;
            return;
        }

        FeatureVector5 sum = FeatureVector5.zero;
        int cnt = 0;
        foreach (var oid in memberObjectIds)
        {
            FeatureVector5 fv = featureResolver != null ? featureResolver(oid) : FeatureVector5.zero;
            if (fv.Equals(FeatureVector5.zero)) continue;
            sum = sum + fv;
            cnt++;
        }
        center5 = (cnt > 0) ? (sum / (float)cnt) : FeatureVector5.zero;
    }
}
