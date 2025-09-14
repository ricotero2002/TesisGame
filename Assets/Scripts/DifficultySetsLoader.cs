// DifficultySetsLoader.cs
// Utilities to load difficulty_sets JSON and query groups by params.
// Requires Newtonsoft.Json (JsonConvert).
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using Newtonsoft.Json;

public static class DifficultySetsLoader
{
    [Serializable]
    public class DifficultySetEntry
    {
        public int size;
        public string difficulty;
        public List<string> group;
        public float intra_mean;
        public float hardness_pct;
        public float easiness_pct;
        public string viz_image;
    }

    [Serializable]
    public class SubpoolDifficulty
    {
        public string subpoolId;
        public List<DifficultySetEntry> sets = new List<DifficultySetEntry>();
    }

    [Serializable]
    public class CategoryDifficulty
    {
        public string category;
        public List<SubpoolDifficulty> subpools = new List<SubpoolDifficulty>();
    }

    [Serializable]
    public class DifficultyRoot
    {
        public List<CategoryDifficulty> categories = new List<CategoryDifficulty>();
    }

    public static DifficultyRoot LoadFromFile(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        try
        {
            if (!File.Exists(path))
            {
                Debug.LogWarning($"DifficultySetsLoader.LoadFromFile: file not found: {path}");
                return null;
            }
            string json = File.ReadAllText(path);
            var root = JsonConvert.DeserializeObject<DifficultyRoot>(json);
            if (root == null) Debug.LogWarning("DifficultySetsLoader: parsed root is null");
            return root;
        }
        catch (Exception ex)
        {
            Debug.LogError($"DifficultySetsLoader.LoadFromFile exception: {ex.Message}\n{ex.StackTrace}");
            return null;
        }
    }

    // Priority search:
    // 1) category(pool) + size + difficulty
    // 2) any category + size + difficulty
    // 3) category + size (any difficulty)
    // 4) any category + size (any difficulty)
    // optional subpoolId filters results (applied on top of selection).
    public static List<List<string>> GetGroupsByParams(DifficultyRoot root, int size, string difficulty = null, string pool = null, string subpoolId = null)
    {
        List<List<string>> result = new List<List<string>>();
        if (root == null) return result;

        Func<CategoryDifficulty, IEnumerable<DifficultySetEntry>> getSetsInCategory = (cat) =>
        {
            if (cat == null) return Enumerable.Empty<DifficultySetEntry>();
            return cat.subpools.SelectMany(sp => sp.sets.Select(s => {
                // attach subpool id info in new object? simple filter later
                return new DifficultySetEntry
                {
                    size = s.size,
                    difficulty = s.difficulty,
                    group = s.group,
                    intra_mean = s.intra_mean,
                    hardness_pct = s.hardness_pct,
                    easiness_pct = s.easiness_pct,
                    viz_image = s.viz_image
                };
            }));
        };

        // helper find sets by predicate, with optional pool/subpool filtering
        Func<IEnumerable<DifficultySetEntry>, IEnumerable<List<string>>> collectGroups = (sets) =>
        {
            var q = sets.Where(s => s.size == size);
            if (!string.IsNullOrEmpty(difficulty)) q = q.Where(s => string.Equals(s.difficulty, difficulty, StringComparison.OrdinalIgnoreCase));
            // if subpoolId provided, filter groups where subpoolId matches any subpool that contains that exact group (we will just attempt to match on containing subpool later)
            return q.Select(s => s.group).Where(g => g != null && g.Count > 0);
        };

        // 1) exact pool + size + difficulty
        if (!string.IsNullOrEmpty(pool))
        {
            var cat = root.categories.FirstOrDefault(c => string.Equals(c.category, pool, StringComparison.OrdinalIgnoreCase));
            if (cat != null)
            {
                var sets = cat.subpools.SelectMany(sp => sp.sets);
                var pick = sets.Where(s => s.size == size && (string.IsNullOrEmpty(difficulty) || string.Equals(s.difficulty, difficulty, StringComparison.OrdinalIgnoreCase)));
                if (!string.IsNullOrEmpty(subpoolId)) pick = pick.Where(s => cat.subpools.Any(sp => sp.subpoolId == subpoolId && sp.sets.Contains(s)));
                result.AddRange(pick.Select(s => s.group).Where(g => g != null));
                if (result.Count > 0) return result;
            }
        }

        // 2) any category + size + difficulty
        {
            var pick = root.categories.SelectMany(c => c.subpools.SelectMany(sp => sp.sets))
                        .Where(s => s.size == size && (string.IsNullOrEmpty(difficulty) || string.Equals(s.difficulty, difficulty, StringComparison.OrdinalIgnoreCase)));
            if (!string.IsNullOrEmpty(subpoolId))
                pick = pick.Where(s => root.categories.SelectMany(c => c.subpools).Any(sp => sp.subpoolId == subpoolId && sp.sets.Contains(s)));
            result.AddRange(pick.Select(s => s.group).Where(g => g != null));
            if (result.Count > 0) return result;
        }

        // 3) pool + size (any difficulty)
        if (!string.IsNullOrEmpty(pool))
        {
            var cat = root.categories.FirstOrDefault(c => string.Equals(c.category, pool, StringComparison.OrdinalIgnoreCase));
            if (cat != null)
            {
                var pick = cat.subpools.SelectMany(sp => sp.sets).Where(s => s.size == size);
                if (!string.IsNullOrEmpty(subpoolId)) pick = pick.Where(s => cat.subpools.Any(sp => sp.subpoolId == subpoolId && sp.sets.Contains(s)));
                result.AddRange(pick.Select(s => s.group).Where(g => g != null));
                if (result.Count > 0) return result;
            }
        }

        // 4) any category + size (any difficulty)
        {
            var pick = root.categories.SelectMany(c => c.subpools.SelectMany(sp => sp.sets)).Where(s => s.size == size);
            if (!string.IsNullOrEmpty(subpoolId))
                pick = pick.Where(s => root.categories.SelectMany(c => c.subpools).Any(sp => sp.subpoolId == subpoolId && sp.sets.Contains(s)));
            result.AddRange(pick.Select(s => s.group).Where(g => g != null));
            return result;
        }
    }

    // Shuffle and pick one candidate group (returns null if none)
    public static List<string> GetRandomGroupByParams(DifficultyRoot root, int size, string difficulty = null, string pool = null, string subpoolId = null, System.Random rng = null)
    {
        rng = rng ?? new System.Random();
        var groups = GetGroupsByParams(root, size, difficulty, pool, subpoolId);
        if (groups == null || groups.Count == 0) return null;
        // shuffle
        var shuffled = groups.OrderBy(x => rng.Next()).ToList();
        return shuffled[0];
    }

    // Compute max prefab bounds (x,y,z) for a list of object ids,
    // using ArtPoolSO.ResolvePrefabForObjectId to get GameObjects.
    // artPool may be null; if null we attempt Resources.Load.
    public static Vector3 GetMaxPrefabSizeForGroup(ArtPoolSO artPool, List<string> groupObjectIds, bool editorMode = true)
    {
        Vector3 maxSize = Vector3.zero;
        if (groupObjectIds == null || groupObjectIds.Count == 0) return Vector3.one;

        foreach (var oid in groupObjectIds)
        {
            GameObject prefab = null;
            try
            {
                if (artPool != null)
                {
                    prefab = artPool.ResolvePrefabForObjectId(oid, editorMode);
                }
            }
            catch { prefab = null; }

            if (prefab == null)
            {
                // fallback: try Resources.Load by last path segment
                var nameOnly = oid.Split(new char[] { '/', '\\' }).Last();
                prefab = Resources.Load<GameObject>(nameOnly);
            }

            if (prefab == null) continue;

#if UNITY_EDITOR
            // Instantiate temporarily (editor-safe)
            var temp = UnityEditor.PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (temp == null) continue;
            try
            {
                temp.hideFlags = HideFlags.HideAndDontSave;
                var rends = temp.GetComponentsInChildren<Renderer>();
                if (rends != null && rends.Length > 0)
                {
                    Bounds b = rends[0].bounds;
                    for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                    var s = b.size;
                    maxSize.x = Mathf.Max(maxSize.x, s.x);
                    maxSize.y = Mathf.Max(maxSize.y, s.y);
                    maxSize.z = Mathf.Max(maxSize.z, s.z);
                }
                else
                {
                    var col = temp.GetComponentInChildren<Collider>();
                    if (col != null)
                    {
                        var s = col.bounds.size;
                        maxSize.x = Mathf.Max(maxSize.x, s.x);
                        maxSize.y = Mathf.Max(maxSize.y, s.y);
                        maxSize.z = Mathf.Max(maxSize.z, s.z);
                    }
                }
            }
            finally
            {
                GameObject.DestroyImmediate(temp);
            }
#else
            // runtime - instantiate normally
            var temp = GameObject.Instantiate(prefab);
            try
            {
                var rends = temp.GetComponentsInChildren<Renderer>();
                if (rends != null && rends.Length > 0)
                {
                    Bounds b = rends[0].bounds;
                    for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                    var s = b.size;
                    maxSize.x = Mathf.Max(maxSize.x, s.x);
                    maxSize.y = Mathf.Max(maxSize.y, s.y);
                    maxSize.z = Mathf.Max(maxSize.z, s.z);
                }
                else
                {
                    var col = temp.GetComponentInChildren<Collider>();
                    if (col != null)
                    {
                        var s = col.bounds.size;
                        maxSize.x = Mathf.Max(maxSize.x, s.x);
                        maxSize.y = Mathf.Max(maxSize.y, s.y);
                        maxSize.z = Mathf.Max(maxSize.z, s.z);
                    }
                }
            }
            finally
            {
                GameObject.Destroy(temp);
            }
#endif
        }

        if (maxSize == Vector3.zero) return Vector3.one;
        return maxSize;
    }

    // Busca el subpoolId que mejor corresponde al chosenGroup.
    // Strategy:
    // 1) exact match (same size, same set of ids) within same pool (if poolHint provided) and difficultyHint preferred
    // 2) exact match anywhere
    // 3) set that contains all chosenGroup (subset) preferring same size/difficulty/pool
    // 4) fallback null
    public static string FindSubpoolIdForGroup(DifficultyRoot root, List<string> chosenGroup, string difficultyHint = null, string poolHint = null)
    {
        if (root == null || chosenGroup == null || chosenGroup.Count == 0) return null;

        // normalize chosen set for comparisons
        var chosenSet = new HashSet<string>(chosenGroup);

        // helper to test equality disregarding order
        Func<List<string>, bool> sameSet = (list) =>
        {
            if (list == null) return false;
            if (list.Count != chosenSet.Count) return false;
            return new HashSet<string>(list).SetEquals(chosenSet);
        };

        // First pass: exact matches, prefer same pool + difficulty
        // 1.a pool + exact + difficulty
        if (!string.IsNullOrEmpty(poolHint))
        {
            var cat = root.categories.FirstOrDefault(c => string.Equals(c.category, poolHint, StringComparison.OrdinalIgnoreCase));
            if (cat != null)
            {
                foreach (var sp in cat.subpools ?? Enumerable.Empty<SubpoolDifficulty>())
                {
                    foreach (var s in sp.sets ?? Enumerable.Empty<DifficultySetEntry>())
                    {
                        if (!string.IsNullOrEmpty(difficultyHint) && !string.Equals(s.difficulty, difficultyHint, StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (sameSet(s.group))
                            return sp.subpoolId;
                    }
                }
            }
        }

        // 1.b any category exact + difficulty
        foreach (var cat in root.categories ?? Enumerable.Empty<CategoryDifficulty>())
        {
            foreach (var sp in cat.subpools ?? Enumerable.Empty<SubpoolDifficulty>())
            {
                foreach (var s in sp.sets ?? Enumerable.Empty<DifficultySetEntry>())
                {
                    if (!string.IsNullOrEmpty(difficultyHint) && !string.Equals(s.difficulty, difficultyHint, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (sameSet(s.group))
                        return sp.subpoolId;
                }
            }
        }

        // 2) exact match ignoring difficulty (prefer pool if given)
        if (!string.IsNullOrEmpty(poolHint))
        {
            var cat = root.categories.FirstOrDefault(c => string.Equals(c.category, poolHint, StringComparison.OrdinalIgnoreCase));
            if (cat != null)
            {
                foreach (var sp in cat.subpools ?? Enumerable.Empty<SubpoolDifficulty>())
                {
                    foreach (var s in sp.sets ?? Enumerable.Empty<DifficultySetEntry>())
                    {
                        if (sameSet(s.group)) return sp.subpoolId;
                    }
                }
            }
        }
        foreach (var cat in root.categories ?? Enumerable.Empty<CategoryDifficulty>())
        {
            foreach (var sp in cat.subpools ?? Enumerable.Empty<SubpoolDifficulty>())
            {
                foreach (var s in sp.sets ?? Enumerable.Empty<DifficultySetEntry>())
                {
                    if (sameSet(s.group)) return sp.subpoolId;
                }
            }
        }

        // 3) subset / contains: chosenGroup is subset of set.group (useful if group order differs or set contains extras)
        // prefer same difficulty/pool if provided
        // 3.a pool + difficulty
        if (!string.IsNullOrEmpty(poolHint))
        {
            var cat = root.categories.FirstOrDefault(c => string.Equals(c.category, poolHint, StringComparison.OrdinalIgnoreCase));
            if (cat != null)
            {
                foreach (var sp in cat.subpools ?? Enumerable.Empty<SubpoolDifficulty>())
                {
                    foreach (var s in sp.sets ?? Enumerable.Empty<DifficultySetEntry>())
                    {
                        if (!string.IsNullOrEmpty(difficultyHint) && !string.Equals(s.difficulty, difficultyHint, StringComparison.OrdinalIgnoreCase))
                            continue;
                        var setHash = new HashSet<string>(s.group ?? Enumerable.Empty<string>());
                        if (chosenSet.IsSubsetOf(setHash)) return sp.subpoolId;
                    }
                }
            }
        }
        // 3.b any category + difficulty
        foreach (var cat in root.categories ?? Enumerable.Empty<CategoryDifficulty>())
        {
            foreach (var sp in cat.subpools ?? Enumerable.Empty<SubpoolDifficulty>())
            {
                foreach (var s in sp.sets ?? Enumerable.Empty<DifficultySetEntry>())
                {
                    if (!string.IsNullOrEmpty(difficultyHint) && !string.Equals(s.difficulty, difficultyHint, StringComparison.OrdinalIgnoreCase))
                        continue;
                    var setHash = new HashSet<string>(s.group ?? Enumerable.Empty<string>());
                    if (chosenSet.IsSubsetOf(setHash)) return sp.subpoolId;
                }
            }
        }

        // 3.c any category any difficulty (subset)
        foreach (var cat in root.categories ?? Enumerable.Empty<CategoryDifficulty>())
        {
            foreach (var sp in cat.subpools ?? Enumerable.Empty<SubpoolDifficulty>())
            {
                foreach (var s in sp.sets ?? Enumerable.Empty<DifficultySetEntry>())
                {
                    var setHash = new HashSet<string>(s.group ?? Enumerable.Empty<string>());
                    if (chosenSet.IsSubsetOf(setHash)) return sp.subpoolId;
                }
            }
        }

        // not found
        return null;
    }

}
