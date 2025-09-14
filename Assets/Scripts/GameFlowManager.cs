using EasyDoorSystem;
using Lean.Gui;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class GameFlowManager : MonoBehaviour
{
    public static GameFlowManager Instance { get; private set; }

    public enum FlowState { Login, InitGame, PlayingSession, EndSession, EndGame }
    private FlowState state = FlowState.Login;
    

    [Header("Offline")]
    [SerializeField] private bool isOffline=false;// solo para desarillo
    [Header("Referencias (assign in inspector)")]
    [SerializeField] private GameObject StartGameItem;
    [SerializeField] private GameObject joystickUIPrefab;
    [SerializeField] private ExitDoorTrigger exitDoor;


    private GameObject _startGameItem = null; // Reference to the StartGameItem prefab

    // Internals
    Transform _hall;

    private void Awake()
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

    public bool IsOffline() { return isOffline; }

    private void OnEnable()
    {

        HallEntryTrigger.onPlayerEnter += TerminoPartida; // Subscribe to the event to start the game when player enters the hall


    }
    public void reiniciarPlayer()
    {
        Debug.Log("Reiniciando posicion del player");
        var GamePlayer = GameObject.Find("Player");
        if(GamePlayer == null)
        {
            Debug.LogWarning("Player GameObject not found. Cannot reset player position.");
            return;
        }
        var playerPosition = GameObject.Find("PlayerSpawn").transform; // Get the player spawn position from the hall
        if (playerPosition == null)
        {
            Debug.LogWarning("PlayerSpawn GameObject not found. Cannot reset player position.");
            return;
        }

        // 3) Reiniciar posición/rotación con cuidado si tiene CharacterController o Rigidbody
        // Desactivar temporalmente CharacterController para evitar "teleport-back"
        var cc = GamePlayer.GetComponent<CharacterController>();
        if (cc != null)
        {
            cc.enabled = false;
        }

        var rb = GamePlayer.GetComponent<Rigidbody>();
        if (rb != null)
        {
            // si tiene rigidbody, parar velocidad y mover con MovePosition (o setear posición si kinematic)
            // hacemos lo siguiente: desactivar temporalmente la simulación si no quieres efectos fisicos
            bool wasKinematic = rb.isKinematic;
            rb.isKinematic = true; // evitar fuerzas/colisiones que contrarresten el teleport
            GamePlayer.transform.position = playerPosition.position;
            GamePlayer.transform.rotation = playerPosition.rotation;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = wasKinematic; // restaurar
        }
        else
        {
            // sin rigidbody: sólo setear transform
            GamePlayer.transform.position = playerPosition.position;
            GamePlayer.transform.rotation = playerPosition.rotation;
        }

    }
    private void OnDisable()
    {

        HallEntryTrigger.onPlayerEnter -= TerminoPartida; // Subscribe to the event to start the game when player enters the hall
    }

    private void TerminoPartida()
    {

        state = FlowState.EndSession;
        this.ChangeState();
    }

    private void Start()
    {


        state = FlowState.Login;
        ChangeState();

    }

    public void CargarJuego()
    {
        // Initialize the game flow
        state = FlowState.InitGame;
        this.ChangeState();
    }

    public void TerminarJuego()
    {
        // Initialize the game flow
        state = FlowState.EndGame;
        this.ChangeState();
    }
    public void EmpezarSesion()
    {
        state = FlowState.PlayingSession;
        this.ChangeState();
    }

    public FlowState getState()
    {
        return state;
    }
    private void ChangeState()
    {
        switch (state)
        {
            case FlowState.Login:
                SessionManager.Instance.Init(); //Inicio el sesion Manager
                Debug.Log("Iniciando Login");
                break;
            case FlowState.InitGame:

                // 2) Buscar el Hall en la escena
                var hallGO = GameObject.Find("Hall");
                _hall = hallGO.transform;

                // 3) Buscar puertas (EasyDoor) dentro del Hall
                var hallDoor = GameObject.Find("HallDoor");
                var hallDoorScript = hallDoor?.GetComponentInChildren<EasyDoor>();

                var allowAutoOpen = hallDoorScript.GetType().GetField("allowAutoOpen", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                if (allowAutoOpen != null) allowAutoOpen.SetValue(hallDoorScript, false);

                InputHandler.Init(joystickUIPrefab);

                if(_startGameItem == null)
                {
                    // 6) Instanciar startItem en Hall.PlayerSpawn si existe
                    Vector3 spawnPosStartItem = _hall.position;
                    var spawnStartItem = _hall.Find("StartGameItemPosition");
                    if (spawnStartItem != null) spawnPosStartItem = spawnStartItem.position;

                    if (spawnStartItem != null)
                    {
                        _startGameItem = Instantiate(StartGameItem, spawnPosStartItem, Quaternion.identity);
                    }
                    else
                    {
                        Debug.LogWarning("[RoomTestLauncher] StartGameItem no asignado.");
                    }

                }
                else
                {
                    var script = _startGameItem.GetComponent<StartGameInteraction>();
                    script.reiniciarInteract();
                }

                    exitDoor.setAbrirPuerta(true); // Habilitar la puerta de salida
                break;
            case FlowState.PlayingSession:
                exitDoor.setAbrirPuerta(false); // Habilitar la puerta de salida
                GameManagerFin.Instance.Init(); //Inicio el GameManager
                break;
            case FlowState.EndSession:
                GameManagerFin.Instance.EliminarUIPartida();
                GameManagerFin.Instance.DestruirSala();
                CargarJuego();
                break;
            case FlowState.EndGame:
                exitDoor.setAbrirPuerta(false); // Habilitar la puerta de salida
                GameManagerFin.Instance.Desabilitar();
                InputHandler.desabilitar();
                state = FlowState.Login;
                this.ChangeState();
                break;
        }
    }


}
