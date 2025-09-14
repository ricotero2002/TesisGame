using UnityEngine;

/// <summary>
/// Componente simple que almacena el objectId dentro del prefab para uso en runtime.
/// </summary>
public class ModelTag : MonoBehaviour
{
    [Tooltip("Identificador estable del objeto: {categoria}/{hash}/{prefabName}")]
    public string objectId;
}