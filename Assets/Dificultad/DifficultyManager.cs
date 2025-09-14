using System;
using System.Linq;
using UnityEngine;

[Serializable]
public class DifficultyParams
{
    public int numItems;
    public int memoriseTimeMs;
    public int swap; // 0 = noSwap, 1 = Swap (copiado del LevelEntry)
    public int poolSimilarityInt; // 0 = hard, 1 = easy
    public int level; // 1..N
    public float D; // score (0..1)
    public string categoria; // categoría de ítems (opcional)
}

public class DifficultyManager : MonoBehaviour, IDifficultyManager
{
    [Header("Config")]
    public DifficultyLevelsConfig levelsConfig; // setear en el inspector o cargar por código

    [Header("Modo simple")]
    public bool useManualLevel = true;         // si true usa manualLevel, si false usa overrideD si >=0
    [Range(1, 20)] public int manualLevel = 1; // índice (1..N) — solo para pruebas
    [Range(-1f, 1f)] public float overrideD = -1f; // si >=0 fuerza usar este D (se buscará en levelsConfig)

    [Header("Rangos (para fallback cuando no hay config)")]
    public int minNumItems = 2;
    public int maxNumItems = 12;
    public int minMemMs = 1;
    public int maxMemMs = 10;

    public DifficultyParams currentParams = new DifficultyParams();

    // allowed set de numItems pares
    private readonly int[] allowedNumItems = new int[] { 2, 4, 6, 8, 10, 12 };

    void Start()
    {
        ApplyCurrentMode();
    }


    // ---------- API principal ----------
    public void ApplyCurrentMode()
    {
        // Prioridad: overrideD (si >=0) > manualLevel (si useManualLevel) > first level in config (si existe) > fallback D=0.5
        if (overrideD >= 0f)
        {
            SetDifficultyByD(overrideD);
        }
        else if (useManualLevel && levelsConfig != null && levelsConfig.levels.Count > 0)
        {
            SetDifficultyByLevel(manualLevel);
        }
        else if (levelsConfig != null && levelsConfig.levels.Count > 0)
        {
            // por defecto usamos el primer level targetD
            SetDifficultyByD(levelsConfig.levels[0].targetD);
        }
        else
        {
            // fallback: mapear D=0.5 a parámetros por defecto
            SetDifficultyByD(0.5f);
        }
    }

    // Busca en levelsConfig el LevelEntry cuyo targetD sea el más cercano al D provisto.
    // Si no hay config, hace map directo desde D a parámetros.
    public void SetDifficultyByD(float D)
    {
        D = Mathf.Clamp01(D);

        if (levelsConfig != null && levelsConfig.levels != null && levelsConfig.levels.Count > 0)
        {
            // buscar el entry con targetD más cercano
            int bestIdx = 0;
            float bestDist = Mathf.Abs(D - levelsConfig.levels[0].targetD);
            for (int i = 1; i < levelsConfig.levels.Count; i++)
            {
                float dist = Mathf.Abs(D - levelsConfig.levels[i].targetD);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestIdx = i;
                }
            }

            var entry = levelsConfig.levels[bestIdx];
            ApplyLevelEntryToCurrent(entry, bestIdx + 1); // nivel = index+1
            return;
        }

        // fallback: sin config, mapear D a params (numItems y memoriseTime) directamente
        currentParams = MapDToParams(D);
    }

    // Directo por índice de nivel (1..N) usando levelsConfig.
    public void SetDifficultyByLevel(int level)
    {
        if (levelsConfig == null || levelsConfig.levels == null || levelsConfig.levels.Count < 1)
        {
            Debug.LogWarning("[DifficultyManager] No hay levelsConfig para SetDifficultyByLevel.");
            return;
        }
        
        int clamped = Mathf.Clamp(level, 1, levelsConfig.levels.Count);
        var entry = levelsConfig.levels[clamped - 1];
        Debug.Log("[DifficultyManager] Nombre del nuvel " + entry.name);
        ApplyLevelEntryToCurrent(entry, clamped);
    }

    // Aplica un LevelEntry a currentParams (usa exactamente los campos que pediste)
    void ApplyLevelEntryToCurrent(DifficultyLevelsConfig.LevelEntry entry, int levelIndex)
    {
        currentParams.level = levelIndex;
        currentParams.D = Mathf.Clamp01(entry.targetD);
        currentParams.numItems = SnapToAllowedNumItems(entry.numItems);
        currentParams.memoriseTimeMs = Mathf.Clamp(entry.memoriseTimeMs, minMemMs, maxMemMs);
        currentParams.swap = Mathf.Clamp(entry.swap, 0, 1);
        currentParams.poolSimilarityInt = Mathf.Clamp(entry.poolSimilarityInt, 0, 1);
        currentParams.categoria = entry.categoria;
    }

    // Map D -> params si no hay config (simple)
    DifficultyParams MapDToParams(float D)
    {
        DifficultyParams p = new DifficultyParams();
        p.D = Mathf.Clamp01(D);
        // level asignado de forma aproximada (5 niveles default)
        if (D <= 0.20f) p.level = 1;
        else if (D <= 0.40f) p.level = 2;
        else if (D <= 0.60f) p.level = 3;
        else if (D <= 0.80f) p.level = 4;
        else p.level = 5;

        int desired = Mathf.RoundToInt(Mathf.Lerp(minNumItems, maxNumItems, p.D));
        p.numItems = SnapToAllowedNumItems(desired);
        p.memoriseTimeMs = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(maxMemMs, minMemMs, p.D)), minMemMs, maxMemMs);
        p.swap = (p.D > 0.6f) ? 1 : 0; // heurística simple si no hay config
        p.poolSimilarityInt = (p.D > 0.5f) ? 0 : 1; // 0=hard,1=easy
        p.categoria = "Any"; // default
        return p;
    }

    int SnapToAllowedNumItems(int desired)
    {
        int best = allowedNumItems[0];
        int bestDiff = Math.Abs(desired - best);
        foreach (var v in allowedNumItems)
        {
            int d = Math.Abs(desired - v);
            if (d < bestDiff) { bestDiff = d; best = v; }
        }
        return best;
    }

    // ---------- Implementación IDifficultyManager ----------

    public void Actualizar()
    {
        ApplyCurrentMode();
    }
    public int GetColumns()
    {
        int cols = currentParams.numItems / 2;
        return Mathf.Max(1, cols);
    }

    public float GetMemoriseTime()
    {
        Debug.Log("[DificultyManager] MemoriseTimeMs: " + currentParams.memoriseTimeMs);
        return currentParams.memoriseTimeMs;
    }

    public float GetTimeBetweenPhases()
    {
        return Mathf.Max(0.2f, currentParams.memoriseTimeMs * 0.3f);
    }

    public IMovementStrategy GetMovementStrategy()
    {
        if (currentParams.swap == 0) return new CommonMovementStrategy();
        return new SwapMovementStrategy();
    }

    public int GetHardEasyMode()
    {
        return currentParams.level;
    }

    public string GetCategory()
    {
        return currentParams.categoria;
    }
}
