using EasyDoorSystem;
using GLTFast.Schema;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;

public interface IRoomLayout
{
    // Posiciones de celdas/artes
    public Vector3[] GetTilePositions(RoomInformation info, int cols, float cellSize);

    // Posiciones de columnas
    public Vector3[] GetColumnsPositions(RoomInformation info, int cols, float cellSize);

    // Devuelve el GameObject de la(s) puerta(s) generada(s) por el layout (puede ser null)
    GameObject GetGeneratedDoorObject();

    // Suelo y techo
    public Vector3[] GetFloorPositions(RoomInformation info, int cols, float cellSize);
    public Vector3[] GetCeilingPositions(RoomInformation info, int cols, float cellSize);

    // Paredes (N, S, E, W: escala y posición)
    public Vector3[] GetWallPositions(RoomInformation info, int cols, float cellSize);

    public Vector3 GetWaitingStationPosition();

    public void SetGeneratedDoorsOutline();

    // Punto de origen (para instanciar player, UI, etc.)
    public Vector3 OriginPosition();

    // Prefabs para cada elemento constructivo
    public GameObject GetPrefabFloor(Vector3 position,
        Vector3 scale,
        Transform parent,
        UnityEngine.Material material);
    public GameObject GetPrefabCeiling(Vector3 position,
        Vector3 scale,
        Quaternion rotation,
        Transform parent,
        UnityEngine.Material material);
    public GameObject GetPrefabWallNorte(Vector3 position,
        Vector3 scale,
        Transform parent,
        UnityEngine.Material material);
    public GameObject GetPrefabWallSur(Vector3 position,
        Vector3 scale,
        Transform parent,
        UnityEngine.Material material);
    public GameObject GetPrefabWallEste(Vector3 position,
        Vector3 scale,
        Transform parent,
        UnityEngine.Material material);
    public GameObject BuildWallOesteWithDoor(
        Vector3 wallScale,
        Vector3 wallPosition,
        GameObject doorPrefab,
        UnityEngine.Material wallMaterial,
        Transform parent
    );

    public GameObject BuildCorridor(
    RoomInformation info,
    Transform parent,
    UnityEngine.Material floorMaterial,
    UnityEngine.Material wallMaterial,
    UnityEngine.Material ceilingMaterial
    );

    // Nuevo:
    /// <summary>Activa o desactiva auto-open en la(s) puerta(s) generada(s) por el layout.</summary>
    void SetGeneratedDoorsAutoOpen(bool enable);

    /// <summary>Crea un trigger en la entrada de la sala que invocará la UnityEvent pasada.</summary>
    GameObject CreateEntryTrigger(Transform parent, UnityEngine.Events.UnityEvent onPlayerEnter);
}


public class RectangularLayout : IRoomLayout
{

    float anchoSala;
    float largoSala;
    float anchoTotal;
    float largoTotal;
    Vector3 origin;
    int posicionActual;

    GameObject _westWallGroup;   // guardamos aquí el GameObject devuelto
    GameObject _generatedDoor;   // referencia rápida a la puerta generada

    // Posiciones de celdas/artes
    public Vector3[] GetTilePositions(RoomInformation info, int cols, float cellSize)
    {
        Vector3[] posicionesCeldas = new Vector3[cols * 2];
        posicionActual = 0;

        for (int row = 0; row < 2; row++)
        {
            float zOffset = row == 0
                ? (cellSize / 2f + info.GetAisleWidth() / 2f)
                : -(cellSize / 2f + info.GetAisleWidth() / 2f);

            for (int col = 0; col < cols; col++)
            {
                float x = info.getWallsSpacing() + col * (cellSize + info.GetColSpacing());
                float z = zOffset;

                Vector3 position = new Vector3(x, 0, z);
                AgregarCelda(cols, posicionesCeldas, position);
                if (row == 0 && col == 0)
                {
                    origin = new Vector3(x, 0, 0);
                }
            }
        }
        return posicionesCeldas;
    }

    // Posiciones de columnas
    public Vector3[] GetColumnsPositions(RoomInformation info, int cols, float cellSize)
    {
        posicionActual = 0;
        Vector3[] posicionesCeldas = new Vector3[2 * (cols - 1)];
        for (int col = 0; col < cols - 1; col++)
        {
            // Calcular posición X intermedia entre celdas
            float xLeft = info.getWallsSpacing() + col * (cellSize + info.GetColSpacing());
            float xRight = info.getWallsSpacing() + (col + 1) * (cellSize + info.GetColSpacing());
            float xColumna = (xLeft + xRight) / 2f;

            // Colocar dos columnas: una arriba (fila 0) y una abajo (fila 1)
            float zArriba = (info.GetColSpacing() / 2) + (info.GetAisleWidth() / 2f);
            float zAbajo = -((info.GetColSpacing() / 2) + (info.GetAisleWidth() / 2f));

            posicionesCeldas[posicionActual] = new Vector3(xColumna, 1, zArriba);
            posicionesCeldas[posicionActual + 1] = new Vector3(xColumna, 1, zAbajo);
            posicionActual = posicionActual + 2;
        }

        return posicionesCeldas;
    }

    // Suelo y techo
    public Vector3[] GetFloorPositions(RoomInformation info, int cols, float cellSize)
    {
        Vector3[] posicionesCeldas = new Vector3[2];

        anchoSala = cols * cellSize + (cols - 1) * info.GetColSpacing();
        largoSala = 2 * cellSize + info.GetAisleWidth();
        anchoTotal = anchoSala + 2 * info.getWallsSpacing();
        largoTotal = largoSala + 2 * info.getWallsSpacing();

        posicionesCeldas[0] = new Vector3(anchoTotal / 2f - info.getWallsSpacing(), -0.01f, 0f);

        posicionesCeldas[1] = new Vector3(anchoTotal / info.unityPlaneSize, 1, largoTotal / info.unityPlaneSize);

        return posicionesCeldas;

    }

    public Vector3[] GetCeilingPositions(RoomInformation info, int cols, float cellSize)
    {
        Vector3[] posicionesCeldas = new Vector3[2];

        posicionesCeldas[0] = new Vector3(anchoTotal / 2f - info.getWallsSpacing(), info.wallHeight, 0f);
        posicionesCeldas[1] = new Vector3(anchoTotal / info.unityPlaneSize, 1, largoTotal / info.unityPlaneSize);

        return posicionesCeldas;
    }

    // Paredes (N, S, E, W: escala y posición)
    public Vector3[] GetWallPositions(RoomInformation info, int cols, float cellSize)
    {
        float mitadAncho = anchoTotal / 2f - cellSize / 2f;
        float mitadLargo = largoTotal / 2f;
        Vector3[] posicionesCeldas = new Vector3[8];
        // Norte
        posicionesCeldas[0] = new Vector3(anchoTotal, info.wallHeight, info.espesor);
        posicionesCeldas[1] = new Vector3(mitadAncho, info.wallHeight / 2f, mitadLargo);
        // Sur
        posicionesCeldas[2] = new Vector3(anchoTotal, info.wallHeight, info.espesor);
        posicionesCeldas[3] = new Vector3(mitadAncho, info.wallHeight / 2f, -mitadLargo);
        // Este
        posicionesCeldas[4] = new Vector3(info.espesor, info.wallHeight, largoTotal);
        posicionesCeldas[5] = new Vector3(anchoTotal - info.getWallsSpacing(), info.wallHeight / 2f, 0);
        // Oeste
        posicionesCeldas[6] = new Vector3(info.espesor, info.wallHeight, largoTotal);
        posicionesCeldas[7] = new Vector3(-info.getWallsSpacing(), info.wallHeight / 2f, 0);
        return posicionesCeldas;
    }

    // Punto de origen (para instanciar player, UI, etc.)
    public Vector3 OriginPosition() { return origin; }

    // Prefabs para cada elemento constructivo
    public GameObject GetPrefabFloor(Vector3 position,
        Vector3 scale,
        Transform parent,
        UnityEngine.Material material)
    {
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.parent = parent;
        floor.transform.position = position;
        floor.transform.localScale = scale;
        floor.GetComponent<Renderer>().sharedMaterial = material;
        return floor;
    }
    public GameObject GetPrefabCeiling(Vector3 position,
        Vector3 scale,
        Quaternion rotation,
        Transform parent,
        UnityEngine.Material material)
    {
        var ceil = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ceil.name = "Ceiling";
        ceil.transform.parent = parent;
        ceil.transform.position = position;
        ceil.transform.localScale = scale;
        ceil.transform.localRotation = rotation;
        ceil.GetComponent<Renderer>().sharedMaterial = material;
        return ceil;
    }

    public GameObject CreateWall(
        string name,
        Vector3 scale,
        Vector3 position,
        Transform parent,
        UnityEngine.Material material
    )
    {
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = name;
        wall.transform.parent = parent;
        wall.transform.position = position;
        wall.transform.localScale = scale;
        wall.GetComponent<Renderer>().sharedMaterial = material;
        return wall;
    }
    public GameObject GetPrefabWallNorte(Vector3 position,
        Vector3 scale,
        Transform parent,
        UnityEngine.Material material)
    {
        return CreateWall("WallNorte", position, scale, parent, material);
    }
    public GameObject GetPrefabWallSur(Vector3 position,
        Vector3 scale,
        Transform parent,
        UnityEngine.Material material)
    {
        return CreateWall("WallSur", position, scale, parent, material);
    }
    public GameObject GetPrefabWallEste(Vector3 position,
        Vector3 scale,
        Transform parent,
        UnityEngine.Material material)
    {
        return CreateWall("WallEste", position, scale, parent, material);
    }
    Bounds CalculateBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return new Bounds(go.transform.position, Vector3.zero);

        // Empieza con el primer renderer
        Bounds b = renderers[0].bounds;
        // Encapsula todos los demás
        for (int i = 1; i < renderers.Length; i++)
            b.Encapsulate(renderers[i].bounds);

        return b;
    }

    public GameObject BuildWallOesteWithDoor(
        Vector3 wallScale,
        Vector3 wallPosition,
        GameObject doorPrefab,
        UnityEngine.Material wallMaterial,
        Transform parent
    )
    {

        // 1) Parámetros
        float wallThickness = wallScale.x;
        float wallHeight = wallScale.y;
        float totalDepth = wallScale.z;

        // 2) Calcula dimensiones de la puerta:
        Bounds doorBounds = CalculateBounds(doorPrefab);
        float doorWidth = doorBounds.size.z;
        float doorHeight = doorBounds.size.y;

        // Queremos abrir un hueco centrado en Z = posOeste.z
        // El muro oeste corre a X = posOeste.x
        float xWall = wallPosition.x;
        float yWall = wallHeight / 2f;
        float halfDepth = totalDepth / 2f;


        // 4) Crea objeto contenedor para la pared oeste
        GameObject wallGroup = new GameObject("Wall_Oeste_Group");
        wallGroup.transform.parent = parent;
        wallGroup.transform.position = Vector3.zero;
        wallGroup.transform.rotation = Quaternion.identity;

        // 1) Segmento superior de pared (por encima de la puerta)
        float headerHeight = wallHeight - doorHeight;
        if (headerHeight > 0.01f)
        {
            var header = GameObject.CreatePrimitive(PrimitiveType.Cube);
            header.transform.localScale = new Vector3(
                wallThickness,
                headerHeight,
                doorWidth + doorWidth / 3 //para darle un poco de margen al hueco de la puerta
            );
            // Posicionamos: 
            //   X = xWall 
            //   Y = doorHeight + headerHeight/2 
            //   Z = posOeste.z - (doorWidth/2) + doorBounds.center.z (compensar pivote)
            float doorCenterOffsetZ = doorBounds.center.z;
            header.transform.position = new Vector3(
                xWall,
                doorHeight + headerHeight / 2,
                0
            );
            header.name = "Wall_Oeste_header";
            header.GetComponent<Renderer>().sharedMaterial = wallMaterial;

            header.transform.parent = wallGroup.transform;
        }


        // 6) Laterales del hueco
        float sideDepth = (totalDepth - doorWidth) / 2f;
        // arriba (al fondo+Z)
        var sideTop = GameObject.CreatePrimitive(PrimitiveType.Cube);
        sideTop.transform.localScale = new Vector3(
            wallThickness,
            wallHeight,
            sideDepth
        );
        sideTop.transform.position = new Vector3(
            xWall,
            yWall,
            wallPosition.z + (totalDepth / 2f) - sideDepth / 2f
        );
        sideTop.name = "Wall_Oeste_SideTop";

        sideTop.GetComponent<Renderer>().sharedMaterial = wallMaterial;

        sideTop.transform.parent = wallGroup.transform;


        // abajo (hacia -Z)
        var sideBot = GameObject.CreatePrimitive(PrimitiveType.Cube);
        sideBot.name = "Wall_Oeste_SideBot";
        sideBot.transform.localScale = new Vector3(
            wallThickness,
            wallHeight,
            sideDepth
        );
        sideBot.transform.position = new Vector3(
            xWall,
            yWall,
            wallPosition.z - (totalDepth / 2f) + sideDepth / 2f
        );
        sideBot.GetComponent<Renderer>().sharedMaterial = wallMaterial;

        sideBot.transform.parent = wallGroup.transform;


        // 7) La puerta dentro del hueco
        var door = GameObject.Instantiate(doorPrefab, wallGroup.transform);
        door.name = "GeneratedDoor";
        // pivot de la puerta ya debe estar centrado; apoyamos en suelo:
        float xDoor = xWall - (wallThickness / 2f) + doorBounds.size.x / 2;

        // Y: para que quede apoyada al suelo, la mitad de su altura
        float yDoor = doorHeight / 2f;

        // Z: centrada en el hueco que está en posOeste.z
        float zDoor = wallPosition.z;

        door.transform.position = new Vector3(xDoor, yDoor, zDoor);
        door.transform.rotation = Quaternion.identity;

        _westWallGroup = wallGroup;
        _generatedDoor = door;
        return wallGroup;
    }
    // Devuelve el GameObject de la(s) puerta(s) generada(s) por el layout (puede ser null)
    // dentro de la clase RectangularLayout, ya existe _generatedDoor (private GameObject)
    // simplemente exponemos:
    public GameObject GetGeneratedDoorObject()
    {
        return _generatedDoor;
    }


    public GameObject BuildCorridor(
    RoomInformation info,
    Transform parent,
    UnityEngine.Material floorMaterial,
    UnityEngine.Material wallMaterial,
    UnityEngine.Material ceilingMaterial
)
    {
        if (_generatedDoor == null)
        {
            Debug.LogWarning("[RoomBuilder] No encontré la puerta generada para enlazar el pasillo.");
            return null;
        }

        // 1) Bounds de puerta para ancho
        var doorB = CalculateBounds(_generatedDoor);
        float doorWidthZ = doorB.size.z * 2; //para probar que sea un poco mas ancho el pasillo

        // 2) Puntos de inicio y fin (centro de puertas)
        Vector3 sC = _generatedDoor.transform.position;
        Vector3 eC = GameObject.Find("Hall").transform.Find("HallDoor").position;

        float startX = sC.x;
        float endX = eC.x;
        float zStart = sC.z;
        float zEnd = eC.z;

        // 3) Longitud y punto medio
        float lengthX = Mathf.Abs(endX - startX);
        Vector3 mid = new Vector3((startX + endX) * 0.5f, 0f, (zStart + zEnd) * 0.5f);

        // 4) Creamos un contenedor para todo el pasillo
        GameObject corridorGroup = new GameObject("Corridor");
        corridorGroup.transform.parent = parent;
        corridorGroup.transform.position = Vector3.zero;

        // ——— 5) Piso ———
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "CorridorFloor";
        floor.transform.parent = corridorGroup.transform;
        floor.transform.position = mid + Vector3.up * 0.01f; // apenas levantado para evitar Z-fighting
                                                             // original plane 10×10 en XZ → aquí ancho Z = doorWidthZ, largo X = lengthX
        floor.transform.localScale = new Vector3(doorWidthZ / 10f, 1.5f, lengthX / 10f);
        floor.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
        floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;

        // ——— 6) Paredes laterales ———
        float halfWidth = doorWidthZ * 0.5f;
        float halfLen = lengthX * 0.5f;
        float wallH = info.wallHeight;
        float wallT = info.espesor;

        // pared izquierda (mirando de la sala al hall): a Z = mid.z + halfWidth
        var wLeft = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wLeft.name = "CorridorWall_Left";
        wLeft.transform.parent = corridorGroup.transform;
        wLeft.transform.position = mid + new Vector3(0f, wallH * 0.5f, +halfWidth);
        wLeft.transform.localScale = new Vector3(wallT, wallH, lengthX);
        wLeft.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
        wLeft.GetComponent<Renderer>().sharedMaterial = wallMaterial;

        // pared derecha
        var wRight = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wRight.name = "CorridorWall_Right";
        wRight.transform.parent = corridorGroup.transform;
        wRight.transform.position = mid + new Vector3(0f, wallH * 0.5f, -halfWidth);
        wRight.transform.localScale = new Vector3(wallT, wallH, lengthX);
        wRight.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
        wRight.GetComponent<Renderer>().sharedMaterial = wallMaterial;

        // ——— 7) Techo ———
        GameObject ceiling = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ceiling.name = "CorridorCeiling";
        ceiling.transform.parent = corridorGroup.transform;
        ceiling.transform.position = mid + Vector3.up * wallH;
        ceiling.transform.localScale = new Vector3(doorWidthZ / 10f, 1f, lengthX / 10f);
        ceiling.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
        // plano nativo mira +Y, así que no rotamos
        ceiling.GetComponent<Renderer>().sharedMaterial = ceilingMaterial;

        // Opcional: marcar todo estático
        // corridorGroup.isStatic = true;

        return corridorGroup;
    }



    void AgregarCelda(int cols, Vector3[] posicionesCeldas, Vector3 position)
    {
        if (posicionActual >= cols)
        {
            posicionesCeldas[(cols * 2) - 1 - posicionActual % cols] = position;
        }
        else
        {
            posicionesCeldas[posicionActual] = position;
        }
        posicionActual++;
    }

    public void SetGeneratedDoorsAutoOpen(bool enable)
    {
        if (_generatedDoor == null) return;

        var comps = _generatedDoor.GetComponentsInChildren<EasyDoor>(true);
        foreach (var c in comps)
        {
            var t = c.GetType();
            if (t.Name == "EasyDoor" || t.Name == "EasyDoorSystem.EasyDoor")
            {
                
                // tratamos de setear campos públicos/privados por reflection
                var fld1 = t.GetField("allowAutoOpen", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (fld1 != null) fld1.SetValue(c, enable);
                c.CloseDoor();

            }
        }
    }
    public Vector3 GetWaitingStationPosition()
    {
        if (_generatedDoor == null)
        {
            Debug.LogWarning("[RectangularLayout] No hay puerta generada aún.");
            return Vector3.zero;
        }

        // Punto base: posición de la puerta
        Vector3 basePos = _generatedDoor.transform.position;

        // Avanzamos un poco hacia afuera según el frente de la puerta
        //Vector3 forwardOffset = _generatedDoor.transform.forward * 2.0f; // 2m hacia adelante

        // Opcional: un poco al costado (ej. derecha de la puerta)
        Vector3 sideOffset = _generatedDoor.transform.right * 1.5f; // 1m a la derecha

        // Altura (levantarlo del piso)
        Vector3 upOffset = Vector3.up * 0.25f; // 0.5m sobre el suelo

        return basePos + sideOffset + upOffset;
    }


    public void SetGeneratedDoorsOutline()
    {
        if (_generatedDoor == null) return;


        // 3) Buscar puertas (EasyDoor) dentro del Hall
        var script = _generatedDoor.GetComponentsInChildren<EasyDoor>(true);

        // 4) Cerrar y desactivar auto-open de las puertas del Hall
        foreach (var d in script)
        {
            // si añadiste allowAutoOpen o similar:
            Debug.Log(d.name);
            var allowAutoOpen = d.GetType().GetField("allowAutoOpen", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (allowAutoOpen != null) allowAutoOpen.SetValue(d, true);
        }

        _generatedDoor.AddComponent<Outline>();
        var outline = _generatedDoor.GetComponent<Outline>();
        outline.enabled = true;
        outline.OutlineMode = Outline.Mode.OutlineAll;
        outline.OutlineColor = Color.green; // o el color que quieras
        outline.OutlineWidth = 10f; // ancho del contorno
    }

    public GameObject CreateEntryTrigger(Transform parent, UnityEngine.Events.UnityEvent onPlayerEnter)
    {
        // Si no hay sala todavía
        if (_generatedDoor == null)
        {
            Debug.LogWarning("[RectangularLayout] No hay puerta generada para crear trigger.");
            return null;
        }

        // Usamos los bounds que ya se calcularon para la sala
        // (si no tenés uno guardado, recalculamos con los Renderers hijos del parent)
        Bounds roomBounds = new Bounds();
        bool initialized = false;

        // Buscar todos los renderers/hijos que forman la sala, excluyendo posibles pasillos/corridor
        var renderers = parent.GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            // Excluir objetos claramente asociados a pasillos o debug names
            string lname = r.gameObject.name.ToLowerInvariant();
            if (lname.Contains("corridor") || lname.Contains("corridorfloor") || lname.Contains("corridor_floor") || lname.Contains("corridor_floor"))
                continue;
            if (lname.Contains("debug") || lname.Contains("trigger")) // opcional, evita recoger tu propio trigger/debug meshes
                continue;

            if (!initialized)
            {
                roomBounds = r.bounds;
                initialized = true;
            }
            else
            {
                roomBounds.Encapsulate(r.bounds);
            }
        }

        if (!initialized)
        {
            Debug.LogWarning("[RectangularLayout] No se pudieron calcular bounds de la sala (no hay Renderers válidos en parent).");
            return null;
        }

        // Opción: reducir ligeramente la profundidad para evitar colisiones con la cara interior
        float depthInset = 0.01f;
        Vector3 size = roomBounds.size;
        size.x = Mathf.Max(0.01f, size.x - depthInset);
        size.y = Mathf.Max(0.01f, size.y); // mantener la altura
        size.z = Mathf.Max(0.01f, size.z - depthInset);

        // Crear el trigger que cubra toda la sala (posicionado en el centro del bounds)
        var go = new GameObject("RoomEntryTrigger");
        go.transform.parent = parent;
        go.transform.position = roomBounds.center;
        go.transform.rotation = Quaternion.identity;

        var col = go.AddComponent<BoxCollider>();
        col.isTrigger = true;

        // BoxCollider.size se interpreta en el espacio local del 'go' (así que si go no está escalado, usamos roomBounds.size directamente)
        col.center = Vector3.zero;
        col.size = size;

        // Hacerlo no seleccionable si querés (opcional)
        // go.hideFlags = HideFlags.DontSave; // evita serializarlo accidentalmente

        // Script helper (espera que exista DoorEntryTrigger en tu proyecto)
        var det = go.AddComponent<DoorEntryTrigger>();
        if (_generatedDoor != null)
        {
            var comps = _generatedDoor.GetComponentsInChildren<EasyDoor>(true);
            if (comps.Length > 0)
                det.door = comps[0];
        }
        det.onPlayerEnter = onPlayerEnter;


        Debug.Log($"[RectangularLayout] RoomEntryTrigger creado en {roomBounds.center} con size {size}.");

        return go;
    }




}

//public class CrossLayout : IRoomLayout{} hacer luego