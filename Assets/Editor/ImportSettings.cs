// Assets/Editor/ImportSettings.cs
using UnityEngine;

[CreateAssetMenu(fileName = "ImportSettings", menuName = "Import/ImportSettings")]
public class ImportSettings : ScriptableObject
{
    [Header("Defaults")]
    public float defaultHumanHeight = 1.8f;         // si detectamos humano y no hay metadata
    public float defaultVehicleHeight = 1.5f;
    public float defaultOtherReference = 1.0f;     // referencia genérica

    [Header("Toy scaling")]
    public float allowedFootprintMultiplier = 5f; // footprint <= avgPool * this -> OK; si > -> toy
    public float toyTargetMultiplier = 0.6f;        // footprint final = avgPool * toyTargetMultiplier
    public float absoluteToyMaxFootprint = 1.2f;    // tamaño (m) máximo si no hay pool (fallback)

    [Header("General")]
    public string metadataJsonSuffix = ".meta.json"; // nombre del JSON si existe (ej: car.fbx.meta.json)
    public bool requireDoctorMetadata = true;      // si true, sin metadata no se importa final

    // ImportSettings.cs (fragmento relevante)
    [Header("Subpooling")]
    [Tooltip("Scales applied to each feature before computing L2 distance. Order: height, footprint, depth, aspect, volumeLog")]
    public float[] featureScales = new float[5] { 1f, 1f, 1f, 1f, 0.5f }; // volumen en menor escala por usar log

    [Tooltip("Distancia L2 (con scales) máxima para asignar a subpool existente; si es mayor se crea un subpool nuevo.")]
    public float subpoolAssignThreshold = 1.2f; // ajustá con pruebas
    [Header("Strip nodes (auto-remove on import)")]
    [Tooltip("Remueve componentes Camera en modelos importados")]
    public bool removeCameras = true;
    [Tooltip("Remueve componentes Light en modelos importados")]
    public bool removeLights = true;

}
