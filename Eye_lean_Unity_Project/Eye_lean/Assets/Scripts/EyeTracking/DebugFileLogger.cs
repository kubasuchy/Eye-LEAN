using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System;
using System.IO;
using System.Text;
using System.Collections;
using EyeTracking.Core;

/// <summary>
/// Writes debug logs to a file for post-session analysis.
/// Unity 6 / Android 11+ compatible - uses app-specific external storage.
///
/// FILE LOCATIONS:
/// - Android: /storage/emulated/0/Android/data/[package]/files/DebugLogs/
/// - Retrieve via ADB: adb pull /sdcard/Android/data/[your.package.id]/files/DebugLogs/ ./logs/
///   (Replace [your.package.id] with your app's package identifier from Project Settings)
/// - Editor: [Project]/DebugLogs/
/// </summary>
public class DebugFileLogger : MonoBehaviour, ISceneTransitionAware
{
    public static DebugFileLogger Instance { get; private set; }

    [Header("Settings")]
    [SerializeField] private bool enableFileLogging = true;
    [SerializeField] private bool logUnityMessages = true;
    [SerializeField] private bool includeStackTrace = false;
    [SerializeField] private int flushIntervalFrames = 60;

    [Header("Filters")]
    [SerializeField] private bool logErrors = true;
    [SerializeField] private bool logWarnings = true;
    [SerializeField] private bool logInfo = true;

    [Header("Debug Display")]
    [SerializeField] private bool showPathOnScreen = true;
    [SerializeField] private float displayDuration = 10f;

    private StreamWriter logWriter;
    private StringBuilder logBuffer;
    private string logFilePath;
    private string logsDirectory;
    private int frameCounter = 0;
    private bool isInitialized = false;
    private string initializationStatus = "Not initialized";

    // UI elements for path display
    private GameObject debugCanvas;
    private Text pathDisplayText;

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("[DebugFileLogger]");
        Instance = go.AddComponent<DebugFileLogger>();
        DontDestroyOnLoad(go);
    }

    void Awake()
    {
        // Duplicate-instance branch must Destroy(this) only — Destroy(gameObject)
        // would nuke any sibling MonoBehaviours on a researcher's host GO.
        // The Bootstrap() above creates a dedicated GO for the canonical
        // singleton, so this path only ever fires for scene-placed instances.
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        if (SceneTransitionCoordinator.Instance != null)
        {
            SceneTransitionCoordinator.Instance.Register(this);
        }

        StartCoroutine(InitializeLoggerDelayed());
    }

    public void OnSceneWillUnload(Scene from)
    {
        // Clear the cached canvas ref so CreatePathDisplay re-instantiates
        // it for the next scene if showPathOnScreen is still true.
        if (debugCanvas != null)
        {
            Destroy(debugCanvas);
            debugCanvas = null;
        }
    }

    public void OnSceneDidLoad(Scene to)
    {
        // No-op; CreatePathDisplay re-creates lazily if needed.
    }

    /// <summary>
    /// Defer initialization by one frame so dependent systems have started.
    /// </summary>
    IEnumerator InitializeLoggerDelayed()
    {
        yield return null;

        InitializeLogger();

        if (showPathOnScreen)
        {
            CreatePathDisplay();
            StartCoroutine(HidePathDisplayAfterDelay());
        }
    }

    void InitializeLogger()
    {
        if (!enableFileLogging)
        {
            initializationStatus = "File logging disabled";
            return;
        }

        Debug.Log($"[DebugFileLogger] === Storage Path Information ===");
        Debug.Log($"[DebugFileLogger] persistentDataPath: {Application.persistentDataPath}");
        Debug.Log($"[DebugFileLogger] dataPath: {Application.dataPath}");
        Debug.Log($"[DebugFileLogger] temporaryCachePath: {Application.temporaryCachePath}");
        Debug.Log($"[DebugFileLogger] Platform: {Application.platform}");

        string[] pathsToTry = GetStoragePathsToTry();

        foreach (string basePath in pathsToTry)
        {
            if (TryInitializeAtPath(basePath))
            {
                initializationStatus = $"SUCCESS: {logFilePath}";
                Debug.Log($"[DebugFileLogger] ✓ Successfully initialized at: {logFilePath}");
                return;
            }
        }

        initializationStatus = "FAILED: Could not write to any storage location";
        Debug.LogError($"[DebugFileLogger] ✗ Failed to initialize - tried all available paths");
    }

    /// <summary>
    /// Get list of storage paths to try, in order of preference.
    /// </summary>
    string[] GetStoragePathsToTry()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return new string[]
        {
            // 1. App's external files directory - accessible via file browser
            //    Path: /storage/emulated/0/Android/data/[package]/files/DebugLogs
            GetAndroidExternalFilesDir(),

            // 2. Unity's persistent data path (internal storage)
            //    Path: /data/data/[package]/files/DebugLogs
            Path.Combine(Application.persistentDataPath, "DebugLogs"),

            // 3. Cache directory as last resort
            Path.Combine(Application.temporaryCachePath, "DebugLogs")
        };
#else
        // Editor or other platforms
        return new string[]
        {
            Path.Combine(Application.dataPath, "..", "DebugLogs"),
            Path.Combine(Application.persistentDataPath, "DebugLogs")
        };
#endif
    }

    /// <summary>
    /// App-specific external storage directory via JNI. No permissions
    /// required on Android 11+.
    /// </summary>
    string GetAndroidExternalFilesDir()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                if (activity != null)
                {
                    using (AndroidJavaObject externalFilesDir = activity.Call<AndroidJavaObject>("getExternalFilesDir", (object)null))
                    {
                        if (externalFilesDir != null)
                        {
                            string path = externalFilesDir.Call<string>("getAbsolutePath");
                            Debug.Log($"[DebugFileLogger] Android external files dir: {path}");
                            return Path.Combine(path, "DebugLogs");
                        }
                        else
                        {
                            Debug.LogWarning("[DebugFileLogger] getExternalFilesDir returned null");
                        }
                    }
                }
                else
                {
                    Debug.LogWarning("[DebugFileLogger] currentActivity is null");
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[DebugFileLogger] Failed to get Android external files dir: {e.Message}");
        }
#endif
        return Path.Combine(Application.persistentDataPath, "DebugLogs");
    }

    /// <summary>
    /// Attempt to initialize logging at the specified path.
    /// </summary>
    bool TryInitializeAtPath(string basePath)
    {
        if (string.IsNullOrEmpty(basePath))
        {
            Debug.LogWarning("[DebugFileLogger] Empty path provided");
            return false;
        }

        try
        {
            Debug.Log($"[DebugFileLogger] Trying path: {basePath}");

            if (!Directory.Exists(basePath))
            {
                Directory.CreateDirectory(basePath);
                Debug.Log($"[DebugFileLogger] Created directory: {basePath}");
            }

            // Probe write access before opening the real log file.
            string testFile = Path.Combine(basePath, "write_test.tmp");
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);
            Debug.Log($"[DebugFileLogger] Write test passed for: {basePath}");

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string filename = $"eye_tracking_log_{timestamp}.txt";
            string fullPath = Path.Combine(basePath, filename);

            logWriter = new StreamWriter(fullPath, false, Encoding.UTF8);
            logBuffer = new StringBuilder();
            logFilePath = fullPath;
            logsDirectory = basePath;

            WriteHeader();

            if (logUnityMessages)
            {
                Application.logMessageReceived += HandleUnityLog;
            }

            isInitialized = true;

            Log("INFO", "DebugFileLogger", $"Logger initialized at: {logFilePath}");

            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[DebugFileLogger] Failed at {basePath}: {e.Message}");
            return false;
        }
    }

    void WriteHeader()
    {
        logWriter.WriteLine("================================================================================");
        logWriter.WriteLine("VR Eye Tracking Debug Log");
        logWriter.WriteLine($"Session Start: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        logWriter.WriteLine($"Device: {SystemInfo.deviceModel}");
        logWriter.WriteLine($"OS: {SystemInfo.operatingSystem}");
        logWriter.WriteLine($"Unity Version: {Application.unityVersion}");
        logWriter.WriteLine($"Platform: {Application.platform}");
        logWriter.WriteLine($"Log Directory: {logsDirectory}");
        logWriter.WriteLine($"Log File: {logFilePath}");
        logWriter.WriteLine();
        logWriter.WriteLine("HOW TO RETRIEVE LOGS:");
        logWriter.WriteLine($"  adb pull /sdcard/Android/data/{Application.identifier}/files/DebugLogs/ ./logs/");
        logWriter.WriteLine("  OR");
        logWriter.WriteLine($"  adb pull \"{logFilePath}\" ./");
        logWriter.WriteLine("================================================================================");
        logWriter.WriteLine();
        logWriter.Flush();
    }

    void HandleUnityLog(string logString, string stackTrace, LogType type)
    {
        if (!isInitialized) return;

        string level;
        bool shouldLog = false;

        switch (type)
        {
            case LogType.Error:
            case LogType.Exception:
                level = "ERROR";
                shouldLog = logErrors;
                break;
            case LogType.Warning:
                level = "WARN";
                shouldLog = logWarnings;
                break;
            default:
                level = "INFO";
                shouldLog = logInfo;
                break;
        }

        if (shouldLog)
        {
            string message = logString;
            if (includeStackTrace && !string.IsNullOrEmpty(stackTrace))
            {
                message += $"\n  Stack: {stackTrace.Split('\n')[0]}";
            }
            WriteToBuffer(level, "Unity", message);
        }
    }

    public void Log(string level, string category, string message)
    {
        if (!isInitialized) return;
        WriteToBuffer(level, category, message);
    }

    public void LogInfo(string category, string message)
    {
        if (logInfo) Log("INFO", category, message);
    }

    public void LogWarning(string category, string message)
    {
        if (logWarnings) Log("WARN", category, message);
    }

    public void LogError(string category, string message)
    {
        if (logErrors) Log("ERROR", category, message);
    }

    void WriteToBuffer(string level, string category, string message)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        string frame = Time.frameCount.ToString();
        string line = $"[{timestamp}] [{frame}] [{level}] [{category}] {message}";

        lock (logBuffer)
        {
            logBuffer.AppendLine(line);
        }
    }

    void Update()
    {
        if (!isInitialized) return;

        frameCounter++;
        if (frameCounter >= flushIntervalFrames)
        {
            FlushBuffer();
            frameCounter = 0;
        }
    }

    void FlushBuffer()
    {
        if (logBuffer == null || logBuffer.Length == 0) return;

        try
        {
            lock (logBuffer)
            {
                logWriter.Write(logBuffer.ToString());
                logWriter.Flush();
                logBuffer.Clear();
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[DebugFileLogger] Failed to flush: {e.Message}");
        }
    }

    public void ForceFlush()
    {
        FlushBuffer();
    }

    public string GetLogFilePath()
    {
        return logFilePath;
    }

    public string GetLogsDirectory()
    {
        return logsDirectory;
    }

    public string GetInitializationStatus()
    {
        return initializationStatus;
    }

    public bool IsInitialized()
    {
        return isInitialized;
    }

    /// <summary>
    /// Create an on-screen display showing the log file path.
    /// </summary>
    void CreatePathDisplay()
    {
        debugCanvas = new GameObject("DebugFileLoggerCanvas");
        debugCanvas.transform.SetParent(transform);

        Canvas canvas = debugCanvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;

        CanvasScaler scaler = debugCanvas.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        GameObject panel = new GameObject("PathPanel");
        panel.transform.SetParent(debugCanvas.transform, false);

        Image panelImage = panel.AddComponent<Image>();
        panelImage.color = new Color(0, 0, 0, 0.8f);

        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0, 1);
        panelRect.anchorMax = new Vector2(1, 1);
        panelRect.pivot = new Vector2(0.5f, 1);
        panelRect.anchoredPosition = new Vector2(0, -10);
        panelRect.sizeDelta = new Vector2(-40, 120);

        GameObject textObj = new GameObject("PathText");
        textObj.transform.SetParent(panel.transform, false);

        pathDisplayText = textObj.AddComponent<Text>();
        pathDisplayText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        pathDisplayText.fontSize = 18;
        pathDisplayText.color = Color.white;
        pathDisplayText.alignment = TextAnchor.MiddleLeft;

        RectTransform textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(20, 10);
        textRect.offsetMax = new Vector2(-20, -10);

        UpdatePathDisplay();
    }

    void UpdatePathDisplay()
    {
        if (pathDisplayText == null) return;

        string status = isInitialized ? "✓ LOGGING ACTIVE" : "✗ LOGGING FAILED";
        string path = string.IsNullOrEmpty(logFilePath) ? "N/A" : logFilePath;

        string packageId = Application.identifier;
        pathDisplayText.text = $"[DebugFileLogger] {status}\n" +
                              $"Path: {path}\n" +
                              $"ADB: adb pull /sdcard/Android/data/{packageId}/files/ ./data/";
    }

    IEnumerator HidePathDisplayAfterDelay()
    {
        yield return new WaitForSeconds(displayDuration);

        if (debugCanvas != null)
        {
            Destroy(debugCanvas);
            debugCanvas = null;
            pathDisplayText = null;
        }
    }

    /// <summary>
    /// Show the path display again (useful for debugging).
    /// </summary>
    public void ShowPathDisplay(float duration = 10f)
    {
        if (debugCanvas != null)
        {
            Destroy(debugCanvas);
        }

        displayDuration = duration;
        CreatePathDisplay();
        StartCoroutine(HidePathDisplayAfterDelay());
    }

    void OnApplicationPause(bool pause)
    {
        if (pause)
        {
            ForceFlush();
        }
    }

    void OnApplicationQuit()
    {
        WriteSessionEnd();
        CloseLogger();
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            WriteSessionEnd();
            CloseLogger();
        }
    }

    void WriteSessionEnd()
    {
        if (!isInitialized) return;

        Log("INFO", "DebugFileLogger", "Session ending");
        FlushBuffer();

        try
        {
            logWriter.WriteLine();
            logWriter.WriteLine("================================================================================");
            logWriter.WriteLine($"Session End: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            logWriter.WriteLine($"Total Frames: {Time.frameCount}");
            logWriter.WriteLine($"Session Duration: {Time.realtimeSinceStartup:F1} seconds");
            logWriter.WriteLine("================================================================================");
            logWriter.Flush();
        }
        catch (Exception e)
        {
            // File logging may be unavailable at shutdown — fall back to Unity console.
            Debug.LogWarning($"[DebugFileLogger] Failed to write session end: {e.Message}");
        }
    }

    void CloseLogger()
    {
        if (logUnityMessages)
        {
            Application.logMessageReceived -= HandleUnityLog;
        }

        if (logWriter != null)
        {
            try
            {
                logWriter.Close();
                logWriter.Dispose();
            }
            catch (Exception e)
            {
                // File logging is being torn down — fall back to Unity console.
                Debug.LogWarning($"[DebugFileLogger] Failed to close log writer: {e.Message}");
            }
            logWriter = null;
        }

        isInitialized = false;
    }
}
