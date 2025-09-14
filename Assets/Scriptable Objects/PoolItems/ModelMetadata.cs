using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "ModelMetadata", menuName = "Metadata/ModelMetadata")]
public class ModelMetadata : ScriptableObject
{
    public Vector3 size;
    public int triangleCount;
    public string[] materialNames;
    public string[] tags;
    public string category;
    public float referenceHeightProvided;
    public string pool;
    public string objectId;
    [Serializable]
    public class SimFeatures
    {
        public float sim_max;
        public float sim_mean_topK;
        public int sim_count_thresh;
        public float sim_entropy;
    }

    [Serializable]
    public class TopSim
    {
        public string objectId;
        public float score;
    }

    public SimFeatures simFeatures;
    public List<TopSim> topSimilar = new List<TopSim>();

    // Si querés más info, agregá campos aquí:
    // public string sourceFile;
    // public string category;
    // public float estimatedHeight;

}
