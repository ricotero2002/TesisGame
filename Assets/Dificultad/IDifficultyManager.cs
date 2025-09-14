using UnityEngine;

/// <summary>
/// Interfaz simple para obtener parámetros de la "dificultad" / configuración de la sala.
/// Implementar como MonoBehaviour (para poder asignarlo en el Inspector).
/// </summary>
public interface IDifficultyManager
{
    void Actualizar();
    int GetColumns();
    float GetMemoriseTime();
    float GetTimeBetweenPhases();
    IMovementStrategy GetMovementStrategy();

    int GetHardEasyMode();

    string GetCategory();
}
