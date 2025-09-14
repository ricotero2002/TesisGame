// Assets/Editor/SubpoolAssignDebugger.cs
using UnityEditor;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

public class SubpoolAssignDebugger : EditorWindow
{
    // Inputs
    GameObject prefabAsset;
    ArtPoolSO poolAsset;
    int subpoolIndex = 0;
    ImportSettings settings;

    [MenuItem("Tools/Pools/Subpool Assign Debugger")]
    public static void ShowWindow() => GetWindow<SubpoolAssignDebugger>("Subpool Assign Debugger");

    void OnGUI()
    {
        GUILayout.Label("Debug distancia prefab -> centro de subpool", EditorStyles.boldLabel);
        prefabAsset = EditorGUILayout.ObjectField("Prefab (asset)", prefabAsset, typeof(GameObject), false) as GameObject;
        poolAsset = EditorGUILayout.ObjectField("Pool (ArtPoolWithSubpoolsSO)", poolAsset, typeof(ArtPoolSO), false) as ArtPoolSO;
        settings = EditorGUILayout.ObjectField("ImportSettings (optional)", settings, typeof(ImportSettings), false) as ImportSettings;
        subpoolIndex = EditorGUILayout.IntField("Subpool index", subpoolIndex);

        if (GUILayout.Button("Calcular distancia y debug detallado"))
        {
            RunDebug();
        }

        GUILayout.Space(10);
        EditorGUILayout.HelpBox("Este debugger calcula:\n1) FeatureVector5 del prefab (AABB combinado)\n2) Centro del subpool (recalculado desde miembros si existen)\n3) Diferencias por componente (raw, scaled), suma de cuadrados y distancia final\n4) Comparación con subpoolAssignThreshold\n\nUsa ImportSettings para scales y threshold.", MessageType.Info);
    }

    void RunDebug()
    {
        if (prefabAsset == null) { Debug.LogError("Seleccioná un prefab asset."); return; }
        if (poolAsset == null) { Debug.LogError("Seleccioná un ArtPoolWithSubpoolsSO."); return; }
        if (settings == null)
        {
            // try to auto-find an ImportSettings in project
            var guids = AssetDatabase.FindAssets("t:ImportSettings");
            if (guids != null && guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                settings = AssetDatabase.LoadAssetAtPath<ImportSettings>(path);
                Debug.Log($"ImportSettings auto-cargado: {path}");
            }
            else
            {
                Debug.LogWarning("No ImportSettings asignado ni encontrado. Usaré valores por defecto.");
            }
        }

        float[] scales = (settings != null && settings.featureScales != null && settings.featureScales.Length >= 5)
            ? settings.featureScales
            : new float[] { 1f, 1f, 1f, 0.8f, 0.5f };
        float thr = (settings != null) ? settings.subpoolAssignThreshold : 0.7f;

        // 1) compute prefab features
        var fPrefab = ComputeFeatureFromPrefabAsset(prefabAsset);
        if (fPrefab == null)
        {
            Debug.LogError("No se pudo calcular features del prefab seleccionado.");
            return;
        }

        // 2) compute center for chosen subpool: if has members, recalc average; else use stored center if exists
        FeatureVector5 center;
        bool centerFromMembers = false;
        if (poolAsset.subpools == null || poolAsset.subpools.Count == 0)
        {
            Debug.LogError("Pool no tiene subpools.");
            return;
        }
        if (subpoolIndex < 0 || subpoolIndex >= poolAsset.subpools.Count)
        {
            Debug.LogError($"Index fuera de rango: {subpoolIndex}. Subpools count = {poolAsset.subpools.Count}");
            return;
        }

        var targetSub = poolAsset.subpools[subpoolIndex];
        if (targetSub.members != null && targetSub.members.Count > 0)
        {
            // recalc center from members for robust comparison
            List<FeatureVector5> memberFeatures = new List<FeatureVector5>();
            foreach (var gm in targetSub.members)
            {
                if (gm == null) continue;
                var feat = ComputeFeatureFromPrefabAsset(gm);
                if (feat != null) memberFeatures.Add(feat);
            }

            if (memberFeatures.Count > 0)
            {
                centerFromMembers = true;
                center = FeatureVector5.Average(memberFeatures);
            }
            else
            {
                Debug.LogWarning("No se pudieron calcular features de miembros; usando center almacenado si existe.");
                center = TryReadCenterFromSubpool(targetSub);
            }
        }
        else
        {
            // no members -> try stored center
            center = TryReadCenterFromSubpool(targetSub);
        }

        if (center == null)
        {
            Debug.LogError("No se pudo obtener centro de subpool.");
            return;
        }

        // 3) compute detailed distance
        Debug.Log("=== Subpool Assign Debug ===");
        Debug.Log($"Prefab: {prefabAsset.name}");
        Debug.Log($"Pool: {poolAsset.categoryName}, subpool: {targetSub.getSubName()} (index {subpoolIndex})");
        Debug.Log($"Using scales: [{string.Join(", ", scales.Select(x => x.ToString("F3")))}], threshold = {thr:F3}");
        Debug.Log($"Center computed from members: {centerFromMembers}");

        PrintFeatureVector("Prefab features", fPrefab);
        PrintFeatureVector("Subpool center", center);

        // differences and scaled squares
        float[] diffs = new float[5];
        float[] scaled = new float[5];
        float[] sq = new float[5];
        for (int i = 0; i < 5; i++)
        {
            diffs[i] = fPrefab[i] - center[i];
            float sc = (Math.Abs(scales[i]) > 1e-9f) ? (diffs[i] / scales[i]) : diffs[i];
            scaled[i] = sc;
            sq[i] = sc * sc;
        }
        Debug.Log($"Differences (prefab - center): A={diffs[0]:F4}, B={diffs[1]:F4}, C={diffs[2]:F4}, D={diffs[3]:F4}, E={diffs[4]:F4}");
        Debug.Log($"Scaled diffs (divide by scales): A={scaled[0]:F4}, B={scaled[1]:F4}, C={scaled[2]:F4}, D={scaled[3]:F4}, E={scaled[4]:F4}");
        Debug.Log($"Squared scaled: A={sq[0]:F6}, B={sq[1]:F6}, C={sq[2]:F6}, D={sq[3]:F6}, E={sq[4]:F6}");

        float sumSq = 0f;
        for (int i = 0; i < 5; i++) sumSq += sq[i];
        float dist = Mathf.Sqrt(sumSq);
        Debug.Log($"L2 distance (sqrt of sum squares) = {dist:F6}");

        bool assignable = dist <= thr;
        Debug.Log($"Decision: dist <= threshold ? {dist:F6} <= {thr:F6} => {assignable}");

        // also print per-dimension contribution percentage
        for (int i = 0; i < 5; i++)
        {
            float pct = (sumSq > 0f) ? (sq[i] / sumSq * 100f) : 0f;
            Debug.Log($"Contribution dimension {i} = {pct:F2}% (component sq {sq[i]:F6})");
        }

        // final hint
        if (!assignable)
        {
            Debug.LogWarning($"El prefab NO encaja en el subpool con threshold {thr:F3}. Considerá subir threshold o reducir influencia de dimensiones con alta contribución (ver arriba).");
        }
        else
        {
            Debug.Log($"El prefab ENCJA en el subpool (dist {dist:F3} <= thr {thr:F3}).");
        }
    }

    // Utility: compute feature vector from prefab asset using combined bounds (robust)
    FeatureVector5 ComputeFeatureFromPrefabAsset(GameObject prefab)
    {
        if (prefab == null) return null;
        string path = AssetDatabase.GetAssetPath(prefab);
        GameObject contents = null;
        bool usedLoad = false;
        try
        {
            if (!string.IsNullOrEmpty(path))
            {
                contents = PrefabUtility.LoadPrefabContents(path);
                usedLoad = true;
            }
            else
            {
                contents = GameObject.Instantiate(prefab);
                contents.hideFlags = HideFlags.HideAndDontSave;
            }

            var renderers = contents.GetComponentsInChildren<Renderer>(true);
            if (renderers != null && renderers.Length > 0)
            {
                Bounds combined = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) combined.Encapsulate(renderers[i].bounds);
                Vector3 size = combined.size;
                return new FeatureVector5(size.y, Mathf.Max(size.x, size.z), Mathf.Min(size.x, size.z), (Mathf.Max(size.x, size.z) > 0f) ? (size.y / Mathf.Max(size.x, size.z)) : size.y, Mathf.Log(Mathf.Max(0.0001f, size.x * size.y * size.z) + 1f));
            }

            // fallback to meshfilters -> world AABB via corners
            var mfs = contents.GetComponentsInChildren<MeshFilter>(true);
            bool any = false;
            Bounds total = new Bounds(Vector3.zero, Vector3.zero);
            foreach (var mf in mfs)
            {
                var mesh = mf.sharedMesh;
                if (mesh == null) continue;
                Vector3 ext = mesh.bounds.extents;
                Vector3 c = mesh.bounds.center;
                Vector3[] corners = new Vector3[8];
                int idx = 0;
                for (int x = -1; x <= 1; x += 2)
                    for (int y = -1; y <= 1; y += 2)
                        for (int z = -1; z <= 1; z += 2)
                            corners[idx++] = mf.transform.TransformPoint(c + Vector3.Scale(ext, new Vector3(x, y, z)));
                Bounds w = new Bounds(corners[0], Vector3.zero);
                for (int i = 1; i < corners.Length; i++) w.Encapsulate(corners[i]);
                if (!any) { total = w; any = true; } else total.Encapsulate(w);
            }
            if (any)
            {
                Vector3 size = total.size;
                return new FeatureVector5(size.y, Mathf.Max(size.x, size.z), Mathf.Min(size.x, size.z), (Mathf.Max(size.x, size.z) > 0f) ? (size.y / Mathf.Max(size.x, size.z)) : size.y, Mathf.Log(Mathf.Max(0.0001f, size.x * size.y * size.z) + 1f));
            }

            return null;
        }
        catch (Exception ex)
        {
            Debug.LogError("ComputeFeatureFromPrefabAsset error: " + ex);
            return null;
        }
        finally
        {
            if (usedLoad && contents != null) PrefabUtility.UnloadPrefabContents(contents);
            else if (contents != null) GameObject.DestroyImmediate(contents);
        }
    }

    // Try to read stored center from subpool (center5 or center4)
    FeatureVector5 TryReadCenterFromSubpool(SubPool sub)
    {
        // try reflection for center5, center4, center (various names)
        var t = sub.GetType();
        var fld5 = t.GetField("center5");
        if (fld5 != null)
        {
            var obj = fld5.GetValue(sub);
            if (obj is Vector4 v4)
            {
                // if center stored as Vector4, treat as height, footprint, depth, aspect and compute volume from elsewhere (set 0)
                return new FeatureVector5(v4.x, v4.y, v4.z, v4.w, 0f);
            }
            // if custom type, try to extract fields A..E
        }
        var fld4 = t.GetField("center4");
        if (fld4 != null)
        {
            var v4 = (Vector4)fld4.GetValue(sub);
            return new FeatureVector5(v4.x, v4.y, v4.z, v4.w, 0f);
        }
        // try property "center5"
        var prop = t.GetProperty("center5");
        if (prop != null)
        {
            var v = prop.GetValue(sub, null);
            if (v is Vector4 vv) return new FeatureVector5(vv.x, vv.y, vv.z, vv.w, 0f);
        }

        // fallback: try stored fields named "center" or "averageSize"
        var fcent = t.GetField("center");
        if (fcent != null)
        {
            var val = fcent.GetValue(sub);
            if (val is Vector4 vf) return new FeatureVector5(vf.x, vf.y, vf.z, vf.w, 0f);
        }

        // fallback: try to compute center by averaging members (already handled outside)
        return null;
    }

    void PrintFeatureVector(string label, FeatureVector5 fv)
    {
        Debug.Log($"{label}: height(A)={fv.a:F4}, footprint(B)={fv.b:F4}, depth(C)={fv.c:F4}, aspect(D)={fv.d:F4}, volLog(E)={fv.e:F4}");
    }

    // Local lightweight struct to hold 5 features and indexer
    class FeatureVector5
    {
        public float a, b, c, d, e;
        public FeatureVector5(float A, float B, float C, float D, float E) { a = A; b = B; c = C; d = D; e = E; }
        public float this[int i] { get { if (i == 0) return a; if (i == 1) return b; if (i == 2) return c; if (i == 3) return d; return e; } }
        public static FeatureVector5 Average(IEnumerable<FeatureVector5> list)
        {
            var arr = list.ToArray();
            if (arr.Length == 0) return new FeatureVector5(0, 0, 0, 0, 0);
            float sa = 0, sb = 0, sc = 0, sd = 0, se = 0;
            foreach (var v in arr) { sa += v.a; sb += v.b; sc += v.c; sd += v.d; se += v.e; }
            float n = arr.Length;
            return new FeatureVector5(sa / n, sb / n, sc / n, sd / n, se / n);
        }
    }

    // We need to reference SubPool class type; define a small proxy if not accessible
    // But we assume ArtPoolWithSubpoolsSO.subpools contains elements of type SubPool (user code).
}
