// Assets/Editor/ModelImportPostprocessorEnhanced.cs
// Versión robusta: cola assets para procesar después de la importación para evitar fallos de SaveAsPrefabAsset durante el import pipeline.

using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor.Animations;
using System;
using System.Net.NetworkInformation;
using static TMPro.SpriteAssetUtilities.TexturePacker_JsonArray;

/// <summary>
/// Postprocessor mejorado (robusto):
/// - OnPostprocessModel: sólo encola el assetPath para procesarlo luego.
/// - ProcessImportedModel: (via delayCall) carga el modelo ya importado y:
///     * normaliza escala según metadata / heurísticas
///     * aplica toy-scaling según pool promedio
///     * crea prefab en Assets/Prefabs
///     * asegura BoxCollider que encierra Renderer.bounds
///     * crea/actualiza ModelMetadata asset
///     * añade el prefab a ArtPoolSO correspondiente
/// </summary>
public class ModelImportPostprocessorEnhanced : AssetPostprocessor
{
    // ---------- Config / helper types ----------
    [System.Serializable]
    public class UploadMetadata
    {
        public string category;
        public float referenceHeightMeters = -1f;
        public string[] tags;
    }

    static ImportSettings _settings;
    static ImportSettings Settings
    {
        get
        {
            if (_settings == null)
            {
                var guids = AssetDatabase.FindAssets("t:ImportSettings");
                if (guids != null && guids.Length > 0)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                    _settings = AssetDatabase.LoadAssetAtPath<ImportSettings>(path);
                }
                else
                {
                    _settings = ScriptableObject.CreateInstance<ImportSettings>();
                }
            }
            return _settings;
        }
    }

    // Cola de assets a procesar (evita procesar varias veces el mismo)
    static HashSet<string> queued = new HashSet<string>();

    // ---------- OnPreprocessModel / OnPostprocessModel simples: sólo encolan ----------
    void OnPreprocessModel()
    {
        // Configs previas de import si las necesitás (globalScale, etc.)
        ModelImporter importer = (ModelImporter)assetImporter;
        importer.globalScale = 1.0f;
    }

    void OnPostprocessModel(GameObject root)
    {
        if (!assetPath.StartsWith("Assets/Incoming/"))
        {
            // Ignorar modelos que no vienen de Incoming
            return;
        }

        // Encolar para procesar después (evita SaveAsPrefabAsset durante la importación)
        if (string.IsNullOrEmpty(assetPath)) return;

        if (!queued.Contains(assetPath))
        {
            queued.Add(assetPath);
            // Ejecutar después de que Unity termine lo que esté procesando.
            EditorApplication.delayCall += () => {
                // Capturamos el valor local para evitar closure problem
                string p = assetPath;
                // Procesar solo si todavía existe en el proyecto
                ProcessImportedModel(p);
                queued.Remove(p);
            };
        }
    }

    // ---------- Procesamiento diferido ----------
    private static void ProcessImportedModel(string assetPath)
    {
        try
        {
            if (!File.Exists(assetPath))
            {
                Debug.LogWarning($"ModelImportDeferred: assetPath no existe (ya eliminado?): {assetPath}");
                return;
            }

            // Cargar el GameObject importado (modelo)
            GameObject importedModel = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (importedModel == null)
            {
                Debug.LogWarning($"ModelImportDeferred: No se pudo LoadAssetAtPath<GameObject> para {assetPath}");
                return;
            }

            Debug.Log($"ModelImportDeferred: Procesando asset importado: {assetPath}");

            // Instanciar copia limpia para modificar sin afectar el asset original
            GameObject instance = GameObject.Instantiate(importedModel);
            instance.name = Path.GetFileNameWithoutExtension(assetPath);
            ClearHideFlagsRecursive(instance);

            // quitar cámaras/luces de la instancia antes de calcular bounds y escalar
            StripCamerasAndLightsFromInstance(instance, Settings.removeCameras, Settings.removeLights);
            
            // Calcular bounds inicial
            Bounds b = CalculateCombinedBounds(instance);
            if (b.size == Vector3.zero)
            {
                Debug.LogWarning($"ModelImportDeferred: Bounds vacíos para {instance.name}, abortando.");
                GameObject.DestroyImmediate(instance);
                return;
            }

            // Cargar metadata JSON si existe
            UploadMetadata metadata = TryLoadMetadata(assetPath);



            // Determinar categoría y referencia
            string category = metadata != null && !string.IsNullOrEmpty(metadata.category) ? metadata.category : InferCategoryFromNameOrShape(instance);

            

            float refHeight = DetermineReferenceHeight(instance, category, metadata);
            string[] tags = (metadata != null && metadata.tags != null) ? metadata.tags : new string[0];

            // Escalar por referencia (si existe)
            if (refHeight > 0.001f && b.size.y > 0.001f)
            {
                float scaleToRef = refHeight / b.size.y;
                instance.transform.localScale = Vector3.one * scaleToRef;
            }

            // Recalcular bounds tras escala
            Bounds boundsAfter = CalculateCombinedBounds(instance);

 

            // Preparar carpeta y ruta de prefab
            string prefabsDir = "Assets/Prefabs";
            if (!AssetDatabase.IsValidFolder(prefabsDir)) AssetDatabase.CreateFolder("Assets", "Prefabs");
            string prefabPath = $"{prefabsDir}/{SanitizeName(instance.name)}.prefab";

            // Intentar guardar prefab desde la copia ya que ahora la importación finalizó
            GameObject createdPrefab = null;
            try
            {
                createdPrefab = PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"ModelImportDeferred: SaveAsPrefabAsset lanzó excepción para {instance.name}: {ex.Message}");
                createdPrefab = null;
            }

            // Si no se creó, intentar limpiar aún más y volver a intentar
            if (createdPrefab == null)
            {
                // Forzar limpiar cualquier componente con hideFlags y reintentar
                ClearHideFlagsRecursive(instance);
                try
                {
                    createdPrefab = PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
                }
                catch (System.Exception ex2)
                {
                    Debug.LogWarning($"ModelImportDeferred: Segundo intento SaveAsPrefabAsset falló para {instance.name}: {ex2.Message}");
                    createdPrefab = null;
                }
            }

            // Destruir la instancia temporal en todo caso (ya no la necesitamos)
            GameObject.DestroyImmediate(instance);

            if (createdPrefab == null)
            {
                Debug.LogError($"ModelImportDeferred: No se pudo crear prefab para {assetPath}. Abort processing.");
                return;
            }

            // ⚡ Nuevo paso: configurar animaciones si el FBX trae clips
            SetupAnimatorForPrefab(prefabPath, assetPath);
            bool normalized = AutoNormalizePrefabScaleToHeight(prefabPath, refHeight, 0.01f);
            if (normalized)
            {
                Debug.Log($"ModelImportDeferred: prefab {prefabPath} auto-normalizado a altura {refHeight}m.");
                // Podemos actualizar info posterior como colliders/metadata basados en nueva escala
            }
            // --- después de que createdPrefab != null y antes del collider ---
            string prefabBase = Path.GetFileNameWithoutExtension(prefabPath);
            string metaPath = $"Assets/Metadata/{prefabBase}Meta.asset";
            ModelMetadata existingMeta = AssetDatabase.LoadAssetAtPath<ModelMetadata>(metaPath);
            string objectId = null;
            if (existingMeta != null && !string.IsNullOrEmpty(existingMeta.objectId))
            {
                objectId = existingMeta.objectId;
            }
            else
            {
                objectId = GenerateObjectId(category, assetPath, prefabBase);
            }
            UpdateModelMetadataWithObjectId(prefabPath, assetPath, category, objectId);
            AddOrUpdateModelTagOnPrefab(prefabPath, objectId);
            // quitar cámaras/luces de la instancia antes de calcular bounds y escalar

            
            // Añadir/actualizar BoxCollider en el prefab (usa LoadPrefabContents)
            AddOrUpdateBoxColliderOnPrefab(prefabPath);

            // Recalcular bounds sobre el prefab final (instanciando rápidamente)
            Bounds finalBounds = GetPrefabCombinedBounds(prefabPath);

            //prueba para ver el tamaño
     

            //--------------------------------------------------------

            // Crear/actualizar ModelMetadata
            CreateOrUpdateModelMetadataForPrefab(prefabPath, finalBounds, metadata != null ? metadata.referenceHeightMeters : -1f, category, tags);

            // Añadir a pool (crear pool si es necesario)
            ArtPoolSO thePool = LoadPoolAssetForCategory(category);
            if (thePool == null)
            {
                thePool = CreatePoolAssetForCategory(category);
            }

            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefabAsset != null)
            {
                // parámetros desde Settings (asegurate que Settings.featureScales tenga 5 entries y Settings.subpoolAssignThreshold existe)
                float[] featureScales = Settings.featureScales; // ahora length >= 5
                float threshold = Settings.subpoolAssignThreshold;

                var poolWithSub = thePool as ArtPoolSO;
                if (poolWithSub != null)
                {
                    FeatureVector5 fv = ComputePhysicalFeatureVector5(prefabAsset);
                    
                    var sp = poolWithSub.AddPrefabToSubpool(prefabAsset, ComputePhysicalFeatureVector5, featureScales, threshold);
                    // AÑADIR llamada para actualizar snapshot JSON:
                    UpdatePoolJsonForPrefab(category, objectId, prefabAsset);
                    //Debug.Log($"ModelImportDeferred: Assigned prefab {prefabAsset.name} to subpool {sp.subpoolName} (pool {poolWithSub.categoryName})");
                }
            }
            else
            {
                Debug.LogWarning($"ModelImportDeferred: No se pudo cargar prefab después de crearlo: {prefabPath}");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            //ModelImportPostprocessorEnhanced.PrintImportDebugForAsset(prefabPath);
            Debug.Log($"ModelImportDeferred: Procesado exitoso: {assetPath} -> {prefabPath} (category: {category})");
        }
        catch (System.Exception exOuter)
        {
            Debug.LogError($"ModelImportDeferred: Exception processing asset {assetPath}: {exOuter}");
        }
    }

    // ---------- Helpers (copiados/adaptados del script original) ----------
    private static void ClearHideFlagsRecursive(GameObject go)
    {
        if (go == null) return;
        go.hideFlags = HideFlags.None;
        foreach (var comp in go.GetComponents<Component>())
        {
            if (comp == null) continue;
            comp.hideFlags = HideFlags.None;
        }
        for (int i = 0; i < go.transform.childCount; i++)
            ClearHideFlagsRecursive(go.transform.GetChild(i).gameObject);
    }

    private static string SanitizeName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }

    private static Bounds CalculateCombinedBounds(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0) return new Bounds(Vector3.zero, Vector3.zero);
        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        return b;
    }

    private static UploadMetadata TryLoadMetadata(string assetPath)
    {
        string jsonPath1 = assetPath + Settings.metadataJsonSuffix;
        if (File.Exists(jsonPath1))
        {
            var txt = File.ReadAllText(jsonPath1);
            return JsonUtility.FromJson<UploadMetadata>(txt);
        }
        string dir = Path.GetDirectoryName(assetPath);
        string baseName = Path.GetFileNameWithoutExtension(assetPath);
        string altPath = Path.Combine(dir, baseName + ".json");
        if (File.Exists(altPath))
        {
            var txt = File.ReadAllText(altPath);
            return JsonUtility.FromJson<UploadMetadata>(txt);
        }
        return null;
    }

    private static string InferCategoryFromNameOrShape(GameObject root)
    {
        string name = root.name.ToLower();
        if (name.Contains("statue") || name.Contains("person") || name.Contains("human")) return "Estatuas";
        if (name.Contains("car") || name.Contains("auto") || name.Contains("vehicle")) return "Vehicle";
        if (name.Contains("dog") || name.Contains("cat") || name.Contains("animal")) return "Animal";
        if (name.Contains("guitar") || name.Contains("chair") || name.Contains("sofa")) return "Prop";
        Bounds b = CalculateCombinedBounds(root);
        if (b.size.y > b.size.x * 1.2f && b.size.y > b.size.z * 1.2f) return "HumanoidLike";
        if (b.size.x > b.size.z * 1.4f || b.size.z > b.size.x * 1.4f) return "VehicleLike";
        return "Misc";
    }

    private static float DetermineReferenceHeight(GameObject root, string category, UploadMetadata metadata)
    {
        if (metadata != null && metadata.referenceHeightMeters > 0f) return metadata.referenceHeightMeters;
        if (category.ToLower().Contains("human") || category.ToLower().Contains("statue") || category.ToLower().Contains("humanoidlike"))
            return Settings.defaultHumanHeight;
        if (category.ToLower().Contains("vehicle") || category.ToLower().Contains("vehiclelike"))
            return Settings.defaultVehicleHeight;
        Bounds b = CalculateCombinedBounds(root);
        float dominant = Mathf.Max(b.size.x, b.size.y, b.size.z);
        if (dominant > 0f) return Mathf.Clamp(dominant, 0.2f, 3.0f);
        return Settings.defaultOtherReference;
    }

    private static ArtPoolSO LoadPoolAssetForCategory(string category)
    {
        string poolPath = $"Assets/Pools/{category}Pool.asset";
        return AssetDatabase.LoadAssetAtPath<ArtPoolSO>(poolPath);
    }

    private static ArtPoolSO CreatePoolAssetForCategory(string category)
    {
        var pool = ScriptableObject.CreateInstance<ArtPoolSO>();
        pool.categoryName = category;
        if (!AssetDatabase.IsValidFolder("Assets/Pools")) AssetDatabase.CreateFolder("Assets", "Pools");
        string poolPath = $"Assets/Pools/{category}Pool.asset";
        AssetDatabase.CreateAsset(pool, poolPath);
        AssetDatabase.SaveAssets();
        return pool;
    }


    private static int CountTriangles(GameObject go)
    {
        int tris = 0;
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            if (mf.sharedMesh != null) tris += mf.sharedMesh.triangles.Length / 3;
        return tris;
    }

    // Reemplaza o añade esta función en tu ModelImportPostprocessorEnhanced.cs
    // Conserva este nombre si hay otros sitios que llaman a AddOrUpdateBoxColliderOnPrefab
    private static void AddOrUpdateBoxColliderOnPrefab(string prefabPath)
    {
        AddOrUpdateBoxCollidersPerRenderer(prefabPath);
    }

    private static void AddOrUpdateBoxCollidersPerRenderer(string prefabPath)
    {
        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        if (root == null) return;

        try
        {
            // Si querés mantener un collider global en root, comentá/quitá la eliminación.
            BoxCollider rootCol = root.GetComponent<BoxCollider>();
            if (rootCol != null) UnityEngine.Object.DestroyImmediate(rootCol, true);

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (var rend in renderers)
            {
                if (rend == null) continue;
                GameObject go = rend.gameObject;

                // Obtener o crear BoxCollider en el mismo GameObject del renderer
                BoxCollider col = go.GetComponent<BoxCollider>();
                if (col == null) col = go.AddComponent<BoxCollider>();

                // Intentar usar MeshFilter.sharedMesh (para MeshRenderer) o SkinnedMeshRenderer.localBounds
                MeshFilter mf = go.GetComponent<MeshFilter>();
                SkinnedMeshRenderer smr = go.GetComponent<SkinnedMeshRenderer>();

                if (mf != null && mf.sharedMesh != null)
                {
                    Bounds mb = mf.sharedMesh.bounds; // local del mesh (coincide con transform del GO)
                    col.center = mb.center;
                    col.size = mb.size;
                }
                else if (smr != null && smr.sharedMesh != null)
                {
                    // Para skinned meshes usamos localBounds (es local al SkinnedMeshRenderer)
                    Bounds lb = smr.localBounds;
                    col.center = lb.center;
                    col.size = lb.size;
                }
                else
                {
                    // Fallback: usar renderer.bounds (world AABB) pero crear la caja en el transform del renderer
                    Bounds wb = rend.bounds;
                    Vector3 c = wb.center;
                    Vector3 e = wb.extents;
                    Vector3 localMin = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
                    Vector3 localMax = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

                    for (int xi = -1; xi <= 1; xi += 2)
                        for (int yi = -1; yi <= 1; yi += 2)
                            for (int zi = -1; zi <= 1; zi += 2)
                            {
                                Vector3 cornerWorld = c + Vector3.Scale(e, new Vector3(xi, yi, zi));
                                Vector3 cornerLocal = go.transform.InverseTransformPoint(cornerWorld);
                                localMin = Vector3.Min(localMin, cornerLocal);
                                localMax = Vector3.Max(localMax, cornerLocal);
                            }

                    if (!float.IsInfinity(localMin.x))
                    {
                        col.center = (localMin + localMax) * 0.5f;
                        col.size = (localMax - localMin);
                    }
                    else
                    {
                        // último recurso: caja pequeña centrada
                        col.center = Vector3.zero;
                        col.size = Vector3.one * 0.1f;
                    }
                }

                // Opcional: si querés que los colliders no hereden escala extraña, podrías crear un child "Collider" con identity transform
                // y mover center/size correspondientemente. Lo dejé simple para no modificar jerarquía.
            }

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }



    private static Bounds GetPrefabCombinedBounds(string prefabPath)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null) return new Bounds(Vector3.zero, Vector3.zero);
        var instance = GameObject.Instantiate(prefab);
        instance.hideFlags = HideFlags.HideAndDontSave;
        var renderers = instance.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
        {
            GameObject.DestroyImmediate(instance);
            return new Bounds(Vector3.zero, Vector3.zero);
        }
        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        GameObject.DestroyImmediate(instance);
        return b;
    }
    // ---------- Helpers para quitar Cameras/Lights ----------

    // Quita componentes Camera/Light en todo el árbol (instancia en memoria).
    // ---------- Helpers para quitar Cameras/Lights (robusto) ----------

    // Busca un Type por nombre en todos los assemblies (evita dependencias directas a paquetes)
    private static Type FindTypeByName(string fullTypeName)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var t = asm.GetType(fullTypeName);
                if (t != null) return t;
            }
            catch { /* ignore */ }
        }
        return null;
    }

    // Intenta eliminar componentes por tipoName del GameObject (si no puede, intenta deshabilitar)
    private static void RemoveDependentComponentsByNames(GameObject go, params string[] typeNames)
    {
        if (go == null) return;
        foreach (var tn in typeNames)
        {
            var t = FindTypeByName(tn);
            if (t == null) continue;
            Component[] comps = go.GetComponents(t);
            foreach (var c in comps)
            {
                if (c == null) continue;
                try
                {
                    UnityEngine.Object.DestroyImmediate(c);
                    Debug.Log($"Removed dependent component {t.FullName} from {go.name}");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Could not DestroyImmediate {t.FullName} on {go.name}: {ex.Message}. Trying to disable instead.");
                    // fallback: try disable if it's a Behaviour
                    var behaviour = c as Behaviour;
                    if (behaviour != null)
                    {
                        try { behaviour.enabled = false; }
                        catch { /* ignore */ }
                    }
                }
            }
        }
    }

    // Quita componentes Camera/Light en todo el árbol (instancia en memoria) - versión robusta
    private static void StripCamerasAndLightsFromInstance(GameObject root, bool removeCameras = true, bool removeLights = true)
    {
        if (root == null) return;

        // tipos dependientes comunes (nombres con namespace completo)
        string[] cameraDependentTypes = new string[] {
        "UnityEngine.Rendering.Universal.UniversalAdditionalCameraData",
        "UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData",
        "Cinemachine.CinemachineBrain",
        // añadir otros si los detectás
    };
        string[] lightDependentTypes = new string[] {
        "UnityEngine.Rendering.Universal.UniversalAdditionalLightData",
        "UnityEngine.Rendering.HighDefinition.HDAdditionalLightData",
        // añadir otros si los detectás
    };

        int removedCount = 0;

        if (removeCameras)
        {
            var cams = root.GetComponentsInChildren<Camera>(true);
            foreach (var cam in cams)
            {
                if (cam == null) continue;
                // 1) remover componentes dependientes en el mismo GameObject primero
                RemoveDependentComponentsByNames(cam.gameObject, cameraDependentTypes);
                // 2) intentar destruir Camera
                try
                {
                    UnityEngine.Object.DestroyImmediate(cam);
                    removedCount++;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Can't remove Camera on {cam.gameObject.name}: {ex.Message}. Trying to disable the component.");
                    try
                    {
                        cam.enabled = false;
                    }
                    catch { }
                }
            }
        }

        if (removeLights)
        {
            var lights = root.GetComponentsInChildren<Light>(true);
            foreach (var lt in lights)
            {
                if (lt == null) continue;
                // 1) remover componentes dependientes en el mismo GameObject primero
                RemoveDependentComponentsByNames(lt.gameObject, lightDependentTypes);
                // 2) intentar destruir Light
                try
                {
                    UnityEngine.Object.DestroyImmediate(lt);
                    removedCount++;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Can't remove Light on {lt.gameObject.name}: {ex.Message}. Trying to disable the component.");
                    try
                    {
                        lt.enabled = false;
                    }
                    catch { }
                }
            }
        }

        if (removedCount > 0)
            Debug.Log($"StripCamerasAndLightsFromInstance: removed/disabled {removedCount} Camera/Light components from '{root.name}'.");
    }

    // Quita componentes Camera/Light dentro de un prefab en disco (LoadPrefabContents) - versión robusta
    // ------------------- Auto-upright (PCA + bbox fallback) -------------------

    private static readonly string[] DefaultUprightCategories = new string[] { "estatuas", "statue", "humanoidlike", "human", "person" };

    // Intent: detectar si el prefab viene acostado y rotarlo para que su eje principal coincida con Y-up.
    // Devuelve true si aplicó alguna rotación.
    private static bool TryAutoUprightInstance(GameObject instance, string category, string[] tags, float angleThresholdDeg = 45f, int minVertices = 50)
    {
        if (instance == null) return false;

        // Normalize category/tag checks to lower
        string catLower = (category ?? "").ToLower();
        bool allowCategory = DefaultUprightCategories.Any(c => catLower.Contains(c));
        // permitir override por tags: si tag contiene "upright" o "statue" permitir
        bool allowTag = (tags != null && tags.Any(t => (t ?? "").ToLower().Contains("statue") || (t ?? "").ToLower().Contains("upright")));

        if (!allowCategory && !allowTag)
        {
            // No intent to upright this category by default (p. ej. vehicles, swords, etc)
            return false;
        }

        // 1) intentar PCA sobre vértices
        Vector3 principal;
        bool okPCA = TryComputePrincipalAxisFromInstance(instance, out principal, minVertices);

        if (!okPCA)
        {
            // fallback rápido usando bounding box
            Bounds b = CalculateCombinedBounds(instance);
            if (b.size == Vector3.zero) return false;
            // si height es la menor dimensión -> probablemente acostado
            if (b.size.y < b.size.x * 0.9f && b.size.y < b.size.z * 0.9f)
            {
                // rotar para que el mayor eje (x o z) quede en Y
                if (b.size.x >= b.size.z)
                    principal = Vector3.right; // x -> up
                else
                    principal = Vector3.forward; // z -> up
                okPCA = true;
            }
            else return false;
        }

        // Calculamos ángulo entre eje principal (abs) y Vector3.up
        float angle = Vector3.Angle(Vector3.Normalize(principal), Vector3.up);
        if (angle > 90f) angle = 180f - angle; // usar ángulo menor con up

        if (angle <= angleThresholdDeg)
        {
            // ya suficientemente upright
            Debug.Log($"AutoUpright: instance already within threshold angle {angle:F1}° -> no se rota.");
            return false;
        }

        // calcular rotación necesaria: queremos alinear principal -> up
        Quaternion rot = Quaternion.FromToRotation(principal.normalized, Vector3.up);

        // Aplicar rotación al root (antes de escalar). Nota: multiplicar la rotación existente.
        instance.transform.rotation = rot * instance.transform.rotation;

        // Log detallado
        Debug.Log($"AutoUpright: Applied rotation to '{instance.name}': principal axis angle {angle:F1}° -> rotated by {Quaternion.Angle(Quaternion.identity, rot):F1}° (category: {category}).");

        return true;
    }

    // Extrae el eje principal de un GameObject (instancia del modelo) mediante PCA sobre vertices de todos los MeshFilters.
    // Devuelve true y el vector principal (dirección) en caso de éxito.
    private static bool TryComputePrincipalAxisFromInstance(GameObject instance, out Vector3 principal, int minVertices = 50)
    {
        principal = Vector3.up;

        var meshFilters = instance.GetComponentsInChildren<MeshFilter>(true);
        if (meshFilters == null || meshFilters.Length == 0) return false;

        List<Vector3> pts = new List<Vector3>();
        foreach (var mf in meshFilters)
        {
            var mesh = mf.sharedMesh;
            if (mesh == null) continue;
            var verts = mesh.vertices;
            // transformar cada vértice a "world" dentro de la instancia
            var trs = mf.transform;
            for (int i = 0; i < verts.Length; i++)
            {
                // TransformPoint coloca en el espacio global de la escena; como instance no está colocado en escena (instancia temporal)
                // esto es consistente para calcular direcciones relativas.
                pts.Add(trs.TransformPoint(verts[i]));
            }
        }

        if (pts.Count < minVertices) return false;

        // Calcular media
        Vector3 mean = Vector3.zero;
        foreach (var p in pts) mean += p;
        mean /= pts.Count;

        // Calcular matriz de covarianza 3x3
        double cxx = 0, cxy = 0, cxz = 0, cyy = 0, cyz = 0, czz = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            Vector3 v = pts[i] - mean;
            double x = v.x, y = v.y, z = v.z;
            cxx += x * x;
            cxy += x * y;
            cxz += x * z;
            cyy += y * y;
            cyz += y * z;
            czz += z * z;
        }
        double n = pts.Count;
        cxx /= n; cxy /= n; cxz /= n; cyy /= n; cyz /= n; czz /= n;

        // Cov matrix as 3x3:
        // [cxx cxy cxz]
        // [cxy cyy cyz]
        // [cxz cyz czz]

        // Use power iteration to approximate principal eigenvector
        Vector3 vcur = new Vector3(1, 0.2f, 0.1f).normalized;
        for (int iter = 0; iter < 50; iter++)
        {
            // multiply matrix * vcur
            double vx = cxx * vcur.x + cxy * vcur.y + cxz * vcur.z;
            double vy = cxy * vcur.x + cyy * vcur.y + cyz * vcur.z;
            double vz = cxz * vcur.x + cyz * vcur.y + czz * vcur.z;
            Vector3 vnext = new Vector3((float)vx, (float)vy, (float)vz);
            float mag = vnext.magnitude;
            if (mag < 1e-6f) break;
            vnext /= mag;
            // convergence check
            if (Vector3.Dot(vcur, vnext) > 0.9999f) { vcur = vnext; break; }
            vcur = vnext;
        }

        principal = vcur.normalized;
        // sanity: if principal is degenerate (nan), fail
        if (float.IsNaN(principal.x) || float.IsNaN(principal.y) || float.IsNaN(principal.z)) return false;
        return true;
    }

    private static void StripCamerasAndLightsFromPrefab(string prefabPath, bool removeCameras = true, bool removeLights = true)
    {
        if (string.IsNullOrEmpty(prefabPath)) return;
        var prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
        if (prefabRoot == null)
        {
            Debug.LogWarning($"StripCamerasAndLightsFromPrefab: No se pudo cargar prefab contents para {prefabPath}");
            return;
        }
        try
        {
            StripCamerasAndLightsFromInstance(prefabRoot, removeCameras, removeLights);
            PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }


    private static void CreateOrUpdateModelMetadataForPrefab(string prefabPath, Bounds bounds, float refHeightProvided, string category, string[] tags)
    {
        string prefabName = Path.GetFileNameWithoutExtension(prefabPath);
        string metaDir = "Assets/Metadata";
        if (!AssetDatabase.IsValidFolder(metaDir)) AssetDatabase.CreateFolder("Assets", "Metadata");
        string metaPath = $"{metaDir}/{prefabName}Meta.asset";

        ModelMetadata mm = AssetDatabase.LoadAssetAtPath<ModelMetadata>(metaPath);
        if (mm == null)
        {
            mm = ScriptableObject.CreateInstance<ModelMetadata>();
            AssetDatabase.CreateAsset(mm, metaPath);
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

        // --- guardar la metadata básica como antes ---
        mm.size = bounds.size;
        mm.triangleCount = CountTriangles(prefab);
        mm.materialNames = prefab.GetComponentsInChildren<MeshRenderer>()
            .SelectMany(r => r.sharedMaterials)
            .Where(m => m != null)
            .Select(m => m.name).Distinct().ToArray();
        mm.category = category;
        mm.referenceHeightProvided = refHeightProvided;
        if (tags != null && tags.Length > 0)
            mm.tags = tags;

        EditorUtility.SetDirty(mm);
        AssetDatabase.SaveAssets();

        // --- Ahora la parte de debug: comparar bounds pasados vs bounds medidos en prefab ---
        try
        {
            // Medir bounds reales instanciando el prefab (usa tu helper GetPrefabCombinedBounds)
            Bounds prefabBounds = GetPrefabCombinedBounds($"Assets/Prefabs/{SanitizeName(prefabName)}.prefab");

            // Si no hay prefab en Assets/Prefabs (fallback)
            if (prefabBounds.size == Vector3.zero && prefab != null)
            {
                // intentar medida rápida instanciando
                var inst = GameObject.Instantiate(prefab);
                inst.hideFlags = HideFlags.HideAndDontSave;
                prefabBounds = CalculateCombinedBounds(inst);
                GameObject.DestroyImmediate(inst);
            }

            // Comparaciones y reglas heurísticas
            string notes = "";
            float ratioY = (prefabBounds.size.y > 0.0001f) ? (bounds.size.y / prefabBounds.size.y) : -1f;
            float ratioX = (prefabBounds.size.x > 0.0001f) ? (bounds.size.x / prefabBounds.size.x) : -1f;
            float ratioZ = (prefabBounds.size.z > 0.0001f) ? (bounds.size.z / prefabBounds.size.z) : -1f;

            if (prefabBounds.size == Vector3.zero)
            {
                notes += "WARNING: prefabBounds == Vector3.zero (no se pudieron medir renderers ni meshes). ";
            }
            else
            {
                // Detectar discrepancias grandes
                if (ratioY > 10f || ratioX > 10f || ratioZ > 10f)
                    notes += $"WARNING: Los bounds proporcionados ({bounds.size}) son MUCHO mayores que los bounds del prefab ({prefabBounds.size}). Posible doble-escala aplicada.\n";
                if ((ratioY > 0f && ratioY < 0.1f) || (ratioX > 0f && ratioX < 0.1f) || (ratioZ > 0f && ratioZ < 0.1f))
                    notes += $"WARNING: Los bounds proporcionados son MUCHO menores que los bounds del prefab. Posible problema de escala invertida o units mismatch.\n";

                // Si final bounds minúsculos (p.e. 0.007) advertir
                if (prefabBounds.size.x < 0.01f || prefabBounds.size.y < 0.01f || prefabBounds.size.z < 0.01f)
                    notes += $"WARNING: PrefabBounds MUY pequeños: {prefabBounds.size}. Revisa si hay child.scale ~0 o mesh con escala interna.\n";
            }

            // Revisar transform scales dentro del prefab (root + hijos)
            List<string> scaleIssues = new List<string>();
            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/{SanitizeName(prefabName)}.prefab");
            if (prefabAsset != null)
            {
                var contents = PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(prefabAsset));
                try
                {
                    foreach (var t in contents.GetComponentsInChildren<Transform>(true))
                    {
                        Vector3 ls = t.localScale;
                        Vector3 lossy = t.lossyScale;
                        // condiciones a alertar: localScale con componentes 0 o muy pequeñas, o muy grandes
                        if (Mathf.Abs(ls.x) < 0.001f || Mathf.Abs(ls.y) < 0.001f || Mathf.Abs(ls.z) < 0.001f)
                            scaleIssues.Add($"Transform {GetFullTransformPath(t, contents)} localScale nearly zero: {ls}");
                        if (Mathf.Abs(ls.x) > 10f || Mathf.Abs(ls.y) > 10f || Mathf.Abs(ls.z) > 10f)
                            scaleIssues.Add($"Transform {GetFullTransformPath(t, contents)} localScale large: {ls}");
                        // si lossy != (1,1,1) y localScale es 1 puede indicar padre escalado
                        if ((ls != Vector3.one) || (lossy != Vector3.one && ls == Vector3.one))
                            scaleIssues.Add($"Transform {GetFullTransformPath(t, contents)} localScale={ls} lossyScale={lossy}");
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }
            if (scaleIssues.Count > 0)
            {
                notes += "Transform scale issues:\n" + string.Join("\n", scaleIssues) + "\n";
            }

            // Guardar informe en JSON dentro de Metadata para inspección
            var report = new DebugImportReport()
            {
                prefabName = prefabName,
                providedBounds = new Vec3Serializable(bounds.size),
                measuredPrefabBounds = new Vec3Serializable(prefabBounds.size),
                ratioY = ratioY,
                ratioX = ratioX,
                ratioZ = ratioZ,
                triangleCount = mm.triangleCount,
                notes = notes,
                timestamp = DateTime.UtcNow.ToString("o")
            };

            string debugPath = $"{metaDir}/Debug_{prefabName}.json";
            File.WriteAllText(debugPath, JsonUtility.ToJson(report, true));
            AssetDatabase.ImportAsset(debugPath);
            AssetDatabase.SaveAssets();

            Debug.Log($"[ModelImportDebug] metadata saved for {prefabName}. ProvidedBounds={bounds.size} PrefabBounds={prefabBounds.size} ratios(Y/X/Z)={ratioY:F3}/{ratioX:F3}/{ratioZ:F3}");
            if (!string.IsNullOrEmpty(notes)) Debug.LogWarning($"[ModelImportDebug] notes for {prefabName}: {notes}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"CreateOrUpdateModelMetadataForPrefab debug error: {ex}");
        }
    }

    // --- helpers serializables para el json del reporte ---
    [Serializable]
    private class DebugImportReport
    {
        public string prefabName;
        public Vec3Serializable providedBounds;
        public Vec3Serializable measuredPrefabBounds;
        public float ratioY;
        public float ratioX;
        public float ratioZ;
        public int triangleCount;
        public string notes;
        public string timestamp;
    }

    [Serializable]
    private struct Vec3Serializable
    {
        public float x, y, z;
        public Vec3Serializable(Vector3 v) { x = v.x; y = v.y; z = v.z; }
    }

    // helper para obtener path del transform dentro del prefab (para debug)
    private static string GetFullTransformPath(Transform t, GameObject root)
    {
        List<string> parts = new List<string>();
        Transform cur = t;
        while (cur != null && cur.gameObject != root)
        {
            parts.Add(cur.name);
            cur = cur.parent;
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    private static void SetupAnimatorForPrefab(string prefabPath, string fbxPath)
    {
        Debug.Log("Haciendo animacion");

        // Buscar AnimationClips en el FBX
        var clips = AssetDatabase.LoadAllAssetsAtPath(fbxPath)
            .OfType<AnimationClip>()
            .Where(c => !c.name.Contains("__preview__"))
            .ToArray();

        if (clips == null || clips.Length == 0) return;

        // Forzar loop en los clips
        foreach (var clip in clips)
        {
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
        }

        // Crear carpeta para controllers
        string controllerDir = "Assets/AnimControllers";
        if (!AssetDatabase.IsValidFolder(controllerDir))
            AssetDatabase.CreateFolder("Assets", "AnimControllers");

        string prefabName = Path.GetFileNameWithoutExtension(prefabPath);
        string controllerPath = $"{controllerDir}/{prefabName}_Controller.controller";

        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        if (controller == null)
        {
            controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var rootSM = controller.layers[0].stateMachine;

            // Crear un estado por clip, el primero es default
            AnimatorState defaultState = null;
            foreach (var clip in clips)
            {
                var state = rootSM.AddState(clip.name);
                state.motion = clip;
                if (defaultState == null) defaultState = state;
            }
            if (defaultState != null) rootSM.defaultState = defaultState;
        }

        // Cargar prefab contents para agregar Animator
        var prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
        if (prefabRoot != null)
        {
            var animator = prefabRoot.GetComponent<Animator>();
            if (animator == null) animator = prefabRoot.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;

            PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    // ----------------- Feature extractor Vector5 (LOG volume) -----------------
    // ----------------- Feature extractor Vector5 (FIXED - usa combined bounds) -----------------
    // ----------------- Feature extractor Vector5 (FIXED - usa combined.bounds tal cual) -----------------
    private static FeatureVector5 ComputePhysicalFeatureVector5(GameObject prefab)
    {
        if (prefab == null) return FeatureVector5.zero;

        string prefabPath = AssetDatabase.GetAssetPath(prefab);
        GameObject root = null;
        bool usedLoadContents = false;

        if (!string.IsNullOrEmpty(prefabPath))
        {
            root = PrefabUtility.LoadPrefabContents(prefabPath);
            usedLoadContents = true;
        }
        else
        {
            root = GameObject.Instantiate(prefab);
            root.hideFlags = HideFlags.HideAndDontSave;
        }

        try
        {
            // Medir AABB combinado usando Renderers (world-space dentro del prefab contents)
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers != null && renderers.Length > 0)
            {
                Bounds combined = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) combined.Encapsulate(renderers[i].bounds);

                // IMPORTANTE: NO intentamos convertir por lossyScale.
                // Usamos combined.size (en las mismas unidades que CalculateCombinedBounds(instancia))
                Vector3 size = combined.size;

                float height = size.y;
                float footprint = Mathf.Max(size.x, size.z);
                float depth = Mathf.Min(size.x, size.z);
                float aspect = (footprint > 0f) ? height / footprint : height;
                float rawVolume = Mathf.Max(0.0001f, size.x * size.y * size.z);
                float volumeLog = Mathf.Log(rawVolume + 1f);

                return new FeatureVector5(height, footprint, depth, aspect, volumeLog);
            }

            // Fallback robusto: si no hay Renderers, construimos AABB desde MeshFilters transformando sus bounds a world
            var meshFilters = root.GetComponentsInChildren<MeshFilter>(true);
            if (meshFilters != null && meshFilters.Length > 0)
            {
                bool haveAny = false;
                Bounds total = new Bounds(Vector3.zero, Vector3.zero);
                foreach (var mf in meshFilters)
                {
                    var mesh = mf.sharedMesh;
                    if (mesh == null) continue;

                    // transformar los 8 vértices de mesh.bounds a world y encapsular
                    Bounds mb = mesh.bounds;
                    Vector3 ext = mb.extents;
                    Vector3 c = mb.center;
                    Vector3[] corners = new Vector3[8];
                    int idx = 0;
                    for (int x = -1; x <= 1; x += 2)
                        for (int y = -1; y <= 1; y += 2)
                            for (int z = -1; z <= 1; z += 2)
                                corners[idx++] = mf.transform.TransformPoint(c + Vector3.Scale(ext, new Vector3(x, y, z)));

                    Bounds w = new Bounds(corners[0], Vector3.zero);
                    for (int i = 1; i < corners.Length; i++) w.Encapsulate(corners[i]);

                    if (!haveAny) { total = w; haveAny = true; }
                    else total.Encapsulate(w);
                }

                if (haveAny)
                {
                    Vector3 size = total.size;
                    float height = size.y;
                    float footprint = Mathf.Max(size.x, size.z);
                    float depth = Mathf.Min(size.x, size.z);
                    float aspect = (footprint > 0f) ? height / footprint : height;
                    float rawVolume = Mathf.Max(0.0001f, size.x * size.y * size.z);
                    float volumeLog = Mathf.Log(rawVolume + 1f);
                    return new FeatureVector5(height, footprint, depth, aspect, volumeLog);
                }
            }

            return FeatureVector5.zero;
        }
        finally
        {
            if (usedLoadContents && root != null) PrefabUtility.UnloadPrefabContents(root);
            else if (root != null) GameObject.DestroyImmediate(root);
        }
    }




    // ---------------------- Helpers para objectId ----------------------

    private static string ComputeFileHashShort(string assetPath, int shortLength = 6)
    {
        try
        {
            // assetPath viene como "Assets/Incoming/....fbx"
            // File.ReadAllBytes funciona relativo al project root, así que Path.GetFullPath lo resuelve bien.
            string full = Path.GetFullPath(assetPath);
            if (!File.Exists(full))
            {
                // fallback: intentar sin GetFullPath (Unity ya acepta rutas relativas)
                full = assetPath;
                if (!File.Exists(full)) return "nofile";
            }

            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                byte[] data = File.ReadAllBytes(full);
                byte[] hash = md5.ComputeHash(data);
                string hex = BitConverter.ToString(hash).Replace("-", "").ToLower();
                if (shortLength <= 0 || shortLength > hex.Length) return hex;
                return hex.Substring(0, shortLength);
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"ComputeFileHashShort error for '{assetPath}': {ex.Message}");
            return "errhash";
        }
    }
    // Ajusta la escala del prefab para que su altura (bounds.y) = targetHeight (si targetHeight>0).
    // Devuelve true si aplicó una corrección.
    private static bool AutoNormalizePrefabScaleToHeight(string prefabPath, float targetHeight, float tol = 0.01f)
    {
        if (string.IsNullOrEmpty(prefabPath) || targetHeight <= 0f) return false;

        try
        {
            // 1) Medir bounds actuales
            Bounds cur = GetPrefabCombinedBounds(prefabPath);
            if (cur.size == Vector3.zero)
            {
                Debug.LogWarning($"AutoNormalize: no se pudieron medir bounds para {prefabPath}.");
                return false;
            }

            float curHeight = cur.size.y;
            if (curHeight <= 0f) return false;

            float factor = targetHeight / curHeight;
            if (Mathf.Approximately(factor, 1f) || Mathf.Abs(1f - factor) <= tol)
            {
                // Dentro de tolerancia: no cambiar
                Debug.Log($"AutoNormalize: {Path.GetFileName(prefabPath)} altura actual {curHeight:F3}m within tol -> no se aplica factor {factor:F3}.");
                return false;
            }

            // 2) Cargar prefab contents y aplicar factor sobre root.localScale
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null)
            {
                Debug.LogWarning($"AutoNormalize: no se pudo LoadPrefabContents para {prefabPath}");
                return false;
            }

            try
            {
                // Registrar issues de transforms (child scales distintas de 1)
                List<string> scaleIssues = new List<string>();
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    Vector3 ls = t.localScale;
                    if (Mathf.Abs(ls.x - 1f) > 0.001f || Mathf.Abs(ls.y - 1f) > 0.001f || Mathf.Abs(ls.z - 1f) > 0.001f)
                    {
                        scaleIssues.Add($"{GetReadableTransformPath(t, root)} localScale={ls}");
                    }
                }
                if (scaleIssues.Count > 0)
                {
                    Debug.LogWarning($"AutoNormalize: transform scale issues detected in {Path.GetFileName(prefabPath)}: {string.Join("; ", scaleIssues.ToArray())}");
                }

                // Aplicar factor directamente al root del prefab (esto escala TODO de forma consistente)
                root.transform.localScale = root.transform.localScale * factor;

                // Guardar el prefab modificado
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            // 3) Comprobar y loguear resultado
            Bounds newBounds = GetPrefabCombinedBounds(prefabPath);
            Debug.Log($"AutoNormalize: aplicado factor {factor:F4} a {Path.GetFileName(prefabPath)}: height {curHeight:F4} -> {newBounds.size.y:F4} (target {targetHeight:F4})");

            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"AutoNormalizePrefabScaleToHeight error for {prefabPath}: {ex}");
            return false;
        }
    }

    // helper para imprimir path legible dentro del prefab
    private static string GetReadableTransformPath(Transform t, GameObject root)
    {
        if (t == null || root == null) return "(unknown)";
        if (t == root.transform) return "(root)";
        List<string> parts = new List<string>();
        Transform cur = t;
        while (cur != null && cur.gameObject != root)
        {
            parts.Add(cur.name);
            cur = cur.parent;
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    private static string GenerateObjectId(string category, string assetPath, string prefabName)
    {
        string safeCategory = string.IsNullOrEmpty(category) ? "Uncategorized" : category;
        string hash = ComputeFileHashShort(assetPath, 6);
        string safeName = SanitizeName(prefabName);
        return $"{safeCategory}/{hash}/{safeName}";
    }

    /// <summary>
    /// Añade o actualiza el campo objectId dentro del ModelMetadata asociado al prefab.
    /// metaPath interno es Assets/Metadata/{prefabName}Meta.asset
    /// </summary>
    private static void UpdateModelMetadataWithObjectId(string prefabPath, string assetPath, string category, string objectId = null)
    {
        string prefabName = Path.GetFileNameWithoutExtension(prefabPath);
        string metaDir = "Assets/Metadata";
        if (!AssetDatabase.IsValidFolder(metaDir)) AssetDatabase.CreateFolder("Assets", "Metadata");
        string metaPath = $"{metaDir}/{prefabName}Meta.asset";

        ModelMetadata mm = AssetDatabase.LoadAssetAtPath<ModelMetadata>(metaPath);
        if (mm == null)
        {
            mm = ScriptableObject.CreateInstance<ModelMetadata>();
            AssetDatabase.CreateAsset(mm, metaPath);
        }

        // si objectId no fue provisto, generar uno
        if (string.IsNullOrEmpty(objectId))
            objectId = GenerateObjectId(category, assetPath, prefabName);

        mm.objectId = objectId;

        EditorUtility.SetDirty(mm);
        AssetDatabase.SaveAssets();
    }

    /// <summary>
    /// Añade/actualiza componente ModelTag en el root del prefab con el objectId.
    /// Usa LoadPrefabContents para editar de forma segura.
    /// </summary>
    private static void AddOrUpdateModelTagOnPrefab(string prefabPath, string objectId)
    {
        var prefabContentsRoot = PrefabUtility.LoadPrefabContents(prefabPath);
        if (prefabContentsRoot == null)
        {
            Debug.LogWarning($"AddOrUpdateModelTagOnPrefab: No se pudo cargar prefab contents para {prefabPath}");
            return;
        }

        try
        {
            var tag = prefabContentsRoot.GetComponent<ModelTag>();
            if (tag == null) tag = prefabContentsRoot.AddComponent<ModelTag>();
            tag.objectId = objectId;

            PrefabUtility.SaveAsPrefabAsset(prefabContentsRoot, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabContentsRoot);
        }
    }
    // ---------------------- fin helpers ----------------------

    // Debug helper: imprime medidas antes/después y el factor de escala aplicado
    public static void PrintImportDebugForAsset(string assetPath)
    {
        try
        {
            Debug.Log($"--- Debug import for {assetPath} ---");
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (go == null) { Debug.LogWarning("No se pudo LoadAssetAtPath<GameObject> (assetPath may need extension)."); return; }

            // 1) instantiate temporary to compute original combined bounds (como hace ProcessImportedModel)
            var inst = GameObject.Instantiate(go);
            inst.hideFlags = HideFlags.HideAndDontSave;
            Bounds orig = CalculateCombinedBounds(inst);
            Debug.Log($"Original combined bounds (instance): size=({orig.size.x:F3},{orig.size.y:F3},{orig.size.z:F3})");

            // 2) determine category/refHeight same way as importer
            var metadata = TryLoadMetadata(assetPath);
            string category = metadata != null && !string.IsNullOrEmpty(metadata.category) ? metadata.category : InferCategoryFromNameOrShape(inst);
            float refH = DetermineReferenceHeight(inst, category, metadata);
            Debug.Log($"Inferred category='{category}', referenceHeight={refH:F3}");

            float scaleToRef = (orig.size.y > 0.0001f) ? (refH / orig.size.y) : 1f;
            Debug.Log($"scaleToRef = {scaleToRef:F4} (orig.height {orig.size.y:F3} -> target {refH:F3})");

            // 3) bounds after scaling on the instance
            inst.transform.localScale = Vector3.one * scaleToRef;
            Bounds afterScale = CalculateCombinedBounds(inst);
            Debug.Log($"After scale combined bounds (instance): size=({afterScale.size.x:F3},{afterScale.size.y:F3},{afterScale.size.z:F3})");

            // 4) check prefab path generated by importer (use file base name)
            string baseName = Path.GetFileNameWithoutExtension(assetPath);
            string prefabPath = $"Assets/Prefabs/{SanitizeName(baseName)}.prefab";
            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefabAsset == null) Debug.LogWarning($"Prefab not found at {prefabPath} (maybe not created yet).");
            else
            {
                var fv = ComputePhysicalFeatureVector5(prefabAsset);
                Debug.Log($"Computed FeatureVector5 from prefab: height={fv.a:F3}, footprint={fv.b:F3}, depth={fv.c:F3}, aspect={fv.d:F3}, volLog={fv.e:F3}");
            }

            GameObject.DestroyImmediate(inst);
        }
        catch (Exception ex)
        {
            Debug.LogError($"PrintImportDebugForAsset error: {ex}");
        }
    }


    [Serializable]
    public class SubpoolJson
    {
        public string subpoolId;
        public string displayName;
        public List<string> memberObjectIds = new List<string>();
        public FeatureVector5 center5 = FeatureVector5.zero;
    }

    [Serializable]
    public class PoolJson
    {
        public string poolName;
        public List<SubpoolJson> subpools = new List<SubpoolJson>();
    }

    // ---------- Función central: actualiza el JSON de la pool añadiendo/removiendo prefab -----------

    private static void UpdatePoolJsonForPrefab(string category, string objectId, GameObject prefabAsset)
    {
        try
        {
            // 1) path del JSON (si existe ArtPoolSO, tomar snapshotJsonPath)
            string defaultPath = $"Assets/PoolsSnapshots/{category}_pool.json";
            ArtPoolSO poolAsset = LoadPoolAssetForCategory(category);
            string jsonPath = null;
            if (poolAsset != null && !string.IsNullOrEmpty(poolAsset.snapshotJsonPath))
                jsonPath = poolAsset.snapshotJsonPath;
            else
                jsonPath = defaultPath;

            // 2) cargar (o crear) PoolJson
            PoolJson pj = null;
            if (File.Exists(jsonPath))
            {
                try { pj = JsonUtility.FromJson<PoolJson>(File.ReadAllText(jsonPath)); }
                catch { pj = null; }
            }
            if (pj == null) pj = new PoolJson() { poolName = category, subpools = new List<SubpoolJson>() };

            // 3) Compute feature vector for this prefab
            FeatureVector5 fv = FeatureVector5.zero;
            if (prefabAsset != null) fv = ComputePhysicalFeatureVector5(prefabAsset);
            else
            {
                // fallback: try to resolve prefab by objectId name
                string prefabName = objectId.Split('/').Last();
                string pPath = $"Assets/Prefabs/{SanitizeName(prefabName)}.prefab";
                var pf = AssetDatabase.LoadAssetAtPath<GameObject>(pPath);
                if (pf != null) fv = ComputePhysicalFeatureVector5(pf);
            }

            float[] scales = Settings.featureScales;
            float threshold = Settings.subpoolAssignThreshold;

            // 4) Remove objectId from any existing subpool (and adjust centers)
            RemoveObjectFromAllSubpools(pj, objectId, scales);

            // 5) Find best subpool
            float best = float.MaxValue;
            SubpoolJson bestSp = null;
            foreach (var sp in pj.subpools)
            {
                float dist = fv.DistanceTo( sp.center5, scales);
                if (dist < best) { best = dist; bestSp = sp; }
            }

            if (bestSp != null && best <= threshold)
            {
                // add to existing
                if (bestSp.memberObjectIds == null) bestSp.memberObjectIds = new List<string>();
                if (!bestSp.memberObjectIds.Contains(objectId))
                {
                    // recalc center incrementally: newCenter = (center * n + fv) / (n+1)
                    int n = bestSp.memberObjectIds.Count;
                    var old = bestSp.center5;
                    bestSp.memberObjectIds.Add(objectId);
                    bestSp.center5 = new FeatureVector5(
                        (old.a * n + fv.a) / (n + 1f),
                        (old.b * n + fv.b) / (n + 1f),
                        (old.c * n + fv.c) / (n + 1f),
                        (old.d * n + fv.d) / (n + 1f),
                        (old.e * n + fv.e) / (n + 1f)
                    );
                }
            }
            else
            {
                // create new subpool
                SubpoolJson snew = new SubpoolJson();
                snew.subpoolId = Guid.NewGuid().ToString("N").Substring(0, 8);
                snew.displayName = $"{category}_{pj.subpools.Count + 1}";
                snew.memberObjectIds = new List<string>() { objectId };
                snew.center5 = fv;
                pj.subpools.Add(snew);
            }

            // 6) Save JSON
            EnsureSnapshotFolderExists();
            string outJson = JsonUtility.ToJson(pj, true);
            File.WriteAllText(jsonPath, outJson);

#if UNITY_EDITOR
            // If there is an ArtPoolSO, ensure it references the jsonPath
            if (poolAsset != null)
            {
                poolAsset.snapshotJsonPath = jsonPath;
                UnityEditor.EditorUtility.SetDirty(poolAsset);
                UnityEditor.AssetDatabase.SaveAssets();
            }
            UnityEditor.AssetDatabase.ImportAsset(jsonPath);
            UnityEditor.AssetDatabase.Refresh();
#endif

            Debug.Log($"UpdatePoolJsonForPrefab: assigned {objectId} to pool JSON {jsonPath}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"UpdatePoolJsonForPrefab error: {ex}");
        }
    }

    // ---------- Helper: remove objectId from any subpool and adjust centers (recompute by loading feature vectors) ----------
    private static void RemoveObjectFromAllSubpools(PoolJson pj, string objectId, float[] scales)
    {
        if (pj == null || string.IsNullOrEmpty(objectId)) return;
        foreach (var sp in pj.subpools)
        {
            if (sp.memberObjectIds != null && sp.memberObjectIds.Contains(objectId))
            {
                // recalc center removing objectId:
                int n = sp.memberObjectIds.Count;
                // try compute featurevector for removed member (so we can adjust)
                FeatureVector5 removedFv = TryComputeFeatureVectorFromObjectId(objectId);
                sp.memberObjectIds.Remove(objectId);

                if (sp.memberObjectIds.Count == 0)
                {
                    sp.center5 = FeatureVector5.zero;
                    continue;
                }

                if (removedFv.Equals(FeatureVector5.zero) == false)
                {
                    // oldCenter * n = sum; newCenter = (oldCenter * n - removedFv) / (n-1)
                    var old = sp.center5;
                    float denom = (n - 1f);
                    sp.center5 = new FeatureVector5(
                        (old.a * n - removedFv.a) / denom,
                        (old.b * n - removedFv.b) / denom,
                        (old.c * n - removedFv.c) / denom,
                        (old.d * n - removedFv.d) / denom,
                        (old.e * n - removedFv.e) / denom
                    );
                }
                else
                {
                    // fallback conservative: recompute center by iterating over remaining members (heavy but correct)
                    FeatureVector5 sum = FeatureVector5.zero;
                    int cnt = 0;
                    foreach (var oid in sp.memberObjectIds)
                    {
                        var fv = TryComputeFeatureVectorFromObjectId(oid);
                        if (fv.Equals(FeatureVector5.zero)) continue;
                        sum = sum + fv;
                        cnt++;
                    }
                    sp.center5 = (cnt > 0) ? (sum / (float)cnt) : FeatureVector5.zero;
                }
            }
        }
    }

    // ---------- Small helpers ----------
    private static void EnsureSnapshotFolderExists()
    {
#if UNITY_EDITOR
        string dir = "Assets/PoolsSnapshots";
        if (!UnityEditor.AssetDatabase.IsValidFolder(dir))
            UnityEditor.AssetDatabase.CreateFolder("Assets", "PoolsSnapshots");
#endif
    }

    // Try compute FeatureVector5 from objectId (loads prefab metadata or prefab)
    private static FeatureVector5 TryComputeFeatureVectorFromObjectId(string objectId)
    {
        try
        {
            // assume objectId last segment is prefabName
            string prefabName = objectId.Split('/').Last();
            string metaPath = $"Assets/Metadata/{prefabName}Meta.asset";
            var mm = AssetDatabase.LoadAssetAtPath<ModelMetadata>(metaPath);
            if (mm != null && mm.size != Vector3.zero)
            {
                float height = mm.size.y;
                float footprint = Mathf.Max(mm.size.x, mm.size.z);
                float depth = Mathf.Min(mm.size.x, mm.size.z);
                float aspect = (footprint > 0f) ? height / footprint : height;
                float rawVol = Mathf.Max(0.0001f, mm.size.x * mm.size.y * mm.size.z);
                float volLog = Mathf.Log(rawVol + 1f);
                return new FeatureVector5(height, footprint, depth, aspect, volLog);
            }

            // fallback to prefab measurement
            string prefabPath = $"Assets/Prefabs/{SanitizeName(prefabName)}.prefab";
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (pf != null)
            {
                return ComputePhysicalFeatureVector5(pf);
            }
        }
        catch { }
        return FeatureVector5.zero;
    }


}



// colocar fuera de la clase ModelImportPostprocessorEnhanced
public static class FeatureVector5Extensions
{
    /// <summary>
    /// Extensión para calcular distancia L2 entre dos FeatureVector5 con escalas seguras.
    /// Está pensada para ser robusta aunque Settings.featureScales no tenga 5 entradas.
    /// </summary>
    public static float DistanceTo(this FeatureVector5 a, FeatureVector5 b, float[] scales)
    {
        // defensivo: si scales es null o incompleto, usamos 1.0f para los índices faltantes
        float s0 = (scales != null && scales.Length > 0) ? scales[0] : 1f;
        float s1 = (scales != null && scales.Length > 1) ? scales[1] : 1f;
        float s2 = (scales != null && scales.Length > 2) ? scales[2] : 1f;
        float s3 = (scales != null && scales.Length > 3) ? scales[3] : 1f;
        float s4 = (scales != null && scales.Length > 4) ? scales[4] : 1f;

        // asumimos que FeatureVector5 tiene campos públicos a,b,c,d,e (igual que usás al crear instancias)
        float da = (a.a - b.a) * s0;
        float db = (a.b - b.b) * s1;
        float dc = (a.c - b.c) * s2;
        float dd = (a.d - b.d) * s3;
        float de = (a.e - b.e) * s4;

        // calcular L2
        float sum = da * da + db * db + dc * dc + dd * dd + de * de;
        return Mathf.Sqrt(sum);
    }
}
