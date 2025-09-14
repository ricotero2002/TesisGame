using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System;
using TMPro;
using Unity.VisualScripting;
using EasyDoorSystem;
using System.Reflection;
using Unity.Mathematics;
using System.Linq;
using static UnityEngine.GraphicsBuffer;
public class GameManagerFin : MonoBehaviour
{
    [Header("Singleton")]
    public static GameManagerFin Instance;
    public static event System.Action OnRoomCreated;

    [Header("AsignarEditor")]
    
    [SerializeField] private RoomInformation roomInformation;
    [SerializeField] private GameObject uiMenuPausaPrefab; // prefab (no la instancia)
    [SerializeField] private GameObject pruebaEfecto;

    [Header("WaitingStation")]
    [Tooltip("Prefab del objeto que aparecerá como estación de espera (puede ser un simple empty con un mesh).")]
    [SerializeField] private GameObject waitingStationPrefab;
    private GameObject floatingWaitingStationText;
    private GameObject station; // instancia de la estación de espera (si se crea)

    // visible en inspector: podés arrastrar cualquier componente (GameObject con el script que implemente IDifficultyManager)
    [Header("Difficulty")]
    [Tooltip("Arrastrar el componente que implemente IDifficultyManager (ej: DemoDifficultyManager).")]
    [SerializeField] private MonoBehaviour difficultyManagerBehaviour; // inspector-friendly

    [Header("DifficultySets")]
    [Tooltip("JSON generado por el script python con los sets (difficulty_sets_with_scores.json).")]
    [SerializeField] private string difficultySetsJsonPath = "Assets/Renders/difficulty_sets_with_scores.json";
    // Historial en memoria para evitar repetir siempre los mismos object_ids en varias partidas
    private HashSet<string> _usedObjectIds = new HashSet<string>();

    [Header("Respuesta - UI dinámica")]
    [SerializeField] private GameObject canvasInteractuarPrefab; // prefab CanvasInteractuarEstatuas (contiene TouchButtonMoved/TouchButtonNotMoved y Title)
    [Tooltip("Texto que aparece flotando sobre el objeto cuando puedas interactuar.")]
    public string promptTextPc = "Left click = Se movió\nRight click = No se movió";
    [Tooltip("Texto que aparece flotando sobre el objeto cuando puedas interactuar.")]
    public string promptTextMobile = "Apreta alguno de los Botone para indicar la respuesta";
    [Tooltip("Config ScriptableObject de Dynamic Floating Text para definir estilo.")]
    public DynamicTextData floatingTextConfig;

    private float trialStartTime = 0f;

    private TrialMeta trialMeta= new TrialMeta();

    private List<string> chosenGroupSession;

    //elementos internos

    private Coroutine _hideCoroutine;
    private GameObject interactCanvasInstance = null; // instancia del canvas (titulo + botones)
    private GameObject activeFloatingTextGO; // referencia al texto flotante creado (si el manager devuelve el GO)
    private TileController currentSelectedTile = null;
    private bool pcResponseModeActive = false;
    private UnityEngine.UI.Text titleText;
    /// <summary>
    /// True si estamos en plataforma móvil (Android / iOS).
    /// </summary>
    public static bool IsMobile => Application.isMobilePlatform || Application.platform == RuntimePlatform.Android || Application.platform == RuntimePlatform.IPhonePlayer;


    // helper para obtener la interfaz (null-safe)
    private IDifficultyManager DifficultyManager => difficultyManagerBehaviour as IDifficultyManager;


    [Header("Variables Internas")]
    private RoomBuilder currentRoom;
    private int currentIndex = -1;
    private int LimitIndex;
    private float timer = 0f;
    private float timeToChange;
    private float timeBetweenPhases;
    private MenuPausa menuPausa;
    // hall connections



    private enum State { Start, WaitingForPlayerEnter, Memorising, LLegarALaEstacion, WatingTime, WaitingAtStation, WatingUserToAnswer, UserChecking, Completed };

    private State state = State.Start;

    void Awake()
    {
        if (Instance == null)
        {

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }
    /// <summary>
    /// Inicia la secuencia de preparación (coroutine) para crear UI y eventualmente la sala.
    /// Llamalo desde StartGameInteraction cuando el usuario quiera crear la room.
    /// </summary>

    
    public void Init()
    {
        // Si ya estamos corriendo una init, ignorar
        StopAllCoroutines();
        StartCoroutine(InitSequenceCoroutine());
    }

    IEnumerator InitSequenceCoroutine()
    {


            // 2) Instanciar UiMenuPausa (si no está)
            if (uiMenuPausaPrefab != null && menuPausa == null)
            {
                var inst = Instantiate(uiMenuPausaPrefab);
                inst.name = uiMenuPausaPrefab.name + "_Instance";
                menuPausa = inst.GetComponent<MenuPausa>();
                if (menuPausa != null)
                {
                    menuPausa.Init();
                    menuPausa.Desabilitar();
                }
                else
                {
                    Debug.LogWarning("[GameManagerFin] UiMenuPausa prefab no tiene componente MenuPausa.");
                }
        }

            // 3) Buscar Hall y preparar indicadores (rojo)
            PrepareHallIndicators(false); // false => set rojo/disabled


            DifficultyManager.Actualizar();
            // 5) Obtener parámetros desde difficulty manager (si hay)
            IDifficultyManager diff = DifficultyManager;
            int cols;
            float timeToChangeParametro;
            float timeBetween;
            int hardEasyMode;
            IMovementStrategy strategy;
            if (diff != null)
            {
                cols = diff.GetColumns();
                timeToChangeParametro = diff.GetMemoriseTime();
                timeBetween = diff.GetTimeBetweenPhases();
                strategy = diff.GetMovementStrategy();
                hardEasyMode = diff.GetHardEasyMode();
            }
            else
            {
                // Fallback: valores por defecto (o podes exponerlos en inspector)
                cols = 4;
                timeToChangeParametro = 5f;
                timeBetween = 2f;
                strategy = new CommonMovementStrategy();
                hardEasyMode = 0;
            }
            // Crear el GO contenedor de la room
            var roomGO = new GameObject("Room");
            currentRoom = roomGO.AddComponent<RoomBuilder>();
            

            currentIndex = -1;
            timer = 0f;

            // Guardamos parámetros
            this.timeBetweenPhases = timeBetween;
            this.LimitIndex = cols * 2;
            this.timeToChange = timeToChangeParametro;

        // --------- NUEVO: elegir grupo (difficulty sets) con DifficultySetsLoader ----------
        List<string> chosenGroup = null;
        string chosenCategory = null;
        string chosenSubpoolId = null;
        try
        {
            var dsRoot = DifficultySetsLoader.LoadFromFile(difficultySetsJsonPath);
            if (dsRoot != null && diff != null)
            {
                string difficultyStr = (hardEasyMode == 1) ? "hard" : "easy";
                System.Random rng = new System.Random();

                int setSize = cols * 2; // si querés mapear distinto, cambialo acá

                // intento evitando repetir ids (historial en _usedObjectIds)
                var excludedIds = _usedObjectIds;

                // preferencia de pool opcional desde IDifficultyManager
                string preferredPool = null;
                if(!diff.GetCategory().Equals("Any"))
                    preferredPool = diff.GetCategory();

                // 1) obtener todos los grupos candidatos con los parámetros (sin excluded porque la función no acepta excluded)
                var candidateGroups = DifficultySetsLoader.GetGroupsByParams(dsRoot, setSize, difficultyStr, preferredPool, null);

                // 2) si no tenemos candidatos, intentar fallback al método simple (GetRandomGroupByParams)
                if (candidateGroups == null || candidateGroups.Count == 0)
                {
                    var single = DifficultySetsLoader.GetRandomGroupByParams(dsRoot, setSize, difficultyStr, preferredPool, null);
                    if (single != null) candidateGroups = new List<List<string>>() { single };
                }

                // 3) elegir un grupo que no contenga ids ya usados (si hay), shuffle candidates
                if (candidateGroups != null && candidateGroups.Count > 0)
                {
                    var shuffled = candidateGroups.OrderBy(x => rng.Next()).ToList();
                    List<string> found = null;
                    foreach (var g in shuffled)
                    {
                        bool intersects = false;
                        foreach (var id in g)
                        {
                            if (excludedIds.Contains(id)) { intersects = true; break; }
                        }
                        if (!intersects) { found = g; break; }
                    }
                    // si encontramos alguno que no intersecta con excluded -> lo usamos
                    if (found != null) chosenGroup = found;
                    else
                    {
                        // si no hay ninguno limpio, usamos el primer candidato (o el que GetRandomGroupByParams devolvió)
                        chosenGroup = shuffled.FirstOrDefault();
                    }
                }

                // 4) si obtuvimos grupo, extraer categoría (prefijo antes de '/')
                if (chosenGroup != null && chosenGroup.Count > 0)
                {
                    var first = chosenGroup[0];
                    if (!string.IsNullOrEmpty(first) && first.Contains("/"))
                        chosenCategory = first.Split('/')[0];
                    else
                        chosenCategory = preferredPool;
                }
                // dsRoot ya lo obtenido antes: var dsRoot = DifficultySetsLoader.LoadFromFile(difficultySetsJsonPath);
                if (dsRoot != null && chosenGroup != null && chosenGroup.Count > 0)
                {
                    // difficultyStr lo definiste arriba (hard/easy)
                    chosenSubpoolId = DifficultySetsLoader.FindSubpoolIdForGroup(dsRoot, chosenGroup, difficultyStr, chosenCategory);
                    if (!string.IsNullOrEmpty(chosenSubpoolId))
                        Debug.Log($"[GameManagerFin] Found subpool for chosen group: {chosenSubpoolId}");
                    else
                        Debug.Log("[GameManagerFin] No subpool found for chosenGroup (will fallback to null)");
                }

            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[GameManagerFin] Error al cargar DifficultySets: " + ex.Message);
        }



        // Si no conseguimos grupo, chosenGroup queda null y RoomBuilder hará fallback a artPoolSO aleatorio como antes.

        // Si elegimos grupo, marcamos ids como usados (para evitar repetir en próximas partidas)
        if (chosenGroup != null)
        {
            foreach (var oid in chosenGroup) _usedObjectIds.Add(oid);
            this.chosenGroupSession = chosenGroup;
        }

        trialMeta.object_category = chosenCategory;
        trialMeta.object_subpool = chosenSubpoolId;
        trialMeta.memorization_time_ms = timeToChangeParametro;
        trialMeta.swap_event = (strategy is CommonMovementStrategy) ? false : true;

        // Lanzar InitCoroutine del RoomBuilder y esperar a que termine
        //aca deberia pasarle la lista de elementos a usar
        //yield return StartCoroutine(currentRoom.InitCoroutine(cols, roomInformation, strategy));

        // Lanzar InitCoroutine del RoomBuilder y pasar la lista elegida (puede ser null)
        // NOTA: se modificó la firma de InitCoroutine para recibir opcionalmente forcedGroup y forcedCategory
        // ----- LLAMADA A InitCoroutine USANDO ARGUMENTOS NOMBRADOS (evita error de tipos/orden) -----
        Debug.Log("timeToChangeParametro: " + timeToChangeParametro+ "timeToChange: "+ timeToChange);
        yield return StartCoroutine(currentRoom.InitCoroutine(
            columnas: cols,
            information: roomInformation,
            movementStrategy: strategy,
            forcedGroup: chosenGroup,
            forcedCategory: chosenCategory
        ));

        // 6) habilitar hall (verde) y emitir evento


        // 6) habilitar hall (verde) y emitir evento
        PrepareHallIndicators(true);
            OnRoomCreated?.Invoke();

            // ahora el estado pasa a 'WaitingForPlayerEnter' en tu lógica original
            state = State.WaitingForPlayerEnter;

            
        
    }



    // Nuevo método público que se llama cuando el player entra:
    public void StartMemorising()
    {
        if (state != State.WaitingForPlayerEnter) return;
        // Desactivar indicador de Hall (quitar verde al entrar)
        GameObject hallGO = GameObject.Find("Hall");
        var halldoor = hallGO.transform.Find("HallDoor");
        var outlineComp = halldoor.GetComponent<Outline>();
        if (outlineComp != null)
        {
            outlineComp.enabled = false;

        }

        state = State.Memorising;
        if ((timeToChange - 1.0f) > 0)
            timer = timeToChange - 1.0f;
        else
            timer = 0.0f;
        menuPausa.Habilitar();
        EnsureTitleOnlyInteractCanvas();
        SessionManager.Instance.StartSession(this.chosenGroupSession);
    }
    //de aca para abajo arreglar
    void Update()
    {
        switch (state)
        {
            case State.Start:
                break;
            case State.Memorising:
                timer += Time.deltaTime;
                if (timer >= timeToChange)
                {
                    Debug.Log("Cambio de estatua");
                    timer = 0f;
                    // Apagar la tile anterior
                    if (currentIndex >= 0 && currentIndex < LimitIndex)
                    {
                        currentRoom.DisableEstatueLigth(currentIndex);
                    }

                    // Avanzar al siguiente �ndice
                    currentIndex++;

                    // Encender la siguiente tile
                    if (currentIndex < LimitIndex)
                    {
                        currentRoom.EnableEstatueLigth(currentIndex);
                    }
                    else
                    {
                        state = State.LLegarALaEstacion;
                        currentIndex = 0;
                        timer = 0f;
                        titleText.text = "Ir hasta donde se indica";
                        generarStation();
                    }
                }
               
                break;
            case State.LLegarALaEstacion:

                break;

            case State.WatingTime:
                // arrancamos la coroutine que crea la estación y hace el countdown
                StartCoroutine(HandleWaitingStationCoroutine(timeBetweenPhases));
                state = State.WaitingAtStation;
                break;

            case State.WaitingAtStation:
                // la coroutine se encarga de avanzar al siguiente estado
                break;


            case State.UserChecking:
                if (trialStartTime <= 0f)
                {
                    trialStartTime = Time.realtimeSinceStartup; // start RT
                }
                //aca avisar que la estatua es valida para ser cliqueada
                currentRoom.EnableEstatueLigth(currentIndex, true);
                //espero a que el usuario lo clickee y diga si se movio o no (Falta acomodar)
                state = State.WatingUserToAnswer;
                break;

            case State.WatingUserToAnswer: //ver si se puede optimizar
                if (pcResponseModeActive) 
                {
                    if (Input.GetMouseButtonDown(0))
                    {
                        HandlePlayerResponse(true); // left = moved
                    }
                    else if (Input.GetMouseButtonDown(1))
                    {
                        HandlePlayerResponse(false); // right = not moved
                    }
                }
                break;

            case State.Completed:
                titleText.text = "Compleado, Volver al Hall";
                ShowFinalOutlines();
                currentRoom.AbleDoor();
                SessionManager.Instance.EndSession();


                menuPausa.Desabilitar(); // Desactivar el menú de pausa
                state = State.Start; // Reiniciar el estado

                //this.DestruirSala();
                break;
        }
    }
    public void EliminarUIPartida()
    {
        Destroy(interactCanvasInstance);
        interactCanvasInstance = null;
    }

    // Llamado desde InteractionUIController cuando el user responde
    public void OnUserAnswered()
    {
        // Desactiva la luz actual y avanza
        currentRoom.DisableEstatueLigth(currentIndex, false);
        currentIndex++;
        if (currentIndex < LimitIndex)
        {
            state = State.UserChecking;
        }
        else
        {
            state = State.Completed;
        }
    }

    public int GetIndex()
    {
        return currentIndex;
    }

    public void Habilitar()
    {
        state = State.Start; // Reiniciar el estado
        menuPausa.Desabilitar();


    }
    private void ShowFinalOutlines()
    {
        if (currentRoom == null) return;

        foreach (var tc in currentRoom.GetTileControllers())
        {
            tc.ShowEndResult();
        }
    }


    public void Desabilitar()
    {
        state = State.Start;
        if(menuPausa != null) menuPausa.Desabilitar();
    }

    public void DestruirSala()
    {
        Debug.Log("Destruir Sala");
        Destroy(currentRoom.gameObject);
    }

    // ------------------ Helpers: Hall indicators & auto-open ------------------

    /// <summary>
    /// Busca en escena el "Hall" y prepara indicadores para sus puertas.
    /// Si ready == true: pone verde y habilita auto-open. Si false: rojo y deshabilita auto-open.
    /// </summary>
    private void PrepareHallIndicators(bool ready)
    {
        // Buscar Hall
        GameObject hallGO = GameObject.Find("Hall");
        if (hallGO == null)
        {
            Debug.LogWarning("[GameManagerFin] No encontré GameObject 'Hall' para preparar indicadores.");
            return;
        }

        var halldoor = hallGO.transform.Find("HallDoor");

        var outlineComp = halldoor.GetComponent<Outline>();
        if (outlineComp != null)
        {
            outlineComp.enabled = true;
            outlineComp.OutlineColor = ready ? Color.green : Color.red;
            outlineComp.OutlineWidth = 10f;
        }
        else
        {
            outlineComp = halldoor.AddComponent<Outline>();
            outlineComp.enabled = true;
            outlineComp.OutlineColor = ready ? Color.green : Color.red;
            outlineComp.OutlineWidth = 10f;
        }
    }

    public bool IsAwaitingUserResponse()
    {
        return state == State.WatingUserToAnswer;
    }

/// <summary>
/// Llamar desde trigger entry en TileController
/// </summary>
public void OnPlayerEnterTile(TileController tc)
    {
        Debug.Log("Entre al trigger 1, IsAwaitingUserResponse: "+ IsAwaitingUserResponse());
        if (!IsAwaitingUserResponse()) return;

        // Guardamos la selección
        currentSelectedTile = tc;

        
        if (floatingTextConfig == null) floatingTextConfig = DynamicTextManager.defaultData;


        string textToShow = GameManagerFin.IsMobile ? promptTextMobile : promptTextPc;

        // calcular top de la estatua
        Bounds b;
        Vector3 topPos = GetObjectTopPosition(tc.artObject, out b);

        // margen por sobre la cabeza en función de tamaño (10% altura, mínimo 0.15)
        float margin = Mathf.Max(0.6f, b.size.y * 0.6f);


        // posición final
        Vector3 spawnPos = topPos + Vector3.up * margin;

        // crear texto con DynamicTextManager (si tu API devuelve el GameObject)
        if (activeFloatingTextGO != null) Destroy(activeFloatingTextGO);
        activeFloatingTextGO = DynamicTextManager.CreateText(spawnPos, textToShow, floatingTextConfig);




        // Si no hay canvas creado, crealo con título (botones por defecto desactivados)
        if (interactCanvasInstance == null)
        {
            if (canvasInteractuarPrefab != null)
            {

                interactCanvasInstance = Instantiate(canvasInteractuarPrefab);
                interactCanvasInstance.name = "CanvasInteractuarEstatuas_Instance";
                // inicialmente los botones vienen desactivados según vos querías; aquí los dejamos inactivos
            }
            else
            {
                Debug.LogWarning("[GameManagerFin] canvasInteractuarPrefab no asignado.");
                return;
            }
        }
        if (GameManagerFin.IsMobile)
        {
            // Hacer visibles + animar botones ahora (TouchButtonMoved / TouchButtonNotMoved)
            var movedBtnGO = interactCanvasInstance.transform.Find("TouchButtonMoved")?.gameObject;
            var notMovedBtnGO = interactCanvasInstance.transform.Find("TouchButtonNotMoved")?.gameObject;
            var movedBtn = movedBtnGO?.GetComponent<UnityEngine.UI.Button>();
            var notMovedBtn = notMovedBtnGO?.GetComponent<UnityEngine.UI.Button>();

            if (movedBtnGO != null) movedBtnGO.SetActive(true);
            if (notMovedBtnGO != null) notMovedBtnGO.SetActive(true);

            // Conectar listeners (remover previos)
            if (movedBtn != null)
            {
                movedBtn.onClick.RemoveAllListeners();
                movedBtn.onClick.AddListener(() => HandlePlayerResponse(true));
            }
            if (notMovedBtn != null)
            {
                notMovedBtn.onClick.RemoveAllListeners();
                notMovedBtn.onClick.AddListener(() => HandlePlayerResponse(false));
            }
        }
        


        // habilitar captura de mouse
        if(!GameManagerFin.IsMobile) pcResponseModeActive = true;

        Debug.Log("Entre al trigger 2");
    }
    public void OnPlayerLeftTile(TileController tc)
    {
        if (!IsAwaitingUserResponse()) return;

        if (_hideCoroutine != null) return;

        float exitDuration = 0.35f;

        if (activeFloatingTextGO != null)
        {

            // Si el texto tiene su propio Animator con parámetro "Exit"/"Close", probar primero
            Animator a = activeFloatingTextGO.GetComponentInChildren<Animator>();
            if (a != null)
            {
                bool triggered = false;
                if (HasTrigger(a, "Exit")) { a.SetTrigger("Exit"); triggered = true; }
                else if (HasTrigger(a, "Close")) { a.SetTrigger("Close"); triggered = true; }

                if (triggered)
                {
                    // esperamos al menos exitDuration y luego destruimos
                    StartCoroutine(WaitAndDestroy(activeFloatingTextGO, exitDuration));
                    activeFloatingTextGO = null;
                }
                else
                {
                    // fallback: animar manualmente
                    _hideCoroutine = StartCoroutine(AnimateAndDestroyWorld(activeFloatingTextGO, exitDuration, () =>
                    {
                        activeFloatingTextGO = null;
                        _hideCoroutine = null;
                    }));
                }
            }
            else
            {
                _hideCoroutine = StartCoroutine(AnimateAndDestroyWorld(activeFloatingTextGO, exitDuration, () =>
                {
                    activeFloatingTextGO = null;
                    _hideCoroutine = null;
                }));
            }
        }

        // Si no había nada, limpias de todas formas
        if (activeFloatingTextGO == null)
        {
            _hideCoroutine = null;
        }

        // habilitar captura de mouse
        pcResponseModeActive = false;

        //botones de celular
        if (interactCanvasInstance == null) return;
        if (IsMobile) {
            var movedBtnGO = interactCanvasInstance.transform.Find("TouchButtonMoved")?.gameObject;
            var notMovedBtnGO = interactCanvasInstance.transform.Find("TouchButtonNotMoved")?.gameObject;

            if (movedBtnGO != null) movedBtnGO.SetActive(false);
            if (notMovedBtnGO != null) notMovedBtnGO.SetActive(false);
        }
        

        
    }

 


    public void HandlePlayerResponse(bool guessedMoved)
    {

        OnPlayerLeftTile(currentSelectedTile);

        // Registrar la respuesta en RoomBuilder (usa currentIndex o la tile seleccionada)
        if (currentRoom != null)
        {
            int indexToSend = currentIndex;
            // preferir la tile seleccionada si está
            if (currentSelectedTile != null)
            {
                // si tenés método para obtener índice desde TileController, usalo; si no, usar currentIndex
                // indexToSend = currentSelectedTile.GetTileIndex(); // implementa si querés
            }
            Debug.Log("Desde GameManagerFin");
            float reactionMs = (trialStartTime > 0f) ? (Time.realtimeSinceStartup - trialStartTime) * 1000f : -1f;

            currentSelectedTile.RegisterResult(guessedMoved, indexToSend, pruebaEfecto, reactionMs);
        }

        // limpiar selección
        currentSelectedTile = null;

        // avanzar estado (reutilizamos tu método existente)
        OnUserAnswered();
    }


    void EnsureTitleOnlyInteractCanvas()
    {
        if (interactCanvasInstance == null && canvasInteractuarPrefab != null)
        {
            interactCanvasInstance = Instantiate(canvasInteractuarPrefab);
            interactCanvasInstance.name = "CanvasInteractuarEstatuas_Instance";

            var texto = interactCanvasInstance.transform.Find("Title")?.gameObject;
            this.titleText = texto?.GetComponent<UnityEngine.UI.Text>();
            this.titleText.text = "Memorizando";

        }
        // Desactivar botones
        if (interactCanvasInstance != null)
        {
            var movedBtnGO = interactCanvasInstance.transform.Find("TouchButtonMoved")?.gameObject;
            var notMovedBtnGO = interactCanvasInstance.transform.Find("TouchButtonNotMoved")?.gameObject;
            if (movedBtnGO != null) movedBtnGO.SetActive(false);
            if (notMovedBtnGO != null) notMovedBtnGO.SetActive(false);
        }
    }


    ///................................................................................................
    ///// Helper: espera y destruye (si usaste Animator)
    System.Collections.IEnumerator WaitAndDestroy(GameObject go, float wait)
    {
        yield return new WaitForSeconds(wait);
        if (go != null) Destroy(go);
    }

    // Helper: destruye tras animación
    System.Collections.IEnumerator AnimateAndDestroyUI(GameObject uiGO, float duration, System.Action onDone = null)
    {
        if (uiGO == null) { onDone?.Invoke(); yield break; }

        // Aseguramos CanvasGroup
        CanvasGroup cg = uiGO.GetComponent<CanvasGroup>();
        if (cg == null) cg = uiGO.AddComponent<CanvasGroup>();

        Vector3 startScale = uiGO.transform.localScale;
        Vector3 endScale = startScale * 0.6f;
        float t = 0f;

        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(1f, 0f, t / duration);
            cg.alpha = k;
            uiGO.transform.localScale = Vector3.Lerp(endScale, startScale, k); // scale down towards endScale
            yield return null;
        }

        // ensure
        if (uiGO != null) Destroy(uiGO);
        onDone?.Invoke();
    }

    // Helper: animación fade + scale para textos / objetos world-space
    System.Collections.IEnumerator AnimateAndDestroyWorld(GameObject go, float duration, System.Action onDone = null)
    {
        if (go == null) { onDone?.Invoke(); yield break; }

        // Recolectamos componentes de texto / renderers
        var tmps = go.GetComponentsInChildren<TMPro.TextMeshPro>(true);
        var tmpgsui = go.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true);
        var uis = go.GetComponentsInChildren<UnityEngine.UI.Text>(true);
        var renderers = go.GetComponentsInChildren<Renderer>(true);

        // Guardar materiales originales y clonarlos para no tocar sharedMaterial
        var mats = new List<Material>();
        foreach (var r in renderers)
        {
            // saltar si es un CanvasRenderer (UI)
            if (r is UnityEngine.MeshRenderer || r is UnityEngine.SkinnedMeshRenderer || r is UnityEngine.MeshRenderer)
            {
                mats.Add(r.material); // acceder material crea instancia
            }
        }

        Vector3 startScale = go.transform.localScale;
        Vector3 endScale = startScale * 0.6f;
        float elapsed = 0f;

        // Obtener colores iniciales para textos
        Color[] tmpColors = tmps.Select(x => x.color).ToArray();
        Color[] tmpgColors = tmpgsui.Select(x => x.color).ToArray();
        Color[] uiColors = uis.Select(x => x.color).ToArray();

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float k = 1f - Mathf.SmoothStep(0f, 1f, elapsed / duration); // 1->0

            // scale
            go.transform.localScale = Vector3.Lerp(endScale, startScale, k);

            // fade TMP (world)
            for (int i = 0; i < tmps.Length; i++)
            {
                var c = tmpColors[i];
                c.a = k * tmpColors[i].a;
                tmps[i].color = c;
            }
            // fade TMP UGUI
            for (int i = 0; i < tmpgsui.Length; i++)
            {
                var c = tmpgColors[i];
                c.a = k * tmpgColors[i].a;
                tmpgsui[i].color = c;
            }
            // fade old UI text
            for (int i = 0; i < uis.Length; i++)
            {
                var c = uiColors[i];
                c.a = k * uiColors[i].a;
                uis[i].color = c;
            }

            // fade renderers' materials (intentar con _Color o material.color)
            foreach (var mat in mats)
            {
                if (mat == null) continue;
                if (mat.HasProperty("_Color"))
                {
                    Color c = mat.color;
                    c.a = k * mat.color.a;
                    mat.color = c;
                }
                // si usan "_BaseColor" (HDRP/URP), intentar también:
                if (mat.HasProperty("_BaseColor"))
                {
                    Color c = mat.GetColor("_BaseColor");
                    c.a = k * c.a;
                    mat.SetColor("_BaseColor", c);
                }
            }

            yield return null;
        }

        // aseguramos destrucción y restauración final
        if (go != null) Destroy(go);
        onDone?.Invoke();
    }

    private bool HasTrigger(Animator anim, string triggerName)
    {
        // reflection a layers/cparams puede ser costoso; se hace de forma simple:
        foreach (var p in anim.parameters)
        {
            if (p.type == AnimatorControllerParameterType.Trigger && p.name == triggerName) return true;
        }
        return false;
    }

    Vector3 GetObjectTopPosition(GameObject go, out Bounds bounds)
    {
        bounds = new Bounds(go.transform.position, Vector3.zero);
        bool initialized = false;

        // 1) Preferimos Renderers (mesh, skinned)
        var rends = go.GetComponentsInChildren<Renderer>(true);
        if (rends.Length > 0)
        {
            bounds = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) bounds.Encapsulate(rends[i].bounds);
            initialized = true;
        }

        // 2) Fallback a Colliders si no hay renderers
        if (!initialized)
        {
            var cols = go.GetComponentsInChildren<Collider>(true);
            if (cols.Length > 0)
            {
                bounds = cols[0].bounds;
                for (int i = 1; i < cols.Length; i++) bounds.Encapsulate(cols[i].bounds);
                initialized = true;
            }
        }

        // 3) Si no hay nada, usar transform (tiny box)
        if (!initialized)
        {
            bounds = new Bounds(go.transform.position, Vector3.one * 0.5f);
        }

        // devolver la punta superior central
        Vector3 topCenter = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
        return topCenter;
    }

    private IEnumerator HandleWaitingStationCoroutine(float waitDuration)
    {
        if (waitingStationPrefab == null)
        {
            Debug.LogWarning("[GameManagerFin] waitingStationPrefab no asignado, salteando WAIT stage.");
            yield return new WaitForSeconds(waitDuration);
            currentRoom.MoveStatues();
            state = State.UserChecking;
            yield break;
        }

        

        // 2) bloquear input del jugador
        InputHandler.SetEnabled(false);

        if (floatingTextConfig == null) floatingTextConfig = DynamicTextManager.defaultData;

        // 3) calcular la posición del floating text sobre la estación
        Bounds b;
        Vector3 topPos = GetObjectTopPosition(station, out b);
        float margin = Mathf.Max(0.50f, b.size.y * 0.35f);
        Vector3 spawnPosFinal = topPos + Vector3.up * margin;

        // 4) crear floating text que muestre el countdown
        if (floatingWaitingStationText != null) Destroy(floatingWaitingStationText);
        floatingWaitingStationText = DynamicTextManager.CreateText(spawnPosFinal, $"Esperá {Mathf.CeilToInt(waitDuration)}s", floatingTextConfig);

        // 6) Al terminar el countdown: mover las estatuas UNA VEZ (mantiene la lógica swap/intacta)
        currentRoom.MoveStatues();

        // 5) Coroutine de countdown: actualiza texto con segundos restantes
        float elapsed = 0f;
        while (elapsed < waitDuration)
        {
            elapsed += Time.deltaTime;
            int remaining = Mathf.Max(0, Mathf.CeilToInt(waitDuration - elapsed));
            UpdateFloatingTextValue($"Esperá {remaining}s");
            yield return null; // no bloquea el juego
        }

        

        // 7) Mostrar texto final "Ahora, indicá"
        UpdateFloatingTextValue("Ahora, indicá");

        // 8) re-habilitar input
        InputHandler.SetEnabled(true);

        // 9) limpiar
        if (floatingWaitingStationText != null) Destroy(floatingWaitingStationText);
        if (station != null) Destroy(station);

        // 10) avanzar estado
        titleText.text = "Contestar";
        state = State.UserChecking;

    }


    public void UpdateFloatingTextValue(string text)
    {
        Debug.Log("[GameManagerFin] UpdateFloatingTextValue: " + text);
        var tm = floatingWaitingStationText.GetComponentInChildren<TextMeshProUGUI>();
        if (tm != null)
        {
            tm.text = text;
        }
    }

    /// <summary>
    /// Llamar desde WaitingStationInteraction cuando el player interactúa con la estación.
    /// Inicia la coroutine que ya tenés (HandleWaitingStationCoroutine).
    /// </summary>
    public void TriggerWaitingStation()
    {
        // solo si estamos en el estado de llegar a la estación o Start
        // ajustá la comprobación de state según tu lógica
        if (state != State.LLegarALaEstacion && state != State.WaitingForPlayerEnter)
        {
            Debug.LogWarning("[GameManagerFin] TriggerWaitingStation llamado en estado no válido: " + state);
            return;
        }
        
        // Cambiamos el estado y lanzamos la coroutine que maneja la espera
        state = State.WatingTime;
    }

    void generarStation()
    {
        // 1) Instanciar la estación cerca de la puerta generada (si existe)
        Transform spawnParent = currentRoom != null ? currentRoom.transform : this.transform;
        var spawnPos = currentRoom.GetWaitingStationPosition();

        station = Instantiate(waitingStationPrefab, spawnPos, Quaternion.identity, spawnParent);
        station.transform.localRotation = Quaternion.Euler(0, 90f, 0);

        station.name = "WaitingStation_Instance";
    }

    public float GetTimeBetweenPhases()
    {
        return timeBetweenPhases;
    }

    public TrialMeta GetTrialMeta()
    {
        return trialMeta;
    }
}

[Serializable]
public class TrialMeta
{
    public string object_category;
    public string object_subpool;      // si no hay data, quedará null/empty
    public float memorization_time_ms;
    public bool swap_event;
}