// Assets/Editor/MetadataTagUpdater.cs
// EditorWindow para actualizar tags en ModelMetadata sin reimportar.
// Soporta:
//  - Carpeta con JSON por objeto (recomendado). Cada JSON debe contener al menos:
//      { "object_id": "Estatuas/abcd12/name", "tags": ["tag1","tag2"] }
//  - CSV simple: "object_id,tag1;tag2;tag3" o "prefabName,tag1;tag2"
//  - Pegado manual en el textarea (una entrada por línea, formato igual al CSV)
// Backup: guarda un pequeño json text con los tags previos en Assets/Metadata/backups/
// Matching: por defecto busca el prefabName (último segmento del object_id) y el asset `Assets/Metadata/{prefabName}Meta.asset`
// Requiere que exista la clase `ModelMetadata` con campo `public string[] tags;` (si no la tenés, adaptá el código).

using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Linq;

public class MetadataTagUpdater : EditorWindow
{
    private string perObjectJsonFolder = "Assets/Renders/metadata_plus_sim"; // carpeta con JSON por objeto (recomendado)
    private string csvPath = "";
    private string manualText = ""; // linea por entrada: object_id,tag1;tag2
    private bool matchByObjectId = true; // si false, match por prefabName (último segmento)
    private bool dryRun = true;
    private Vector2 scroll;
    private List<(string key, string[] tags)> parsedMappings = new List<(string, string[])>();
    private string status = "";

    [MenuItem("Tools/Metadata/Tag Updater")]
    public static void ShowWindow() => GetWindow<MetadataTagUpdater>("Metadata Tag Updater");

    private void OnGUI()
    {
        GUILayout.Label("Actualizar tags de ModelMetadata (sin reimport)", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        GUILayout.Label("1) Opción A: Carpeta con JSON por objeto (recomendado)", EditorStyles.miniBoldLabel);
        EditorGUILayout.BeginHorizontal();
        perObjectJsonFolder = EditorGUILayout.TextField(perObjectJsonFolder);
        if (GUILayout.Button("Browse", GUILayout.MaxWidth(80))) perObjectJsonFolder = EditorUtility.OpenFolderPanel("Carpeta JSON por objeto", perObjectJsonFolder, "");
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.HelpBox("Cada archivo JSON: { \"object_id\":\"Estatuas/..../name\", \"tags\": [\"a\",\"b\"] }", MessageType.Info);

        EditorGUILayout.Space();
        GUILayout.Label("2) Opción B: CSV o manual (una línea por entrada)", EditorStyles.miniBoldLabel);
        EditorGUILayout.BeginHorizontal();
        csvPath = EditorGUILayout.TextField(csvPath);
        if (GUILayout.Button("Browse CSV", GUILayout.MaxWidth(100))) csvPath = EditorUtility.OpenFilePanel("CSV file", Application.dataPath, "csv");
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.HelpBox("CSV formato: object_id,tag1;tag2;tag3  (o prefabName,tag1;tag2)", MessageType.None);

        GUILayout.Label("o pegar manualmente (una entrada por línea):", EditorStyles.label);
        manualText = EditorGUILayout.TextArea(manualText, GUILayout.Height(80));

        EditorGUILayout.Space();
        matchByObjectId = EditorGUILayout.ToggleLeft("Match by full object_id (if false matches by prefabName = last segment)", matchByObjectId);
        dryRun = EditorGUILayout.ToggleLeft("Dry run (no modifica assets, solo muestra lo que haría)", dryRun);

        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Parse Inputs")) { ParseInputs(); }
        if (GUILayout.Button("Preview (first 50)")) { PreviewMappings(); }
        if (GUILayout.Button("Apply Tags")) { ApplyTags(); }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        scroll = EditorGUILayout.BeginScrollView(scroll);
        GUILayout.Label("Parsed mappings (first 200 shown):", EditorStyles.boldLabel);
        int show = Mathf.Min(parsedMappings.Count, 200);
        for (int i = 0; i < show; i++)
        {
            var p = parsedMappings[i];
            EditorGUILayout.LabelField($"{i + 1}. {p.key} -> [{string.Join(", ", p.tags)}]");
        }
        if (parsedMappings.Count == 0) EditorGUILayout.LabelField("(no mappings parsed yet)");
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Status:");
        EditorGUILayout.HelpBox(status, MessageType.Info);
    }

    private void ParseInputs()
    {
        parsedMappings.Clear();
        status = "";

        // 1) from per-object JSON folder
        if (!string.IsNullOrEmpty(perObjectJsonFolder) && Directory.Exists(perObjectJsonFolder))
        {
            try
            {
                var files = Directory.GetFiles(perObjectJsonFolder, "*.json", SearchOption.TopDirectoryOnly);
                foreach (var f in files)
                {
                    try
                    {
                        string txt = File.ReadAllText(f);
                        // simple parsing using Unity JsonUtility: define small wrapper
                        try
                        {
                            POCO obj = JsonUtility.FromJson<POCO>(txt);
                            if (!string.IsNullOrEmpty(obj.object_id) && obj.tags != null && obj.tags.Length > 0)
                            {
                                parsedMappings.Add((obj.object_id, obj.tags));
                            }
                            else
                            {
                                // If no object_id, try to use file name as prefabName
                                string fname = Path.GetFileNameWithoutExtension(f);
                                if (obj.tags != null && obj.tags.Length > 0)
                                    parsedMappings.Add((fname, obj.tags));
                            }
                        }
                        catch
                        {
                            // fallback: try manual crude extraction (search for "object_id" and "tags")
                            var objId = HeuristicExtractString(txt, "object_id");
                            var tags = HeuristicExtractArray(txt, "tags");
                            if (!string.IsNullOrEmpty(objId) && tags != null && tags.Length > 0)
                                parsedMappings.Add((objId, tags));
                            else
                            {
                                // skip silently
                            }
                        }
                    }
                    catch (System.Exception exFile)
                    {
                        Debug.LogWarning($"MetadataTagUpdater: Error reading {f}: {exFile.Message}");
                    }
                }
                status += $"Parsed {parsedMappings.Count} mappings from folder '{perObjectJsonFolder}'.\n";
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"MetadataTagUpdater: Folder parse error: {ex.Message}");
                status += $"Error leyendo carpeta JSON: {ex.Message}\n";
            }
        }

        // 2) from CSV file
        if (!string.IsNullOrEmpty(csvPath) && File.Exists(csvPath))
        {
            try
            {
                var lines = File.ReadAllLines(csvPath).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
                int added = 0;
                foreach (var l in lines)
                {
                    var parts = l.Split(new char[] { ',' }, 2);
                    if (parts.Length >= 2)
                    {
                        var key = parts[0].Trim();
                        var tags = parts[1].Trim().Split(new char[] { ';', ',' }, System.StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s != "").ToArray();
                        if (tags.Length > 0) { parsedMappings.Add((key, tags)); added++; }
                    }
                }
                status += $"Parsed {added} mappings from CSV '{csvPath}'.\n";
            }
            catch (System.Exception ex)
            {
                status += $"Error leyendo CSV: {ex.Message}\n";
            }
        }

        // 3) manual textarea
        if (!string.IsNullOrWhiteSpace(manualText))
        {
            var lines = manualText.Split(new string[] { "\n", "\r\n" }, System.StringSplitOptions.RemoveEmptyEntries);
            int added = 0;
            foreach (var l in lines)
            {
                var parts = l.Split(new char[] { ',' }, 2);
                if (parts.Length >= 2)
                {
                    var key = parts[0].Trim();
                    var tags = parts[1].Trim().Split(new char[] { ';', ',' }, System.StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s != "").ToArray();
                    if (tags.Length > 0) { parsedMappings.Add((key, tags)); added++; }
                }
            }
            status += $"Parsed {added} mappings from manual text.\n";
        }

        // Deduplicate by key (keep last)
        var dedup = new Dictionary<string, string[]>();
        for (int i = 0; i < parsedMappings.Count; i++)
        {
            dedup[parsedMappings[i].key] = parsedMappings[i].tags;
        }
        parsedMappings = dedup.Select(kv => (kv.Key, kv.Value)).ToList();
        status += $"Total unique mappings: {parsedMappings.Count}\n";
    }

    private void PreviewMappings()
    {
        if (parsedMappings == null || parsedMappings.Count == 0) { status = "No hay mappings. Haz Parse Inputs primero."; return; }
        int found = 0, missing = 0;
        foreach (var m in parsedMappings.Take(50))
        {
            string prefabName = GetPrefabNameFromKey(m.key);
            string metaPath = $"Assets/Metadata/{prefabName}Meta.asset";
            if (File.Exists(metaPath)) found++; else missing++;
        }
        status = $"Preview: {parsedMappings.Count} total mappings. De las primeras 50 -> exists: {found}, missing metadata: {missing}.";
    }

    private void ApplyTags()
    {
        if (parsedMappings == null || parsedMappings.Count == 0) { status = "No hay mappings. Haz Parse Inputs primero."; return; }

        string backupDir = "Assets/Metadata/backups";
        if (!AssetDatabase.IsValidFolder("Assets/Metadata")) AssetDatabase.CreateFolder("Assets", "Metadata");
        if (!Directory.Exists(backupDir)) Directory.CreateDirectory(backupDir);

        int applied = 0;
        int skipped = 0;
        foreach (var m in parsedMappings)
        {
            string key = m.key;
            string[] tags = m.tags;
            string prefabName = GetPrefabNameFromKey(key);

            string metaPath = $"Assets/Metadata/{prefabName}Meta.asset";
            var mm = AssetDatabase.LoadAssetAtPath<Object>(metaPath);
            if (mm == null)
            {
                // try alternative: sometimes metadata file has different naming, search by prefabName
                string[] guids = AssetDatabase.FindAssets($"{prefabName} t:ScriptableObject");
                bool foundAlt = false;
                foreach (var g in guids)
                {
                    string p = AssetDatabase.GUIDToAssetPath(g);
                    if (p.ToLower().Contains(prefabName.ToLower()))
                    {
                        mm = AssetDatabase.LoadAssetAtPath<Object>(p);
                        if (mm != null) { metaPath = p; foundAlt = true; break; }
                    }
                }
                if (!foundAlt)
                {
                    Debug.LogWarning($"MetadataTagUpdater: metadata asset not found for '{prefabName}' (key:{key}) expected at {metaPath}");
                    skipped++;
                    continue;
                }
            }

            // Try to get ModelMetadata type via reflection-safe approach
            var mmType = mm.GetType();
            var tagsField = mmType.GetField("tags");
            if (tagsField == null)
            {
                Debug.LogError($"ModelMetadata does not contain public field 'tags' (asset: {metaPath}). Please add 'public string[] tags;' to your ModelMetadata class.");
                status = "Error: ModelMetadata class missing 'tags' field.";
                return;
            }

            // read current tags to backup
            string[] prevTags = tagsField.GetValue(mm) as string[] ?? new string[0];
            // backup previous tags to json
            string backupPath = Path.Combine(backupDir, $"{prefabName}_tags_backup.json");
            var backupObj = new BackupEntry { prefabName = prefabName, key = key, previousTags = prevTags };
            File.WriteAllText(backupPath, JsonUtility.ToJson(backupObj, true));

            // apply new tags
            tagsField.SetValue(mm, tags);

            if (!dryRun)
            {
                EditorUtility.SetDirty(mm);
            }

            applied++;
            Debug.Log($"MetadataTagUpdater: {(dryRun ? "[DRY] " : "")}Applied tags for {prefabName}: [{string.Join(", ", tags)}]");
        }

        if (!dryRun) { AssetDatabase.SaveAssets(); AssetDatabase.Refresh(); }

        status = $"Finished. Applied: {applied}. Skipped (no metadata): {skipped}. DryRun: {dryRun}";
    }

    private string GetPrefabNameFromKey(string key)
    {
        if (matchByObjectId)
        {
            // key might be full object_id (Estatuas/abc/name) or could already be prefabName
            if (key.Contains("/"))
            {
                var parts = key.Split('/');
                return parts[parts.Length - 1];
            }
            else return key;
        }
        else
        {
            return key;
        }
    }

    // small helper classes for JsonUtility
    [System.Serializable]
    private class POCO
    {
        public string object_id;
        public string[] tags;
    }

    [System.Serializable]
    private class BackupEntry
    {
        public string prefabName;
        public string key;
        public string[] previousTags;
    }

    // Heuristics to extract basic fields if JsonUtility fails
    private static string[] HeuristicExtractArray(string txt, string prop)
    {
        try
        {
            int idx = txt.IndexOf($"\"{prop}\"");
            if (idx < 0) return null;
            int start = txt.IndexOf('[', idx);
            int end = txt.IndexOf(']', start);
            if (start < 0 || end < 0) return null;
            string inner = txt.Substring(start + 1, end - start - 1);
            var parts = inner.Split(new char[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim().Trim('"')).Where(s => s != "").ToArray();
            return parts;
        }
        catch { return null; }
    }

    private static string HeuristicExtractString(string txt, string prop)
    {
        try
        {
            int idx = txt.IndexOf($"\"{prop}\"");
            if (idx < 0) return null;
            int colon = txt.IndexOf(':', idx);
            int quoteStart = txt.IndexOf('"', colon + 1);
            int quoteEnd = txt.IndexOf('"', quoteStart + 1);
            if (quoteStart < 0 || quoteEnd < 0) return null;
            return txt.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
        }
        catch { return null; }
    }
}
