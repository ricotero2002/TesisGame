using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using System.Collections;
// IMovementStrategy.cs
public interface IMovementStrategy
{
    void Move(TileController[] tiles, Light[] ligth);

    public static void moverObjetos(TileController[] tiles)
    {
        int total = tiles.Count();
        int baseCount = total / 4;
        int remainder = total % 4;

        int noneCount = baseCount;
        int highCount = baseCount;
        int lowCount = baseCount;
        int zeroCount = baseCount + remainder; // remainder -> Zero

        Debug.Log($"[DoSwapAll] total {total} => None:{noneCount} High:{highCount} Low:{lowCount} Zero:{zeroCount}");

        // indices de tiles
        List<int> allIdx = new List<int>();
        for (int i = 0; i < tiles.Count(); i++) allIdx.Add(i);

        // separar tiles que ya tienen spawn en esquina vs no
        List<int> cornerTiles = new List<int>();
        List<int> nonCornerTiles = new List<int>();
        for (int i = 0; i < tiles.Count(); i++)
        {
            var inst = tiles[i];
            if (IsCornerIndex(inst.GetCurrentIndex())) cornerTiles.Add(i);
            else nonCornerTiles.Add(i);
        }

        // Reservar tiles para Zero: preferir las que ya están en esquina, y si faltan convertir algunas no-corner
        List<int> zeroAssigned = new List<int>();
        // tomar de cornerTiles primero
        while (zeroAssigned.Count < zeroCount && cornerTiles.Count > 0)
        {
            zeroAssigned.Add(cornerTiles[0]);
            cornerTiles.RemoveAt(0);
        }

        // si aún faltan, convertir nonCornerTiles a corner (moviendo su spawned a una esquina dentro de la tile)
        if (zeroAssigned.Count < zeroCount && nonCornerTiles.Count > 0) Debug.LogError("Faltan en el corner");

        // Marcar como reservadas para que no se asignen otros modos
        HashSet<int> reserved = new HashSet<int>(zeroAssigned);

        List<int> remaining = new List<int>();
        for (int i = 0; i < tiles.Count(); i++)
            if (!reserved.Contains(i)) remaining.Add(i);

        // Mezclar remaining para asignaciones aleatorias
        for (int i = 0; i < remaining.Count; i++)
        {
            int r = Random.Range(i, remaining.Count);
            int tmp = remaining[i];
            remaining[i] = remaining[r];
            remaining[r] = tmp;
        }

        // Asignar counts
        int ptr = 0;
        int assignedNone = 0, assignedHigh = 0, assignedLow = 0, assignedZero = 0;

        // apply Zero first to our reserved list (deterministic)
        foreach (int idx in zeroAssigned)
        {
            bool ok = tiles[idx].MoveNoSimilarity();
            assignedZero += ok ? 1 : 0;
        }

        // Now assign None, High, Low among remaining in that order with their counts
        // NONE
        for (int i = 0; i < noneCount && ptr < remaining.Count; i++, ptr++)
        {
            assignedNone += 1;
        }
        // HIGH
        for (int i = 0; i < highCount && ptr < remaining.Count; i++, ptr++)
        {
            var ok = tiles[ptr].MoveHighSimilarity();
            assignedHigh += ok ? 1 : 0;
        }
        // LOW
        for (int i = 0; i < lowCount && ptr < remaining.Count; i++, ptr++)
        {
            var ok = tiles[ptr].MoveLowSimilarity();
            assignedLow += ok ? 1 : 0;
        }

        Debug.Log($"[DoSwapAll] Result -> Zero:{assignedZero} None:{assignedNone} High:{assignedHigh} Low:{assignedLow}");
    }

    public static bool IsCornerIndex(int idx)
    {
        return idx == 0 || idx == 3 || idx == 12 || idx == 15;
    }
}



// CommonMovementStrategy.cs
public class CommonMovementStrategy : IMovementStrategy
{
    public void Move(TileController[] tiles, Light[] ligth)
    {
        IMovementStrategy.moverObjetos(tiles);
    }
}

// SwapMovementStrategy.cs
public class SwapMovementStrategy : IMovementStrategy
{
    public void Move(TileController[] tiles, Light[] ligth)
    {
        int n = tiles.Length;
        var indices = Enumerable.Range(0, n).OrderBy(_ => Random.value).ToArray();
        var originalPositions = tiles.Select(t => t.transform.position).ToArray();
        int half = n / 2;

        // Nuevos arrays para el nuevo orden
        TileController[] newTiles = new TileController[n];
        Light[] newLigth = new Light[n];

        for (int i = 0; i < n; i++)
        {
            int targetIdx = indices[i];
            Vector3 newPos = originalPositions[targetIdx];
            bool crossed = (i < half) ^ (targetIdx < half);
            Quaternion rot = crossed
                ? Quaternion.Euler(0, 180f, 0)
                : tiles[i].transform.localRotation;
            tiles[i].MoveToPosition(newPos, rot);

            // Reordenar ambos arrays
            newTiles[targetIdx] = tiles[i];
            newLigth[targetIdx] = ligth[i];
        }

        // Copiar el nuevo orden a los arrays originales
        for (int i = 0; i < n; i++)
        {
            tiles[i] = newTiles[i];
            ligth[i] = newLigth[i];
        }

        IMovementStrategy.moverObjetos(tiles);
    }

}