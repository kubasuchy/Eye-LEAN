using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Visual feedback component for gaze-interactive objects.
/// Changes color when gazed at and tracks gaze statistics.
/// </summary>
public class GazeTarget : MonoBehaviour
{
    [Header("Visual Feedback")]
    [SerializeField] private Color defaultColor = Color.blue;
    [SerializeField] private Color gazedColor = new Color(1f, 0.5f, 0f);  // Orange - distinct from vergence point
    [SerializeField] private float colorTransitionSpeed = 10f;
    [SerializeField] private bool useEmissiveHighlight = true;

    [Header("Gaze Detection")]
    [SerializeField] private float gazeTimeThreshold = 0.1f;

    [Header("Debug Display")]
    [SerializeField] private bool showCoordinateDisplay = true;

    // Component references
    private Renderer objectRenderer;
    private Material objectMaterial;
    private GameObject coordinateDisplay;

    // Gaze tracking state
    private bool isBeingGazedAt = false;
    private float gazeStartTime;
    private float totalGazeTime = 0f;
    private Vector3 lastHitPoint;

    // Object properties
    private bool isDynamicObject;
    private string objectId;

    /// <summary>
    /// Initialize the gaze target with its properties.
    /// Called by EnvironmentGenerator when creating objects.
    /// </summary>
    public void Initialize(bool isDynamic)
    {
        isDynamicObject = isDynamic;
        objectId = gameObject.name;

        objectRenderer = GetComponent<Renderer>();
        if (objectRenderer != null)
        {
            if (objectRenderer.material != null)
            {
                objectMaterial = new Material(objectRenderer.material);
                objectRenderer.material = objectMaterial;
                defaultColor = objectMaterial.color;
            }
            else
            {
                Color transparentColor = isDynamic ? new Color(1f, 0f, 0f, 0.4f) : new Color(0f, 0f, 1f, 0.4f);
                objectMaterial = GetMaterial(transparentColor, true);
                objectRenderer.material = objectMaterial;
                defaultColor = transparentColor;
            }

            objectMaterial.color = defaultColor;
            Debug.Log($"[GazeTarget] Initialized {gameObject.name} with color {defaultColor}");
        }
        else
        {
            Debug.LogWarning($"[GazeTarget] {gameObject.name} has no Renderer component!");
        }

        CreateCoordinateDisplay();
    }

    void Update()
    {
        UpdateVisualFeedback();
        UpdateCoordinateDisplay();
    }

    /// <summary>
    /// Called when gaze enters this object.
    /// </summary>
    public void OnGazeEnter(Vector3 hitPoint, float gazeDistance)
    {
        if (!isBeingGazedAt)
        {
            isBeingGazedAt = true;
            gazeStartTime = Time.time;
            lastHitPoint = hitPoint;

            Debug.Log($"[GazeTarget] GAZE_ENTER: {objectId} at distance {gazeDistance:F2}m");
        }

        lastHitPoint = hitPoint;
    }

    /// <summary>
    /// Called when gaze exits this object.
    /// </summary>
    public void OnGazeExit()
    {
        if (isBeingGazedAt)
        {
            isBeingGazedAt = false;
            float gazeDuration = Time.time - gazeStartTime;
            totalGazeTime += gazeDuration;

            if (gazeDuration >= gazeTimeThreshold)
            {
                Debug.Log($"[GazeTarget] GAZE_EXIT: {objectId} after {gazeDuration:F2}s");
            }
        }
    }

    /// <summary>
    /// Called continuously while being gazed at.
    /// </summary>
    public void OnGazeStay(Vector3 hitPoint, float gazeDistance)
    {
        if (isBeingGazedAt)
        {
            lastHitPoint = hitPoint;
        }
    }

    /// <summary>
    /// Handles smooth color transitions for visual feedback.
    /// </summary>
    void UpdateVisualFeedback()
    {
        if (objectMaterial != null)
        {
            Color targetColor = isBeingGazedAt ? gazedColor : defaultColor;
            Color currentColor = objectMaterial.color;

            Color newColor = Color.Lerp(currentColor, targetColor, colorTransitionSpeed * Time.deltaTime);
            objectMaterial.color = newColor;

            if (useEmissiveHighlight && objectMaterial.HasProperty("_EmissionColor"))
            {
                Color emissionColor = isBeingGazedAt ? gazedColor * 0.5f : Color.black;
                objectMaterial.SetColor("_EmissionColor", emissionColor);

                if (isBeingGazedAt)
                {
                    objectMaterial.EnableKeyword("_EMISSION");
                }
                else
                {
                    objectMaterial.DisableKeyword("_EMISSION");
                }
            }
        }
    }

    /// <summary>
    /// Public getter for current gaze state.
    /// </summary>
    public bool IsBeingGazedAt => isBeingGazedAt;

    /// <summary>
    /// Get total accumulated gaze time on this object.
    /// </summary>
    public float GetTotalGazeTime() => totalGazeTime;

    /// <summary>
    /// Reset gaze statistics.
    /// </summary>
    public void ResetGazeStats()
    {
        totalGazeTime = 0f;
        isBeingGazedAt = false;
    }

    void OnDestroy()
    {
        if (objectMaterial != null)
        {
            DestroyImmediate(objectMaterial);
        }
    }

    /// <summary>
    /// Gets a material using VRMaterialProvider for reliable VR/Android compatibility.
    /// </summary>
    Material GetMaterial(Color color, bool transparent)
    {
        return VRMaterialProvider.GetMaterial(color, transparent);
    }

    void CreateCoordinateDisplay()
    {
        if (!showCoordinateDisplay) return;

        GameObject canvasObj = new GameObject("TargetCoordinateDisplay");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;

        RectTransform canvasRect = canvas.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(800f, 150f);
        canvasRect.localScale = Vector3.one * 0.002f;

        GameObject textObj = new GameObject("CoordinateText");
        textObj.transform.SetParent(canvasObj.transform, false);

        Text textComponent = textObj.AddComponent<Text>();
        textComponent.text = "T: (0.0, 0.0, 0.0)";
        textComponent.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        textComponent.fontSize = 60;
        textComponent.color = Color.cyan;
        textComponent.alignment = TextAnchor.MiddleCenter;

        RectTransform textRect = textComponent.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        GameObject bgObj = new GameObject("Background");
        bgObj.transform.SetParent(canvasObj.transform, false);
        bgObj.transform.SetAsFirstSibling();

        Image bgImage = bgObj.AddComponent<Image>();
        bgImage.color = new Color(0, 0, 0, 0.8f);

        RectTransform bgRect = bgImage.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;

        coordinateDisplay = canvasObj;
        coordinateDisplay.transform.SetParent(transform, false);
    }

    void UpdateCoordinateDisplay()
    {
        if (coordinateDisplay == null || !showCoordinateDisplay) return;

        Vector3 displayPos = transform.position + Vector3.up * 0.3f;
        coordinateDisplay.transform.position = displayPos;

        if (Camera.main != null)
        {
            coordinateDisplay.transform.LookAt(Camera.main.transform.position);
            coordinateDisplay.transform.Rotate(0, 180, 0);
        }

        Vector3 pos = transform.position;
        Text textComponent = coordinateDisplay.GetComponentInChildren<Text>();
        if (textComponent != null)
        {
            textComponent.text = $"T: ({pos.x:F2}, {pos.y:F2}, {pos.z:F2})";
        }
    }
}
