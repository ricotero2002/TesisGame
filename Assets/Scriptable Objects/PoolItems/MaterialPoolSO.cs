
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "MaterialPool", menuName = "Scriptable Objects/MaterialPool")]
public class MaterialPoolSO : ScriptableObject
{
    [Tooltip("Aqu� arrastra todos los prefabs de tu pool (por ejemplo, distintos modelos de c�psulas, estatuas, pinturas, etc.).")]
    public List<Material> artPrefabs = new List<Material>();

    // Lista de �ndices disponibles para GetRandomPrefab() (no serializada, se usa s�lo en tiempo de ejecuci�n)
    [System.NonSerialized] private List<int> availableIndices;

    /// <summary>
    /// Debe llamarse al iniciar la sesi�n (por ejemplo, en Awake() o Start() de quien use esta pool)
    /// para �resetear� la l�gica y que todos los prefabs vuelvan a estar disponibles.
    /// </summary>
    public void ResetPool()
    {
        // (Re)llenamos availableIndices con todos los �ndices [0 .. artPrefabs.Count-1]
        availableIndices = new List<int>(artPrefabs.Count);
        for (int i = 0; i < artPrefabs.Count; i++)
        {
            availableIndices.Add(i);
        }
    }

    /// <summary>
    /// Devuelve un prefab aleatorio de la lista, pero sin repetir mientras haya a�n disponibles.
    /// Si ya no quedan, vuelve a �resetear� internamente y comienza de nuevo.
    /// </summary>
    /// <returns>Un GameObject (prefab) tomado de artPrefabs.</returns>
    public Material GetRandomPrefab()
    {
        if (artPrefabs == null || artPrefabs.Count == 0)
        {
            Debug.LogWarning("ArtPoolSO.GetRandomPrefab: no hay prefabs cargados en artPrefabs.");
            return null;
        }

        // Si no hemos inicializado o ya vaciamos la lista de �ndices, reseteamos
        if (availableIndices == null || availableIndices.Count == 0)
        {
            ResetPool();
        }

        // Elegimos un �ndice al azar dentro de availableIndices
        int randomListIndex = Random.Range(0, availableIndices.Count);
        int prefabIndex = availableIndices[randomListIndex];

        // Lo quitamos de la lista para no repetirlo en esta �vuelta�
        availableIndices.RemoveAt(randomListIndex);

        // Devolvemos el prefab en artPrefabs[prefabIndex]
        return artPrefabs[prefabIndex];
    }
}

