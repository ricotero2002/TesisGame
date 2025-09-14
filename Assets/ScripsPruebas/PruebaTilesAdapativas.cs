using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class AdaptiveTileTester : MonoBehaviour
{
    [Tooltip("Pool de arte a probar")]
    public ArtPoolSO artPool;

    [Tooltip("Tiempo total entre movimientos (segundos)")]
    public float moveInterval = 2f;

    [Tooltip("Factor k: longitud de cada cuadrante = k × smallCellSize")]
    public float quadrantScaleFactor = 2f;

    [Tooltip("Número de sub‑celdas por eje dentro de cada cuadrante (n×n)")]
    public int cellsPerQuadrantAxis = 2;

    private List<Vector3> allPositions;
    private GameObject movingArt;
    private float smallCellSize;
    private float quadrantSize;
    private float tileSize;

    void Start()
    {
        if (artPool == null)
        {
            Debug.LogError("Asigná un ArtPoolSO para AdaptiveTileTester.");
            return;
        }
        BuildTile();
        StartCoroutine(MoveRoutine());
    }

    void BuildTile()
    {
        // 1) smallCellSize: promedio XZ de colliders
        Vector3 avg = artPool.GetAverageSize();
        smallCellSize = (avg.x + avg.z) / 2f;

        // 2) quadrantSize y tileSize
        quadrantSize = smallCellSize * quadrantScaleFactor;
        tileSize = quadrantSize * 2f;

        // 3) Creamos contenedor “TileGroup” a escala 1
        var group = new GameObject("TileGroup");
        group.transform.parent = transform;
        group.transform.localPosition = Vector3.zero;
        group.transform.localRotation = Quaternion.identity;
        group.transform.localScale = Vector3.one;

        // 4) Dentro del group, creamos el Plane
        var tile = GameObject.CreatePrimitive(PrimitiveType.Plane);
        tile.name = "AdaptiveTile";
        tile.transform.parent = group.transform;
        // SOLO escala XZ
        tile.transform.localScale = new Vector3(tileSize / 10f, 1f, tileSize / 10f);
        tile.transform.localPosition = Vector3.zero;

        // 5) Generamos allPositions en el espacio LOCAL de group
        allPositions = new List<Vector3>();
        float halfTile = tileSize / 2f;
        float step = smallCellSize;
        for (int row = 0; row < 4; row++)
        {
            for (int col = 0; col < 4; col++)
            {
                float x = -halfTile + step * (col + 0.5f);
                float z = -halfTile + step * (row + 0.5f);
                var localPos = new Vector3(x, 0, z);
                allPositions.Add(localPos);

                // 6) Instanciamos el marker como hijo de group
                var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                marker.name = $"Marker_{row}_{col}";
                marker.transform.parent = group.transform;
                float s = smallCellSize * 0.1f;
                marker.transform.localScale = Vector3.one * s;
                marker.transform.localPosition = localPos + Vector3.up * (s / 2f);
                var rend = marker.GetComponent<Renderer>();
                rend.sharedMaterial = new Material(Shader.Find("Standard"));
                rend.sharedMaterial.color = Color.red;
            }
        }

        // 7) Instanciamos movingArt como hijo de group TAMBIÉN
        var prefab = artPool.GetRandomPrefab();
        movingArt = Instantiate(prefab, group.transform);
        movingArt.name = "MovingArt";
        // No tocamos su escala

        // Ajuste Y para que apoye en Z=0 de group
        var rends = movingArt.GetComponentsInChildren<Renderer>();
        if (rends.Length > 0)
        {
            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            float lowY = b.center.y - b.extents.y;
            movingArt.transform.localPosition = new Vector3(0, -lowY, 0);
        }
    }


    IEnumerator MoveRoutine()
    {
        if (allPositions == null || movingArt == null) yield break;

        int idx = 0;
        while (true)
        {
            // Teletransporta sin lerp:
            Vector3 target = allPositions[idx];
            target.y += movingArt.transform.localPosition.y;
            movingArt.transform.localPosition = target;
            Debug.Log($"Movido a celda #{idx}");

            // Avanza y envuelve
            idx = (idx + 1) % allPositions.Count;

            // Espera
            yield return new WaitForSeconds(moveInterval);
        }
    }


}
