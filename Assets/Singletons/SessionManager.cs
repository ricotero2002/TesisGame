using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using System;
using System.Collections;
using UnityEngine.SceneManagement;
using UnityEditor;
using Lean.Gui;
using System.Linq;
using System.Collections.Generic;
public class SessionManager : MonoBehaviour
{
    public static SessionManager Instance;

    public string SessionId { get; private set; }
    public int RenderSeed { get; private set; }
    public List<string> ChosenGroupIds { get; private set; } = new List<string>();
    public DateTime SessionStartUtc { get; private set; }

    private List<TrialLog> trials = new List<TrialLog>();

    public UserConecctionData user { get; private set; }
    public string CsrfToken { get; set; }

    [Header("MenuInicial")]
    [SerializeField] private GameObject Menu;

    [Header("Login")]
    private GameObject LoginMenu;
    private GameObject MainMenu;
    private Button Aceptar;
    private Button Registarse;
    private LeanButton Desloguearse;
    private TMP_InputField usernameInput;
    private TMP_InputField passwordInput;
    private LeanButton JugarButton;
    private LeanButton SalirButton;
    [Header("Camera")]
    private Camera camera;
    private bool FirstInit=true;

    public bool IsLoggedIn { get { return !string.IsNullOrEmpty(user.RefreshToken); } }

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

    public void Init()
    {

        if (FirstInit)
        {
            Menu = Instantiate(Menu);

            MainMenu = Menu.transform.Find("MainMenu").gameObject;
            LoginMenu = Menu.transform.Find("Login").gameObject;

            LoginMenu.SetActive(false);
            MainMenu.SetActive(false);

            Aceptar = LoginMenu.transform.Find("Aceptar").GetComponent<Button>();
            Aceptar.onClick.AddListener(() => OnLoginButtonPressed());

            Registarse = LoginMenu.transform.Find("Register").GetComponent<Button>();
            Registarse.onClick.AddListener(OnRegisterButtonPressed);

            Desloguearse = MainMenu.transform.Find("Desloguearse").GetComponent<LeanButton>();
            Desloguearse.OnClick.AddListener(ClearSession);

            usernameInput = LoginMenu.transform.Find("Username").GetComponent<TMP_InputField>();

            passwordInput = LoginMenu.transform.Find("Password").GetComponent<TMP_InputField>();

            JugarButton = MainMenu.transform.Find("Jugar").GetComponent<LeanButton>();
            JugarButton.OnClick.AddListener(Jugar);

            SalirButton = MainMenu.transform.Find("Salir").GetComponent<LeanButton>();
            SalirButton.OnClick.AddListener(Salir);

            this.camera = Menu.transform.Find("Camera").GetComponent<Camera>();

            Debug.Log(Aceptar.onClick);
            if (LoadSession())
            {
                if (!GameFlowManager.Instance.IsOffline()) StartCoroutine(APIManager.Instance.AttemptRefresh(user));
                else SessionManager.Instance.ShowMainMenu();
            }
            else
            {
                if (!GameFlowManager.Instance.IsOffline()) ShowLogin();
                else SessionManager.Instance.ShowMainMenu();
            }
            this.FirstInit = false;
        }
        else
        {

            Habilitar();
            
        }
    }


    public void ShowMainMenu()
    {
        LoginMenu.SetActive(false);
        MainMenu.SetActive(true);
    }

    public void ShowLogin()
    {
        MainMenu.SetActive(false);
        LoginMenu.SetActive(true);
    }
    void OnRegisterButtonPressed()
    {
        // Open external registration page or show in-game form
        Application.OpenURL(APIManager.Instance.getRegisterSide());
    }
    public void SetSession(string username, string accessToken, string refreshToken)
    {
        user = new UserConecctionData();
        user.Username = username;
        user.RefreshToken = refreshToken;
        user.AccessToken = accessToken;
        SaveSession();

        SaveSession();
        ShowMainMenu();
    }

    public void SaveSession()
    {
        EncryptedSessionStorage.SaveSession(user);
    }

    public bool LoadSession()
    {
        user = EncryptedSessionStorage.LoadSession();
        
        if (user == null)
        {
            return false;
        }
        return true;
    }

    public void ClearSession()
    {
        Debug.Log("aca toy");
        StartCoroutine(APIManager.Instance.Logout());
        EncryptedSessionStorage.ClearSession();
        user = null;
        ShowLogin();
    }

    //regin boton
    // Este es el método que el botón llama directamente
    private void OnLoginButtonPressed()
    {
        StartCoroutine(LoginCoroutine());
    }

    // Esta es la corrutina que hace el trabajo pesado
    private IEnumerator LoginCoroutine()
    {
        Debug.Log("Iniciando login...");

        string username = usernameInput.text;
        string password = passwordInput.text;

        yield return APIManager.Instance.Login(username, password);
    }

    private void Desabilitar()
    {
        Menu.SetActive(false);
        LoginMenu.SetActive(false);
        MainMenu.SetActive(false);
        camera.gameObject.SetActive(false);
    }
    private void Habilitar()
    {
        Menu.SetActive(true);
        LoginMenu.SetActive(false);
        MainMenu.SetActive(true);
        camera.gameObject.SetActive(true);
    }

    //MainMenu Functions
    public void Jugar()
    {
        Desabilitar();
        GameFlowManager.Instance.CargarJuego();
    }

    public void Salir()
    {
        Application.Quit(); // Solo funciona en una build, no en el editor
    }
    public void StartSession(List<string> chosenGroupIds, int renderSeed)
    {
        this.SessionId = System.Guid.NewGuid().ToString();
        this.RenderSeed = renderSeed;
        this.ChosenGroupIds = chosenGroupIds ?? new List<string>();
        this.trials = new List<TrialLog>();
        this.SessionStartUtc = DateTime.UtcNow;

        LogManager.Instance.AddMarkerLog("InicioPartida", $"StartSession {SessionId}", 0);
    }

    public void AddTrial(TrialLog t)
    {
        if (string.IsNullOrEmpty(t.session_id)) t.session_id = this.SessionId;
        if (string.IsNullOrEmpty(t.participant_id)) t.participant_id = this.user?.Username ?? "unknown";
        if (string.IsNullOrEmpty(t.timestamp)) t.timestamp = DateTime.UtcNow.ToString("o");
        if (t.render_group == null) t.render_group = this.ChosenGroupIds.ToArray();
        if (t.render_seed == 0) t.render_seed = this.RenderSeed;

        trials.Add(t);

        // Encolar para envío crudo
        LogManager.Instance?.EnqueueTrial(t);
    }

    public void EndSession()
    {
        LogManager.Instance.AddMarkerLog("FinPartida", $"EndSession {SessionId}",-1);
        // NO calcular summaries aquí; servidor lo hace.
        // Opcional: forzar envío de cola
        LogManager.Instance?.SendLogsAndWrapSession();
    }
}

[System.Serializable]
public class TokenRefreshResponse
{
    public string access;
}

[Serializable]
public class TrialLog
{
    public string session_id;
    public string participant_id;
    public string timestamp;
    public int trial_index;
    public string phase;
    public string object_id;
    public string object_category;
    public string object_similarity_label;
    public bool object_actual_moved;
    public bool participant_said_moved;
    public string response; // "same"/"different"/"no_response"
    public int reaction_time_ms;
    public int memorization_time_ms;
    public bool swap_event;
    public SwapEntry[] swap_history;
    public int render_seed;
    public string[] render_group;
}

[Serializable]
public class SwapEntry
{
    public int from;
    public int to;
}
