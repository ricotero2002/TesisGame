using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
#if TMP_PRESENT
using TMPro;
#endif

[RequireComponent(typeof(Collider))]
public class WaitingStationInteraction : MonoBehaviour
{
    [Header("Visuales / Floating Text")]
    [Tooltip("Config de DynamicText (opcional). Si es null se usa DynamicTextManager.defaultData")]
    public DynamicTextData floatingTextConfig;

    [Tooltip("Nombre exacto del child que representa la figura con la que se debe clickear.")]
    public string clickableChildName = "Cube";


    [Header("Comportamiento")]
    [Tooltip("Si true, solo se podr� usar una vez")]
    public bool singleUse = true;

    // runtime
    GameObject _activeFloatingGO;
    GameObject _clickableChild;
    Outline _outlineChild;
    Collider _clickableCollider;
    bool _playerNearby = false;
    bool _used = false;
    Canvas _rootCanvas;

    void Start()
    {
        // Asegurar trigger en el collider del prefab
        var col = GetComponent<Collider>();
        if (col != null && !col.isTrigger)
        {
            Debug.LogWarning($"{name}: el collider principal debe ser isTrigger. Lo activo autom�ticamente.");
            col.isTrigger = true;
        }

        if (floatingTextConfig == null)
            floatingTextConfig = DynamicTextManager.defaultData;

        _rootCanvas = FindObjectOfType<Canvas>();

        // Buscar hijo clickable
        _clickableChild = FindChildByName(transform, clickableChildName);
        if (_clickableChild == null)
        {
            Debug.LogWarning($"[{name}] No encontr� child '{clickableChildName}'. La estaci�n no podr� activarse por click.");
        }
        else
        {
            // Asegurar collider en el child
            _clickableCollider = _clickableChild.GetComponent<Collider>();
            if (_clickableCollider == null)
            {
                // intentar a�adir MeshCollider si hay mesh, si no BoxCollider
                var mf = _clickableChild.GetComponentInChildren<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    var mc = _clickableChild.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    // MeshCollider necesita convex para OnMouseDown en algunas plataformas / f�sicas
                    mc.convex = true;
                    _clickableCollider = mc;
                }
                else
                {
                    _clickableCollider = _clickableChild.AddComponent<BoxCollider>();
                }
                Debug.Log($"[{name}] A�ad� collider a '{clickableChildName}'.");
            }

            // A�adir manejador de click simple (OnMouseDown) que invoca nuestro m�todo
            var handler = _clickableChild.GetComponent<WaitingStationChildClickHandler>();
            if (handler == null)
                handler = _clickableChild.AddComponent<WaitingStationChildClickHandler>();
            handler.onClicked = new UnityEvent();
            handler.onClicked.AddListener(OnPlayerClickedFigure);
        }

        _outlineChild = _clickableChild.GetComponent<Outline>();
    }

    // Trigger de proximidad
    void OnTriggerEnter(Collider other)
    {
        if (_used) return;
        if (!other.CompareTag("Player")) return;

        _playerNearby = true;

        // activar outline (si est� presente)
        if (_outlineChild != null) _outlineChild.enabled = true;

        ShowFloatingPrompt();
    }

    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        _playerNearby = false;

        // desactivar outline
        if (_outlineChild != null) _outlineChild.enabled = false;

        HideFloatingPrompt();
    }

    void Update()
    {
        if (_used) return;

#if UNITY_ANDROID || UNITY_IOS
        // touch: si toc�s la pantalla hacemos raycast y si toc� el child, activamos
        if (Input.touchCount > 0 && _playerNearby && _clickableCollider != null)
        {
            var t = Input.GetTouch(0);
            if (t.phase == TouchPhase.Began)
            {
                Ray ray = Camera.main.ScreenPointToRay(t.position);
                if (Physics.Raycast(ray, out RaycastHit hit, 100f))
                {
                    if (hit.collider == _clickableCollider || hit.collider.transform.IsChildOf(_clickableChild.transform))
                    {
                        OnPlayerClickedFigure();
                    }
                }
            }
        }
#else
        // En PC no usamos E: el usuario debe clickear la figura (OnMouseDown manejado por handler).
        // Aun as� soportamos click por raycast si por alguna raz�n OnMouseDown no funca:
        if (Input.GetMouseButtonDown(0) && _playerNearby && _clickableCollider != null)
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 100f))
            {
                if (hit.collider == _clickableCollider || hit.collider.transform.IsChildOf(_clickableChild.transform))
                {
                    OnPlayerClickedFigure();
                }
            }
        }
#endif
    }

    void ShowFloatingPrompt()
    {
        if (_activeFloatingGO != null) return;

        string text = Application.isMobilePlatform ? "Toc� la figura para continuar" : "Click en la figura para continuar";
        Vector3 spawnPos = GetTopPositionAbove(transform) + Vector3.up * 0.15f;

        if (DynamicTextManager.canvasPrefab != null || DynamicTextManager.defaultData != null)
        {
            _activeFloatingGO = DynamicTextManager.CreateText(spawnPos, text, floatingTextConfig);
            if (_activeFloatingGO != null) _activeFloatingGO.transform.SetParent(transform, true);
        }
        else
        {
            // Fallback simple
            _activeFloatingGO = CreateFallbackText(spawnPos, text);
            _activeFloatingGO.transform.SetParent(transform, true);
        }
    }

    void HideFloatingPrompt()
    {
        if (_activeFloatingGO != null)
        {
            Destroy(_activeFloatingGO);
            _activeFloatingGO = null;
        }
    }

    void OnPlayerClickedFigure()
    {
        // Protecci�n b�sica
        if (_used) return;
        if (!_playerNearby) return;

        _used = true;

        // 1) Apagar outline si existe
        if (_outlineChild != null)
        {
            _outlineChild.enabled = false;
        }

        // 2) Borrar el floating text (si existe) inmediatamente
        if (_activeFloatingGO != null)
        {
            Destroy(_activeFloatingGO);
            _activeFloatingGO = null;
        }

        // 3) Deshabilitar la interacci�n: collider del hijo clickable y collider del root (trigger)
        if (_clickableCollider != null)
        {
            _clickableCollider.enabled = false;
        }

        var rootCol = GetComponent<Collider>();
        if (rootCol != null)
        {
            rootCol.enabled = false;
        }

        // 4) Llamar a GameManagerFin (solo la llamada simple que pediste)
        if (GameManagerFin.Instance != null)
        {
            try
            {
                GameManagerFin.Instance.TriggerWaitingStation();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WaitingStationInteraction] Error llamando TriggerWaitingStation(): {ex.Message}");
            }
        }
        else
        {
            Debug.LogWarning("[WaitingStationInteraction] GameManagerFin.Instance es null. No se pudo iniciar la estaci�n de espera.");
        }

        // 5) Opcional: desactivar este componente para que no siga procesando Update/Triggers
        this.enabled = false;
    }


    // Helpers ----------------------------

    GameObject CreateFallbackText(Vector3 pos, string text)
    {
        GameObject go = new GameObject("WS_FallbackFloatingText");
        go.transform.position = pos;
#if TMP_PRESENT
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = text;
        tmp.fontSize = 2f;
        tmp.color = Color.black;
        tmp.alignment = TMPro.TextAlignmentOptions.Center;
#else
        var tm = go.AddComponent<TextMesh>();
        tm.text = text;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.characterSize = 0.1f;
        tm.fontSize = 60;
        tm.color = Color.black;
#endif
        return go;
    }

    Vector3 GetTopPositionAbove(Transform t)
    {
        var rs = GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return t.position;
        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return new Vector3(b.center.x, b.max.y, b.center.z);
    }


    // Recurse para buscar child por nombre (case-sensitive)
    static GameObject FindChildByName(Transform parent, string name)
    {
        if (parent == null) return null;
        foreach (Transform c in parent)
        {
            if (c.name == name) return c.gameObject;
            var r = FindChildByName(c, name);
            if (r != null) return r;
        }
        return null;
    }
}

/// <summary>
/// Simple helper que se a�ade al child clickable para invocar UnityEvent onClicked cuando se hace OnMouseDown.
/// Usa OnMouseDown porque es la soluci�n 3D m�s directa; adem�s el WaitingStationInteraction a�adido tambi�n hace raycast en touch/mouse.
/// </summary>
public class WaitingStationChildClickHandler : MonoBehaviour
{
    [NonSerialized] public UnityEvent onClicked;

    void OnMouseDown()
    {
        if (onClicked != null) onClicked.Invoke();
    }
}
