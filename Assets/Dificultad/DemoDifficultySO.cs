using UnityEngine;

[CreateAssetMenu(menuName = "Demo/DifficultyData", fileName = "DemoDifficulty")]
public class DemoDifficultySO : ScriptableObject
{
    [Header("Room defaults (demo)")]
    public int columns = 4;
    public float memoriseTime = 8f;
    public float timeBetweenPhases = 2f;

    [Header("Movement mode (0 = Common, 1 = Swap)")]
    public int movementMode = 0;

    [Header("Hard-Easy mode (0 = Hard, 1 = Easy)")]
    public int hard_easy = 0;

    [Header("CategoriaDeLaPool")]
    public string category = "Estatuas";
}
