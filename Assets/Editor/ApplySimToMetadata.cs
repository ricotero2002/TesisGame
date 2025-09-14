// Assets/Editor/ApplySimToMetadata.cs
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class ApplySimToMetadata
{
    private const string FEATURES_JSON = "Assets/Renders/subpool_features.json"; // ajustá si está en otra ruta
    [MenuItem("Tools/Metadata/Apply SimFeatures From JSON")]
    public static void Apply()
    {
        if (!File.Exists(FEATURES_JSON))
        {
            Debug.LogError($"ApplySimToMetadata: no existe {FEATURES_JSON}");
            return;
        }

        string txt = File.ReadAllText(FEATURES_JSON);
        JObject root = JObject.Parse(txt); // root: { "PoolName": { "object_id": { "features": {...}, "topK": [...] } } }

        int applied = 0;
        foreach (var poolProp in root.Properties())
        {
            string poolName = poolProp.Name;
            JObject poolObj = (JObject)poolProp.Value;

            foreach (var objProp in poolObj.Properties())
            {
                string objectId = objProp.Name; // e.g. "Estatuas/36c89e/ancient-titan-statue"
                JObject objInfo = (JObject)objProp.Value;

                // derive prefab name = last segment
                string[] parts = objectId.Split('/');
                string prefabName = parts.Length > 0 ? parts.Last() : objectId;

                string metaPath = $"Assets/Metadata/{prefabName}Meta.asset";
                ModelMetadata mm = AssetDatabase.LoadAssetAtPath<ModelMetadata>(metaPath);
                if (mm == null)
                {
                    Debug.LogWarning($"ApplySimToMetadata: no existe metadata asset para {prefabName} (esperado en {metaPath})");
                    continue;
                }

                // Ensure simFeatures container exists
                if (mm.simFeatures == null) mm.simFeatures = new ModelMetadata.SimFeatures();

                // features
                var featuresToken = objInfo["features"];
                if (featuresToken != null)
                {
                    mm.simFeatures.sim_max = featuresToken.Value<float?>("sim_max") ?? 0f;
                    mm.simFeatures.sim_mean_topK = featuresToken.Value<float?>("sim_mean_topK") ?? 0f;
                    mm.simFeatures.sim_count_thresh = featuresToken.Value<int?>("sim_count_thresh") ?? 0;
                    mm.simFeatures.sim_entropy = featuresToken.Value<float?>("sim_entropy") ?? 0f;
                }

                // topK
                mm.topSimilar = mm.topSimilar ?? new List<ModelMetadata.TopSim>();
                mm.topSimilar.Clear();
                var topKToken = objInfo["topK"];
                if (topKToken != null && topKToken.Type == JTokenType.Array)
                {
                    foreach (var t in topKToken)
                    {
                        string otherId = t.Value<string>("object_id");
                        float score = t.Value<float?>("score") ?? 0f;
                        mm.topSimilar.Add(new ModelMetadata.TopSim { objectId = otherId, score = score });
                    }
                }

                // optional: update pool/category/tags if present in JSON metadata section (si generaste export.json y lo integraste)
                // por ejemplo si tienes export_obj metadata en otro JSON podrías volver a agregar tags.

                EditorUtility.SetDirty(mm);
                applied++;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"ApplySimToMetadata: Aplicadas {applied} entradas desde {FEATURES_JSON}");
    }
}
