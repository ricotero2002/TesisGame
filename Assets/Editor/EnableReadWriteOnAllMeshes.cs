using UnityEngine;
using UnityEditor;

public class EnableReadWriteOnAllMeshes
{
    [MenuItem("Tools/Enable Read/Write On All Meshes")]
    public static void EnableReadWrite()
    {
        string[] guids = AssetDatabase.FindAssets("t:Model");
        int count = 0;
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer != null && !importer.isReadable)
            {
                importer.isReadable = true;
                importer.SaveAndReimport();
                count++;
                Debug.Log("Read/Write Enabled: " + path);
            }
        }
        Debug.Log($"Read/Write Enabled en {count} modelos.");
    }
}