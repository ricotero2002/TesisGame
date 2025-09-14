using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections;
using System.Text;
using System.Diagnostics;

public class APIManager : MonoBehaviour
{
    // Instancia del Singleton
    public static APIManager Instance { get; private set; }

    [Header("UI")]
    private string baseUrl = "http://localhost:8000/";
    private string frontEndUrl = "http://192.168.0.4:3000";
    private string refreshUrl = "api/token/refresh/";
    private string loginUrl = "login/";
    private string csrfUrl =  "csrf/"; 
    private string registerUrl = "register/"; 
    private string logoutUrl = "logout/";

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            // Cargar datos de sesión, si los hubiera
            // AccessToken = ...; RefreshToken = ...;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public string getRegisterSide()
    {
        return frontEndUrl + registerUrl;
    }
    public IEnumerator RefreshAccessToken(Action<string> onSuccess, Action onFailure)
    {
        // Asegurate de enviar el refresh token en el cuerpo de la peticion
        UnityWebRequest request = new UnityWebRequest(baseUrl + refreshUrl, "POST");
        request.SetRequestHeader("Content-Type", "application/json");

        string token = SessionManager.Instance.user.RefreshToken;
        RefToken t = new RefToken();
        t.refresh = token;
        string jsonData = JsonUtility.ToJson(t);
        byte[] dataToSend = new System.Text.UTF8Encoding().GetBytes(jsonData);
        request.uploadHandler = new UploadHandlerRaw(dataToSend);
        request.downloadHandler = new DownloadHandlerBuffer();

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success && request.responseCode == 200)
        {
            // Supongamos que el JSON de respuesta es { "token": "nuevoAccessToken", ... }
            var resp = JsonUtility.FromJson<RefToken>(request.downloadHandler.text);
            if (!string.IsNullOrEmpty(resp.refresh))
            { 
                SessionManager.Instance.user.AccessToken = resp.refresh;
                onSuccess(resp.refresh);
            }
            else
            {
                onFailure();
            }
        }
        else
        {
            onFailure();
        }
    }

    // Otros métodos para realizar peticiones protegidas etc.
    public IEnumerator ApiFetch(string url, Action<UnityWebRequest> onComplete)
    {
        UnityWebRequest request = UnityWebRequest.Get(url);
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", $"Bearer {SessionManager.Instance.user.AccessToken}");
        yield return request.SendWebRequest();

        if (request.responseCode == 401)
        {
            UnityEngine.Debug.Log("Access token expirado, intentando refrescar...");
            yield return RefreshAccessToken(
                newToken => {
                    // Reintentar la petición con el nuevo token
                    request = UnityWebRequest.Get(url);
                    request.SetRequestHeader("Content-Type", "application/json");
                    request.SetRequestHeader("Authorization", $"Bearer {newToken}");
                    StartCoroutine(RetryRequest(request, onComplete));
                },
                () =>
                {
                    UnityEngine.Debug.LogError("No se pudo renovar el token.");
                    onComplete(request);
                }
            );
        }
        else
        {
            onComplete(request);
        }
    }

    public IEnumerator Login(string username, string password)
    {
        UnityWebRequest request = new UnityWebRequest(baseUrl + loginUrl, "POST");
        request.SetRequestHeader("Content-Type", "application/json");

        // Verificar si el CSRF token ya está asignado en el SessionManager
        if (string.IsNullOrEmpty(SessionManager.Instance.CsrfToken))
        {
            // Esperar a que la coroutine de obtener CSRF termine
            yield return GetCSRFToken();
        }

        UnityEngine.Debug.LogError("csrf tonken: " + SessionManager.Instance.CsrfToken);
        // Ahora debería existir el token
        request.SetRequestHeader("X-CSRFToken", SessionManager.Instance.CsrfToken);

        // Crear cuerpo con username y password:
        string body = JsonUtility.ToJson(new LoginRequestData(username, password));
        byte[] bodyRaw = Encoding.UTF8.GetBytes(body);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();


        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {


            string responseJson = request.downloadHandler.text;
            UnityEngine.Debug.Log("Login exitoso: " + responseJson);

            // Parsear la respuesta en el objeto LoginResponse
            LoginResponse response = JsonUtility.FromJson<LoginResponse>(responseJson);
            if (response != null && response.tokens != null)
            {
                // Crear el UserConecctionData con los datos parseados
                SessionManager.Instance.SetSession(response.username, response.tokens.access, response.tokens.refresh);

            }
            else
            {
                UnityEngine.Debug.LogError("Error al parsear la respuesta del login.");
            }
        }
        else
        {
            PopupManager.Instance.ShowPopup(
                "Error de login",
                "Usuario o contraseña incorrectos."
            );
            UnityEngine.Debug.LogError("Error en el login: " + request.error);
        }

    }

    public IEnumerator Logout()
    {
        UnityWebRequest request = new UnityWebRequest(baseUrl + logoutUrl, "POST");
        request.SetRequestHeader("Content-Type", "application/json");


        // Suponiendo que tu JSON requiere { "refresh": "token" }
        string token = SessionManager.Instance.user.RefreshToken;
        RefToken t = new RefToken();
        t.refresh = token;
        string jsonData = JsonUtility.ToJson(t);
        byte[] dataToSend = new System.Text.UTF8Encoding().GetBytes(jsonData);
        request.uploadHandler = new UploadHandlerRaw(dataToSend);
        request.downloadHandler = new DownloadHandlerBuffer();


        UnityEngine.Debug.Log("Logout 1");
        yield return request.SendWebRequest();

        UnityEngine.Debug.Log("Logout 2");

        if (request.result == UnityWebRequest.Result.Success)
        {


            string responseJson = request.downloadHandler.text;
            UnityEngine.Debug.Log("Logout exitoso: " + responseJson);
        }
        else
        {
            PopupManager.Instance.ShowPopup(
                "Error de logout",
                "Usuario o contraseña incorrectos."
            );
            UnityEngine.Debug.LogError("Error en el logout: " + request.error);
        }

    }
    private IEnumerator RetryRequest(UnityWebRequest request, Action<UnityWebRequest> onComplete)
    {
        yield return request.SendWebRequest();
        onComplete(request);
    }

    public IEnumerator GetCSRFToken()
    {
        UnityWebRequest request = UnityWebRequest.Get(baseUrl + csrfUrl);
        request.downloadHandler = new DownloadHandlerBuffer();
        yield return request.SendWebRequest();

        UnityEngine.Debug.Log(" Peticion CSRF token.");
        if (request.result == UnityWebRequest.Result.Success)
        {
            CSRFResponse resp = JsonUtility.FromJson<CSRFResponse>(request.downloadHandler.text);
            if (!string.IsNullOrEmpty(resp.csrfToken))
            {
                SessionManager.Instance.CsrfToken = resp.csrfToken;
                yield break; //  Token obtenido con éxito
            }
        }

        UnityEngine.Debug.Log(" No se pudo obtener el CSRF token.");
        SessionManager.Instance.CsrfToken = null;
    }

    public IEnumerator AttemptRefresh(UserConecctionData user)
    {
        // Call your backend’s refresh endpoint
        UnityWebRequest request = new UnityWebRequest(baseUrl + refreshUrl, "POST");
        request.SetRequestHeader("Content-Type", "application/json");
        // The refresh token is in cookie; UnityWebRequest will not send cookies by default,
        // so you may need to pass it in the body or header as your API expects.
        // e.g. req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(
        //    JsonUtility.ToJson(new { refresh = user.RefreshToken })
        // ));

        string token = SessionManager.Instance.user.RefreshToken;
        RefToken t = new RefToken();
        t.refresh = token;
        string jsonData = JsonUtility.ToJson(t);
        byte[] dataToSend = new System.Text.UTF8Encoding().GetBytes(jsonData);
        request.uploadHandler = new UploadHandlerRaw(dataToSend);
        request.downloadHandler = new DownloadHandlerBuffer();

        UnityEngine.Debug.Log(SessionManager.Instance.user.RefreshToken);
        yield return request.SendWebRequest();


        if (request.result == UnityWebRequest.Result.Success && request.responseCode == 200)
        {
            // Parse new access token
            var json = request.downloadHandler.text;
            var resp = JsonUtility.FromJson<TokenRefreshResponse>(json);
            user.AccessToken = resp.access;
            SessionManager.Instance.SaveSession();

            SessionManager.Instance.ShowMainMenu();
        }
        else
        {
            UnityEngine.Debug.LogError(request.error);
            // Session invalid -> clear and show login
            SessionManager.Instance.ClearSession();
            PopupManager.Instance.ShowPopup(
                "Sesión expirada",
                "Tu sesión ha finalizado. Por favor inicia sesión de nuevo."
            );
        }
    }

}

