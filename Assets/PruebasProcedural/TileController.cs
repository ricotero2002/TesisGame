using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEditor.Rendering.LookDev;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;
using static UnityEngine.GraphicsBuffer;

public enum Similitud { AltaSimilitud, BajaSimilitud, NoSimilitud, NoSeMovio }

public class TileController : MonoBehaviour
{
    [Header("Art")]
    public GameObject artObject;      // La obra dentro de la celda
    [Header("Movimiento")]
    // Guarda la última posición usada

    private Similitud similitud= Similitud.NoSeMovio;                // �se movi� esta ronda?
    private bool itMoved=false;
    private bool lastGuessCorrect;
    [Header("Interaccion")]
    private Outline outline;
    private bool isPlayerNearby = false;
    private Collider collider;
    private bool interactingMode = false;
    [Header("Posiciones")]
    private int PrevIndex;
    private int chosenIndex;
    private static Vector3[] internalOffsets;   // n×n dentro de cada cuadrante
    private RoomInformation inf;

    private SwapEntry swapEntry;


    // Evento para notificar al GameManager cuando el usuario clica esta tile
    public event Action<TileController> OnTileClicked;
    public bool IsPlayerNearby() => isPlayerNearby;
    // Coloca el objeto en una partici�n aleatoria inicial
    public int GetCurrentIndex() => chosenIndex;

    public void Initialize(GameObject prefab, bool cambioFila, GameObject BarrierPrefab,float cellSize, int forcedSpawnIndex, RoomInformation inf)
    {

        this.inf = inf;
        if (prefab == null)
        {
            Debug.LogWarning("[TileController] Initialize: prefab null");
            return;
        }

        if(internalOffsets == null)
        {
            ComputeInternalOffsetsFixed();
        }
        SetupProximityTrigger(inf.desiredHeightMeters,inf.padding);

        // Instanciamos prefab SIN parentear para capturar su world scale original
        GameObject art = Instantiate(prefab);
        art.name = prefab.name; // conservar nombre base
        Vector3 originalWorldScale = art.transform.lossyScale;

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

        // Parentear al tile (este transform)
        art.transform.SetParent(this.transform, true);


        // 3) Obtener escala mundial del parent (tileRoot)
        Vector3 parentLossy = this.transform.lossyScale;

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

        // restablecer MBs/Animators
        for (int i = 0; i < monoStates.Count; i++)
            try { monoStates[i].mb.enabled = monoStates[i].enabled; } catch { }
        foreach (var a in anims) if (a != null) a.enabled = true;

        artObject = art;


        // Elegir spawn index (forced o random)
        chosenIndex = -1;
        if (forcedSpawnIndex >= 0 && forcedSpawnIndex < 16)
            chosenIndex = forcedSpawnIndex;
        else
            chosenIndex = UnityEngine.Random.Range(0, 16);


        /*
        // Crear barrier si hace falta
        if (BarrierPrefab != null)
        {
            Vector3 PositionBarrier = new Vector3(0, 1.666f, (cambioFila ? 1f : -1f) * 5.91f);
            var b = Instantiate(BarrierPrefab, transform);
            b.transform.localPosition = PositionBarrier;
        }
        */


        // Posicionar (local) base, luego alinear en Y por mundo
        Vector3 LocalPosition = internalOffsets[chosenIndex];
        float yOffset = ComputeArtYOffsetLocal(art, this.transform);
        art.transform.localPosition = LocalPosition + Vector3.up * yOffset;



        if (artObject.GetComponent<Collider>() == null)
        {
            artObject.AddComponent<BoxCollider>();
        }
        //var clickHandler = artObject.AddComponent<ArtClickHandler>();
        //clickHandler.tileController = this;
        if (cambioFila)
        {
            Vector3 e = artObject.transform.localEulerAngles;
            e.y = (e.y + 180f) % 360f;
            artObject.transform.localEulerAngles = e;

            AlignSpawnToTileSurface(artObject.transform, this.transform);
        }



        //glow cuando te acercas
        outline = artObject.AddComponent<Outline>();
        outline.OutlineWidth = 10f;
        outline.OutlineColor = Color.yellow;
        outline.enabled = false;




    }

    public void SetSwapEntry(SwapEntry entry)
    {
        this.swapEntry = entry;
    }

    // ---------- internal move helper ----------
    private void MoveToPosition(int newIndex, string moveType)
    {
        if (artObject == null)
        {
            Debug.LogWarning("[TileController] MoveToPosition: artObject null");
            return;
        }

        // en vez de usar yOffset local, fija la posición local base:
        artObject.transform.localPosition = internalOffsets[newIndex];
        // y luego ajusta en mundo
        AlignSpawnToTileSurface(artObject.transform, this.transform);

        Debug.Log($"[DoSwapOnInstance] {this.gameObject.name} - {artObject.name}: moved {PrevIndex} -> {chosenIndex} (mode {moveType})");


    }

    public bool MoveLowSimilarity()
    {
        /*
        if (_lastQuadrant < 0) // si no hay aún posición
        {
            // elegimos aleatorio libre
            int q = UnityEngine.Random.Range(0, quadrantCenters.Length);
            int oo = UnityEngine.Random.Range(0, internalOffsets.Length);
            MoveToPosition(q, oo, "HighSimilarity (init)");
            return;
        }

        int o;
        do
        {
            o = UnityEngine.Random.Range(0, internalOffsets.Length);
        } while (o == _lastOffset);

       
        */
        var candidates = FindNonAdjacentCandidates(chosenIndex);
        if (candidates.Count <= 0)
        {
            Debug.LogWarning("[TileController] MoveLowSimilarity: no other index in same quadrant");
            return false;
        }
        PrevIndex = chosenIndex;
        chosenIndex = candidates[UnityEngine.Random.Range(0, candidates.Count)];
        itMoved = true;
        similitud = Similitud.BajaSimilitud;
        MoveToPosition(chosenIndex, "LowSimilarity");
        return true;
    }

    /// <summary>
    /// Baja similitud: cuadrante adyacente (no diagonal), offset cualquiera.
    /// </summary>
    public bool MoveHighSimilarity()
    {

        var sameQ = FindIndicesInSameQuadrant(chosenIndex, internalOffsets);
        if (sameQ.Count <= 0)
        {
            Debug.LogWarning("[TileController] MoveHighSimilarity: no other index in same quadrant");
            return false;
        }
        PrevIndex = chosenIndex;
        chosenIndex = sameQ[UnityEngine.Random.Range(0, sameQ.Count)];

        /*
        // definimos vecinos adyacentes a cada índice
        int[] neigh;
        switch (_lastQuadrant)
        {
            case 0: neigh = new[] { 1, 2 }; break;    // TL → TR,B L
            case 1: neigh = new[] { 0, 3 }; break;    // TR → TL,BR
            case 2: neigh = new[] { 0, 3 }; break;    // BL → TL,BR
            default: neigh = new[] { 1, 2 }; break;    // BR → TR,BL
        }
        int q = neigh[UnityEngine.Random.Range(0, neigh.Length)];
        int o = UnityEngine.Random.Range(0, internalOffsets.Length);
        */

        itMoved = true;
        similitud = Similitud.AltaSimilitud;
        MoveToPosition(chosenIndex, "HighSimilarity");
        return true;
    }

    /// <summary>
    /// Sin similitud: cuadrante opuesto (diagonal), offset cualquiera.
    /// </summary>
    public bool MoveNoSimilarity()
    {/*
        // Si nunca nos hemos movido aún, caemos en alta similitud
        if (_lastQuadrant < 0)
        {
            MoveHighSimilarity();
            return;
        }

        // 1) Determinar cuadrante opuesto
        int opp = (_lastQuadrant + 2) % 4;

        // 2) Calcular posición previa absoluta
        Vector3 prevPos = quadrantCenters[_lastQuadrant] + internalOffsets[_lastOffset];

        // 3) Buscar el offset en 'opp' que maximice la distancia a prevPos
        float maxDist = -1f;
        int bestOffset = 0;
        for (int i = 0; i < internalOffsets.Length; i++)
        {
            Vector3 candPos = quadrantCenters[opp] + internalOffsets[i];
            float d = Vector3.Distance(candPos, prevPos);
            if (d > maxDist)
            {
                maxDist = d;
                bestOffset = i;
            }
        }
        */
        if(IsCornerIndex(chosenIndex) == false)
        {
            Debug.Log("No era una esquina el indice: " + chosenIndex + ", del prefab: "+artObject.name);
            return false;
        }
        PrevIndex = chosenIndex;
        // ahora currentIndex es esquina; hacemos move a opuesta
        chosenIndex = GetOppositeCornerIndex(chosenIndex);

        itMoved = true;
        similitud = Similitud.NoSimilitud;
        // 4) Movernos allí
        MoveToPosition(chosenIndex, "NoSimilarity");
        return true;
    }


    // Registra si el jugador acert� o no:
    // Registra si el jugador acertó o no y datos extra (movimiento, nombre del arte, última posición)
    public void RegisterResult(bool guessedMoved, int posicion,GameObject efecto,float reactionMs)
    {
        // Último resultado booleano
        lastGuessCorrect = (guessedMoved == itMoved);

        // Usuario seguro (fallback si SessionManager o user es null)
        string user = SessionManager.Instance?.user?.Username ?? "Guest";

        // Texto legible
        string respuesta = guessedMoved ? "Se movio" : "No se movio";
        string movimientoEnum = similitud.ToString();               // Ninguno/Cerca/Lejos
        string resultado = lastGuessCorrect? "Same" : "Different";

        // Nombre del arte (prefab/instancia)
        string artName = (artObject != null) ? artObject.name : "Unknown";

        // Información de posición / última ubicación interna
        int q = chosenIndex;
        int o = PrevIndex;
        Vector3 worldPos = this.transform.position;



        // JSON-like para descripción (útil para logs)
        TrialLog trial = new TrialLog();
        trial.trial_index = posicion;
        var objectid = artObject.GetComponent<ModelTag>();
        trial.object_id = objectid.objectId;
        trial.object_category=GameManagerFin.Instance.GetTrialMeta().object_category;
        trial.object_subpool = GameManagerFin.Instance.GetTrialMeta().object_subpool;
        trial.object_similarity_label = similitud.ToString();
        trial.object_actual_moved = itMoved;
        trial.participant_said_moved = guessedMoved;
        trial.response = resultado;
        trial.reaction_time_ms = (int)Mathf.Round(reactionMs);
        trial.memorization_time_ms = (int)Mathf.Round(GameManagerFin.Instance.GetTrialMeta().memorization_time_ms);
        trial.swap_event= GameManagerFin.Instance.GetTrialMeta().swap_event;
        trial.swap_history= swapEntry;

        /*
        string description = $"{{" +
            $"\"usuario\": \"{user}\", " +
            $"\"respuesta\": \"{respuesta}\", " +
            $"\"movimientoEnum\": \"{movimientoEnum}\", " +
            $"\"resultado\": \"{resultado}\", " +
            $"\"posicionIndice\": {posicion}, " +
            $"\"NuevoIndice\": {q}, " +
            $"\"PrevIndex\": {o}, " +
            $"\"artName\": \"{artName}\"" +
            $"}}";
        */
        SessionManager.Instance.AddTrial(trial);

        //Debug.Log($"[TileController.RegisterResult] {description}");

        //LogData log = new LogData(user, event_type, description, timestamp, worldPos.x, worldPos.y, worldPos.z);
        //LogManager.Instance.AddLog(log);

        if (guessedMoved == true)
        {
            var efectoInstanciado = Instantiate(efecto, this.artObject.transform);
            efectoInstanciado.transform.localScale = this.artObject.transform.localScale;
        }
            

    }

    //setear Modo interacting
    public void setInteractingMode(bool b)
    {
        interactingMode = b;
        if (b == false) //porque cuando se desactiva el modo porque el chabon vota no entaria el trigger exit
        {
            outline.enabled = false;
            isPlayerNearby = false;
        }
    }


    // Cuando el player entra en el trigger
    void OnTriggerEnter(Collider other)
    {
        if (interactingMode && other.CompareTag("Player") )
        {
            Debug.Log("Entre pa");
            isPlayerNearby = true;
            outline.enabled = true;
            if (GameManagerFin.Instance != null && GameManagerFin.Instance.IsAwaitingUserResponse())
            { 
               GameManagerFin.Instance.OnPlayerEnterTile(this);
            }

        }
    }
    // Cuando el player sale en el trigger
    void OnTriggerExit(Collider other)
    {
        if (interactingMode && other.CompareTag("Player"))
        {
            Debug.Log("Sali pa");
            isPlayerNearby = false;
            outline.enabled = false;
            if (GameManagerFin.Instance != null && GameManagerFin.Instance.IsAwaitingUserResponse())
            {
                GameManagerFin.Instance.OnPlayerLeftTile(this);
            }
        }
    }

    public void ShowEndResult()
    {
        if (outline == null || lastGuessCorrect == null) return;

        outline.enabled = true;
        outline.OutlineWidth = 8f;
        outline.OutlineColor = (lastGuessCorrect ? Color.green : Color.red);
    }
    

    public void InvokeOnTileClicked()
    {
        OnTileClicked?.Invoke(this);
    }

    public void MoveToPosition(Vector3 newPosition, Quaternion rotation)
    {
        this.gameObject.transform.localPosition = newPosition;
        this.gameObject.transform.localRotation = rotation;
    }

    ///-------------HELPERS----------------

    // grilla fija (coordenadas de pruebas)
    private void ComputeInternalOffsetsFixed()
    {
        float[] coords = new float[] { -3.75f, -1.25f, 1.25f, 3.75f };
        var list = new List<Vector3>();
        foreach (var x in coords)
            foreach (var z in coords)
                list.Add(new Vector3(x, 0f, z));
        internalOffsets = list.ToArray(); // length 16 -> usamos offsetsPerQuadrant = 4 (4 per quadrant)
        // Note: we keep offsetsPerQuadrant = 4 because we conceptually group 4 offsets per quadrant (here mapping done via index math)
    }


    /// <summary>
    /// Alinea en mundo el punto minY de los renderers del art con la superficie del tile (tileTransform).
    /// </summary>
    private void AlignSpawnToTileSurface(Transform art, Transform tileTransform, float epsilon = 0.001f)
    {
        if (art == null || tileTransform == null) return;
        var rends = art.GetComponentsInChildren<Renderer>();
        if (rends == null || rends.Length == 0) return;

        Bounds gb = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) gb.Encapsulate(rends[i].bounds);

        float worldMinY = gb.min.y;
        float tileSurfaceWorldY = tileTransform.TransformPoint(Vector3.zero).y;

        float delta = (tileSurfaceWorldY + epsilon) - worldMinY;
        art.transform.position += new Vector3(0f, delta, 0f);
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

    private bool IsCornerIndex(int idx)
    {
        return idx == 0 || idx == 3 || idx == 12 || idx == 15;
    }

    private int GetOppositeCornerIndex(int idx)
    {
        // 0 <-> 15? Wait: depende de cómo ordenaste la grilla.
        // Con tu orden (primera fila z=+3.75, x -3.75..3.75):
        // indices: row0: 0,1,2,3 (z=+3.75) ... row3: 12,13,14,15 (z=-3.75)
        // esquinas: top-left = 0, top-right = 3, bottom-left = 12, bottom-right = 15
        switch (idx)
        {
            case 0: return 15;  // TL -> BR
            case 3: return 12;  // TR -> BL
            case 12: return 3;  // BL -> TR
            case 15: return 0;  // BR -> TL
            default: return idx;
        }
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

    // Llamar: SetupProximityTrigger(desiredHeightMeters: 2.0f, padding:1.05f);
    public void SetupProximityTrigger(float desiredHeightMeters = 2f, float padding = 1.05f)
    {
        // eliminar triggers previos creados por este script
        var existing = GetComponents<Collider>();
        foreach (var c in existing)
            if (c != null && c.isTrigger) Destroy(c);

        Renderer[] rends = GetComponentsInChildren<Renderer>();
        if (rends == null || rends.Length == 0)
        {
            Debug.LogWarning($"[TileController] No renderers en {gameObject.name}, creando fallback Capsule.");
            CreateCapsuleFallback(desiredHeightMeters, padding);
            return;
        }

        // bounds combinados en espacio mundial
        Bounds combined = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) combined.Encapsulate(rends[i].bounds);

        // Floor (suelo) y top deseado en WORLD space
        float worldFloorY = combined.min.y;
        float worldTopY = worldFloorY + Mathf.Max(desiredHeightMeters, 0.01f); // evitar 0
        float heightWorld = worldTopY - worldFloorY;
        if (heightWorld <= 0f) heightWorld = 0.1f;

        // Centro en WORLD del collider (en Y)
        float centerWorldY = worldFloorY + heightWorld * 0.5f;

        // Tamaño en X/Z: usar bounds.size.x/z y padding
        float worldSizeX = combined.size.x * padding;
        float worldSizeZ = combined.size.z * padding;
        // mínimos razonables
        worldSizeX = Mathf.Max(worldSizeX, 0.2f);
        worldSizeZ = Mathf.Max(worldSizeZ, 0.2f);


        Vector3 worldCenter = new Vector3(combined.center.x, centerWorldY, combined.center.z);

        // aplicar centerYOffset opcional (en metros), suma en WORLD Y antes de convertir
        float centerYOffsetWorld = inf.centerYOffset; // si quieres un offset absoluto en metros
        worldCenter.y += centerYOffsetWorld;

        // --- Conversión correcta world -> local usando transform.lossyScale ---
        Vector3 lossy = transform.lossyScale;
        if (Mathf.Approximately(lossy.x, 0f)) lossy.x = 1e-6f;
        if (Mathf.Approximately(lossy.y, 0f)) lossy.y = 1e-6f;
        if (Mathf.Approximately(lossy.z, 0f)) lossy.z = 1e-6f;

        // worldSizeX/worldSizeZ/heightWorld son tamaños en WORLD space (metros).
        Vector3 localSize = new Vector3(worldSizeX / lossy.x, heightWorld / lossy.y, worldSizeZ / lossy.z);

        // asegurar positivos
        localSize = new Vector3(Mathf.Abs(localSize.x), Mathf.Abs(localSize.y), Mathf.Abs(localSize.z));

        // center: convertir punto world center a local space (esto está bien)
        Vector3 localCenter = transform.InverseTransformPoint(worldCenter);

        // Crear/ajustar BoxCollider
        BoxCollider box = gameObject.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = localSize;
        box.center = localCenter;

        // Debug (útil para comprobar por qué no se expandía)
        Debug.Log($"[TileTrigger] worldSizeX={worldSizeX:F3} worldSizeZ={worldSizeZ:F3} heightWorld={heightWorld:F3} padding={padding}");
        Debug.Log($"[TileTrigger] lossyScale={lossy} -> localSize={localSize} box.center(local)={localCenter}");
        // guardarlo si lo necesitas
        collider = box;
    }

    // Fallback capsule que también respeta desiredHeightMeters como altura
    void CreateCapsuleFallback(float desiredHeightMeters, float padding = 1.05f)
    {
        // eliminar triggers previos
        var existing = GetComponents<Collider>();
        foreach (var c in existing)
            if (c != null && c.isTrigger) Destroy(c);

        CapsuleCollider cap = gameObject.AddComponent<CapsuleCollider>();
        cap.isTrigger = true;
        cap.direction = 1; // eje Y

        // usar escala como referencia
        float baseX = Mathf.Max(transform.localScale.x, 0.2f);
        float baseZ = Mathf.Max(transform.localScale.z, 0.2f);
        float worldRadius = Mathf.Max(baseX, baseZ) * 0.5f * padding;

        cap.radius = Mathf.Abs(transform.InverseTransformVector(new Vector3(worldRadius, 0, 0)).x);
        cap.height = desiredHeightMeters;
        // center: colocarlo centrado verticalmente sobre el piso -> centerY = height/2 en local
        cap.center = new Vector3(0f, cap.height * 0.5f, 0f);

        collider = cap;
    }


}

