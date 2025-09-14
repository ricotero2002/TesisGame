// Assets/Editor/TilePositionTesterEditor.cs
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TilePositionTester))]
public class TilePositionTesterEditor : Editor
{
    int multiTileCount = 4;
    bool showAdvanced = false;

    void OnEnable()
    {
        if (multiTileCount <= 0) multiTileCount = 4;
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Tile Tester - Tools", EditorStyles.boldLabel);

        TilePositionTester t = (TilePositionTester)target;

        EditorGUI.BeginDisabledGroup(Application.isPlaying);
        if (GUILayout.Button("Show Prefab In All Positions (single tile, 16 copies)"))
        {
            Undo.RegisterFullObjectHierarchyUndo(t.gameObject, "ShowPrefabInAllPositions");
            t.ShowPrefabInAllPositions();
            EditorUtility.SetDirty(t);
        }

        if (GUILayout.Button("Generate Tile From Current Group (one tile)"))
        {
            Undo.RegisterFullObjectHierarchyUndo(t.gameObject, "GenerateTileFromCurrentGroup");
            t.GenerateTileFromCurrentGroup();
            EditorUtility.SetDirty(t);
        }

        EditorGUILayout.BeginHorizontal();
        multiTileCount = EditorGUILayout.IntField("Multi Tile Count", multiTileCount);
        if (multiTileCount < 1) multiTileCount = 1;
        if (GUILayout.Button("Generate Multiple Tiles"))
        {
            Undo.RegisterFullObjectHierarchyUndo(t.gameObject, "GenerateMultipleTilesFromCurrentGroup");
            t.GenerateMultipleTilesFromCurrentGroup(multiTileCount);
            EditorUtility.SetDirty(t);
        }
        EditorGUILayout.EndHorizontal();

        // NEW: buttons for per-prefab testing
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Per-prefab tests", EditorStyles.boldLabel);
        if (GUILayout.Button("Generate Tiles For All Prefabs (OldMethod)"))
        {
            Undo.RegisterFullObjectHierarchyUndo(t.gameObject, "GenerateTilesForAllPrefabs_OldMethod");
            t.GenerateTilesForAllPrefabs_OldMethod();
            EditorUtility.SetDirty(t);
        }
        if (GUILayout.Button("Generate Tiles For All Prefabs (PrefabPlacer)"))
        {
            Undo.RegisterFullObjectHierarchyUndo(t.gameObject, "GenerateTilesForAllPrefabs_PrefabPlacer");
            t.GenerateTilesForAllPrefabs_PrefabPlacer();
            EditorUtility.SetDirty(t);
        }

        if (GUILayout.Button("Try Setup From DifficultySets (apply group & compute cellSize)"))
        {
            Undo.RegisterFullObjectHierarchyUndo(t.gameObject, "TrySetupFromDifficultySets");
            t.TrySetupFromDifficultySets();
            EditorUtility.SetDirty(t);
        }

        if (GUILayout.Button("Clear All (destroy spawned & tiles)"))
        {
            Undo.RegisterFullObjectHierarchyUndo(t.gameObject, "ClearAll");
            t.ClearAll();
            EditorUtility.SetDirty(t);
        }
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.Space();
        showAdvanced = EditorGUILayout.Foldout(showAdvanced, "Advanced / Debug");
        if (showAdvanced)
        {
            EditorGUILayout.HelpBox("Debug utilities: use them if algo falla. These actions may modify the scene.", MessageType.Info);

            if (GUILayout.Button("Spawn Selected Origin/Target (uses artPoolSO)"))
            {
                Undo.RegisterFullObjectHierarchyUndo(t.gameObject, "SpawnSelected");
                t.SpawnSelected();
                EditorUtility.SetDirty(t);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("DoSwap None"))
            {
                Undo.RegisterFullObjectHierarchyUndo(t.gameObject, "DoSwap_None");
                t.DoSwap(TilePositionTester.SimilarityMode.None);
            }
            if (GUILayout.Button("DoSwap High"))
            {
                Undo.RegisterFullObjectHierarchyUndo(t.gameObject, "DoSwap_High");
                t.DoSwap(TilePositionTester.SimilarityMode.High);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("DoSwap Low"))
            {
                Undo.RegisterFullObjectHierarchyUndo(t.gameObject, "DoSwap_Low");
                t.DoSwap(TilePositionTester.SimilarityMode.Low);
            }
            if (GUILayout.Button("DoSwap Zero"))
            {
                Undo.RegisterFullObjectHierarchyUndo(t.gameObject, "DoSwap_Zero");
                t.DoSwap(TilePositionTester.SimilarityMode.Zero);
            }
            EditorGUILayout.EndHorizontal();
        }

        if (GUI.changed)
        {
            EditorUtility.SetDirty(t);
            serializedObject.ApplyModifiedProperties();
        }
    }
}
