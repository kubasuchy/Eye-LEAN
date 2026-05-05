using UnityEngine;

/// <summary>
/// Provides materials programmatically for VR environments. Avoids Shader.Find()
/// when possible because shader stripping on Android can drop named shaders;
/// falls back to extracting a working material from a Unity primitive.
/// </summary>
public class VRMaterialProvider : MonoBehaviour
{
    public static VRMaterialProvider Instance { get; private set; }

    private static Material cachedPrimitiveMaterial;
    private static bool materialsInitialized = false;
    private static int initAttempts = 0;

    void Awake()
    {
        Debug.Log("[VRMaterialProvider] Awake called");
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        InitializeMaterials();
    }

    /// <summary>
    /// First tries Shader.Find on a list of shaders that should be present in
    /// "Always Included Shaders"; if all return null, extracts a material from
    /// a temporary primitive as a last resort.
    /// </summary>
    static void InitializeMaterials()
    {
        initAttempts++;
        Debug.Log($"[VRMaterialProvider] InitializeMaterials attempt #{initAttempts}, already initialized: {materialsInitialized}");

        if (materialsInitialized && cachedPrimitiveMaterial != null)
        {
            Debug.Log("[VRMaterialProvider] Already initialized with valid material");
            return;
        }

        Debug.Log("[VRMaterialProvider] Trying Shader.Find...");
        Shader shader = null;

        string[] shadersToTry = {
            "Unlit/Color",
            "Mobile/Diffuse",
            "Mobile/VertexLit",
            "Unlit/Texture",
            "Legacy Shaders/Diffuse"
        };

        foreach (string shaderName in shadersToTry)
        {
            shader = Shader.Find(shaderName);
            if (shader != null)
            {
                Debug.Log($"[VRMaterialProvider] Found shader via Shader.Find: {shaderName}");
                cachedPrimitiveMaterial = new Material(shader);
                cachedPrimitiveMaterial.name = "VRMaterialProvider_BaseMaterial";
                materialsInitialized = true;
                return;
            }
            Debug.Log($"[VRMaterialProvider] Shader.Find failed for: {shaderName}");
        }

        Debug.Log("[VRMaterialProvider] All Shader.Find failed, extracting from primitive...");

        // Unity primitives always have a working sharedMaterial, even when
        // Shader.Find fails due to Android shader stripping.
        GameObject tempPrimitive = null;
        try
        {
            tempPrimitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tempPrimitive.name = "TempMaterialExtractor";

            var renderer = tempPrimitive.GetComponent<Renderer>();
            if (renderer == null)
            {
                Debug.LogError("[VRMaterialProvider] CRITICAL: Primitive has no Renderer!");
                return;
            }

            // sharedMaterial avoids the auto-instancing that .material would do.
            Material sharedMat = renderer.sharedMaterial;
            if (sharedMat == null)
            {
                Debug.LogError("[VRMaterialProvider] CRITICAL: Primitive sharedMaterial is null!");
                return;
            }

            Debug.Log($"[VRMaterialProvider] Primitive sharedMaterial: {sharedMat.name}, shader: {sharedMat.shader?.name ?? "NULL"}");

            cachedPrimitiveMaterial = new Material(sharedMat);
            cachedPrimitiveMaterial.name = "VRMaterialProvider_BaseMaterial";

            Debug.Log($"[VRMaterialProvider] SUCCESS: Created base material with shader: {cachedPrimitiveMaterial.shader?.name ?? "NULL"}");
            materialsInitialized = true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[VRMaterialProvider] Exception during init: {e.Message}\n{e.StackTrace}");
        }
        finally
        {
            if (tempPrimitive != null)
            {
                // DestroyImmediate because this can run from Awake/Start.
                Object.DestroyImmediate(tempPrimitive);
            }
        }
    }

    /// <summary>
    /// Returns a new Material instance cloned from the cached primitive material,
    /// tinted to <paramref name="color"/>.
    /// </summary>
    public static Material GetMaterial(Color color, bool transparent = false)
    {
        Debug.Log($"[VRMaterialProvider] GetMaterial called: color={color}, transparent={transparent}");

        if (!materialsInitialized || cachedPrimitiveMaterial == null)
        {
            Debug.Log("[VRMaterialProvider] Not initialized, calling InitializeMaterials");
            InitializeMaterials();
        }

        if (cachedPrimitiveMaterial == null)
        {
            Debug.LogError("[VRMaterialProvider] CRITICAL: No cached material available!");
            return null;
        }

        Material instance = new Material(cachedPrimitiveMaterial);
        instance.name = $"VRMaterial_{color}";

        SetMaterialColor(instance, color);

        Debug.Log($"[VRMaterialProvider] Created material: shader={instance.shader?.name ?? "NULL"}, color={instance.color}");
        return instance;
    }

    /// <summary>
    /// Sets a material's color across both built-in (_Color) and URP (_BaseColor)
    /// property names so the same call works regardless of the active pipeline.
    /// </summary>
    private static void SetMaterialColor(Material mat, Color color)
    {
        if (mat == null) return;

        mat.color = color;

        if (mat.HasProperty("_BaseColor"))
        {
            mat.SetColor("_BaseColor", color);
        }

        if (mat.HasProperty("_Color"))
        {
            mat.SetColor("_Color", color);
        }
    }

    /// <summary>
    /// Tints the primitive's existing material in-place rather than allocating
    /// a new one — preferred path because primitives always have a valid material.
    /// </summary>
    public static Material TintPrimitiveMaterial(GameObject primitive, Color color)
    {
        if (primitive == null)
        {
            Debug.LogWarning("[VRMaterialProvider] TintPrimitiveMaterial: primitive is null");
            return null;
        }

        var renderer = primitive.GetComponent<Renderer>();
        if (renderer == null)
        {
            Debug.LogWarning($"[VRMaterialProvider] TintPrimitiveMaterial: {primitive.name} has no Renderer");
            return null;
        }

        Material mat = renderer.material;
        if (mat == null)
        {
            Debug.LogWarning($"[VRMaterialProvider] TintPrimitiveMaterial: {primitive.name} material is null, using GetMaterial fallback");
            mat = GetMaterial(color);
            if (mat != null)
            {
                renderer.material = mat;
            }
            return mat;
        }

        if (initAttempts <= 3)
        {
            Debug.Log($"[VRMaterialProvider] TintPrimitiveMaterial: {primitive.name}, shader={mat.shader?.name ?? "NULL"}, color={color}");
        }

        SetMaterialColor(mat, color);

        return mat;
    }

    public static Material GetOpaqueMaterial(Color color)
    {
        return GetMaterial(color, false);
    }

    public static Material GetTransparentMaterial(Color color)
    {
        return GetMaterial(color, true);
    }
}
