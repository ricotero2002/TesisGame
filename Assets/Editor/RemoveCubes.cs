// Assets/Editor/RemoveCubes.cs
using UnityEngine;
using UnityEditor;
using System.Linq;

public static class RemoveCubes
{
    [MenuItem("Tools/Remove All Primitive Cubes")]
    public static void RemoveAllPrimitiveCubes()
    {
        // Busca todos los MeshFilter en la escena
        var allFilters = Object.FindObjectsOfType<MeshFilter>();
        // Filtra los que usan la mesh "Cube"
        var cubeFilters = allFilters
            .Where(mf => mf.sharedMesh != null && mf.sharedMesh.name == "Cube")
            .ToArray();

        int removed = 0;
        foreach (var mf in cubeFilters)
        {
            // Borramos el GameObject completo
            Undo.DestroyObjectImmediate(mf.gameObject);
            removed++;
        }

        Debug.Log($"Removed {removed} primitive Cube(s).");
    }
}
