// PrefabPlacer.cs
using System;
using System.Collections.Generic;
using UnityEngine;

public static class PrefabPlacer
{
    /// <summary>
    /// Instancia un prefab y lo coloca correctamente dentro de 'parent' en la posición local 'localPos'.
    /// - deshabilita temporalmente MonoBehaviours/Animator/Rigidbody para evitar que el prefab se mueva en Awake/Start.
    /// - preserva el tamaño WORLD del prefab neutralizando la escala local respecto al parent.
    /// - alinea su punto más bajo (renderer.bounds.min.y) con la superficie del parent.
    /// Retorna el Transform instanciado (activo).
    /// </summary>
    public static Transform InstantiateAndPlace(GameObject prefab, Transform parent, Vector3 localPos, bool cyclicScaleNeutralize = true, bool reenableMonoBehaviours = true)
    {
        if (prefab == null) return null;

        // 1) Instanciar
        GameObject go = GameObject.Instantiate(prefab);

        // Registrar y desactivar MBs para evitar movimientos en Awake/OnEnable (más seguro)
        var allMBs = go.GetComponentsInChildren<MonoBehaviour>(true);
        var mbStates = new List<(MonoBehaviour mb, bool wasEnabled)>();
        foreach (var mb in allMBs)
        {
            if (mb == null) continue;
            mbStates.Add((mb, mb.enabled));
            try { mb.enabled = false; } catch { }
        }

        var allAnimators = go.GetComponentsInChildren<Animator>(true);
        foreach (var a in allAnimators) a.enabled = false;
        var allRbs = go.GetComponentsInChildren<Rigidbody>(true);
        var rbStates = new List<(Rigidbody rb, bool wasKinematic)>();
        foreach (var r in allRbs) { rbStates.Add((r, r.isKinematic)); r.isKinematic = true; }

        // 2) Medir escala mundial original (antes de parentear)
        Vector3 originalWorldScale = go.transform.lossyScale;

        // 3) Parentear con worldPositionStays = false (para controlar local transform)
        go.transform.SetParent(parent, false);

        // 4) Neutralizar escala local para preservar tamaño world (si pediste)
        if (cyclicScaleNeutralize)
        {
            Vector3 pLossy = parent.lossyScale;
            float sx = Math.Max(1e-6f, Mathf.Abs(pLossy.x));
            float sy = Math.Max(1e-6f, Mathf.Abs(pLossy.y));
            float sz = Math.Max(1e-6f, Mathf.Abs(pLossy.z));
            Vector3 desiredLocalScale = new Vector3(originalWorldScale.x / sx, originalWorldScale.y / sy, originalWorldScale.z / sz);
            // preservar signos coherentes con parent
            desiredLocalScale = new Vector3(Mathf.Sign(pLossy.x) * Mathf.Abs(desiredLocalScale.x),
                                            Mathf.Sign(pLossy.y) * Mathf.Abs(desiredLocalScale.y),
                                            Mathf.Sign(pLossy.z) * Mathf.Abs(desiredLocalScale.z));
            go.transform.localScale = desiredLocalScale;
        }

        // 5) Recalcular bounds y alinear en Y
        Bounds bounds;
        bool hasR = TryGetRenderersBounds(go, out bounds);
        float worldMinY = hasR ? bounds.min.y : go.transform.position.y;
        float tileSurfaceWorldY = parent.TransformPoint(Vector3.zero).y;
        float yOffset = (tileSurfaceWorldY) - worldMinY;

        // 6) Aplicar posición local con offset Y
        go.transform.localPosition = localPos + Vector3.up * yOffset;
        go.transform.localRotation = Quaternion.identity;

        // 7) Reactivar componentes
        if (reenableMonoBehaviours)
        {
            foreach (var a in allAnimators) a.enabled = true;
            foreach (var pair in rbStates) { try { pair.rb.isKinematic = pair.wasKinematic; } catch { } }
            foreach (var pair in mbStates) { try { pair.mb.enabled = pair.wasEnabled; } catch { } }
        }

        Debug.Log($"[PrefabPlacer] Placed '{prefab.name}' at parent '{parent.name}' localPos={go.transform.localPosition} lossyScale={go.transform.lossyScale}");
        return go.transform;
    }

    private static bool TryGetRenderersBounds(GameObject go, out Bounds bounds)
    {
        var rends = go.GetComponentsInChildren<Renderer>(true);
        if (rends == null || rends.Length == 0)
        {
            bounds = default;
            return false;
        }
        bounds = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) bounds.Encapsulate(rends[i].bounds);
        return true;
    }
}
