using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Difficulty/LevelsConfig", fileName = "DifficultyLevelsConfig")]
public class DifficultyLevelsConfig : ScriptableObject
{
    [Serializable]
    public class LevelEntry
    {
        public string name = "Level";
        [Range(0f, 1.2f)]
        public float targetD = -1f; // si < 0 -> calculado automáticamente si pongo > 1.2 para casos especiales
        public int numItems = 6; // valor preferido (se hará snap al conjunto permitido)
        public int memoriseTimeMs = 3;
        [Tooltip("0 = noSwap, 1 = Swap (valor entero)")]
        public int swap = 0; // 0 = noSwap, 1 = Swap
        [Tooltip("0 = hard, 1 = easy (valor entero)")]
        public int poolSimilarityInt = 1; // 0 = hard, 1 = easy
        [Tooltip("Si true se usan estos parámetros exactamente; si false se calcularán desde targetD")]
        public bool useManualParams = true;
        [Tooltip("Por si queres probar una categoria")]
        public string categoria = "Any";
    }

    public List<LevelEntry> levels = new List<LevelEntry>();

    [Header("Auto-assign")]
    public bool autoAssignTargetD = true;

    private void OnValidate()
    {
        // asigna targetD equiespaciados si autoAssign y hay >=2 niveles
        if (autoAssignTargetD && levels != null && levels.Count > 0)
        {
            int n = levels.Count;
            if (n == 1)
            {
                levels[0].targetD = 0.5f;
            }
            else
            {
                for (int i = 0; i < n; i++)
                {
                    // distribuir en [0,1] excepto que dejemos márgenes si se desea
                    levels[i].targetD = i / (float)(n - 1);
                }
            }
        }
    }
}
