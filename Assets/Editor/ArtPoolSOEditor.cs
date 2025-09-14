// Assets/Editor/ArtPoolSOEditor.cs
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System;
using System.Reflection;
using System.IO;
using System.Linq;
using System.Collections.Generic;

/// <summary>
/// Editor window combinado para ArtPoolSO:
/// - Populate members from JSON (invoca pool.PopulateMembersFromSnapshot)
/// - Choose random subpool (invoca pool.ChooseRandomSubpool)
/// - Get random prefab from chosen subpool (invoca pool.GetRandomPrefabFromChosenSubpool)
/// - Auto-assign environment overrides desde Assets/PoolMateriales/{category}
/// - Limpieza de subpools vacías
/// Usa reflection para llamar a métodos opcionales presentes en tu ArtPoolSO.
/// </summary>
public class ArtPoolSOEditorWindow : EditorWindow
{
    ArtPoolSO pool;
    Vector2 scroll;

    [MenuItem("Tools/ArtPool/JSON & AutoAssign Editor")]
    public static void ShowWindow() => GetWindow<ArtPoolSOEditorWindow>("ArtPool Tools");

    void OnGUI()
    {
        // --- (in OnGUI) add the merge UI here ---
        EditorGUILayout.Space();
        DrawMergeSmallSubpoolsUI();   // <-- nueva línea: mostrará el panel Merge small subpools

        GUILayout.Space(6);
        GUILayout.Label("ArtPool Tools (JSON / Auto-assign / Debug / Cleanup)", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        pool = (ArtPoolSO)EditorGUILayout.ObjectField("ArtPoolSO", pool, typeof(ArtPoolSO), false);

        EditorGUILayout.Space();

        if (pool == null)
        {
            EditorGUILayout.HelpBox("Arrastra un ArtPoolSO aquí para operar (Project window).", MessageType.Info);
            return;
        }

        scroll = EditorGUILayout.BeginScrollView(scroll);

        // --- Populate from snapshot JSON ---
        if (GUILayout.Button("Populate members from snapshot JSON (editor)"))
        {
            bool called = TryCall(pool, "PopulateMembersFromSnapshot", new object[] { true });
            if (!called)
            {
                // try parameterless
                called = TryCall(pool, "PopulateMembersFromSnapshot", null);
            }
            if (!called) EditorUtility.DisplayDialog("PopulateMembersFromSnapshot", "El método PopulateMembersFromSnapshot no se encontró en ArtPoolSO.", "OK");
            else { EditorUtility.SetDirty(pool); AssetDatabase.SaveAssets(); AssetDatabase.Refresh(); }
        }

        // --- Auto assign env overrides (default folder) ---
        if (GUILayout.Button("Auto-assign environment overrides (Assets/PoolMateriales/{category})"))
        {
            bool ok = TryCall(pool, "AutoAssignEnvironmentOverridesFromFolder", new object[] { "Assets/PoolMateriales" });
            if (!ok)
            {
                // try parameterless
                ok = TryCall(pool, "AutoAssignEnvironmentOverridesFromFolder", null);
            }
            if (!ok) EditorUtility.DisplayDialog("Auto-assign", "No se encontró AutoAssignEnvironmentOverridesFromFolder en ArtPoolSO.", "OK");
            else { EditorUtility.SetDirty(pool); AssetDatabase.SaveAssets(); AssetDatabase.Refresh(); }
        }

        // --- Auto assign from custom folder ---
        if (GUILayout.Button("Auto-assign from custom folder (choose)"))
        {
            string folder = EditorUtility.OpenFolderPanel("Select PoolMateriales root for this category", Application.dataPath, "");
            if (!string.IsNullOrEmpty(folder))
            {
                if (folder.StartsWith(Application.dataPath))
                {
                    string rel = "Assets" + folder.Substring(Application.dataPath.Length);
                    bool ok = TryCall(pool, "AutoAssignEnvironmentOverridesFromFolder", new object[] { rel });
                    if (!ok) EditorUtility.DisplayDialog("Auto-assign", "No se encontró AutoAssignEnvironmentOverridesFromFolder en ArtPoolSO.", "OK");
                    else { EditorUtility.SetDirty(pool); AssetDatabase.SaveAssets(); AssetDatabase.Refresh(); }
                }
                else
                {
                    EditorUtility.DisplayDialog("Carpeta inválida", "La carpeta debe estar dentro del proyecto (Assets/...) para usar AssetDatabase.", "OK");
                }
            }
        }

        EditorGUILayout.Space();

        // --- Choose random subpool ---
        if (GUILayout.Button("Choose random subpool (editor)"))
        {
            object sp = TryCallWithResult(pool, "ChooseRandomSubpool", new object[] { true }, out bool worked);
            if (!worked)
            {
                sp = TryCallWithResult(pool, "ChooseRandomSubpool", null, out worked);
            }
            if (worked && sp != null)
            {
                var dspName = GetFieldOrPropString(sp, "displayName");
                var spId = GetFieldOrPropString(sp, "subpoolId");
                Debug.Log($"Chosen subpool: {dspName ?? sp.ToString()} ({spId ?? "no-id"})");
            }
            else if (!worked)
            {
                EditorUtility.DisplayDialog("ChooseRandomSubpool", "El método ChooseRandomSubpool no se encontró en ArtPoolSO.", "OK");
            }
        }

        // --- Get random prefab from chosen subpool ---
        if (GUILayout.Button("Get random prefab from chosen subpool (editor)"))
        {
            object go = TryCallWithResult(pool, "GetRandomPrefabFromChosenSubpool", null, out bool ok);
            if (!ok) go = TryCallWithResult(pool, "GetRandomPrefabFromChosenSubpool", new object[] { true }, out ok);
            if (ok && go != null)
            {
                var goObj = go as UnityEngine.Object;
                if (goObj != null) Debug.Log($"Random prefab: {goObj.name}");
                else Debug.Log($"Random prefab returned: {go}");
            }
            else
            {
                EditorUtility.DisplayDialog("GetRandomPrefabFromChosenSubpool", "El método GetRandomPrefabFromChosenSubpool no se encontró o no devolvió un prefab.", "OK");
            }
        }

        EditorGUILayout.Space();

        // --- Clean empty subpools ---
        if (GUILayout.Button("Clean empty subpools"))
        {
            int removed = CleanEmptySubpools(pool);
            if (removed >= 0)
            {
                string msg = $"Se eliminaron {removed} subpool(s) vacía(s) de '{pool.name}'.";
                if (!string.IsNullOrEmpty(pool.snapshotJsonPath))
                    msg += $"\nAdvertencia: snapshotJsonPath está establecido ({pool.snapshotJsonPath}). Puede que el JSON necesite re-generarse para quedar sincronizado.";
                EditorUtility.DisplayDialog("Clean empty subpools", msg, "OK");
                EditorUtility.SetDirty(pool);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            else
            {
                EditorUtility.DisplayDialog("Clean empty subpools", "Error al limpiar subpools (revisá la Consola).", "OK");
            }
        }

        EditorGUILayout.Space();

        EditorGUILayout.LabelField("Snapshot Json Path:", pool.snapshotJsonPath ?? "(empty)");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Open snapshot JSON"))
        {
            if (!string.IsNullOrEmpty(pool.snapshotJsonPath) && File.Exists(pool.snapshotJsonPath))
            {
                EditorUtility.RevealInFinder(pool.snapshotJsonPath);
            }
            else
            {
                EditorUtility.DisplayDialog("Open snapshot", "snapshotJsonPath vacío o no existe.", "OK");
            }
        }
        if (GUILayout.Button("Set snapshot JSON path..."))
        {
            string file = EditorUtility.SaveFilePanelInProject("Choose snapshot JSON location", $"{pool.categoryName}_pool.json", "json", "Location for pool snapshot JSON");
            if (!string.IsNullOrEmpty(file))
            {
                bool assigned = TrySetFieldOrProp(pool, "snapshotJsonPath", file);
                if (!assigned)
                {
                    EditorUtility.DisplayDialog("Set snapshot", "No se encontró el campo snapshotJsonPath en ArtPoolSO. Puedes crear/public string snapshotJsonPath en el script para que esto funcione.", "OK");
                }
                else
                {
                    EditorUtility.SetDirty(pool);
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                }
            }
        }
        GUILayout.EndHorizontal();

        EditorGUILayout.Space();

        // --- Export current subpools to snapshot JSON (backup + write) ---
        if (GUILayout.Button("Export snapshot JSON (save current subpools)"))
        {
            try
            {
                string targetPath = pool.snapshotJsonPath;
                if (string.IsNullOrEmpty(targetPath))
                {
                    string cat = string.IsNullOrEmpty(pool.categoryName) ? pool.name : pool.categoryName;
                    targetPath = $"Assets/PoolsSnapshots/{cat}_pool.json";
                }

                // backup existing
                if (File.Exists(targetPath))
                {
                    string bak = targetPath + ".bak";
                    File.Copy(targetPath, bak, true);
                    Debug.Log($"Export snapshot: existing JSON backed up to {bak}");
                }
                else
                {
                    // ensure directory exists
                    var dir = Path.GetDirectoryName(targetPath);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                }

                // build export DTO
                var pj = new PoolJsonExport() { poolName = pool.categoryName ?? pool.name, subpools = new List<SubpoolJsonExport>() };
                if (pool.subpools != null)
                {
                    for (int i = 0; i < pool.subpools.Count; i++)
                    {
                        var s = pool.subpools[i];
                        var se = new SubpoolJsonExport()
                        {
                            subpoolId = s.subpoolId ?? Guid.NewGuid().ToString("N").Substring(0, 8),
                            displayName = s.displayName ?? $"{pj.poolName}_{i + 1}",
                            memberObjectIds = s.memberObjectIds != null ? new List<string>(s.memberObjectIds) : new List<string>(),
                            center5 = s.center5
                        };
                        pj.subpools.Add(se);
                    }
                }

                string outJson = JsonUtility.ToJson(pj, true);
                File.WriteAllText(targetPath, outJson);
                AssetDatabase.ImportAsset(targetPath);
                AssetDatabase.Refresh();

                // ensure ArtPoolSO points to this snapshot
                TrySetFieldOrProp(pool, "snapshotJsonPath", targetPath);
                EditorUtility.SetDirty(pool);
                AssetDatabase.SaveAssets();
                Debug.Log($"Export snapshot: wrote {pj.subpools.Count} subpools to {targetPath}");
                EditorUtility.DisplayDialog("Export snapshot", $"Exportado snapshot JSON a:\n{targetPath}", "OK");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Export snapshot failed: {ex}");
                EditorUtility.DisplayDialog("Export snapshot", $"Error: {ex.Message}", "OK");
            }
        }


        // --- Utilities ---
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Force re-save ArtPool asset"))
        {
            EditorUtility.SetDirty(pool);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"ArtPoolEditor: Saved {pool.name}");
        }
        if (GUILayout.Button("Refresh subpools (call RecalculateAllSubpools)"))
        {
            bool called = TryCall(pool, "RecalculateAllSubpools", null);
            if (!called) EditorUtility.DisplayDialog("Refresh", "No se encontró RecalculateAllSubpools en ArtPoolSO (o firma diferente).", "OK");
            else
            {
                EditorUtility.SetDirty(pool);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }
        GUILayout.EndHorizontal();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Subpools count:", pool.subpools != null ? pool.subpools.Count.ToString() : "0");

        EditorGUILayout.EndScrollView();
    }
    // --- Merge small subpools UI ---
    int mergeSmallLimit = 2;
    float mergeThreshold = -1f; // if <=0 we'll compute a sensible default

    void DrawMergeSmallSubpoolsUI()
    {
        GUILayout.Space(8);
        EditorGUILayout.LabelField("Merge small subpools", EditorStyles.boldLabel);

        mergeSmallLimit = EditorGUILayout.IntSlider(new GUIContent("Max members to consider \"small\""), mergeSmallLimit, 1, 5);
        mergeThreshold = EditorGUILayout.FloatField(new GUIContent("Merge threshold (distance) - <=0 uses ImportSettings default*1.5"), mergeThreshold);

        EditorGUILayout.HelpBox("Busca subpools con <= 'Max members' y las une a la subpool más cercana *si* la distancia <= threshold. Si no se puede, te muestra la subpool más cercana y cuánto falta.", MessageType.Info);

        if (GUILayout.Button("Merge small subpools into nearest (editor)"))
        {
            if (pool == null)
            {
                EditorUtility.DisplayDialog("Merge small subpools", "Arrastra primero un ArtPoolSO en el campo.", "OK");
            }
            else
            {
                MergeSmallSubpools(pool, mergeSmallLimit, mergeThreshold);
            }
        }
    }

    // Call this function from OnGUI somewhere (e.g. right after Clean empty subpools block)
    int MergeSmallSubpools(ArtPoolSO pool, int smallLimit, float threshold)
    {
        try
        {
            if (pool == null || pool.subpools == null || pool.subpools.Count == 0)
            {
                EditorUtility.DisplayDialog("Merge small subpools", "Pool vacío o inválido.", "OK");
                return 0;
            }

            // 1) Try to call RecalculateAllSubpools to ensure center5 are up to date (if the method exists)
            TryCall(pool, "RecalculateAllSubpools", null);

            // 2) Load ImportSettings (if present) to obtain featureScales and default threshold
            float[] scales = new float[] { 1f, 1f, 1f, 1f, 1f };
            float defaultThresh = 1.0f;
            try
            {
                var guids = AssetDatabase.FindAssets("t:ImportSettings");
                if (guids != null && guids.Length > 0)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                    var settings = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                    // Use reflection to read fields to avoid compile-time dependency
                    var t = settings.GetType();
                    var fScales = t.GetField("featureScales", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var fThresh = t.GetField("subpoolAssignThreshold", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (fScales != null)
                    {
                        var val = fScales.GetValue(settings) as float[];
                        if (val != null && val.Length > 0) scales = val;
                    }
                    if (fThresh != null)
                    {
                        var val = fThresh.GetValue(settings);
                        if (val is float vf) defaultThresh = vf;
                    }
                }
            }
            catch { /* silent fallback */ }

            // If threshold not provided, pick a sensible default > importer threshold
            if (threshold <= 0f) threshold = defaultThresh * 1.5f;

            var sps = pool.subpools.ToList(); // copy to iterate safely
            var toRemove = new List<object>(); // will hold subpool objects to remove after merging
            var logLines = new List<string>();
            int mergedCount = 0;

            for (int i = 0; i < sps.Count; i++)
            {
                var sp = sps[i];
                if (sp == null) continue;

                // read member list/count via reflection to be generic
                var tsp = sp.GetType();
                var fMembers = tsp.GetField("memberObjectIds", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var fCenter = tsp.GetField("center5", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var fDisplay = tsp.GetField("displayName", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                var members = fMembers != null ? fMembers.GetValue(sp) as List<string> : null;
                var center = fCenter != null ? (FeatureVector5)fCenter.GetValue(sp) : FeatureVector5.zero;
                var dspName = fDisplay != null ? fDisplay.GetValue(sp) as string : null;

                int memberCount = (members != null) ? members.Count : 0;
                if (memberCount == 0 || memberCount > smallLimit) continue; // skip empties and those bigger than smallLimit

                // Find nearest candidate with strictly more members
                object bestCandidate = null;
                float bestDist = float.MaxValue;
                int bestCandidateCount = 0;
                string bestCandidateName = "(unknown)";

                foreach (var cand in pool.subpools)
                {
                    if (cand == null || cand == sp) continue;
                    var tc = cand.GetType();
                    var fm = tc.GetField("memberObjectIds", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var fc = tc.GetField("center5", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var fdn = tc.GetField("displayName", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                    var cmembers = fm != null ? fm.GetValue(cand) as List<string> : null;
                    int cmCount = (cmembers != null) ? cmembers.Count : 0;

                    if (cmCount <= memberCount) continue; // want a strictly larger pool (según pedido)

                    var ccenter = fc != null ? (FeatureVector5)fc.GetValue(cand) : FeatureVector5.zero;

                    float dist = float.MaxValue;
                    if (!center.Equals(FeatureVector5.zero) && !ccenter.Equals(FeatureVector5.zero))
                    {
                        dist = center.DistanceTo(ccenter, scales);
                    }

                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestCandidate = cand;
                        bestCandidateCount = cmCount;
                        bestCandidateName = (fdn != null) ? fdn.GetValue(cand) as string : "(cand)";
                    }
                }

                if (bestCandidate == null)
                {
                    logLines.Add($"'{dspName ?? "subpool"}' ({memberCount}): no se encontró subpool más grande para fusionar.");
                    continue;
                }

                if (bestDist <= threshold)
                {
                    // Merge: add members to candidate (avoid duplicates) and update center incrementally
                    var tc = bestCandidate.GetType();
                    var fm = tc.GetField("memberObjectIds", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var fc = tc.GetField("center5", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                    var candMembers = fm != null ? fm.GetValue(bestCandidate) as List<string> : null;
                    if (candMembers == null) { candMembers = new List<string>(); if (fm != null) fm.SetValue(bestCandidate, candMembers); }

                    // add non-duplicate members
                    foreach (var oid in (members ?? new List<string>()))
                        if (!candMembers.Contains(oid)) candMembers.Add(oid);

                    // update center: weighted average
                    var oldCenter = fc != null ? (FeatureVector5)fc.GetValue(bestCandidate) : FeatureVector5.zero;
                    float nc = (float)bestCandidateCount;
                    float ns = (float)memberCount;
                    FeatureVector5 newCenter;
                    if (!oldCenter.Equals(FeatureVector5.zero) && !center.Equals(FeatureVector5.zero))
                    {
                        newCenter = new FeatureVector5(
                            (oldCenter.a * nc + center.a * ns) / (nc + ns),
                            (oldCenter.b * nc + center.b * ns) / (nc + ns),
                            (oldCenter.c * nc + center.c * ns) / (nc + ns),
                            (oldCenter.d * nc + center.d * ns) / (nc + ns),
                            (oldCenter.e * nc + center.e * ns) / (nc + ns)
                        );
                    }
                    else if (!oldCenter.Equals(FeatureVector5.zero))
                    {
                        newCenter = oldCenter;
                    }
                    else
                    {
                        newCenter = center;
                    }

                    if (fc != null) fc.SetValue(bestCandidate, newCenter);

                    // mark this small subpool for removal
                    toRemove.Add(sp);
                    mergedCount++;
                    logLines.Add($"Merged '{dspName ?? "subpool"}' ({memberCount}) -> '{bestCandidateName}' (dist={bestDist:F3}).");
                }
                else
                {
                    float remaining = bestDist - threshold;
                    logLines.Add($"'{dspName ?? "subpool"}' ({memberCount}) NOT merged. Closest='{bestCandidateName}', dist={bestDist:F3}, falta {remaining:F3} para threshold={threshold:F3}.");
                }
            }

            // Remove merged subpools (robusto sin conocer el tipo genérico)
            var listAsIList = pool.subpools as System.Collections.IList;
            if (listAsIList != null)
            {
                foreach (var r in toRemove)
                {
                    try { listAsIList.Remove(r); }
                    catch (Exception ex) { Debug.LogWarning($"Remove failed for item: {ex.Message}"); }
                }
            }
            else
            {
                Debug.LogWarning("pool.subpools no implementa IList (improbable).");
            }


            // Reindex/rename subpools for tidiness
            for (int i = 0; i < pool.subpools.Count; i++)
            {
                var spn = pool.subpools[i];
                if (spn == null) continue;
                var tsp = spn.GetType();
                var fId = tsp.GetField("subpoolId", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var fDisp = tsp.GetField("displayName", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (fId != null)
                {
                    var cur = fId.GetValue(spn) as string;
                    if (string.IsNullOrEmpty(cur)) fId.SetValue(spn, Guid.NewGuid().ToString("N").Substring(0, 8));
                }
                if (fDisp != null)
                {
                    string cat = string.IsNullOrEmpty(pool.categoryName) ? pool.name : pool.categoryName;
                    fDisp.SetValue(spn, $"{cat}_{i + 1}");
                }
            }

            EditorUtility.SetDirty(pool);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Show summary
            string summary = $"Merge small subpools complete. Merged count: {mergedCount}\n\nDetails:\n" + string.Join("\n", logLines.Take(40));
            if (logLines.Count > 40) summary += $"\n... ({logLines.Count - 40} more lines)";
            EditorUtility.DisplayDialog("Merge small subpools", summary, "OK");

            return mergedCount;
        }
        catch (Exception ex)
        {
            Debug.LogError($"MergeSmallSubpools error: {ex}");
            EditorUtility.DisplayDialog("Merge small subpools", $"Error: {ex.Message}", "OK");
            return 0;
        }
    }

    // ---------- Clean empty subpools implementation ----------
    // Returns number removed, -1 on error
    int CleanEmptySubpools(ArtPoolSO pool)
    {
        try
        {
            if (pool == null) return -1;
            if (pool.subpools == null || pool.subpools.Count == 0) return 0;

            // find empties: member lists null or count == 0
            var empties = pool.subpools.Where(sp =>
                sp == null ||
                (sp.memberObjectIds == null) ||
                (sp.memberObjectIds != null && sp.memberObjectIds.Count == 0)
            ).ToList();

            int removed = 0;
            foreach (var e in empties)
            {
                pool.subpools.Remove(e);
                removed++;
            }

            // Re-index / rename displayName for remaining subpools for tidy-ness
            for (int i = 0; i < pool.subpools.Count; i++)
            {
                var sp = pool.subpools[i];
                if (sp == null) continue;
                // assign id if missing
                if (string.IsNullOrEmpty(sp.subpoolId))
                    sp.subpoolId = Guid.NewGuid().ToString("N").Substring(0, 8);
                // create display name based on category and index
                string cat = string.IsNullOrEmpty(pool.categoryName) ? pool.name : pool.categoryName;
                sp.displayName = $"{cat}_{i + 1}";
            }

            // mark dirty & save
            EditorUtility.SetDirty(pool);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"ArtPoolEditor: CleanEmptySubpools removed {removed} empty subpools for {pool.name}.");
            return removed;
        }
        catch (Exception ex)
        {
            Debug.LogError($"ArtPoolEditor.CleanEmptySubpools error: {ex}");
            return -1;
        }
    }

    // ---------- Reflection helpers ----------
    bool TryCall(object target, string methodName, object[] args)
    {
        if (target == null) return false;
        var t = target.GetType();
        var m = t.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (m == null) return false;
        try
        {
            m.Invoke(target, args);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Invoke {methodName} failed: {ex}");
            return false;
        }
    }

    object TryCallWithResult(object target, string methodName, object[] args, out bool worked)
    {
        worked = false;
        if (target == null) return null;
        var t = target.GetType();
        var m = t.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (m == null) return null;
        try
        {
            worked = true;
            return m.Invoke(target, args);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Invoke {methodName} failed: {ex}");
            worked = false;
            return null;
        }
    }

    bool TrySetFieldOrProp(object target, string name, string value)
    {
        if (target == null) return false;
        var t = target.GetType();
        var f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (f != null && f.FieldType == typeof(string))
        {
            f.SetValue(target, value);
            return true;
        }
        var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (p != null && p.PropertyType == typeof(string) && p.CanWrite)
        {
            p.SetValue(target, value);
            return true;
        }
        return false;
    }

    string GetFieldOrPropString(object obj, string name)
    {
        if (obj == null) return null;
        var t = obj.GetType();
        var f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (f != null && f.FieldType == typeof(string)) return (string)f.GetValue(obj);
        var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (p != null && p.PropertyType == typeof(string)) return (string)p.GetValue(obj, null);
        return null;
    }
    [Serializable]
    public class SubpoolJsonExport
    {
        public string subpoolId;
        public string displayName;
        public List<string> memberObjectIds = new List<string>();
        public FeatureVector5 center5 = new FeatureVector5(0, 0, 0, 0, 0); // requiere que FeatureVector5 sea public & serializable
    }

    [Serializable]
    public class PoolJsonExport
    {
        public string poolName;
        public List<SubpoolJsonExport> subpools = new List<SubpoolJsonExport>();
    }

}
#endif
