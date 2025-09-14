// PrefabRenderExporter.cs
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Linq;

public class PrefabRenderExporter : EditorWindow
{
    private string poolsFolder = "Assets/PoolsSnapshots";
    private string renderOutDir = "Assets/Renders";
    private int resolution = 224;

    [MenuItem("Tools/Prefabs/Render & Export from Pools JSON Folder")]
    public static void ShowWindow()
    {
        GetWindow<PrefabRenderExporter>("Prefab Render Exporter");
    }

    private void OnGUI()
    {
        GUILayout.Label("Render Prefabs from Pools JSON Folder", EditorStyles.boldLabel);
        poolsFolder = EditorGUILayout.TextField("Pools JSON Folder", poolsFolder);
        renderOutDir = EditorGUILayout.TextField("Output Dir", renderOutDir);

        if (GUILayout.Button("Run Export"))
            RunExport();
    }

    private void RunExport()
    {
        if (!Directory.Exists(poolsFolder))
        {
            Debug.LogError($"Pools folder not found: {poolsFolder}");
            return;
        }
        Directory.CreateDirectory(renderOutDir);

        List<object> jsonEntries = new List<object>();

        string[] files = Directory.GetFiles(poolsFolder, "*.json", SearchOption.TopDirectoryOnly);
        if (files.Length == 0)
        {
            Debug.LogWarning($"No pool JSON files found in {poolsFolder}");
        }

        foreach (var file in files)
        {
            string json = File.ReadAllText(file);
            ArtPoolSO.PoolJson pool = null;
            try
            {
                pool = JsonConvert.DeserializeObject<ArtPoolSO.PoolJson>(json);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"Failed to parse pool json '{file}': {ex.Message}");
                continue;
            }
            if (pool == null) continue;

            string category = string.IsNullOrEmpty(pool.poolName) ? "Uncategorized" : pool.poolName;

            foreach (var sub in pool.subpools)
            {
                foreach (var oid in sub.memberObjectIds ?? Enumerable.Empty<string>())
                {
                    string prefabName = oid.Split('/')[^1];
                    string prefabPath = $"Assets/Prefabs/{prefabName}.prefab";
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                    if (prefab == null)
                    {
                        Debug.LogWarning($"Prefab not found: {prefabName} (expected {prefabPath})");
                        // continue; // opcional: seguir aunque no exista prefab
                    }

                    string prefabRenderDir = Path.Combine(renderOutDir, prefabName);
                    Directory.CreateDirectory(prefabRenderDir);
                    List<string> images = new List<string>();

                    // 8 vistas × 2 luces = 16 imágenes
                    for (int view = 0; view < 8; view++)
                    {
                        for (int lightId = 0; lightId < 2; lightId++)
                        {
                            string imgPath = Path.Combine(prefabRenderDir, $"{prefabName}_v{view}_l{lightId}.png");
                            RenderPrefabFromAngle(prefab, imgPath, resolution, view, 8, lightId);
                            images.Add(imgPath);
                        }
                    }

                    var entry = new
                    {
                        object_id = oid,
                        category = category,
                        subpool = sub.displayName,
                        images = images
                    };
                    jsonEntries.Add(entry);
                }
            }
        }

        string jsonOut = Path.Combine(renderOutDir, "export.json");
        File.WriteAllText(jsonOut, JsonConvert.SerializeObject(jsonEntries, Formatting.Indented));
        AssetDatabase.Refresh();
        Debug.Log($"Export finished. JSON at {jsonOut}  (entries: {jsonEntries.Count})");
    }

    private void RenderPrefabFromAngle(GameObject prefab, string outPath, int res, int viewIndex, int totalViews, int lightConfig)
    {
        GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
        if (instance == null) return;
        instance.transform.position = Vector3.zero;
        instance.transform.rotation = Quaternion.identity;

        var rends = instance.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) { DestroyImmediate(instance); return; }
        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

        float radius = Mathf.Max(b.extents.magnitude, 0.5f) * 2.5f;
        float baseAngle = (360f / totalViews) * viewIndex;
        float yaw = baseAngle + Random.Range(-10f, 10f);
        float pitch = (viewIndex >= 4) ? 45f + Random.Range(-10f, 10f) : Random.Range(-5f, 5f);
        float dist = radius * (1f + Random.Range(-0.1f, 0.1f));

        var camGO = new GameObject("TempCam");
        var cam = camGO.AddComponent<Camera>();
        cam.backgroundColor = Color.clear;
        cam.clearFlags = CameraClearFlags.Color;
        cam.orthographic = false;
        cam.fieldOfView = 45f;

        Vector3 dir = Quaternion.Euler(pitch, yaw, 0f) * Vector3.back;
        Vector3 camPos = b.center + dir * dist + Vector3.up * (b.size.y * 0.15f);
        cam.transform.position = camPos;
        cam.transform.LookAt(b.center);

        var lightGO = new GameObject("TempLight");
        var light = lightGO.AddComponent<Light>();
        light.type = LightType.Directional;
        if (lightConfig == 0)
            light.transform.rotation = Quaternion.Euler(50f, yaw + 30f, 0f);
        else
            light.transform.rotation = Quaternion.Euler(70f, yaw - 60f, 20f);
        light.intensity = 1.2f;

        RenderTexture rt = new RenderTexture(res, res, 24);
        cam.targetTexture = rt;
        Texture2D tex = new Texture2D(res, res, TextureFormat.RGBA32, false);

        cam.Render();
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, res, res), 0, 0);
        tex.Apply();
        File.WriteAllBytes(outPath, tex.EncodeToPNG());

        DestroyImmediate(instance);
        DestroyImmediate(camGO);
        DestroyImmediate(lightGO);
        RenderTexture.active = null;
        rt.Release();
    }
}
