using UnityEngine;
using UnityEditor;

public class ModelReadWritePostprocessor : AssetPostprocessor
{
    void OnPreprocessModel()
    {
        ModelImporter importer = (ModelImporter)assetImporter;
        if (!importer.isReadable)
        {
            importer.isReadable = true;
            // Opcional: puedes agregar un log para saber que se aplic�
            // Debug.Log("Read/Write Enabled autom�ticamente en: " + assetPath);
        }
    }
}