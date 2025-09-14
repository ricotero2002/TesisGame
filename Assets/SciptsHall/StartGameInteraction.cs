using System;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using EasyDoorSystem;

using Unity.Mathematics;
using TMPro;



#if TMP_PRESENT
using TMPro;
#endif
// Añadir al principio del script (si no está)
using System.Linq;

[RequireComponent(typeof(Collider))]
public class StartGameInteraction : MonoBehaviour
{
    [Header("Opciones UI (si no asignás nada crea dinámicamente)")]
    [Tooltip("Prefab opcional para el botón móvil (debe contener Button). Si es null se crea uno simple en tiempo de ejecución.")]
    public GameObject mobileButtonPrefab;

    [Tooltip("Texto que aparece flotando sobre el objeto cuando puedas interactuar.")]
    public string promptTextPc = "Press E to interact";
    [Tooltip("Texto que aparece flotando sobre el objeto cuando puedas interactuar.")]
    public string promptTextMobile = "Press Boton to interact";
    [Tooltip("Config ScriptableObject de Dynamic Floating Text para definir estilo.")]
    public DynamicTextData floatingTextConfig;

    private GameObject activeFloatingTextGO; // referencia al texto flotante creado (si el manager devuelve el GO)
    private Coroutine _hideCoroutine;
    private Transform _hall;
    [Header("Comportamiento")]
    [Tooltip("Si true, se desactiva el trigger después de usarlo (no se puede reabrir).")]

    private Outline outline;
    private bool isPlayerNearby = false;
    private bool interacted = false;


    // Instancias creadas
    private GameObject activePrompt;
    private Canvas rootCanvas;

    void Start()
    {
        // asegurarse de que el collider es trigger
        var col = GetComponent<Collider>();
        if (!col.isTrigger)
        {
            Debug.LogWarning($"{name}: Collider should be marked as 'isTrigger'. Setting it automatically.");
            col.isTrigger = true;
        }
        // 2) Buscar el Hall en la escena
        _hall = null;
        var hallGO = GameObject.Find("Hall");
        if (hallGO == null)
        {
            Debug.LogWarning("[RoomTestLauncher] No encontré un GameObject llamado 'Hall' en la escena. Coloca uno o ajusta el nombre.");
        }
        _hall = hallGO.transform;

        outline = GetComponent<Outline>();
        if (outline != null) outline.enabled = false;

        // buscar canvas ya existente
        rootCanvas = FindObjectOfType<Canvas>();
    }

    void OnTriggerEnter(Collider other)
    {
        if (interacted) return;
        if (!other.CompareTag("Player")) return;

        isPlayerNearby = true;
        if (outline != null) outline.enabled = true;

        ShowPrompt();
    }

    public void reiniciarInteract()
    {
        interacted = false;
    }

    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        isPlayerNearby = false;
        if (outline != null) outline.enabled = false;

        HidePrompt();
    }

    void Update()
    {
        if (!isPlayerNearby || interacted) return;

#if UNITY_ANDROID || UNITY_IOS
        // en móvil preferimos el botón UI (ya creado). No chequeamos teclas.
#else
        // PC: tecla E para interactuar
        if (Input.GetKeyDown(KeyCode.E))
        {
            DoInteract();
        }
#endif
    }

    private void ShowPrompt()
    {
        // Si ya hay uno activo, no hacemos nada
        if (activePrompt != null) return;

        bool isMobile = Application.isMobilePlatform || Application.platform == RuntimePlatform.Android || Application.platform == RuntimePlatform.IPhonePlayer;

        Vector3 spawnPos = transform.position + Vector3.up * 2.7f; // ajustar alto según prefieras
        string textToShow = isMobile ? promptTextMobile : promptTextPc;

        // 1) Botón móvil
        if (isMobile)
        {
            // Asegurar canvas
            if (rootCanvas == null)
            {
                CreateCanvas();
            }
            if (mobileButtonPrefab != null)
            {
                activePrompt = Instantiate(mobileButtonPrefab, rootCanvas.transform, false);
                var btn = activePrompt.GetComponentsInChildren<UnityEngine.UI.Button>().FirstOrDefault();
                if (btn != null) btn.onClick.AddListener(DoInteract);
                // si querés cambiar label dinámicamente:
                var lbl = activePrompt.GetComponentInChildren<UnityEngine.UI.Text>();
                if (lbl != null) lbl.text = "Interactuar";
#if TMP_PRESENT
            var ttmp = activePrompt.GetComponentInChildren<TMPro.TextMeshProUGUI>();
            if (ttmp != null) ttmp.text = "Interactuar";
#endif
            }
            else
            {
                activePrompt = CreateSimpleButton(rootCanvas.transform, "Interactuar", DoInteract);
            }
        }

        // 2) Texto flotante (usa tu DynamicText system)
        // Si el manager devuelve el GameObject (ver abajo la función opcional), guardalo.
        if (floatingTextConfig == null) floatingTextConfig = DynamicTextManager.defaultData;
        // Llamada simple (la versión que tenés devuelve void):

        // Si modificás DynamicTextManager.CreateText para que devuelva el GameObject,
        // podés descomentar la línea siguiente y guardar referencia:
        activeFloatingTextGO = DynamicTextManager.CreateText(spawnPos, textToShow, floatingTextConfig);
    }

    private GameObject CreateSimpleButton(Transform parent, string label, Action onClick)
    {
        GameObject btnGO = new GameObject("MobileInteractButton");
        btnGO.transform.SetParent(parent, false);

        RectTransform rt = btnGO.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.15f);
        rt.anchorMax = new Vector2(0.5f, 0.15f);
        rt.sizeDelta = new Vector2(200, 80);
        rt.anchoredPosition = Vector2.zero;

        var img = btnGO.AddComponent<UnityEngine.UI.Image>();
        img.color = new Color(0.1f, 0.6f, 0.9f, 0.95f);
        img.raycastTarget = true;

        var button = btnGO.AddComponent<UnityEngine.UI.Button>();
        button.targetGraphic = img;

        GameObject txtGO = new GameObject("Label");
        txtGO.transform.SetParent(btnGO.transform, false);

#if TMP_PRESENT
    var txt = txtGO.AddComponent<TMPro.TextMeshProUGUI>();
    txt.text = label;
    txt.alignment = TMPro.TextAlignmentOptions.Center;
    txt.fontSize = 28;
    txt.color = Color.black;
#else
        var txt = txtGO.AddComponent<UnityEngine.UI.Text>();
        txt.text = label;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        txt.fontSize = 20;
        txt.color = Color.black;
#endif

        RectTransform txtRT = txtGO.GetComponent<RectTransform>();
        txtRT.anchorMin = Vector2.zero;
        txtRT.anchorMax = Vector2.one;
        txtRT.offsetMin = Vector2.zero;
        txtRT.offsetMax = Vector2.zero;

        button.onClick.AddListener(() => onClick?.Invoke());
        return btnGO;
    }

    private void HidePrompt()
    {
        // Si ya estamos corriendo una animación de salida, no lanzamos otra
        if (_hideCoroutine != null) return;

        float exitDuration = 0.35f;

        if (activePrompt != null)
        {
            // Si es UI, animamos y destruimos
            _hideCoroutine = StartCoroutine(AnimateAndDestroyUI(activePrompt, exitDuration, () =>
            {
                activePrompt = null;
                _hideCoroutine = null;
            }));
        }

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
        if (activePrompt == null && activeFloatingTextGO == null)
        {
            _hideCoroutine = null;
        }
    }

    // Helper: espera y destruye (si usaste Animator)
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
    private void OnEnable()
    {
        GameManagerFin.OnRoomCreated += HandleRoomCreated;
    }

    private void OnDisable()
    {
        GameManagerFin.OnRoomCreated -= HandleRoomCreated;
    }
    // Utility: comprobar si Animator tiene trigger
    private bool HasTrigger(Animator anim, string triggerName)
    {
        // reflection a layers/cparams puede ser costoso; se hace de forma simple:
        foreach (var p in anim.parameters)
        {
            if (p.type == AnimatorControllerParameterType.Trigger && p.name == triggerName) return true;
        }
        return false;
    }

    private void DoInteract()
    {
        if (interacted) return;

        

        interacted = true;
        // Desactivar prompts/outline
        // En vez de HidePrompt() eliminamos el prompt actual y lo reemplazamos
        UpdatePromptToCreatingState();
        if (outline != null) outline.enabled = false;
        // Lógica de interacción: hace aparecer UI de inicio / iniciar flujo
        if (GameFlowManager.Instance != null)
        {
            GameFlowManager.Instance.EmpezarSesion();
        }
        else
        {
            Debug.LogWarning("GameManagerFin.Instance no encontrado. Asegurate de que GameManagerFin está en la escena y es singleton.");
        }

    }

    // ----- Helpers UI dinámicos (no requieren prefabs) -----

    private void CreateCanvas()
    {
        // Buscar canvas existente aunque esté inactivo
        rootCanvas = FindObjectOfType<Canvas>();
        if (rootCanvas != null) return;

        GameObject go = new GameObject("StartInteractionCanvas");
        rootCanvas = go.AddComponent<Canvas>();
        rootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        go.AddComponent<CanvasScaler>();
        go.AddComponent<GraphicRaycaster>();
        DontDestroyOnLoad(go);
    }
    private void HandleRoomCreated()
    {
        Debug.Log("[RoomTestLauncher] Sala creada → habilitando HallDoor.");

        var halldoor = _hall.Find("HallDoor");
        var scriptPuerta = halldoor.GetComponentsInChildren<EasyDoor>(true);
        if (scriptPuerta == null)
        {
            Debug.LogWarning("[RoomTestLauncher] No se encontró EasyDoor en HallDoor.");
        }
        scriptPuerta[0].setStateAllowAutoOpen(true);
        OnRoomCreatedHandler();


    }
    // Cambia el prompt (UI o floating) al texto de "creando la sala..."
    private void UpdatePromptToCreatingState()
    {
        string creatingText = "Se está creando la sala — esperar a que marco el de la derecha sea verde";

        // Si hay botón móvil (UI)
        if (activePrompt != null)
        {
            // intentar encontrar Button/Text y actualizar/desactivar
            var btn = activePrompt.GetComponentsInChildren<UnityEngine.UI.Button>(true).FirstOrDefault();
            if (btn != null) btn.interactable = false;

#if TMP_PRESENT
        var tmp = activePrompt.GetComponentInChildren<TMPro.TextMeshProUGUI>();
        if (tmp != null) tmp.text = "Creando sala...";
        else
#endif
            {
                var t = activePrompt.GetComponentInChildren<UnityEngine.UI.Text>();
                if (t != null) t.text = "Creando sala...";
            }
        }

        // Si hay floating text (DynamicTextManager), actualizar su texto
        if (activeFloatingTextGO != null)
        {
#if TMP_PRESENT
        var tmp = activeFloatingTextGO.GetComponentInChildren<TMPro.TextMeshPro>();
        if (tmp != null) tmp.text = creatingText;
        else
        {
            var tmpUI = activeFloatingTextGO.GetComponentInChildren<TMPro.TextMeshProUGUI>();
            if (tmpUI != null) tmpUI.text = creatingText;
        }
#else
            var tm = activeFloatingTextGO.GetComponentInChildren<TextMeshProUGUI>();
            if (tm != null)
            {
                Debug.Log($"[StartGameInteraction] Actualizando texto flotante de {tm} a: {creatingText}");
#endif
                tm.text = creatingText;
            }
            
        }
        else
        {
            // si no hay floating creado, crearlo ahora (y guardar referencia si la CreateText devuelve GO)
            Vector3 spawnPos = transform.position + Vector3.up * 2.7f;
            if (floatingTextConfig == null) floatingTextConfig = DynamicTextManager.defaultData;
            activeFloatingTextGO = DynamicTextManager.CreateText(spawnPos, creatingText, floatingTextConfig);
        }
    }

    // Handler llamado cuando GameManagerFin reporta que la room se creó.
    private void OnRoomCreatedHandler()
    {
        // cambiar el texto al indicar que la sala está lista
        string readyText = "Sala creada — dirigite al marco verde a la derecha";

        if (activeFloatingTextGO != null)
        {
#if TMP_PRESENT
        var tmp = activeFloatingTextGO.GetComponentInChildren<TMPro.TextMeshPro>();
        if (tmp != null) tmp.text = readyText;
        else
        {
            var tmpUGUI = activeFloatingTextGO.GetComponentInChildren<TMPro.TextMeshProUGUI>();
            if (tmpUGUI != null) tmpUGUI.text = readyText;
        }
#else
            var tm = activeFloatingTextGO.GetComponentInChildren<TextMeshProUGUI>();
            if (tm != null) tm.text = readyText;
#endif
        }

        if (activePrompt != null)
        {
#if TMP_PRESENT
        var ttmp = activePrompt.GetComponentInChildren<TMPro.TextMeshProUGUI>();
        if (ttmp != null) ttmp.text = "Sala creada";
        else
#endif
            {
                var t = activePrompt.GetComponentInChildren<UnityEngine.UI.Text>();
                if (t != null) t.text = "Sala creada";
            }
        }
    }



}
