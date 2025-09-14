
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "ArtPoolGroup", menuName = "Scriptable Objects/Art Pool Group")]
public class ArtPoolGroupSO : ScriptableObject
{
    [Tooltip("Arrastra aquí todas las instancias de ArtPoolSO que quieras agrupar.")]
    public List<ArtPoolSO> artPools = new List<ArtPoolSO>();

    // Índices disponibles para GetRandomPool() (no serializado; recreado en Runtime)
    [System.NonSerialized] private List<int> availablePoolIndices;

    /// <summary>
    /// Resetea la lista interna de índices de pools para poder volver a elegirlos sin repetir.
    /// </summary>
    public void ResetGroup()
    {
        availablePoolIndices = new List<int>(artPools.Count);
        for (int i = 0; i < artPools.Count; i++)
        {
            availablePoolIndices.Add(i);
        }
    }

    /// <summary>
    /// Devuelve un ArtPoolSO al azar (sin repetir mientras haya disponibles).
    /// Si ya no quedan, hace ResetGroup() y empieza nuevamente.
    /// </summary>
    /// <returns>Un ArtPoolSO elegido aleatoriamente de la lista artPools.</returns>
    public ArtPoolSO GetRandomPool()
    {
        if (artPools == null || artPools.Count == 0)
        {
            Debug.LogWarning("ArtPoolGroupSO.GetRandomPool: no hay pools cargadas en artPools.");
            return null;
        }

        // Si no hay indices o no se ha inicializado, reseteamos
        if (availablePoolIndices == null || availablePoolIndices.Count == 0)
        {
            ResetGroup();
        }

        int randomListIndex = Random.Range(0, availablePoolIndices.Count);
        int poolIndex = availablePoolIndices[randomListIndex];
        availablePoolIndices.RemoveAt(randomListIndex);

        return artPools[poolIndex];
    }
}