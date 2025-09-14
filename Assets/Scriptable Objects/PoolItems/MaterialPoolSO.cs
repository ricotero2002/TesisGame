
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "MaterialPool", menuName = "Scriptable Objects/MaterialPool")]
public class MaterialPoolSO : ScriptableObject
{
    [Tooltip("Aquí arrastra todos los prefabs de tu pool (por ejemplo, distintos modelos de cápsulas, estatuas, pinturas, etc.).")]
    public List<Material> artPrefabs = new List<Material>();

    // Lista de índices disponibles para GetRandomPrefab() (no serializada, se usa sólo en tiempo de ejecución)
    [System.NonSerialized] private List<int> availableIndices;

    /// <summary>
    /// Debe llamarse al iniciar la sesión (por ejemplo, en Awake() o Start() de quien use esta pool)
    /// para “resetear” la lógica y que todos los prefabs vuelvan a estar disponibles.
    /// </summary>
    public void ResetPool()
    {
        // (Re)llenamos availableIndices con todos los índices [0 .. artPrefabs.Count-1]
        availableIndices = new List<int>(artPrefabs.Count);
        for (int i = 0; i < artPrefabs.Count; i++)
        {
            availableIndices.Add(i);
        }
    }

    /// <summary>
    /// Devuelve un prefab aleatorio de la lista, pero sin repetir mientras haya aún disponibles.
    /// Si ya no quedan, vuelve a “resetear” internamente y comienza de nuevo.
    /// </summary>
    /// <returns>Un GameObject (prefab) tomado de artPrefabs.</returns>
    public Material GetRandomPrefab()
    {
        if (artPrefabs == null || artPrefabs.Count == 0)
        {
            Debug.LogWarning("ArtPoolSO.GetRandomPrefab: no hay prefabs cargados en artPrefabs.");
            return null;
        }

        // Si no hemos inicializado o ya vaciamos la lista de índices, reseteamos
        if (availableIndices == null || availableIndices.Count == 0)
        {
            ResetPool();
        }

        // Elegimos un índice al azar dentro de availableIndices
        int randomListIndex = Random.Range(0, availableIndices.Count);
        int prefabIndex = availableIndices[randomListIndex];

        // Lo quitamos de la lista para no repetirlo en esta “vuelta”
        availableIndices.RemoveAt(randomListIndex);

        // Devolvemos el prefab en artPrefabs[prefabIndex]
        return artPrefabs[prefabIndex];
    }
}

