// DifficultySetsTesterWindow.cs
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

public class DifficultySetsTesterWindow : EditorWindow
{
    private ArtPoolSO poolSO;
    private string jsonPath = "Assets/Renders/difficulty_sets_with_scores.json";
    private int size = 6;
    private string difficulty = "hard";
    private string poolName = ""; // opcional (category)
    private string subpoolId = "";

    [MenuItem("Tools/Difficulty Sets/Tester")]
    public static void ShowWindow() => GetWindow<DifficultySetsTesterWindow>("DifficultySets Tester");

    private void OnGUI()
    {
        GUILayout.Label("Difficulty Sets Loader Tester", EditorStyles.boldLabel);
        poolSO = (ArtPoolSO)EditorGUILayout.ObjectField("ArtPoolSO", poolSO, typeof(ArtPoolSO), false);
        jsonPath = EditorGUILayout.TextField("JSON Path", jsonPath);
        size = EditorGUILayout.IntField("Size", size);
        difficulty = EditorGUILayout.TextField("Difficulty (easy/hard)", difficulty);
        poolName = EditorGUILayout.TextField("Pool (category) optional", poolName);
        subpoolId = EditorGUILayout.TextField("SubpoolId optional", subpoolId);

        if (GUILayout.Button("Load and Print groups"))
        {
            LoadAndPrint();
        }
    }

    private void LoadAndPrint()
    {
        if (!File.Exists(jsonPath))
        {
            Debug.LogError($"DifficultySetsTesterWindow: JSON not found: {jsonPath}");
            return;
        }
        var root = DifficultySetsLoader.LoadFromFile(jsonPath);
        if (root == null)
        {
            Debug.LogError("Failed to parse difficulty JSON");
            return;
        }

        var groups = DifficultySetsLoader.GetGroupsByParams(root, size, difficulty, poolName, string.IsNullOrWhiteSpace(subpoolId) ? null : subpoolId);
        Debug.Log($"Found {(groups?.Count ?? 0)} groups for size={size} difficulty={difficulty} pool={poolName} subpool={subpoolId}");
        int i = 1;
        foreach (var g in groups)
        {
            Debug.Log($"Group #{i++}: {string.Join(", ", g)}");
            // print max prefab size using poolSO (if assigned)
            if (poolSO != null)
            {
                var mx = DifficultySetsLoader.GetMaxPrefabSizeForGroup(poolSO, g, editorMode: true);
                Debug.Log($"  -> Max prefab size for this group: {mx}");
            }
        }

        // also try random pick
        var randomGroup = DifficultySetsLoader.GetRandomGroupByParams(root, size, difficulty, poolName, subpoolId, new System.Random());
        if (randomGroup != null)
        {
            Debug.Log($"Random group chosen: {string.Join(", ", randomGroup)}");
            if (poolSO != null)
            {
                var mx = DifficultySetsLoader.GetMaxPrefabSizeForGroup(poolSO, randomGroup, editorMode: true);
                Debug.Log($"  -> Max prefab size for random group: {mx}");
            }
        }
    }
}
