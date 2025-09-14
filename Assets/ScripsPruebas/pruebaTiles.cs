// TilePositionTester.cs
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System;
using Unity.Burst.Intrinsics;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteInEditMode]
public class TilePositionTester : MonoBehaviour
{
    [Header("Entrada")]
    public ArtPoolSO artPoolSO;          // tu arte a testear
    [Tooltip("Tamaño objetivo de la tile (se recalcula si artPoolSO está asignado)")]
    public float cellSize = 20f;         // tamaño de la tile (igual que en RoomBuilder luego del cálculo)
    [Tooltip("n por eje dentro de cada cuadrante (igual a TileController)")]
    public int positionsPerAxis = 2;     // n por eje dentro de cada cuadrante (igual a TileController)
    public float unityPlaneSize = 10f;   // el lado del Plane de Unity (por defecto 10)
    public Material tileMaterial;        // opcional

    [Header("Difficulty sets input")]
    public string difficultySetsJsonPath = "Assets/Renders/difficulty_sets_with_scores.json";
    public int desiredSize = 6;
    public string desiredDifficulty = "hard";
    public string desiredPool = ""; // category name optional

    [Header("Opcional")]
    public bool regenerateOnStart = true;
    public float gizmoSphereSize = 0.08f;

    // selección desde el editor
    [HideInInspector] public int selectedOrigin = -1;
    [HideInInspector] public int selectedTarget = -1;

    // Currents
    [HideInInspector] public List<string> currentGroup = new List<string>(); // lista activa de object_ids
    [HideInInspector] public string currentGroupLabel = "";

    // Runtimes
    private Transform _tileRoot;
    private readonly List<Transform> _spawned = new List<Transform>();
    private Vector3[] _localPositions; // 16 posiciones locales dentro de la tile (4x4)

    [Header("Multiple Tiles")]
    public GameObject singlePrefabForMulti;  // si querés instanciar un prefab específico por tile
    public int multiTilesCount = 4;
    public bool ensureCornersForZero = true;

    private class TileInstance
    {
        public Transform tileRoot;
        public Transform spawned;         // el prefab instanciado en esta tile (solo 1 por tile según lo pedido)
        public Vector3[] positions;       // 16 posiciones locales
        public int spawnedIndex = -1;
    }

    private readonly List<TileInstance> _tileInstances = new List<TileInstance>();

    private void Start()
    {
        if (regenerateOnStart)
        {
            TrySetupFromDifficultySets();
        }
    }

    // ---------------- Group loading & applying ----------------

    /// <summary>
    /// Intenta recuperar un grupo aleatorio desde el JSON según los parámetros configurados y lo aplica.
    /// </summary>
    [ContextMenu("Try Setup From DifficultySets")]
    public void TrySetupFromDifficultySets()
    {
        var root = DifficultySetsLoader.LoadFromFile(difficultySetsJsonPath);
        if (root == null)
        {
            Debug.LogWarning("pruebaTiles: no difficulty JSON found or failed to parse.");
            return;
        }

        var group = DifficultySetsLoader.GetRandomGroupByParams(root, desiredSize, desiredDifficulty, string.IsNullOrWhiteSpace(desiredPool) ? null : desiredPool, null, new System.Random());
        if (group == null)
        {
            Debug.LogWarning($"pruebaTiles: no group found for size={desiredSize}, difficulty={desiredDifficulty}, pool={desiredPool}. Trying without difficulty...");
            group = DifficultySetsLoader.GetRandomGroupByParams(root, desiredSize, null, string.IsNullOrWhiteSpace(desiredPool) ? null : desiredPool, null, new System.Random());
        }

        if (group == null)
        {
            Debug.LogWarning("pruebaTiles: still no group found. aborting.");
            return;
        }

        ApplyGroup(group, $"auto(size{desiredSize}_{desiredDifficulty}_{desiredPool})");
    }
    public void ShowPrefabInAllPositions()
    {
        // elegir prefab: prioridad -> campo testPrefabForAllPositions -> currentGroup[0] -> artPoolSO.GetRandomPrefab()
        GameObject prefab = null;
        if (prefab == null && currentGroup != null && currentGroup.Count > 0)
        {
            prefab = ResolvePrefabFromObjectId(currentGroup[0]);
        }
        if (prefab == null && artPoolSO != null)
        {
            prefab = artPoolSO.GetRandomPrefab();
        }
        if (prefab == null)
        {
            Debug.LogWarning("[ShowPrefabInAllPositions] No prefab available to test.");
            return;
        }

        // limpiar estado previo y crear una sola tile
        ClearAll();
        _localPositions = ComputeLocalPositions();
        _tileRoot = CreateTile(); // crea tile centrada en this.transform.position

        // instanciar 16 copias (una por posición)
        for (int i = 0; i < _localPositions.Length; i++)
        {
            // 1) instanciar sin parent para capturar su escala mundial original
            GameObject inst = Instantiate(prefab);
            inst.name = $"Test_{i}_{prefab.name}";

            Vector3 prefabWorldScale = inst.transform.lossyScale; // world-scale original
            // 2) parentear al tile (manteniendo local transform)
            inst.transform.SetParent(_tileRoot, false);

            // 3) corregir localScale para preservar el world-scale original:
            Vector3 parentLossy = _tileRoot.lossyScale;
            Vector3 correctedLocal = new Vector3(
                SafeDiv(prefabWorldScale.x, parentLossy.x),
                SafeDiv(prefabWorldScale.y, parentLossy.y),
                SafeDiv(prefabWorldScale.z, parentLossy.z)
            );
            // mantener signos coherentes
            correctedLocal = new Vector3(
                Mathf.Sign(parentLossy.x) * Mathf.Abs(correctedLocal.x),
                Mathf.Sign(parentLossy.y) * Mathf.Abs(correctedLocal.y),
                Mathf.Sign(parentLossy.z) * Mathf.Abs(correctedLocal.z)
            );
            inst.transform.localScale = correctedLocal;

            // 4) colocar y alinear
            inst.transform.localPosition = _localPositions[i];
            AlignSpawnToTileSurface(inst.transform, _tileRoot);

            // guardar en spawned para poder manipular después si querés
            _spawned.Add(inst.transform);
        }

        Debug.Log($"[ShowPrefabInAllPositions] Placed {_localPositions.Length} copies of '{prefab.name}' on a single tile. Tile root: {_tileRoot.name}");
    }
    /// <summary>
    /// Aplica un grupo (lista de object_ids): lo guarda en memoria, calcula cellSize usando max prefab size.
    /// </summary>
    public void ApplyGroup(List<string> group, string label = null)
    {
        if (group == null || group.Count == 0)
        {
            Debug.LogWarning("ApplyGroup: grupo nulo o vacío.");
            return;
        }

        currentGroup = new List<string>(group);
        currentGroupLabel = string.IsNullOrEmpty(label) ? $"group_{group.Count}" : label;

        // Calcula maxSize desde el grupo (si falla, fallback a artPoolSO.GetMaxPrefabSize())
        Vector3 maxSize = Vector3.one;
        try
        {
            maxSize = DifficultySetsLoader.GetMaxPrefabSizeForGroup(artPoolSO, currentGroup, editorMode: Application.isEditor);
        }
        catch
        {
            if (artPoolSO != null) maxSize = artPoolSO.GetMaxPrefabSize();
        }

        float smallCellSize = (maxSize.x + maxSize.z) / 2f;
        float quadrantSize = smallCellSize * 2f;
        this.cellSize = quadrantSize * 2f;

        Debug.Log($"ApplyGroup: label={currentGroupLabel} count={currentGroup.Count} computed cellSize={cellSize} maxSize={maxSize}");
    }

    // ----- Generation using currentGroup -----

    /// <summary>
    /// Genera 1 tile y instancia en él los prefabs según currentGroup (uno por item hasta llenar posiciones).
    /// </summary>
    public void GenerateTileFromCurrentGroup()
    {
        if (currentGroup == null || currentGroup.Count == 0)
        {
            Debug.LogWarning("GenerateTileFromCurrentGroup: currentGroup vacío. Aplica un grupo antes.");
            return;
        }

        ClearAll(); // limpia estado anterior

        // crea tile
        _tileRoot = CreateTile();

        // calcula posiciones
        _localPositions = ComputeLocalPositions();

        // instanciar objetos del grupo en las primeras N posiciones
        int n = Mathf.Min(_localPositions.Length, currentGroup.Count);
        for (int i = 0; i < n; i++)
        {
            GameObject prefab = ResolvePrefabFromObjectId(currentGroup[i]);
            if (prefab == null)
            {
                Debug.LogWarning($"GenerateTileFromCurrentGroup: no pude resolver prefab para {currentGroup[i]}");
                continue;
            }

            var art = Instantiate(prefab, _tileRoot);
            art.name = $"Art_{i}_{prefab.name}";

            // neutralizar escala del tile
            Vector3 parentScale = _tileRoot.lossyScale;
            art.transform.localScale = new Vector3(
                1f / Mathf.Max(0.0001f, parentScale.x),
                1f / Mathf.Max(0.0001f, parentScale.y),
                1f / Mathf.Max(0.0001f, parentScale.z)
            );

            float artYOffset = ComputeArtYOffsetLocal(art, _tileRoot);
            art.transform.localPosition = _localPositions[i] + Vector3.up * artYOffset;

            _spawned.Add(art.transform);
        }

        // reset selections
        selectedOrigin = -1;
        selectedTarget = -1;
    }

    /// <summary>
    /// Genera 'count' tiles y en cada una instancia 1 prefab. Para elegir prefabs usa la lista currentGroup en ciclo,
    /// o singlePrefabForMulti si está configurado (prefab alternativa).
    /// </summary>
    public void GenerateMultipleTilesFromCurrentGroup(int count)
    {
        if (count <= 0)
        {
            Debug.LogWarning("GenerateMultipleTilesFromCurrentGroup: count <= 0");
            return;
        }
        if ((currentGroup == null || currentGroup.Count == 0) && singlePrefabForMulti == null && artPoolSO == null)
        {
            Debug.LogWarning("No hay currentGroup ni singlePrefabForMulti ni artPoolSO. Nada que instanciar.");
            return;
        }

        ClearAll();

        // preparativos: localPositions y spacing
        _localPositions = ComputeLocalPositions();
        int cols = Mathf.CeilToInt(Mathf.Sqrt(count));
        int rows = Mathf.CeilToInt(count / (float)cols);
        float spacing = cellSize + 2f;

        int created = 0;
        for (int y = 0; y < rows && created < count; y++)
        {
            for (int x = 0; x < cols && created < count; x++)
            {
                Vector3 worldPos = transform.position + new Vector3((x - cols / 2f) * spacing, 0f, (y - rows / 2f) * spacing);
                Transform tileRoot = CreateTileAt(worldPos, $"TestTile_Plane_{created}");

                TileInstance inst = new TileInstance();
                inst.tileRoot = tileRoot;
                inst.positions = _localPositions;

                int spawnIndex = UnityEngine.Random.Range(0, _localPositions.Length);

                // escoger prefab: prioriza singlePrefabForMulti, sino ciclo por currentGroup
                GameObject chosenPrefab = singlePrefabForMulti;
                if (chosenPrefab == null)
                {
                    if (currentGroup != null && currentGroup.Count > 0)
                    {
                        string oid = currentGroup[created % currentGroup.Count];
                        chosenPrefab = ResolvePrefabFromObjectId(oid);
                    }
                    else if (artPoolSO != null)
                    {
                        chosenPrefab = artPoolSO.GetRandomPrefab();
                    }
                }

                if (chosenPrefab == null)
                {
                    Debug.LogWarning($"Skipping tile {created}: no prefab resolved.");
                    created++;
                    continue;
                }

                GameObject art = Instantiate(chosenPrefab);
                art.name = $"Tile_{created}_Art";
                art.transform.SetParent(tileRoot, false);
                Vector3 originalWorldScale = art.transform.lossyScale;
                // neutralizar escala mundial
                Vector3 parentScale = tileRoot.lossyScale;
                art.transform.localScale = new Vector3(
                    originalWorldScale.x / Mathf.Max(0.0001f, parentScale.x),
                    originalWorldScale.y / Mathf.Max(0.0001f, parentScale.y),
                    originalWorldScale.z / Mathf.Max(0.0001f, parentScale.z)
                );

                art.transform.localPosition = inst.positions[spawnIndex];

                // importante: después de parentear y ajustar escala, alineamos en mundo
                AlignSpawnToTileSurface(art.transform, tileRoot);

                inst.spawned = art.transform;
                inst.spawnedIndex = spawnIndex;

                _tileInstances.Add(inst);
                _spawned.Add(art.transform);

                created++;
            }
        }

        Debug.Log($"GenerateMultipleTilesFromCurrentGroup: created {created} tiles.");
    }

    // ---------------- new requested methods ----------------

    /// <summary>
    /// Crea una tile por cada prefab encontrado en Assets/Prefabs usando tu método "viejo"
    /// (instanciar + neutralizar escala manual + alinear Y). Coloca el prefab en la esquina (x=-3.75,z=-3.75).
    /// </summary>
    [ContextMenu("Generate Tiles For All Prefabs (OldMethod)")]
    public void GenerateTilesForAllPrefabs_OldMethod()
    {
#if UNITY_EDITOR
        string folder = "Assets/Prefabs";
        var guids = UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { folder });
        if (guids == null || guids.Length == 0)
        {
            Debug.LogWarning("[OldMethod] No prefabs found in Assets/Prefabs");
            return;
        }

        ClearAll();
        _localPositions = ComputeLocalPositions();

        int count = guids.Length;
        float pad = 2f;

        // --- 1) cargar prefabs y calcular sizes para cada uno ---
        var entries = new List<(GameObject prefab, float cellSize, Bounds bounds)>();
        for (int i = 0; i < count; i++)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[i]);
            GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;

            Bounds b; Vector3 ws;
            if (!MeasurePrefabBoundsAndWorldScale(prefab, out b, out ws))
            {
                b = new Bounds(Vector3.zero, Vector3.one);
            }
            Vector3 prefabSize = b.size;
            float smallCellSize = (prefabSize.x + prefabSize.z) / 2f;
            float quadrantSize = smallCellSize * 2f;
            float thisCellSize = quadrantSize * 2f;

            entries.Add((prefab, thisCellSize, b));
        }

        if (entries.Count == 0)
        {
            Debug.LogWarning("[OldMethod] no valid prefabs measured.");
            return;
        }

        // --- 2) calcular totalLength y posicion inicial (left-most center) ---
        float totalSizes = 0f;
        foreach (var e in entries) totalSizes += e.cellSize;
        float totalLength = totalSizes + pad * (entries.Count - 1);
        float leftMostCenterX = transform.position.x - (totalLength / 2f);

        // --- 3) iterar y crear tiles en fila centradas, usando half-width stepping ---
        float cursorX = leftMostCenterX;
        for (int idx = 0; idx < entries.Count; idx++)
        {
            var (prefab, thisCellSize, bounds) = entries[idx];
            float half = thisCellSize * 0.5f;
            // center for this tile:
            float centerX = cursorX + half;
            Vector3 worldPos = new Vector3(centerX, transform.position.y, transform.position.z);

            // Create tile root
            Transform tileRoot = CreateTileAt(worldPos, $"Tile_Old_{idx}_{prefab.name}");
            float scale = thisCellSize / Mathf.Max(0.0001f, unityPlaneSize);
            tileRoot.localScale = new Vector3(scale, scale, scale);

            // Instantiate prefab (no parent)
            GameObject art = GameObject.Instantiate(prefab);
            art.name = $"Inst_{prefab.name}";

            // Desactivar MBs/Animators temporalmente para evitar movimiento en Awake/Start
            var monoBeh = art.GetComponentsInChildren<MonoBehaviour>(true);
            var monoStates = new List<(MonoBehaviour mb, bool enabled)>();
            foreach (var mb in monoBeh)
            {
                if (mb == null) continue;
                monoStates.Add((mb, mb.enabled));
                try { mb.enabled = false; } catch { }
            }
            var anims = art.GetComponentsInChildren<Animator>(true);
            foreach (var a in anims) { if (a != null) a.enabled = false; }

            // 1) Leer escala mundial original
            Vector3 originalWorldScale = art.transform.lossyScale;

            // 2) Parentar AL tileRoot conservando world transform (true)
            art.transform.SetParent(tileRoot, true);

            // 3) Obtener escala mundial del parent (tileRoot)
            Vector3 parentLossy = tileRoot.lossyScale;

            // 4) calcular localScale que mantiene tamaño visual original
            Vector3 newLocal = new Vector3(
                originalWorldScale.x / Mathf.Max(1e-6f, parentLossy.x),
                originalWorldScale.y / Mathf.Max(1e-6f, parentLossy.y),
                originalWorldScale.z / Mathf.Max(1e-6f, parentLossy.z)
            );
            newLocal = new Vector3(
                Mathf.Sign(parentLossy.x) * Mathf.Abs(newLocal.x),
                Mathf.Sign(parentLossy.y) * Mathf.Abs(newLocal.y),
                Mathf.Sign(parentLossy.z) * Mathf.Abs(newLocal.z)
            );
            art.transform.localScale = newLocal;

            // restablecer MBs/Animators
            for (int i = 0; i < monoStates.Count; i++)
                try { monoStates[i].mb.enabled = monoStates[i].enabled; } catch { }
            foreach (var a in anims) if (a != null) a.enabled = true;

            // colocarlo en la esquina local index 12 (dentro del tileRoot)
            Vector3 cornerLocal = _localPositions != null ? _localPositions[12] : new Vector3(-3.75f, 0f, -3.75f);
            float yOffset = ComputeArtYOffsetLocal(art, tileRoot);
            art.transform.localPosition = cornerLocal + Vector3.up * yOffset;

            _spawned.Add(art.transform);

            // avanzar cursor: next left is current center + half + pad
            cursorX = cursorX + thisCellSize + pad;
        }

        Debug.Log($"[OldMethod] Generated {entries.Count} tiles for prefabs (placed corner index 12).");
#else
    Debug.LogWarning("This method is editor-only.");
#endif
    }

    [ContextMenu("Generate Tiles For All Prefabs (PrefabPlacer)")]
    public void GenerateTilesForAllPrefabs_PrefabPlacer()
    {
#if UNITY_EDITOR
        string folder = "Assets/Prefabs";
        var guids = UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { folder });
        if (guids == null || guids.Length == 0)
        {
            Debug.LogWarning("[PrefabPlacer] No prefabs found in Assets/Prefabs");
            return;
        }

        ClearAll();
        _localPositions = ComputeLocalPositions();

        int count = guids.Length;
        float pad = 2f;

        // load prefabs and sizes first
        var entries = new List<(GameObject prefab, float cellSize)>();
        for (int i = 0; i < count; i++)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[i]);
            GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;

            Bounds b; Vector3 ws;
            if (!MeasurePrefabBoundsAndWorldScale(prefab, out b, out ws))
                b = new Bounds(Vector3.zero, Vector3.one);

            Vector3 prefabSize = b.size;
            float smallCellSize = (prefabSize.x + prefabSize.z) / 2f;
            float quadrantSize = smallCellSize * 2f;
            float thisCellSize = quadrantSize * 2f;

            entries.Add((prefab, thisCellSize));
        }

        if (entries.Count == 0)
        {
            Debug.LogWarning("[PrefabPlacer] no valid prefabs measured.");
            return;
        }

        // compute total length & left-most center
        float totalSizes = 0f;
        foreach (var e in entries) totalSizes += e.cellSize;
        float totalLength = totalSizes + pad * (entries.Count - 1);
        float leftMostCenterX = transform.position.x - (totalLength / 2f);
        float cursorX = leftMostCenterX;

        // iterate and place
        for (int idx = 0; idx < entries.Count; idx++)
        {
            var (prefab, thisCellSize) = entries[idx];
            float half = thisCellSize * 0.5f;
            float centerX = cursorX + half;
            Vector3 worldPos = new Vector3(centerX, transform.position.y, transform.position.z);

            Transform tileRoot = CreateTileAt(worldPos, $"Tile_PP_{idx}_{prefab.name}");
            float scale = thisCellSize / Mathf.Max(0.0001f, unityPlaneSize);
            tileRoot.localScale = new Vector3(scale, scale, scale);

            // attempt to use PrefabPlacer
            Transform placedT = null;
            GameObject placedGO = null;
            try
            {
                placedT = PrefabPlacer.InstantiateAndPlace(prefab, tileRoot, _localPositions != null ? _localPositions[12] : new Vector3(-3.75f, 0f, -3.75f), cyclicScaleNeutralize: false, reenableMonoBehaviours: true);
            }
            catch (Exception)
            {
                try
                {
                    var mi = typeof(PrefabPlacer).GetMethod("InstantiateAndPlace", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (mi != null)
                    {
                        var res = mi.Invoke(null, new object[] { prefab, tileRoot, _localPositions != null ? _localPositions[12] : new Vector3(-3.75f, 0f, -3.75f), false, true });
                        if (res is Transform) placedT = (Transform)res;
                        else if (res is GameObject) placedGO = (GameObject)res;
                    }
                }
                catch { /* ignore */ }
            }

            if (placedT != null) placedGO = placedT.gameObject;

            if (placedGO == null)
            {
                // fallback instantiate manual and neutralize
                GameObject inst = GameObject.Instantiate(prefab);
                inst.name = $"Inst_{prefab.name}";
                // parent & neutralize to tileRoot
                inst.transform.SetParent(tileRoot, true); // keep world transform then fix local scale
                Vector3 originalWorldScale = inst.transform.lossyScale;
                Vector3 parentLossy = tileRoot.lossyScale;
                Vector3 newLocal = new Vector3(
                    originalWorldScale.x / Mathf.Max(1e-6f, parentLossy.x),
                    originalWorldScale.y / Mathf.Max(1e-6f, parentLossy.y),
                    originalWorldScale.z / Mathf.Max(1e-6f, parentLossy.z)
                );
                newLocal = new Vector3(Mathf.Sign(parentLossy.x) * Mathf.Abs(newLocal.x),
                                       Mathf.Sign(parentLossy.y) * Mathf.Abs(newLocal.y),
                                       Mathf.Sign(parentLossy.z) * Mathf.Abs(newLocal.z));
                inst.transform.localScale = newLocal;

                Vector3 cornerLocal = _localPositions != null ? _localPositions[12] : new Vector3(-3.75f, 0f, -3.75f);
                float yOffset = ComputeArtYOffsetLocal(inst, tileRoot);
                inst.transform.localPosition = cornerLocal + Vector3.up * yOffset;
                _spawned.Add(inst.transform);
            }
            else
            {
                // placedGO returned by PrefabPlacer: ensure parent is tileRoot and neutralize scale
                if (placedGO.transform.parent != tileRoot) placedGO.transform.SetParent(tileRoot, true);
                Vector3 originalWorldScale = placedGO.transform.lossyScale;
                Vector3 parentLossy = tileRoot.lossyScale;
                Vector3 newLocal = new Vector3(
                    originalWorldScale.x / Mathf.Max(1e-6f, parentLossy.x),
                    originalWorldScale.y / Mathf.Max(1e-6f, parentLossy.y),
                    originalWorldScale.z / Mathf.Max(1e-6f, parentLossy.z)
                );
                newLocal = new Vector3(Mathf.Sign(parentLossy.x) * Mathf.Abs(newLocal.x),
                                       Mathf.Sign(parentLossy.y) * Mathf.Abs(newLocal.y),
                                       Mathf.Sign(parentLossy.z) * Mathf.Abs(newLocal.z));
                placedGO.transform.localScale = newLocal;

                Vector3 cornerLocal = _localPositions != null ? _localPositions[12] : new Vector3(-3.75f, 0f, -3.75f);
                float yOffset = ComputeArtYOffsetLocal(placedGO, tileRoot);
                placedGO.transform.localPosition = cornerLocal + Vector3.up * yOffset;

                _spawned.Add(placedGO.transform);
            }

            // advance cursor for next tile
            cursorX = cursorX + thisCellSize + pad;
        }

        Debug.Log($"[PrefabPlacer] Generated {entries.Count} tiles for prefabs (placed corner index 12).");
#else
    Debug.LogWarning("This method is editor-only.");
#endif
    }



    // ----- small helpers -----

    /// <summary>
    /// Intenta resolver un prefab para un object id usando artPoolSO.ResolvePrefabForObjectId (si existe),
    /// o Resources.Load fallback por el segmento final del object id.
    /// </summary>
    private GameObject ResolvePrefabFromObjectId(string objectId)
    {
        GameObject prefab = null;
        if (artPoolSO != null)
        {
            try
            {
                prefab = artPoolSO.ResolvePrefabForObjectId(objectId, Application.isEditor);
            }
            catch { prefab = null; }
        }
        if (prefab == null)
        {
            // fallback a buscar por nombre en Resources
            string nameOnly = objectId.Split(new char[] { '/', '\\' }).Last();
            prefab = Resources.Load<GameObject>(nameOnly);
        }
        return prefab;
    }

    private Transform CreateTileAt(Vector3 worldPos, string name = "TestTile_Plane")
    {
        var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
        plane.name = name;
        plane.transform.SetParent(this.transform, false);
        plane.transform.position = worldPos;

        float scale = cellSize / Mathf.Max(0.0001f, unityPlaneSize);
        plane.transform.localScale = new Vector3(scale, 1f, scale);

        if (tileMaterial != null)
        {
            var r = plane.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = tileMaterial;
        }

        return plane.transform;
    }

    /// <summary>
    /// CreateTile: crea un plane centrado en este.transform.position y devuelve su transform.
    /// </summary>
    public Transform CreateTile()
    {
        var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
        plane.name = "TestTile_Plane";
        plane.transform.SetParent(this.transform, false);
        plane.transform.localPosition = Vector3.zero;
        float scale = cellSize / Mathf.Max(0.0001f, unityPlaneSize);
        plane.transform.localScale = new Vector3(scale, 1f, scale);

        if (tileMaterial != null)
        {
            var r = plane.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = tileMaterial;
        }

        return plane.transform;
    }

    [ContextMenu("Generate All Positions")]
    public void GenerateAllPositions()
    {
        ClearAll();

        // Si hay artPoolSO, calcula cellSize como antes (fallback)
        if (artPoolSO != null && (currentGroup == null || currentGroup.Count == 0))
        {
            Vector3 maxPrefabSize = artPoolSO.GetMaxPrefabSize();
            float smallCellSize = (maxPrefabSize.x + maxPrefabSize.z) / 2f;
            float quadrantSize = smallCellSize * 2f;
            cellSize = quadrantSize * 2f;
        }

        // 1) Crear la tile (Plane)
        _tileRoot = CreateTile();

        // 3) GENERAR la lista de 16 posiciones usando la grilla fija y en el orden solicitado
        _localPositions = ComputeLocalPositions();

        // 4) Instanciar prefabs (si corresponde)
        if (artPoolSO != null && (currentGroup == null || currentGroup.Count == 0))
        {
            for (int i = 0; i < _localPositions.Length; i++)
            {
                var prefab = artPoolSO.GetRandomPrefab();
                if (prefab == null) continue;
                var art = Instantiate(prefab, _tileRoot);
                art.name = $"Art_{i}";
                Vector3 parentScale = _tileRoot.lossyScale;
                art.transform.localScale = new Vector3(
                    1f / Mathf.Max(0.0001f, parentScale.x),
                    1f / Mathf.Max(0.0001f, parentScale.y),
                    1f / Mathf.Max(0.0001f, parentScale.z)
                );

                float artYOffset = ComputeArtYOffsetLocal(art, _tileRoot);
                art.transform.localPosition = _localPositions[i] + Vector3.up * artYOffset;

                _spawned.Add(art.transform);
            }
        }

        // limpiar selecciones
        selectedOrigin = -1;
        selectedTarget = -1;
    }

    [ContextMenu("Clear")]
    public void ClearAll()
    {
        // destruir spawned global
        for (int i = _spawned.Count - 1; i >= 0; i--)
        {
            if (_spawned[i] != null) DestroyImmediate(_spawned[i].gameObject);
        }
        _spawned.Clear();

        // destruir tiles que creamos (si existen)
        foreach (var inst in _tileInstances)
        {
            if (inst != null && inst.tileRoot != null) DestroyImmediate(inst.tileRoot.gameObject);
        }
        _tileInstances.Clear();

        // destruir tile root simple si existe
        if (_tileRoot != null) DestroyImmediate(_tileRoot.gameObject);
        _tileRoot = null;

        _localPositions = null;

        selectedOrigin = -1;
        selectedTarget = -1;
    }

    /// <summary>
    /// Instancia 'count' tiles separadas en grilla; en cada tile instanciamos 1 prefab.
    /// </summary>
    public void InstantiateMultipleTiles(int count, GameObject prefab = null)
    {
        GenerateMultipleTilesFromCurrentGroup(count);
    }

    private static float ComputeArtYOffsetLocal(GameObject art, Transform parent)
    {
        var rends = art.GetComponentsInChildren<Renderer>();
        if (rends == null || rends.Length == 0) return 0f;

        // Encapsular bounds en world space
        Bounds gb = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) gb.Encapsulate(rends[i].bounds);

        // Punto mundial más bajo del renderer
        Vector3 worldMin = gb.min; // ya está en coordenadas mundo

        // Convertir ese punto a coordenadas locales del parent (tileRoot)
        Vector3 localMin = parent.InverseTransformPoint(worldMin);

        // Queremos que el punto localMin.y quede en 0 => offset = -localMin.y
        float localYOffset = -localMin.y;
        return localYOffset;
    }

    // ---------------- Swap logic (DoSwapAll, DoSwapOnInstance, DoSwap) ----------------

    public void DoSwapAll()
    {
        int total = _tileInstances.Count;
        if (total == 0)
        {
            Debug.LogWarning("[TilePositionTester] No hay tileInstances para swap.");
            return;
        }

        int baseCount = total / 4;
        int remainder = total % 4;

        int noneCount = baseCount;
        int highCount = baseCount;
        int lowCount = baseCount;
        int zeroCount = baseCount + remainder; // remainder -> Zero

        Debug.Log($"[DoSwapAll] total {total} => None:{noneCount} High:{highCount} Low:{lowCount} Zero:{zeroCount}");

        // indices de tiles
        List<int> allIdx = new List<int>();
        for (int i = 0; i < _tileInstances.Count; i++) allIdx.Add(i);
        // separar tiles que ya tienen spawn en esquina vs no
        List<int> cornerTiles = new List<int>();
        List<int> nonCornerTiles = new List<int>();
        for (int i = 0; i < _tileInstances.Count; i++)
        {
            var inst = _tileInstances[i];
            if (inst == null || inst.spawnedIndex < 0) { nonCornerTiles.Add(i); continue; }
            if (IsCornerIndex(inst.spawnedIndex)) cornerTiles.Add(i);
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
        while (zeroAssigned.Count < zeroCount && nonCornerTiles.Count > 0)
        {
            int idx = nonCornerTiles[0];
            nonCornerTiles.RemoveAt(0);
            var inst = _tileInstances[idx];
            if (inst == null) continue;

            // elegir una esquina aleatoria para "convertir"
            int[] corners = new int[] { 0, 3, 12, 15 };
            int chosenCorner = corners[UnityEngine.Random.Range(0, corners.Length)];

            // mover el spawned a la esquina dentro de la misma tile
            if (inst.spawned != null)
            {
                float yOffset = ComputeArtYOffsetLocal(inst.spawned.gameObject, inst.tileRoot);
                inst.spawned.localPosition = inst.positions[chosenCorner] + Vector3.up * yOffset;
                Debug.Log($"[DoSwapAll] Converted tile {inst.tileRoot.name} - {inst.spawned.name} -> cornerIdx {chosenCorner}");
            }
            inst.spawnedIndex = chosenCorner;
            zeroAssigned.Add(idx);
        }

        // Si todavía faltan zeros (no hay tiles suficientes), avisar y reducir zeroCount
        if (zeroAssigned.Count < zeroCount)
        {
            Debug.LogWarning($"[DoSwapAll] No alcanzo a reservar suficientes tiles para Zero. Pedidos: {zeroCount}, disponibles: {zeroAssigned.Count}. Ajusto.");
            zeroCount = zeroAssigned.Count;
        }

        // Marcar como reservadas para que no se asignen otros modos
        HashSet<int> reserved = new HashSet<int>(zeroAssigned);

        // Preparar lista de tiles restantes para asignar None/High/Low
        List<int> remaining = new List<int>();
        for (int i = 0; i < _tileInstances.Count; i++)
            if (!reserved.Contains(i)) remaining.Add(i);

        // Mezclar remaining para asignaciones aleatorias
        for (int i = 0; i < remaining.Count; i++)
        {
            int r = UnityEngine.Random.Range(i, remaining.Count);
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
            var ok = DoSwapOnInstance(_tileInstances[idx], SimilarityMode.Zero);
            assignedZero += ok ? 1 : 0;
        }

        // Now assign None, High, Low among remaining in that order with their counts
        // NONE
        for (int i = 0; i < noneCount && ptr < remaining.Count; i++, ptr++)
        {
            var ok = DoSwapOnInstance(_tileInstances[remaining[ptr]], SimilarityMode.None);
            assignedNone += ok ? 1 : 0;
        }
        // HIGH
        for (int i = 0; i < highCount && ptr < remaining.Count; i++, ptr++)
        {
            var ok = DoSwapOnInstance(_tileInstances[remaining[ptr]], SimilarityMode.High);
            assignedHigh += ok ? 1 : 0;
        }
        // LOW
        for (int i = 0; i < lowCount && ptr < remaining.Count; i++, ptr++)
        {
            var ok = DoSwapOnInstance(_tileInstances[remaining[ptr]], SimilarityMode.Low);
            assignedLow += ok ? 1 : 0;
        }

        Debug.Log($"[DoSwapAll] Result -> Zero:{assignedZero} None:{assignedNone} High:{assignedHigh} Low:{assignedLow}");
    }

    private bool DoSwapOnInstance(TileInstance inst, SimilarityMode mode)
    {
        if (inst == null || inst.spawned == null)
        {
            Debug.LogWarning("[DoSwapOnInstance] Instancia o spawned nulo.");
            return false;
        }

        string prefabName = inst.spawned.name;
        var positions = inst.positions;
        int currentIndex = inst.spawnedIndex;
        if (currentIndex < 0 || currentIndex >= positions.Length)
        {
            Debug.LogWarning($"[DoSwapOnInstance] {inst.tileRoot.name} - {prefabName}: indice actual inválido ({currentIndex}).");
            return false;
        }

        int newIndex = currentIndex;
        switch (mode)
        {
            case SimilarityMode.None:
                Debug.Log($"[DoSwapOnInstance] {inst.tileRoot.name} - {prefabName}: Mode None - no se mueve. (idx {currentIndex})");
                return true;

            case SimilarityMode.High:
                {
                    var sameQ = FindIndicesInSameQuadrant(currentIndex, positions);
                    sameQ.Remove(currentIndex);
                    if (sameQ.Count == 0)
                    {
                        Debug.Log($"[DoSwapOnInstance] {inst.tileRoot.name} - {prefabName}: High - no hay otra pos en mismo cuadrante.");
                        return false;
                    }
                    newIndex = sameQ[UnityEngine.Random.Range(0, sameQ.Count)];
                }
                break;

            case SimilarityMode.Low:
                {
                    var candidates = FindNonAdjacentCandidates(currentIndex);
                    if (candidates.Count == 0)
                    {
                        Debug.Log($"[DoSwapOnInstance] {inst.tileRoot.name} - {prefabName}: Low - no se encontraron candidatos no adyacentes.");
                        return false;
                    }
                    newIndex = candidates[UnityEngine.Random.Range(0, candidates.Count)];
                }
                break;

            case SimilarityMode.Zero:
                {
                    // Si no está en esquina, lo convertimos primero (suponiendo que en DoSwapAll ya lo reservamos)
                    if (!IsCornerIndex(currentIndex))
                    {
                        // elegir una esquina y mover ahí
                        int[] corners = new int[] { 0, 3, 12, 15 };
                        int chosen = corners[UnityEngine.Random.Range(0, corners.Length)];
                        float yOffset = ComputeArtYOffsetLocal(inst.spawned.gameObject, inst.tileRoot);
                        inst.spawned.localPosition = inst.positions[chosen] + Vector3.up * yOffset;
                        Debug.Log($"[DoSwapOnInstance] {inst.tileRoot.name} - {prefabName}: Zero pre-convert -> moved to corner {chosen} (was {currentIndex})");
                        currentIndex = chosen;
                        inst.spawnedIndex = chosen;
                    }

                    // ahora currentIndex es esquina; hacemos move a opuesta
                    newIndex = GetOppositeCornerIndex(currentIndex);
                }
                break;
        }

        if (newIndex < 0 || newIndex >= positions.Length)
        {
            Debug.LogError($"[DoSwapOnInstance] {inst.tileRoot.name} - {prefabName}: índice destino inválido {newIndex}.");
            return false;
        }

        // en vez de usar yOffset local, fija la posición local base:
        inst.spawned.localPosition = positions[newIndex];
        // y luego ajusta en mundo
        AlignSpawnToTileSurface(inst.spawned, inst.tileRoot);
        inst.spawnedIndex = newIndex;

        Debug.Log($"[DoSwapOnInstance] {inst.tileRoot.name} - {prefabName}: moved -> {newIndex} (mode {mode})");
        return true;
    }

    public enum SimilarityMode { None, High, Low, Zero }

    public void DoSwap(SimilarityMode mode)
    {
        if (_spawned.Count == 0)
        {
            Debug.LogWarning("[TilePositionTester] No spawned items to swap.");
            return;
        }

        var positions = ComputeLocalPositions();
        if (positions == null || positions.Length != 16)
        {
            Debug.LogError("[TilePositionTester] posiciones no calculadas correctamente.");
            return;
        }

        // Usamos el primer spawned como antes (puedes cambiar esto luego para usar selectedOrigin)
        var art = _spawned[0];
        int currentIndex = GetNearestPositionIndex(art.localPosition, positions, out float dist);

        if (currentIndex < 0)
        {
            Debug.LogWarning("[TilePositionTester] No pude encontrar el índice cercano al spawned (distance: " + dist + ").");
            return;
        }

        int newIndex = currentIndex;

        switch (mode)
        {
            case SimilarityMode.None:
                Debug.Log("[DoSwap] Mode None: no se mueve.");
                return;

            case SimilarityMode.High:
                {
                    var sameQuad = FindIndicesInSameQuadrant(currentIndex, positions);
                    sameQuad.Remove(currentIndex);
                    if (sameQuad.Count == 0)
                    {
                        Debug.Log("[DoSwap] High: no hay otra posición en el mismo cuadrante.");
                        return;
                    }
                    newIndex = sameQuad[UnityEngine.Random.Range(0, sameQuad.Count)];
                }
                break;

            case SimilarityMode.Low:
                {
                    var candidates = FindNonAdjacentCandidates(currentIndex);
                    if (candidates.Count == 0)
                    {
                        Debug.Log("[DoSwap] Low: no se encontraron candidatos no adyacentes.");
                        return;
                    }
                    newIndex = candidates[UnityEngine.Random.Range(0, candidates.Count)];
                }
                break;

            case SimilarityMode.Zero:
                {
                    if (!IsCornerIndex(currentIndex))
                    {
                        Debug.LogWarning("[DoSwap] Zero: el objeto debe estar en una esquina para aplicar Zero-sim. Indice actual: " + currentIndex);
                        return;
                    }
                    newIndex = GetOppositeCornerIndex(currentIndex);
                }
                break;
        }

        if (newIndex < 0 || newIndex >= positions.Length)
        {
            Debug.LogError("[DoSwap] índice destino inválido: " + newIndex);
            return;
        }

        // si hay alguien en newIndex, hacemos swap; sino movemos
        Transform other = FindSpawnAtIndex(newIndex, positions);
        if (other != null)
        {
            Vector3 tmp = other.localPosition;
            other.localPosition = positions[currentIndex];
            art.localPosition = positions[newIndex];
            Debug.Log($"[DoSwap] Swapped {art.name} (idx {currentIndex}) with {other.name} (idx {newIndex}).");
        }
        else
        {
            art.localPosition = positions[newIndex];
            Debug.Log($"[DoSwap] Moved {art.name} from idx {currentIndex} to idx {newIndex}.");
        }
    }

    private int GetNearestPositionIndex(Vector3 localPos, Vector3[] positions, out float outDistance)
    {
        int best = -1;
        float bestDist = float.MaxValue;
        for (int i = 0; i < positions.Length; i++)
        {
            float d = Vector3.Distance(new Vector3(localPos.x, 0f, localPos.z), new Vector3(positions[i].x, 0f, positions[i].z));
            if (d < bestDist)
            {
                bestDist = d;
                best = i;
            }
        }

        outDistance = bestDist;
        if (bestDist > 0.5f) return -1;
        return best;
    }

    private bool IsCornerIndex(int idx) => idx == 0 || idx == 3 || idx == 12 || idx == 15;

    private int GetOppositeCornerIndex(int idx)
    {
        switch (idx)
        {
            case 0: return 15;
            case 3: return 12;
            case 12: return 3;
            case 15: return 0;
            default: return idx;
        }
    }

    private Transform FindSpawnAtIndex(int index, Vector3[] positions)
    {
        Vector3 targetPos = positions[index];
        for (int i = 0; i < _spawned.Count; i++)
        {
            var t = _spawned[i];
            if (t == null) continue;
            // comparar solo XZ con tolerancia
            Vector3 p = t.localPosition;
            if (Vector3.Distance(new Vector3(p.x, 0f, p.z), new Vector3(targetPos.x, 0f, targetPos.z)) < 0.01f)
                return t;
        }
        return null;
    }

    private List<int> FindIndicesInSameQuadrant(int index, Vector3[] positions)
    {
        var res = new List<int>();
        Vector3 p = positions[index];

        bool xPos = p.x > 0f;
        bool zPos = p.z > 0f;

        for (int i = 0; i < positions.Length; i++)
        {
            Vector3 q = positions[i];
            bool qx = q.x > 0f;
            bool qz = q.z > 0f;
            if (qx == xPos && qz == zPos) res.Add(i);
        }
        return res;
    }

    private List<int> FindNonAdjacentCandidates(int index)
    {
        List<int> candidates = new List<int>();
        (int r0, int c0) = IndexToRowCol(index);

        for (int i = 0; i < 16; i++)
        {
            (int r, int c) = IndexToRowCol(i);
            int manhattan = Mathf.Abs(r - r0) + Mathf.Abs(c - c0);
            if (manhattan > 1) candidates.Add(i);
        }
        return candidates;
    }

    private (int row, int col) IndexToRowCol(int idx)
    {
        int row = idx / 4; // 0..3
        int col = idx % 4; // 0..3
        return (row, col);
    }

    private void AlignSpawnToTileSurface(Transform art, Transform tileRoot, float epsilon = 0.001f)
    {
        if (art == null || tileRoot == null) return;

        // obtener bounds mundiales del objeto (todos los renderers)
        var rends = art.GetComponentsInChildren<Renderer>();
        if (rends == null || rends.Length == 0) return;

        Bounds gb = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) gb.Encapsulate(rends[i].bounds);

        float worldMinY = gb.min.y;

        // determinar Y de la "superficie" de la tile en mundo.
        // Para el Plane, su surface está en tileRoot.position.y si no hay offsets.
        // Usamos TransformPoint(Vector3.zero) por si el tile tiene transformaciones.
        float tileSurfaceWorldY = tileRoot.TransformPoint(Vector3.zero).y;

        float delta = (tileSurfaceWorldY + epsilon) - worldMinY;

        // aplicar desplazamiento en mundo
        art.transform.position += new Vector3(0f, delta, 0f);
    }

    // ComputeLocalPositions (idéntico que tu versión)
    public Vector3[] ComputeLocalPositions()
    {
        float[] coords = new float[] { -3.75f, -1.25f, 1.25f, 3.75f };

        var positions = new Vector3[16];
        int idx = 0;

        // Querés que la primera fila sea z = +3.75 y avanzar hacia z = -3.75,
        // mientras que x avanza -3.75 -> 3.75 dentro de cada fila.
        for (int zi = coords.Length - 1; zi >= 0; zi--)   // 3 -> 0 => z: 3.75,1.25,-1.25,-3.75
        {
            for (int xi = 0; xi < coords.Length; xi++)    // 0 -> 3 => x: -3.75,-1.25,1.25,3.75
            {
                positions[idx++] = new Vector3(coords[xi], 0f, coords[zi]);
            }
        }

        // Guardar resultado (para que otras llamadas reutilicen la matriz si no se regenera)
        _localPositions = positions;

        return _localPositions;
    }

    public void SetSelection(int index, bool isOrigin)
    {
        if (index < 0 || index >= 16) return;
        if (isOrigin) selectedOrigin = index;
        else selectedTarget = index;
    }

    // Spawnea **un** prefab (artPoolSO.GetRandomPrefab) en cada selección (origin y target si existen)
    public void SpawnSelected()
    {
        if (_tileRoot == null) _tileRoot = CreateTile();
        var positions = ComputeLocalPositions();
        if (artPoolSO == null)
        {
            Debug.LogWarning("[TilePositionTester] artPoolSO no asignado. No puedo spawnear.");
            return;
        }

        if (selectedOrigin >= 0 && selectedOrigin < positions.Length)
        {
            SpawnAt(selectedOrigin, artPoolSO.GetRandomPrefab());
        }
        if (selectedTarget >= 0 && selectedTarget < positions.Length)
        {
            SpawnAt(selectedTarget, artPoolSO.GetRandomPrefab());
        }
    }

    private void SpawnAt(int index, GameObject prefab)
    {
        if (prefab == null) return;

        // Instanciar desparentado para medir y luego parentear correctamente
        var art = Instantiate(prefab);
        art.name = $"Selected_{index}";
        Vector3 originalWorldScale = art.transform.lossyScale;
        Debug.Log($"SpawnAt {index}: original world scale = {originalWorldScale}");
        Vector3 parentScale = _tileRoot.lossyScale;
        Debug.Log("SpawnAt: parent world scale = " + parentScale);
        art.transform.localScale = new Vector3(
            originalWorldScale.x / Mathf.Max(0.0001f, parentScale.x),
            originalWorldScale.y / Mathf.Max(0.0001f, parentScale.y),
            originalWorldScale.z / Mathf.Max(0.0001f, parentScale.z)
        );
        Debug.Log("SpawnAt: Nueva escala = " + art.transform.localScale);
        float yOffset = ComputeArtYOffsetLocal(art, _tileRoot);
        art.transform.localPosition = ComputeLocalPositions()[index] + Vector3.up * yOffset;
        art.transform.SetParent(_tileRoot, true); // conservar world pos
        _spawned.Add(art.transform);
    }

    // ---------------- helpers adicionales ----------------

    private bool MeasurePrefabBoundsAndWorldScale(GameObject prefab, out Bounds bounds, out Vector3 worldScale)
    {
        bounds = new Bounds(Vector3.zero, Vector3.one);
        worldScale = Vector3.one;
        if (prefab == null) return false;

        // instantiate temporarily (editor/runtime safe)
        GameObject inst = GameObject.Instantiate(prefab);
        inst.hideFlags = HideFlags.HideAndDontSave;

        // try to disable MBs to avoid movement
        var mbs = inst.GetComponentsInChildren<MonoBehaviour>(true);
        var mbStates = new List<(MonoBehaviour mb, bool wasEnabled)>();
        foreach (var mb in mbs)
            if (mb != null) { mbStates.Add((mb, mb.enabled)); try { mb.enabled = false; } catch { } }

        var anims = inst.GetComponentsInChildren<Animator>(true);
        foreach (var a in anims) a.enabled = false;

        var rends = inst.GetComponentsInChildren<Renderer>(true);
        if (rends != null && rends.Length > 0)
        {
            bounds = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) bounds.Encapsulate(rends[i].bounds);
        }
        else
        {
            bounds = new Bounds(inst.transform.position, Vector3.one);
        }

        worldScale = inst.transform.lossyScale;

#if UNITY_EDITOR
        UnityEngine.Object.DestroyImmediate(inst);
#else
        GameObject.Destroy(inst);
#endif

        return true;
    }

    // helper safe div
    private float SafeDiv(float a, float b) { if (Mathf.Abs(b) < 1e-6f) return a / (b < 0 ? -1e-6f : 1e-6f); return a / b; }
    private Vector3 SafeDivideVec(Vector3 a, Vector3 b)
    {
        return new Vector3(SafeDiv(a.x, b.x), SafeDiv(a.y, b.y), SafeDiv(a.z, b.z));
    }

    private int GetNearestPositionIndex(Vector3 localPos)
    {
        var positions = ComputeLocalPositions();
        return GetNearestPositionIndex(localPos, positions, out _);
    }

    // ---------------- end of class ----------------

    // --- helper nuevo: neutraliza la escala para que worldScale quede igual a originalWorldScale ---
    private void NeutralizeScaleToParent(GameObject inst, Transform parent)
    {
        if (inst == null || parent == null) return;
        // world scale actual del instance (antes de modificar)
        Vector3 originalWorldScale = inst.transform.lossyScale;

        // parentear conservando transform world (true): evita saltos de posición.
        inst.transform.SetParent(parent, true);

        // calcular desired local scale de forma que: localScale * parent.lossyScale = originalWorldScale
        Vector3 parentLossy = parent.lossyScale;
        Vector3 newLocal = new Vector3(
            SafeDiv(originalWorldScale.x, parentLossy.x),
            SafeDiv(originalWorldScale.y, parentLossy.y),
            SafeDiv(originalWorldScale.z, parentLossy.z)
        );

        // preservar signos (evita invertir ejes si parent tiene scale negativo)
        newLocal = new Vector3(Mathf.Sign(parentLossy.x) * Mathf.Abs(newLocal.x),
                               Mathf.Sign(parentLossy.y) * Mathf.Abs(newLocal.y),
                               Mathf.Sign(parentLossy.z) * Mathf.Abs(newLocal.z));

        inst.transform.localScale = newLocal;
    }



}
