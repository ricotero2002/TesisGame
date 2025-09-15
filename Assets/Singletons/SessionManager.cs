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
    public void StartSession(List<string> chosenGroupIds)
    {
        this.SessionId = System.Guid.NewGuid().ToString();
        int seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
        this.RenderSeed = seed;
        UnityEngine.Random.InitState(seed); // importante: hace reproducible UnityEngine.Random.value / Range / etc.
        this.ChosenGroupIds = chosenGroupIds ?? new List<string>();
        this.trials = new List<TrialLog>();
        this.SessionStartUtc = DateTime.UtcNow;

        LogManager.Instance?.AddMarkerLog("InicioPartida", $"StartSession {SessionId} seed:{seed}", 0);
    }

    // Dentro de SessionManager.cs (reemplaza tu AddTrial por este)
    public void AddTrial(TrialLog t)
    {
        if (t == null) return;

        // IDs / timestamps básicos
        if (string.IsNullOrEmpty(t.session_id)) t.session_id = this.SessionId;
        if (string.IsNullOrEmpty(t.participant_id)) t.participant_id = this.user?.Username ?? "unknown";
        if (string.IsNullOrEmpty(t.timestamp)) t.timestamp = DateTime.UtcNow.ToString("o");

        // Phase: normalizar a token lowercase ("test", "study", etc.). Default "test"
        t.phase = NormalizePhase(t.phase);

        // Response: normalizar a "different" / "same" / "no_response"
        t.response = NormalizeResponse(t.response);

        // Similarity label: mapear etiquetas (español/inglés/mix) a canonical: "high","low","zero","target","foil",...
        t.object_similarity_label = NormalizeSimilarityLabel(t.object_similarity_label);

        // Tiempos: si vienen muy pequeños (<= TIME_SECONDS_THRESHOLD) asumimos que están en segundos -> convertir a ms
        t.reaction_time_ms = NormalizeTimeMs(t.reaction_time_ms, "reaction_time_ms");
        t.memorization_time_ms = NormalizeTimeMs(t.memorization_time_ms, "memorization_time_ms");

        // Render defaults
        if (t.render_group == null || t.render_group.Length == 0)
            t.render_group = this.ChosenGroupIds?.ToArray() ?? new string[0];
        if (t.render_seed == 0)
            t.render_seed = this.RenderSeed;

        // Guardar y encolar
        trials.Add(t);
        LogManager.Instance?.EnqueueTrial(t);
    }

    ////////////////////////////////////////////////////////////////////////////////
    // Helpers (añadir estos métodos privados dentro de SessionManager)
    ////////////////////////////////////////////////////////////////////////////////

    private const int TIME_SECONDS_THRESHOLD = 100; // <= 10 -> very likely seconds => convert to ms

    private int NormalizeTimeMs(int timeValue, string fieldName = "")
    {
        // Preservar valores negativos (sin respuesta)
        if (timeValue <= 0) return timeValue;

        // Si el valor es pequeño (<= TIME_SECONDS_THRESHOLD) interpretamos que vino en segundos -> pasar a ms
        if (timeValue <= TIME_SECONDS_THRESHOLD)
        {
            int ms = Mathf.RoundToInt(timeValue * 1000f);
            Debug.Log($"[SessionManager] Converted {fieldName} from {timeValue} (s) -> {ms} ms (assumed seconds).");
            return ms;
        }

        // Si ya es mayor a threshold, asumimos que está en ms y lo dejamos tal cual
        return timeValue;
    }

    private string NormalizePhase(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "test";
        var r = raw.Trim().ToLowerInvariant();
        // normalizar valores comunes
        if (r == "test" || r == "testing" || r == "testphase") return "test";
        if (r == "study" || r == "studyphase" || r == "study_phase") return "study";
        return r; // fallback: devolver lowercase
    }

    private string NormalizeResponse(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "no_response";
        var r = raw.Trim().ToLowerInvariant();

        // mapear variantes en español/inglés
        if (r == "different" || r == "diff" || r == "diferente" || r == "distinto" || r.Contains("different") || r.Contains("difer")) return "different";
        if (r == "same" || r == "igual" || r == "mismo" || r.Contains("same") || r.Contains("igual")) return "same";
        if (r == "no_response" || r == "noresponse" || r == "no_response" || r == "no_res" || r == "no" || r == "none") return "no_response";

        // si viene "true"/"false" en participant_said_moved, preferir inferir:
        // (pero NO lo hacemos automáticamente aquí, mejor que el caller pase response explícito)

        // fallback: devolver lower original (pero no vacío)
        return r;
    }

    private string NormalizeSimilarityLabel(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "unknown";
        var r = raw.Trim().ToLowerInvariant();

        // mapear variantes en español/inglés a tokens canónicos
        // HIGH
        var highTokens = new HashSet<string> { "alta", "altasimilitud", "altasim", "high", "highsim", "alta_similitud", "alta-similitud" };
        // LOW
        var lowTokens = new HashSet<string> { "baja", "bajasimilitud", "low", "lowsim", "baja_similitud", "baja-similitud" };
        // ZERO / NO SIM
        var zeroTokens = new HashSet<string> { "nosemovio", "no_se_movio", "no_similitud", "nosimilitud", "no_sim", "none", "zero", "no" };
        // TARGET / FOIL / LURE (si los usás)
        var targetTokens = new HashSet<string> { "target", "targets", "targ" };
        var foilTokens = new HashSet<string> { "foil", "foils" };
        var lureTokens = new HashSet<string> { "lure", "lures" };

        // normalize small variants (remove spaces/accents)
        var r_clean = r.Replace(" ", "").Replace("-", "").Replace("_", "");

        if (highTokens.Contains(r_clean) || highTokens.Contains(r)) return "high";
        if (lowTokens.Contains(r_clean) || lowTokens.Contains(r)) return "low";
        if (zeroTokens.Contains(r_clean) || zeroTokens.Contains(r)) return "zero";
        if (targetTokens.Contains(r_clean) || targetTokens.Contains(r)) return "target";
        if (foilTokens.Contains(r_clean) || foilTokens.Contains(r)) return "foil";
        if (lureTokens.Contains(r_clean) || lureTokens.Contains(r)) return "lure";

        // Additional heuristics: if contains words
        if (r.Contains("alta") || r.Contains("high")) return "high";
        if (r.Contains("baja") || r.Contains("low")) return "low";
        if (r.Contains("no") || r.Contains("none") || r.Contains("nose")) return "zero";

        // fallback: devolver lowercase original (documentar que es 'unknown' o el token original)
        return r;
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
    public string object_subpool;
    public string object_similarity_label;
    public bool object_actual_moved;
    public bool participant_said_moved;
    public string response; // "same"/"different"/"no_response"
    public int reaction_time_ms;
    public int memorization_time_ms;
    public bool swap_event;
    public SwapEntry swap_history;
    public int render_seed;
    public string[] render_group;
}

[Serializable]
public class SwapEntry
{
    public int from;
    public int to;
    public SwapEntry(int from, int to) { this.from = from; this.to = to; }
}
