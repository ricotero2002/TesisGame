// DifficultySetsLoader.cs
using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class DifficultyGroup
{
    public int size;
    public string difficulty; // "easy" or "hard"
    public string[][] groups;
}

[Serializable]
public class SubpoolJson
{
    public string subpoolId;
    public DifficultyGroup[] sets;
}

[Serializable]
public class DifficultyRoot
{
    public SubpoolJson[] subpools;
}

[CreateAssetMenu(fileName = "DifficultyDB", menuName = "Difficulty/DifficultyDB")]
public class DifficultyDB : ScriptableObject
{
    public List<SubpoolEntry> subpools = new List<SubpoolEntry>();

    [Serializable]
    public class SubpoolEntry
    {
        public string subpoolId;
        public List<DifficultyGroup> sets = new List<DifficultyGroup>();
    }

    public static DifficultyRoot LoadJson(string path)
    {
        if (!File.Exists(path)) { Debug.LogError("Difficulty JSON not found: " + path); return null; }
        string txt = File.ReadAllText(path);
        return JsonUtility.FromJson<DifficultyRoot>(txt);
    }

    public void PopulateFromJson(string jsonPath)
    {
        var root = LoadJson(jsonPath);
        if (root == null) return;
        subpools.Clear();
        foreach (var sp in root.subpools)
        {
            var e = new SubpoolEntry();
            e.subpoolId = sp.subpoolId;
            if (sp.sets != null)
            {
                foreach (var s in sp.sets)
                {
                    e.sets.Add(s);
                }
            }
            subpools.Add(e);
        }
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
        UnityEditor.AssetDatabase.SaveAssets();
#endif
    }

    // Helper: get groups for a subpool, size and difficulty
    public List<string[]> GetGroups(string subpoolId, int size, string difficulty)
    {
        var sp = subpools.Find(x => x.subpoolId == subpoolId);
        if (sp == null) return null;
        var sets = sp.sets.Find(s => s.size == size && s.difficulty == difficulty);
        if (sets == null || sets.groups == null) return null;
        var outl = new List<string[]>();
        foreach (var g in sets.groups) outl.Add(g);
        return outl;
    }
}
