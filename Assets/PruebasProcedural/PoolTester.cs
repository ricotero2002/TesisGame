using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Clase auxiliar que crea una ArtPoolSO “de prueba” con cápsulas de 8 colores distintos,
/// para probar la lógica de la pool sin necesidad de tener prefabs previos en el proyecto.
/// Las cápsulas se crean desactivadas, de modo que no aparezcan en la escena hasta que las 
/// instancies manualmente más adelante.
/// </summary>
public static class PoolTester
{
    /// <summary>
    /// Crea dinámicamente una instancia de ArtPoolSO en memoria (no se guarda en Assets),
    /// la llena con 8 cápsulas primitivas de distintos colores (desactivadas), y la resetea 
    /// para poder usarla.
    /// </summary>
    /// <returns>Una ArtPoolSO lista para usar.</returns>
    public static ArtPoolSO CreateTestCapsulePool()
    {
        // 1) Crear el ScriptableObject en memoria
        ArtPoolSO testPool = ScriptableObject.CreateInstance<ArtPoolSO>();
        testPool.artPrefabs = new List<GameObject>();

        // 2) Definir 8 colores diferentes
        Color[] colores = new Color[]
        {
            Color.red,
            Color.green,
            Color.blue,
            Color.yellow,
            Color.magenta,
            Color.cyan,
            new Color(1f, 0.5f, 0f),   // naranja
            new Color(0.5f, 0f, 1f),    // púrpura
            Color.gray,                    // gris
            new Color(0.5f, 0.25f, 0f),    // marrón
            new Color(0f, 0.7f, 0.3f),     // verde esmeralda
            new Color(1f, 0.8f, 0.2f)      // dorado claro
        };

        // 3) Crear 8 cápsulas (primitives), asignarles color y desactivarlas
        for (int i = 0; i < colores.Length; i++)
        {
            GameObject capsula = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsula.name = $"CapsulaTest_{i}";
            capsula.GetComponent<Renderer>().material.color = colores[i];

            // Quitar collider si no se necesita
            Collider col = capsula.GetComponent<Collider>();
            if (col != null) GameObject.Destroy(col);

            // Desactivar de inmediato para que no aparezca en la escena

            // Añadir la cápsula desactivada a la lista de prefabs de la pool
            testPool.artPrefabs.Add(capsula);
        }

        // 4) Inicializar la lista interna de índices para no repetir
        testPool.ResetPool();

        return testPool;
    }
}
