using UnityEngine;

/// <summary>
/// Implementación de IDifficultyManager que usa un DemoDifficultySO para devolver parámetros.
/// Attach a cualquier GameObject en escena y referencia el SO desde el inspector.
/// </summary>
public class DemoDifficultyManager : MonoBehaviour, IDifficultyManager
{
    public DemoDifficultySO demoData;

    public void Actualizar()
    {
        // No hace nada, pero podría recargar datos si se quiere.
    }
    public int GetColumns()
    {
        return demoData != null ? Mathf.Max(1, demoData.columns) : 4;
    }

    public float GetMemoriseTime()
    {
        return demoData != null ? Mathf.Max(0.1f, demoData.memoriseTime) : 8f;
    }

    public float GetTimeBetweenPhases()
    {
        return demoData != null ? Mathf.Max(0f, demoData.timeBetweenPhases) : 2f;
    }

    public IMovementStrategy GetMovementStrategy()
    {
        int m = demoData != null ? demoData.movementMode : 0;
        if (m == 1) return new SwapMovementStrategy();
        return new CommonMovementStrategy();
    }

    public int GetHardEasyMode()
    {
        return demoData != null ? demoData.hard_easy : 0;
    }

    public string GetCategory()
    {
        return demoData != null ? demoData.category : "Estatuas";
    }
}
