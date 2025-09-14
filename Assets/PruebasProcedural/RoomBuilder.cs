using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;

public class RoomBuilder : MonoBehaviour
{

    [Header("Parametros")]
    private int cols;
    private float cellSize;
    private RoomInformation roomInformation;
    private IMovementStrategy movementStrategy;
    //luego poner en los ArtPool

    // NUEVO: grupo forzado (lista de object ids) y categoría forzada
    private List<string> _forcedGroup = null;
    private string _forcedCategory = null;

    [Header("Luces")]
    private Light[] StatueLights;

    [Tooltip("Distancia mínima deseada en metros entre el centro de la puerta generada y la puerta del Hall (eje X).")]
    private float desiredDoorSeparation;

    [Header("VariablesInternas")]
    private GameObject[] tiles;
    private TileController[] tilesManagers;
    private IRoomLayout layout;
    private ArtPoolSO artPoolSO;
    public IEnumerator InitCoroutine(int columnas, RoomInformation information, IMovementStrategy movementStrategy, List<string> forcedGroup = null, string forcedCategory = null)

    {
        //aca tengo que modificar todo lo que tenga que ver con la artpool para usar la que traemos por parametro y como obtengo tamaño y esas cosas
        //segunrametne necesite obetener la ArtPool por el tema de los materiales.
        this.roomInformation = information;
        this._forcedGroup = (forcedGroup != null && forcedGroup.Count > 0) ? new List<string>(forcedGroup) : null;
        this._forcedCategory = forcedCategory;
        this.desiredDoorSeparation = information.desiredDoorSeparation;
        // Seleccionar ArtPool por categoría (forcedCategory) buscando en roomInformation.poolGroup.artPools
        if (!string.IsNullOrEmpty(forcedCategory))
        {
            try
            {
                ArtPoolSO found = null;
                var group = roomInformation.poolGroup;
                if (group != null && group.artPools != null)
                {
                    // buscar por categoryName o poolName (ambos checks)
                    found = group.artPools.FirstOrDefault(p => p != null &&
                        (string.Equals(p.categoryName, forcedCategory, StringComparison.OrdinalIgnoreCase)));
                }

                if (found != null) this.artPoolSO = found;
                if (found != null) Debug.Log("Encontro bien la pool a usar en RoomBuilder");
                else this.artPoolSO = roomInformation.poolGroup.GetRandomPool(); // fallback
            }
            catch
            {
                this.artPoolSO = roomInformation.poolGroup.GetRandomPool(); // fallback
            }
        }
        else
        {
            this.artPoolSO = roomInformation.poolGroup.GetRandomPool();
        }


        //this.artPoolSO = roomInformation.poolGroup.GetRandomPool();
        this.artPoolSO.InitMaterialTheme();
        this.cols = columnas;

        // calculo de cuántos Zero swaps habrá (mismo criterio que DoSwapAll)
        int total = columnas*2;
        int baseCount = total / 4;
        int remainder = total % 4;
        int zeroCount = baseCount + remainder;
        // según tu regla: necesitamos minimo (zeroCount + 1) tiles en esquinas (para despistar)
        int requiredCornerTiles = Mathf.Min(total, zeroCount + 1);

        Debug.Log($"[InstantiateMultipleTiles] total={total}, zeroCount={zeroCount}, requiredCornerTiles={requiredCornerTiles}");

        // Elegir indices de tiles que forzaremos a corner
        HashSet<int> forcedCornerTileIndices = new HashSet<int>();
        while (forcedCornerTileIndices.Count < requiredCornerTiles)
            forcedCornerTileIndices.Add(UnityEngine.Random.Range(0, total));




        // --------- NOVEDAD: calcular maxPrefabSize usando DifficultySetsLoader si tenemos group forzado ----------
        Vector3 maxPrefabSize;
        if (_forcedGroup != null && _forcedGroup.Count > 0)
        {
            try
            {
                // Asegurarse de que DifficultySetsLoader tenga este método: GetMaxPrefabSizeForGroup(ArtPoolSO, List<string>, bool editorMode)
                maxPrefabSize = DifficultySetsLoader.GetMaxPrefabSizeForGroup(artPoolSO, _forcedGroup, editorMode: Application.isEditor);
                Debug.Log("Calculo max size bien");
            }
            catch
            {
                // fallback
                maxPrefabSize = artPoolSO.GetMaxPrefabSize();
            }
        }
        else
        {
            maxPrefabSize = artPoolSO.GetMaxPrefabSize();
        }

        //hacer que elija el maximo entre x y z, quizas si es muy chico, como espadas hacer smallCellSize * 2.5f o algo asi
        float smallCellSize = Mathf.Max(maxPrefabSize.x, maxPrefabSize.z);

        float multiplier;
        if (smallCellSize < 1.0f)
            multiplier = 2.25f;
        else
            multiplier=2.0f;
        Debug.Log("Multiplier: " + multiplier + " SmallCellSize: " + smallCellSize);
        float quadrantSize = smallCellSize * multiplier;
        this.cellSize = quadrantSize * 2f;



        roomInformation.setMultiplier(cellSize);

        this.movementStrategy = movementStrategy;

        //VariablesInternas
        tiles = new GameObject[cols * 2];
        tilesManagers = new TileController[cols * 2];
        StatueLights = new Light[cols * 2];

        switch (RoomInformation.GetRandomShape())
        {
            case RoomShape.Rectangle: this.layout = new RectangularLayout(); break;
                //case RoomShape.Cross: layout = new CrossLayout(roomInformation); break;
        }



        // --- ahora llamamos a sub-coroutines que van cediendo el frame ---
        yield return StartCoroutine(BuildRoomCoroutine(forcedCornerTileIndices));
        yield return StartCoroutine(BuildColumnsCoroutine());
        yield return StartCoroutine(BuildFloorCoroutine());
        yield return StartCoroutine(BuildCeilingCoroutine());
        yield return StartCoroutine(BuildWallsCoroutine());
        EnsureDoorSeparationBeforeCorridor();
        yield return StartCoroutine(BuildCorridorCoroutine());

        // 4) Pedir al RoomBuilder crear el trigger y enlazarlo a GameManagerFin.StartMemorising
        var ev = new UnityEvent();
        ev.AddListener(() =>
        {
            Debug.Log("[RoomTestLauncher] Player entró a la sala → StartMemorising()");
            GameManagerFin.Instance.StartMemorising();
            // además bloqueamos la puerta de la sala para que no se abra sola
            SetGeneratedDoorsAutoOpen(false);
        });
        CreateEntryTrigger(ev);


        yield break;
    }

    //Metodos corutinas---------------------------------

    // === Coroutines ===

    // generar celdas y luces de forma incremental
    IEnumerator BuildRoomCoroutine(HashSet<int> forcedCornerTileIndices)
    {
        GameObject barrierPrefab = roomInformation.barrierPool.GetRandomPrefab();
        GameObject statueLightPrefab = roomInformation.statueLightPrefab;
        GameObject hallLightPrefab = roomInformation.hallLightPrefab;
        float scale = this.cellSize / Mathf.Max(0.0001f, roomInformation.unityPlaneSize);
        var mat = artPoolSO.GetThemeTileMat();

        Vector3[] posicionesCeldas = layout.GetTilePositions(roomInformation, cols, cellSize);
        int position = 0;
        int yieldEvery = 2; // ceder cada 3 celdas

        // Si tenemos forcedGroup, creamos una cola PERO mezclada (shuffle) para variar el orden entre partidas
        Queue<string> forcedQueue = null;
        if (_forcedGroup != null && _forcedGroup.Count > 0)
        {
            // shuffle (_forcedGroup) -- Fisher-Yates
            var list = new List<string>(_forcedGroup);
            var rnd = new System.Random();
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                var tmp = list[j];
                list[j] = list[i];
                list[i] = tmp;
            }
            forcedQueue = new Queue<string>(list);
        }

        //falta meter el tema de las posiciones de los elementos para luego los swaps.
        int created = 0;
        foreach (var pos in posicionesCeldas)
        {
            bool paseMitad = position >= cols;

            // 1) Crear un plane para la celda
            GameObject tile = GameObject.CreatePrimitive(PrimitiveType.Plane);
            tile.name = $"Tile_{position}";
            tile.transform.parent = transform;
            tile.transform.position = pos;
            tile.transform.localScale = new Vector3(scale, scale, scale);

            // 2) Asignar el material de la tile
            var rend = tile.GetComponent<Renderer>();
            if (mat != null) rend.sharedMaterial = mat;


            // 3) decidir forced index para esta tile si corresponde (de aca para abajo)

            TileController tc = tile.AddComponent<TileController>();

            GameObject prefabArte = null;
            
            if (forcedQueue != null && forcedQueue.Count > 0)
            {
                // tomar next oid de la cola
                string oid = forcedQueue.Dequeue();
                // resolver prefab a partir de oid (si tu ArtPoolSO tiene ResolvePrefabForObjectId lo usamos)
                // Intentar resolver desde ArtPoolSO (metodo disponible en ArtPoolSO)
                try
                {
                    prefabArte = artPoolSO.ResolvePrefabForObjectId(oid, Application.isEditor);
                }
                catch
                {
                    prefabArte = null;
                }

                // fallback a Resources.Load por nombre final
                if (prefabArte == null)
                {
                    string nameOnly = oid.Split(new char[] { '/', '\\' }).Last();
                    prefabArte = Resources.Load<GameObject>(nameOnly);
                }

                // volver a encolar para comportamiento cíclico (si querés que no repita, no reencolar)
                //forcedQueue.Enqueue(oid);
            }
            else
            {
                prefabArte = artPoolSO.GetRandomPrefab();
            }


            int forcedIndex = -1;
            if (forcedCornerTileIndices.Contains(created))
            {
                int[] corners = new int[] { 0, 3, 12, 15 };
                forcedIndex = corners[UnityEngine.Random.Range(0, corners.Length)];
            }
            tc.Initialize(prefabArte, paseMitad, barrierPrefab, cellSize, forcedIndex, roomInformation);


            if (tc != null && InteractionUIController.Instance != null)
                tc.OnTileClicked += InteractionUIController.Instance.Show;

            //--------------------------Luces----------------------------
            // --- Instanciación igual que antes ---
            GameObject lightObj = Instantiate(statueLightPrefab, Vector3.zero, Quaternion.identity, transform);
            lightObj.transform.parent = tile.transform;
            lightObj.transform.localPosition = new Vector3(0, roomInformation.wallHeight / scale, (paseMitad ? 1f : -1f) * 6.88f);
            lightObj.transform.localRotation = Quaternion.Euler(0, (paseMitad ? 0 : 180), 0);

            Light light = lightObj.GetComponentInChildren<Light>();

            // --- Ajuste simple basado en tamaño de la tile ---
            // Intentamos obtener el tamaño en mundo; si no hay Renderer usamos localScale como fallback.
            float tileAverageSize = 1f;
            Renderer tileRenderer = tile.GetComponent<Renderer>();
            if (tileRenderer != null)
            {
                Vector3 s = tileRenderer.bounds.size;
                tileAverageSize = (s.x + s.z) * 0.5f;
            }
            else
            {
                // fallback razonable (si tu proyecto tiene 'cellSize' úsalo en lugar de esto)
                tileAverageSize = Mathf.Max(0.0001f, Mathf.Max(tile.transform.localScale.x, tile.transform.localScale.z));
            }

            // referenceSize = tamaño "normal" con el que diseñaste (ajustá si tenés un valor mejor)
            float referenceSize = (this.cellSize > 0f) ? this.cellSize : 1f;

            // factor relativo: >1 si la tile es más grande que la referencia, <1 si es más chica
            float sizeFactor = tileAverageSize / referenceSize;

            // valores base (los que tenías/querés por defecto)
            float baseOuter = 80f;   // outer spot angle por defecto
            float baseInner = 80f;   // inner (si querés que sea porcentaje, se recalcula abajo)
            float baseRange = 10f;   // range por defecto

            // Aplicar factor — ajustar sensibilidad si quieres (p.ej. Mathf.Pow para respuesta no lineal)
            float outerAngle = baseOuter * sizeFactor;
            outerAngle = Mathf.Clamp(outerAngle, 6f, 140f); // límites razonables

            // inner como porcentaje del outer (mantener coherencia visual)
            float innerAngle = Mathf.Clamp(outerAngle * 0.8f, 1f, outerAngle);

            // range escala también con la tile (si es más grande, rango mayor)
            float range = baseRange * sizeFactor;
            range = Mathf.Clamp(range, 0.5f, 50f);

            // Aplicar al Light (compatibilidad con versiones modernas)
            if (light != null)
            {
                light.spotAngle = outerAngle;
#if UNITY_2019_1_OR_NEWER
                light.innerSpotAngle = innerAngle;
#endif
                light.range = range;
                light.enabled = false; // setea true temporal si querés debug
            }

#if UNITY_EDITOR
            Debug.LogFormat("[RoomBuilder] light scaled: tileSize={0:F2} factor={1:F2} outer={2:F1} inner={3:F1} range={4:F2}",
                            tileAverageSize, sizeFactor, outerAngle, innerAngle, range);
#endif


            //--------------------------Luces----------------------------




            StatueLights[position] = light;
            tiles[position] = tile;
            tilesManagers[position] = tc;
            position++;
            created++;
            if (position % yieldEvery == 0)
                yield return null; // cedemos control para que no se congele

            
        }
    }

    // columnas
    IEnumerator BuildColumnsCoroutine()
    {
        GameObject columnaPrefab = roomInformation.columnPool.GetRandomPrefab();
        Vector3[] posicionesCeldas = layout.GetColumnsPositions(roomInformation, cols, cellSize);
        int counter = 0;
        foreach (var pos in posicionesCeldas)
        {
            Instantiate(columnaPrefab, pos, Quaternion.identity, transform);
            counter++;
            if (counter % 4 == 0) yield return null; // ceder cada 5 columnas
        }
    }

    // floor
    IEnumerator BuildFloorCoroutine()
    {
        var fp = layout.GetFloorPositions(roomInformation, cols, cellSize);
        Vector3 pos = fp[0];
        Vector3 scale = fp[1];

        var matF = artPoolSO.GetThemeFloorMat()
                   ?? roomInformation.floorMat.GetRandomPrefab();

        layout.GetPrefabFloor(pos, scale, this.transform, matF);
        yield return null;
    }

    // ceiling
    IEnumerator BuildCeilingCoroutine()
    {
        var cp = layout.GetCeilingPositions(roomInformation, cols, cellSize);
        Vector3 pos = cp[0];
        Vector3 scale = cp[1];
        Quaternion rot = Quaternion.Euler(180, 0, 0);

        var matC = artPoolSO.GetThemeCeilingMat()
                   ?? roomInformation.ceilingMat.GetRandomPrefab();

        layout.GetPrefabCeiling(pos, scale, rot, this.transform, matC);
        yield return null;
    }

    // walls
    IEnumerator BuildWallsCoroutine()
    {
        var wp = layout.GetWallPositions(roomInformation, cols, cellSize);
        var matW = artPoolSO.GetThemeWallMat()
                   ?? roomInformation.wallMat.GetRandomPrefab();
        var door = artPoolSO.GetThemeDoor() ?? roomInformation.doorPool.GetRandomPrefab();

        layout.GetPrefabWallEste(wp[0], wp[1], this.transform, matW);
        layout.GetPrefabWallEste(wp[2], wp[3], this.transform, matW);
        layout.GetPrefabWallEste(wp[4], wp[5], this.transform, matW);

        layout.BuildWallOesteWithDoor(wp[6], wp[7], door, matW, this.transform);
        yield return null;
    }

    // corridor
    IEnumerator BuildCorridorCoroutine()
    {
        var matWall = artPoolSO.GetThemeWallMat()
                   ?? roomInformation.wallMat.GetRandomPrefab();
        var matCeling = artPoolSO.GetThemeCeilingMat()
                   ?? roomInformation.ceilingMat.GetRandomPrefab();
        var matFloor = artPoolSO.GetThemeFloorMat()
                   ?? roomInformation.floorMat.GetRandomPrefab();

        layout.BuildCorridor(roomInformation, this.transform, matFloor, matWall, matCeling);
        yield return null;
    }

    // En RoomBuilder.cs
    public TileController[] GetTileControllers()
    {
        return tilesManagers;
    }

    public Vector3 GetWaitingStationPosition()
    {
        return layout.GetWaitingStationPosition();
    }


    //---------------------------------------------

    //generar celdas y luces.
    void BuildRoom()
    {
        //GameObject tilePrefab = roomInformation.tilePool.GetRandomPrefab(); Cambiar por el amterial
        GameObject barrierPrefab = roomInformation.barrierPool.GetRandomPrefab();
        GameObject statueLightPrefab = roomInformation.statueLightPrefab;
        GameObject hallLightPrefab = roomInformation.hallLightPrefab;
        float scale = this.cellSize / roomInformation.unityPlaneSize;
        var mat = artPoolSO.GetThemeTileMat();  // devuelve Material

        Vector3[] posicionesCeldas = layout.GetTilePositions(roomInformation, cols, cellSize);
        int position = 0;
        foreach (var pos in posicionesCeldas)
        {
            bool paseMitad = position >= cols;

            // 1) Crear un plane para la celda
            GameObject tile = GameObject.CreatePrimitive(PrimitiveType.Plane);
            tile.name = $"Tile_{position}";
            tile.transform.parent = transform;
            tile.transform.position = pos;
            tile.transform.localScale = new Vector3(scale, 1, scale);

            // 2) Asignar el material de la tile
            var rend = tile.GetComponent<Renderer>();
            
            if (mat != null) rend.sharedMaterial = mat;

            TileController tc = tile.AddComponent<TileController>();
            GameObject prefabArte = artPoolSO.GetRandomPrefab();
            tc.Initialize(prefabArte, paseMitad, barrierPrefab,cellSize,2, roomInformation);
            //hago que en clickear la obra se llame al show de InteractionUIController
            if (tc != null && InteractionUIController.Instance != null)
            {
                tc.OnTileClicked += InteractionUIController.Instance.Show;
            }
            else
            {
                Debug.LogError($"[RoomBuilder] No se pudo suscribir OnTileClicked: tc={tc != null}, IUIController={InteractionUIController.Instance != null}");
            }

            // Instanciar luces para cada tile
            //Vector3 positionLight = new Vector3(pos.x, roomInformation.wallHeight, pos.z); 



            GameObject lightObj = Instantiate(statueLightPrefab, new Vector3(0,0,0), Quaternion.identity, transform);
            lightObj.transform.parent = tile.transform;
            lightObj.transform.localPosition = new Vector3(0, roomInformation.wallHeight, (paseMitad ? 1f : -1f) * 6.88f); ;
            lightObj.transform.localRotation = Quaternion.Euler(0, (paseMitad ? 0 : 180) , 0);
            Light light = lightObj.GetComponentInChildren<Light>();
            
        
 /*
            float mult = artPoolSO.getSizeCell();
            // 1) Estatua (celda)
            light.range = light.range * mult;      // ej: 10 * 2 = 20
            light.spotAngle = this.Map(80, mult);  // ej: 80 * 2 = 160 (o clamp si te pasa de 120)
            light.innerSpotAngle =  (mult==1) ? 80f : this.Map(80,mult) *0.9f;  // ej: 80 * 2 = 160 (o clamp si te pasa de 120
 */
            light.enabled = false;
            
            //agregar cada uno en position
            StatueLights[position] = light;
            tiles[position] = tile;
            tilesManagers[position] = tc;
            position++;
        }
    }

    void BuildColumns()
    {
        GameObject columnaPrefab = roomInformation.columnPool.GetRandomPrefab();
        Vector3[] posicionesCeldas = layout.GetColumnsPositions(roomInformation, cols, cellSize);
        foreach (var pos in posicionesCeldas)
        {
            Instantiate(columnaPrefab, pos, Quaternion.identity, transform);
        }
    }

    void BuildFloor()
    {
        // obtenemos posiciones y escala
        var fp = layout.GetFloorPositions(roomInformation, cols, cellSize);
        Vector3 pos = fp[0];
        Vector3 scale = fp[1];

        // elegimos material override o por defecto
        var matF = artPoolSO.GetThemeFloorMat()
                   ?? roomInformation.floorMat.GetRandomPrefab();

        // invocamos al layout
        layout.GetPrefabFloor(pos, scale, this.transform, matF);
    }


    void BuildCeiling()
    {

        var cp = layout.GetCeilingPositions(roomInformation, cols, cellSize);
        Vector3 pos = cp[0];
        Vector3 scale = cp[1];
        Quaternion rot = Quaternion.Euler(180, 0, 0);

        var matC = artPoolSO.GetThemeCeilingMat()
                   ?? roomInformation.ceilingMat.GetRandomPrefab();

        layout.GetPrefabCeiling(pos, scale, rot, this.transform, matC);
    }

    void BuildWalls()
    {

        // posiciones:
        var wp = layout.GetWallPositions(roomInformation, cols, cellSize);

        var matW = artPoolSO.GetThemeWallMat()
                   ?? roomInformation.wallMat.GetRandomPrefab();


        // Norte
        layout.GetPrefabWallEste(wp[0], wp[1], this.transform, matW);



        // Sur
        layout.GetPrefabWallEste(wp[2], wp[3], this.transform, matW);


        // Este
        layout.GetPrefabWallEste(wp[4], wp[5], this.transform, matW);


        // Oeste con puerta
        // posiciones[6] = escala, posiciones[7] = posición
        layout.BuildWallOesteWithDoor(wp[6], wp[7],artPoolSO.GetThemeDoor(),matW,this.transform);
    }

    
    void BuildCorridor()
    {
        // 4) Material del pasillo: puedes tomarlo del ArtPool o del RoomInformation
        var mat = artPoolSO.GetThemeWallMat()
               ?? roomInformation.wallMat.GetRandomPrefab();

        layout.BuildCorridor(roomInformation, this.transform, mat, mat, mat);
    }
    //Metodos para usar desde GameManager
    public void DisableEstatueLigth(int i)
    {
        StatueLights[i].enabled = false;
    }
    public void EnableEstatueLigth(int i)
    {
        StatueLights[i].enabled = true;
    }
    public void MoveStatues()
    {

        movementStrategy.Move(tilesManagers,StatueLights);
    }


    //Ademas habilitan o desabilitan la interaccion
    public void EnableEstatueLigth(int i, bool b)
    {
        EnableEstatueLigth(i);
        tilesManagers[i].setInteractingMode(b);
    }
    public void DisableEstatueLigth(int i, bool b)
    {
        DisableEstatueLigth(i);
        tilesManagers[i].setInteractingMode(b);
    }

    public void GetResultados()
    {
        LogManager.Instance.SendLogsAndWrapSession();
    }
    public void SetGeneratedDoorsAutoOpen(bool enable)
    {
        if (layout != null)
            layout.SetGeneratedDoorsAutoOpen(enable);
    }

    public GameObject CreateEntryTrigger(UnityEngine.Events.UnityEvent onPlayerEnter)
    {
        if (layout != null)
            return layout.CreateEntryTrigger(this.transform, onPlayerEnter);
        return null;
    }

    //para el shape de las luces:
    float Map(float x, float M)
    {
        return x + (M - 1) * (260f - 3f * x);
    }

    public void AbleDoor()
    {

        layout.SetGeneratedDoorsOutline();
    }

    private void EnsureDoorSeparationBeforeCorridor()
    {
        if (layout == null)
        {
            Debug.LogWarning("[RoomBuilder] No hay layout para ajustar separación de puertas.");
            return;
        }

        // obtener puerta generada por el layout (puede ser null si algo falló)
        GameObject generatedDoor = layout.GetGeneratedDoorObject();
        if (generatedDoor == null)
        {
            Debug.LogWarning("[RoomBuilder] La puerta generada no existe todavía - no puedo asegurar separación.");
            return;
        }

        // localizar la puerta del Hall (tu proyecto usa "Hall"/"HallDoor" — mantener consistente)
        GameObject hall = GameObject.Find("Hall");
        if (hall == null)
        {
            Debug.LogWarning("[RoomBuilder] No encontré el objeto 'Hall' en la escena. No puedo asegurar separación.");
            return;
        }
        Transform hallDoorT = hall.transform.Find("HallDoor");
        if (hallDoorT == null)
        {
            Debug.LogWarning("[RoomBuilder] No encontré 'Hall/HallDoor' en la jerarquía del Hall.");
            return;
        }

        Vector3 genPos = generatedDoor.transform.position;
        Vector3 hallPos = hallDoorT.position;

        // distancia actual en X
        float currentDistX = Mathf.Abs(genPos.x - hallPos.x);

        if (currentDistX >= desiredDoorSeparation)
        {
            // todo ok
            return;
        }

        // necesitamos desplazar la sala entera en X en la dirección que aleje la puerta de la del Hall
        float delta = desiredDoorSeparation - currentDistX;
        float direction = Mathf.Sign(genPos.x - hallPos.x);
        if (direction == 0f)
        {
            // si están exactamente en la misma X, elegimos desplazar hacia +X (o podrías usar -X)
            direction = 1f;
        }

        float translationX = direction * delta;
        Debug.Log($"[RoomBuilder] Ajustando sala en X por {translationX} para mantener separación mínima {desiredDoorSeparation} (actual {currentDistX}).");

        // trasladar el GameObject padre que contiene la sala (this.transform)
        this.transform.position += new Vector3(translationX, 0f, 0f);

        // NOTA: mover el transform padre actualiza automáticamente las posiciones de tiles / paredes / puerta.


    }



}
