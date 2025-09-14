using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using System.IO; // <-- agregar

public class LogManager : MonoBehaviour
{
    // Singleton
    public static LogManager Instance { get; private set; }

    [Header("Send configuration")]
    [Tooltip("Log endpoint URL")]
    [SerializeField] private string logsUrl = "http://localhost:8000/api/logs/log/";

    // Thread-safe log queue
    private List<LogData> logQueue = new List<LogData>();

    private void Awake()
    {
        // Initialize singleton
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
    /// Adds a log to the buffer (thread-safe).
    /// </summary>
    public void AddLog(LogData log)
    {
        logQueue.Add(log);
    }
    /// <summary>
    /// Public method for UI button to force send all logs now.
    /// Wraps logs between InicioPartida and FinPartida.
    /// </summary>
    public void SendLogsAndWrapSession()
    {
        // Add start-of-game log
        //AddMarkerLog("InicioPartida", "Comienzo de la partida", 0);
        //AddMarkerLog("FinPartida", "Fin de la partida");
        // Send all logs
        StartCoroutine(SendAndWrap());
    }

    private IEnumerator SendAndWrap()
    {
        yield return SendLogsCoroutine();
        // Add end-of-game log after sending
        
    }

    /// <summary>
    /// Adds a marker log (InicioPartida / FinPartida) with default position.
    /// </summary>
    public void AddMarkerLog(string eventType, string description, int posicion)
    {
        string timestamp = DateTime.UtcNow.ToString("o");
        var marker = new LogData(
            username: SessionManager.Instance.user?.Username ?? "unknown",
            event_type: eventType,
            description: description,
            timestamp: timestamp,
            x: 0f, y: 0f, z: 0f
        );
        if(posicion == -1)
            logQueue.Add( marker); // Insert at the beginning
        else
            logQueue.Insert(posicion, marker); // Insert at the beginning
    }
    private void AddMarkerLog(string eventType, string description)
    {
        string timestamp = DateTime.UtcNow.ToString("o");
        var marker = new LogData(
            username: SessionManager.Instance.user?.Username ?? "unknown",
            event_type: eventType,
            description: description,
            timestamp: timestamp,
            x: 0f, y: 0f, z: 0f
        );
        logQueue.Add( marker);
    }
    public void ClearLogs()
    {
        logQueue.Clear();
    }
    //----------------------------------------------------------------------------------------------------------------------
    /// <summary>
    /// Coroutine that groups and sends pending logs.
    /// </summary>
    private IEnumerator SendLogsCoroutine()
    {


            // 1) Drain the queue
            // 1) Drain the queue
            var batch = new List<LogData>(logQueue);
            logQueue.Clear();

            if (batch.Count == 0)
                yield break;

        // Si estamos offline --> guardar localmente para pruebas y salir
        if (GameFlowManager.Instance.IsOffline())
        {
            Debug.Log("[LogManager] Offline detected - saving logs locally for later inspection.");
            SaveLogsLocally(batch);
            yield break;
        }

        // Helper to build + send a request with a given token
        IEnumerator SendBatch(string token, Action<bool> callback)
            {
                var wrapper = new LogDataList { logs = batch.ToArray() };
                string json = JsonUtility.ToJson(wrapper);
                byte[] body = Encoding.UTF8.GetBytes(json);

                using (var req = new UnityWebRequest(logsUrl, "POST"))
                {
                    req.uploadHandler = new UploadHandlerRaw(body);
                    req.downloadHandler = new DownloadHandlerBuffer();
                    req.SetRequestHeader("Content-Type", "application/json");

                    if (!string.IsNullOrEmpty(token))
                        req.SetRequestHeader("Authorization", "Bearer " + token);

                    yield return req.SendWebRequest();

                    Debug.Log("[LogManager] HTTP Status: " + req.responseCode);
                    Debug.Log("[LogManager] Raw response: " + req.downloadHandler.text);

                    bool ok = (req.result == UnityWebRequest.Result.Success)
                              && (req.responseCode >= 200 && req.responseCode < 300);

                    callback(ok);
                }
            }

            // 2) Try with current token
            string token1 = SessionManager.Instance.user?.AccessToken;
            bool sent = false;
            yield return SendBatch(token1, ok => sent = ok);

            if (sent)
            {
                Debug.Log("[LogManager] Sent " + batch.Count + " logs successfully");
                yield break;
            }

            // 3) Initial send failed — refresh token
            Debug.LogWarning("[LogManager] Initial send failed - attempting token refresh...");

            yield return APIManager.Instance.RefreshAccessToken(
                newToken =>
                {
                    SessionManager.Instance.user.AccessToken = newToken;
                },
                () =>
                {
                    PopupManager.Instance.ShowPopup(
                        "Session expired",
                        "Your session has ended. Please log in again."
                    );
                    SessionManager.Instance.ClearSession();
                }
            );

            // 4) Retry with refreshed token
            string token2 = SessionManager.Instance.user?.AccessToken;
            sent = false;
            yield return SendBatch(token2, ok => sent = ok);

            if (sent)
            {
                Debug.Log("[LogManager] Retried and sent " + batch.Count + " logs successfully");
                yield break;
            }

            // 5) Both attempts failed — re-enqueue for next time
            Debug.LogError("[LogManager] Retry after refresh also failed - re-enqueuing logs");
            foreach (var l in batch)
                logQueue.Add(l);
        
    }
    public void EnqueueTrial(TrialLog trial)
    {
        // serializar trial a JSON y guardarlo como LogData.description
        string json = JsonUtility.ToJson(trial);
        var marker = new LogData(
            username: SessionManager.Instance.user?.Username ?? "unknown",
            event_type: "trial",
            description: json,
            timestamp: DateTime.UtcNow.ToString("o"),
            x: 0f, y: 0f, z: 0f
        );
        AddLog(marker);
    }



    /// <summary>
    /// Guarda un batch de logs en Application.persistentDataPath con timestamp.
    /// Crea un archivo JSON separado por cada guardado: offline_logs_yyyyMMdd_HHmmss.json
    /// </summary>
    private void SaveLogsLocally(List<LogData> batch)
    {
        try
        {
            if (batch == null || batch.Count == 0) return;

            var wrapper = new LogDataList { logs = batch.ToArray() };
            string json = JsonUtility.ToJson(wrapper, true);

            string folder = Path.Combine(Application.persistentDataPath, "offline_logs");
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

            string filename = $"offline_logs_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
            string fullPath = Path.Combine(folder, filename);

            File.WriteAllText(fullPath, json, Encoding.UTF8);
            Debug.Log($"[LogManager] Saved {batch.Count} logs locally to: {fullPath}");
        }
        catch (Exception ex)
        {
            Debug.LogError("[LogManager] Error saving logs locally: " + ex.Message);
        }
    }

    /// <summary>
    /// Opcional: intenta reenviar archivos guardados localmente (llamar cuando vuelvas online).
    /// </summary>
    public IEnumerator SendSavedLogsIfAny()
    {
        string folder = Path.Combine(Application.persistentDataPath, "offline_logs");
        if (!Directory.Exists(folder)) yield break;

        var files = Directory.GetFiles(folder, "offline_logs_*.json").OrderBy(f => f).ToArray();
        if (files.Length == 0) yield break;

        foreach (var file in files)
        {
            string text = null;
            try
            {
                text = File.ReadAllText(file, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LogManager] Could not read saved log file: " + file + " -> " + ex.Message);
                // skip this file
                continue;
            }

            // Enviamos el contenido como payload (no hacemos parsing a objetos para simplicidad)
            byte[] body = Encoding.UTF8.GetBytes(text);

            using (var req = new UnityWebRequest(logsUrl, "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(body);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                string token = SessionManager.Instance.user?.AccessToken;
                if (!string.IsNullOrEmpty(token)) req.SetRequestHeader("Authorization", "Bearer " + token);

                yield return req.SendWebRequest();

                bool ok = (req.result == UnityWebRequest.Result.Success) && (req.responseCode >= 200 && req.responseCode < 300);
                if (ok)
                {
                    Debug.Log("[LogManager] Successfully resent saved logs: " + Path.GetFileName(file));
                    // borrar archivo
                    try { File.Delete(file); } catch { /* no crítico */ }
                }
                else
                {
                    Debug.LogWarning("[LogManager] Failed to resend saved logs: " + Path.GetFileName(file) + " responseCode:" + req.responseCode);
                    // si falla, salimos para reintentar más tarde (no borramos el archivo)
                    yield break;
                }
            }
        }
    }


}

// Serializable container for JsonUtility
[Serializable]
public class LogDataList
{
    public LogData[] logs;
}

// Example LogData class (adjust as needed)
[Serializable]
public class LogData
{
    public string username;
    public string event_type;
    public string description;
    public string timestamp;
    public float x, y, z;

    public LogData(string username, string event_type, string description, string timestamp, float x, float y, float z)
    {
        this.username = username;
        this.event_type = event_type;
        this.description = description;
        this.timestamp = timestamp;
        this.x = x;
        this.y = y;
        this.z = z;
    }
}

