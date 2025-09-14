using Lean.Gui;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class ExitDoorTrigger : MonoBehaviour
{
    [Header("Door & player (opcional)")]
    public GameObject doorRoot;                 // asigna el GameObject que contiene EasyDoor (o la propia puerta)

    [Header("Pause / UI")]
    public GameObject menuPrefab;               // opcional: prefab UI con 2 botones (Salir / Cancelar)

    GameObject _instancedUI;
    private LeanButton salirLeanBtn;
    private LeanButton quedarseLeanBtn;
    private Button salirUnityBtn;
    private Button quedarseUnityBtn;
    private EasyDoorSystem.EasyDoor doorScript;

    // parámetros ajustables
    [Tooltip("Velocidad a la que el jugador retrocede (u/s).")]
    public float retreatSpeed = 8f;
    [Tooltip("Tiempo máximo de retroceso en segundos (seguridad).")]
    public float maxRetreatTime = 12f;

    [Header("Retreat target (si quieres mover a una posición concreta)")]
    [Tooltip("Transform objetivo al que retroceder. Si es null, se usará fallback desde la puerta.")]
    public Transform retreatTarget;

    [Tooltip("Distancia considerada llegada al target.")]
    public float arrivalThreshold = 0.15f;

    void Start()
    {
        // 0) seguridad: doorRoot
        if (doorRoot == null)
        {
            Debug.LogWarning("[ExitDoorTrigger] doorRoot no asignado en inspector.");
        }
        else
        {
            doorScript = doorRoot.GetComponentInChildren<EasyDoorSystem.EasyDoor>();
            if (doorScript == null) Debug.LogWarning("[ExitDoorTrigger] No encontré EasyDoor en doorRoot.");
        }


        // 2) Instanciar UI si hay prefab
        if (menuPrefab == null)
        {
            Debug.LogWarning("[ExitDoorTrigger] menuPrefab no asignado.");
            return;
        }

        _instancedUI = Instantiate(menuPrefab);
        _instancedUI.name = menuPrefab.name + "_Instance";

        // 3) Si es UI (RectTransform), asegurarnos de que esté en un Canvas
        RectTransform rt = _instancedUI.GetComponent<RectTransform>();
        if (rt != null)
        {
            Canvas canvasRoot = FindObjectOfType<Canvas>();
            if (canvasRoot == null)
            {
                // crear Canvas básico
                GameObject canvasGO = new GameObject("Canvas_Auto");
                canvasRoot = canvasGO.AddComponent<Canvas>();
                canvasRoot.renderMode = RenderMode.ScreenSpaceOverlay;
                canvasGO.AddComponent<CanvasScaler>();
                canvasGO.AddComponent<GraphicRaycaster>();
                DontDestroyOnLoad(canvasGO);
                Debug.Log("[ExitDoorTrigger] Canvas creado automáticamente.");
            }
            // parentear con keep world=false para que se posicione correctamente en UI
            _instancedUI.transform.SetParent(canvasRoot.transform, false);
        }

        // 4) Buscar LeanButton por nombre y fallback a cualquier LeanButton o Unity Button
        var salirTransform = _instancedUI.transform.Find("TouchButtonSi");
        if (salirTransform != null) salirLeanBtn = salirTransform.GetComponent<LeanButton>();
        if (salirLeanBtn == null) salirLeanBtn = _instancedUI.GetComponentInChildren<LeanButton>();

        if (salirLeanBtn != null)
        {
            salirLeanBtn.OnClick.AddListener(OnSalirClicked);
            Debug.Log("[ExitDoorTrigger] LeanButton 'Salir' conectado.");
        }
        else
        {
            // fallback a Unity Button
            if (salirTransform != null)
            {
                salirUnityBtn = salirTransform.GetComponent<Button>();
            }
            if (salirUnityBtn == null) salirUnityBtn = _instancedUI.GetComponentInChildren<Button>();
            if (salirUnityBtn != null)
            {
                salirUnityBtn.onClick.AddListener(OnSalirClicked);
                Debug.Log("[ExitDoorTrigger] Unity Button 'Salir' conectado como fallback.");
            }
            else
            {
                Debug.LogWarning("[ExitDoorTrigger] No encontré ni LeanButton ni Button para 'Salir' en el prefab.");
            }
        }

        // 5) Quedarse (No)
        var quedarseTransform = _instancedUI.transform.Find("TouchButtonNo");
        if (quedarseTransform != null) quedarseLeanBtn = quedarseTransform.GetComponent<LeanButton>();
        if (quedarseLeanBtn == null) quedarseLeanBtn = _instancedUI.GetComponentInChildren<LeanButton>(); // puede devolver el mismo que salir, por eso chequea botón de unity abajo

        if (quedarseLeanBtn != null)
        {
            quedarseLeanBtn.OnClick.AddListener(OnQuedarseClicked);
            Debug.Log("[ExitDoorTrigger] LeanButton 'Quedarse' conectado.");
        }
        else
        {
            if (quedarseTransform != null)
            {
                quedarseUnityBtn = quedarseTransform.GetComponent<Button>();
            }
            if (quedarseUnityBtn == null) quedarseUnityBtn = _instancedUI.GetComponentInChildren<Button>();
            if (quedarseUnityBtn != null)
            {
                quedarseUnityBtn.onClick.AddListener(OnQuedarseClicked);
                Debug.Log("[ExitDoorTrigger] Unity Button 'Quedarse' conectado como fallback.");
            }
            else
            {
                Debug.LogWarning("[ExitDoorTrigger] No encontré ni LeanButton ni Button para 'Quedarse' en el prefab.");
            }
        }

        // 6) Iniciar UI oculto
        _instancedUI.SetActive(false);
        doorScript.setStateAllowAutoOpen(false);
    }

    public void setAbrirPuerta(bool abrirPuerta)
    {
        doorScript.setStateAllowAutoOpen(abrirPuerta);
    }

    void OnDestroy()
    {
        // limpiar listeners para evitar memory leaks / llamadas a objetos destruidos
        if (salirLeanBtn != null) salirLeanBtn.OnClick.RemoveListener(OnSalirClicked);
        if (quedarseLeanBtn != null) quedarseLeanBtn.OnClick.RemoveListener(OnQuedarseClicked);
        if (salirUnityBtn != null) salirUnityBtn.onClick.RemoveListener(OnSalirClicked);
        if (quedarseUnityBtn != null) quedarseUnityBtn.onClick.RemoveListener(OnQuedarseClicked);
    }

    private void OnSalirClicked()
    {
        Debug.Log("[ExitDoorTrigger] Salir clicked");
        // asegúrate que GameFlowManager existe
        if (GameFlowManager.Instance != null)
        {
            GameFlowManager.Instance.reiniciarPlayer();
            GameFlowManager.Instance.TerminarJuego();
        }
        else
        {
            Debug.LogWarning("[ExitDoorTrigger] GameFlowManager.Instance es null.");
        }

        if (_instancedUI != null) _instancedUI.SetActive(false);
    }

    private void OnQuedarseClicked()
    {
        Debug.Log("[ExitDoorTrigger] Quedarse pulsado -> mover al player a target hasta que la puerta cierre o llegue al punto.");
        if (_instancedUI != null) _instancedUI.SetActive(false);

        Vector3 targetPos;
        if (retreatTarget != null)
        {
            targetPos = retreatTarget.position;
        }
        else if (doorRoot != null)
        {
            // fallback: tomar un punto a cierta distancia hacia atrás de la puerta
            targetPos = doorRoot.transform.position - doorRoot.transform.forward * 2.0f;
        }
        else
        {
            Debug.LogWarning("[ExitDoorTrigger] No hay retreatTarget ni doorRoot -> no se puede retroceder.");
            return;
        }

        StartCoroutine(MovePlayerToTargetUntilDoorClosedCoroutine(targetPos));
    }

    private IEnumerator MovePlayerToTargetUntilDoorClosedCoroutine(Vector3 targetPosition)
    {
        var player = GameObject.FindWithTag("Player") ?? GameObject.Find("Player") ?? GameObject.Find("Player(Clone)");
        if (player == null)
        {
            Debug.LogWarning("[ExitDoorTrigger] No se encontró Player para mover.");
            yield break;
        }

        // Deshabilitar input mientras retrocede
        InputHandler.SetEnabled(false);

        // Intentar cerrar la puerta (si tiene método CloseDoor)
        if (doorScript != null)
        {
            var closeMethod = doorScript.GetType().GetMethod("CloseDoor");
            if (closeMethod != null) closeMethod.Invoke(doorScript, null);
        }

        // Preferencias de movimiento
        var cc = player.GetComponent<CharacterController>();
        var rb = player.GetComponent<Rigidbody>();

        float elapsed = 0f;
        Vector3 startPos = player.transform.position;

        while (elapsed < maxRetreatTime)
        {
            // Salir si la puerta ya está cerrada
            if (doorScript != null && !doorScript.IsOpen)
            {
                Debug.Log("[ExitDoorTrigger] Puerta cerrada -> detener movimiento.");
                break;
            }

            // Mover hacia target
            Vector3 currentPos = player.transform.position;
            Vector3 toTarget = targetPosition - currentPos;
            float dist = toTarget.magnitude;

            if (dist <= arrivalThreshold)
            {
                Debug.Log("[ExitDoorTrigger] Llegó al target.");
                break;
            }

            Vector3 dir = toTarget.normalized;
            Vector3 delta = dir * retreatSpeed * Time.deltaTime;

            // No sobrepasar el target
            if (delta.magnitude > dist) delta = dir * dist;

            if (cc != null)
            {
                cc.Move(delta);
            }
            else if (rb != null)
            {
                rb.MovePosition(player.transform.position + delta);
            }
            else
            {
                player.transform.position += delta;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        // Restaurar input
        InputHandler.SetEnabled(true);

        Debug.Log("[ExitDoorTrigger] Fin MovePlayerToTargetUntilDoorClosedCoroutine.");
    }
    void Update()
    {
        if (_instancedUI == null) return;
        if (doorScript == null) return;

        // muestra UI cuando el state sea InitGame y la puerta esté abierta
        if (GameFlowManager.Instance != null && GameFlowManager.Instance.getState() == GameFlowManager.FlowState.InitGame && doorScript.IsOpen)
        {
            _instancedUI.SetActive(true);
            InputHandler.SetEnabled(false);
        }
        else
        {
            // si querés que se oculte automáticamente cuando la puerta cierre, lo dejamos así:
            _instancedUI.SetActive(false);
        }
    }
}
