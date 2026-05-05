using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using TMPro;
using System.Collections.Generic;
using System.IO;
using EyeLean.Replay.Visualization;

namespace EyeLean.Replay.UI
{
    /// <summary>
    /// User interface for controlling eye tracking replay.
    /// Provides file selection, playback controls, and status display.
    /// </summary>
    public class ReplayUI : MonoBehaviour
    {
        #region References

        [Header("References")]
        [Tooltip("Reference to the ReplayManager (recommended)")]
        public ReplayManager replayManager;

        [Tooltip("Reference to the ReplayController (auto-found if ReplayManager set)")]
        public ReplayController replayController;

        [Tooltip("Reference to ReplayVisualizer (optional)")]
        public ReplayVisualizer visualizer;

        #endregion

        #region UI Settings

        [Header("UI Settings")]
        [Tooltip("Auto-create UI if not manually configured")]
        public bool autoCreateUI = true;

        [Tooltip("UI canvas for controls")]
        public Canvas uiCanvas;

        [Tooltip("Position of the control panel")]
        public Vector2 panelPosition = new Vector2(10, 10);

        [Tooltip("Size of the control panel")]
        public Vector2 panelSize = new Vector2(400, 380);

        #endregion

        #region UI Components

        // Auto-created UI components
        private GameObject panelObject;
        private TMP_Dropdown fileDropdown;
        private TMP_Dropdown taskDropdown;
        private Button loadButton;
        private Button playButton;
        private Button pauseButton;
        private Button stopButton;
        private Slider progressSlider;
        private Slider speedSlider;
        private TextMeshProUGUI statusText;
        private TextMeshProUGUI timeText;
        private TextMeshProUGUI currentTaskText;
        private Toggle loopToggle;

        private bool isDraggingSlider = false;
        private List<string> availableFiles = new List<string>();
        private bool dataLoaded = false;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            // Find ReplayManager first (preferred)
            if (replayManager == null)
            {
                replayManager = FindFirstObjectByType<ReplayManager>();
            }

            // Get ReplayController from ReplayManager or find directly
            if (replayController == null)
            {
                if (replayManager != null)
                {
                    replayController = replayManager.replayController;
                }
                if (replayController == null)
                {
                    replayController = FindFirstObjectByType<ReplayController>();
                }
            }

            if (visualizer == null)
            {
                visualizer = FindFirstObjectByType<ReplayVisualizer>();
            }
        }

        private void Start()
        {
            if (autoCreateUI)
            {
                CreateUI();
            }

            SubscribeToEvents();
            RefreshFileList();
            UpdateUI();
        }

        private void Update()
        {
            UpdateUI();
        }

        private void OnDestroy()
        {
            UnsubscribeFromEvents();
        }

        #endregion

        #region UI Creation

        private void CreateUI()
        {
            // Create or find canvas
            if (uiCanvas == null)
            {
                var existingCanvas = FindFirstObjectByType<Canvas>();
                if (existingCanvas != null && existingCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
                {
                    uiCanvas = existingCanvas;
                }
                else
                {
                    var canvasObj = new GameObject("ReplayUICanvas");
                    uiCanvas = canvasObj.AddComponent<Canvas>();
                    uiCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    canvasObj.AddComponent<CanvasScaler>();
                    canvasObj.AddComponent<GraphicRaycaster>();
                }
            }

            // Ensure EventSystem exists (required for UI interaction)
            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var eventSystemObj = new GameObject("EventSystem");
                eventSystemObj.AddComponent<EventSystem>();
                eventSystemObj.AddComponent<InputSystemUIInputModule>();
            }

            // Create main panel
            panelObject = CreatePanel("ReplayControlPanel", uiCanvas.transform);
            var panelRect = panelObject.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.zero;
            panelRect.pivot = Vector2.zero;
            panelRect.anchoredPosition = panelPosition;
            panelRect.sizeDelta = panelSize;

            // Add vertical layout
            var layout = panelObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 10, 10);
            layout.spacing = 8;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            // Title
            CreateLabel(panelObject.transform, "Eye Tracking Replay", 18, TextAlignmentOptions.Center);

            // File dropdown row
            var fileRow = CreateRow(panelObject.transform);
            CreateLabel(fileRow.transform, "File:", 14, TextAlignmentOptions.Left, 50);
            fileDropdown = CreateDropdown(fileRow.transform);
            var refreshBtn = CreateButton(fileRow.transform, "Refresh", 60);
            refreshBtn.onClick.AddListener(RefreshFileList);

            // Load button row
            var loadRow = CreateRow(panelObject.transform);
            loadButton = CreateButton(loadRow.transform, "Load Selected File", 0);
            var loadFitter = loadButton.GetComponent<LayoutElement>();
            loadFitter.flexibleWidth = 1;
            loadButton.onClick.AddListener(OnLoadClicked);

            // Task filter dropdown (filters by experiment phase/task)
            var taskFilterRow = CreateRow(panelObject.transform);
            CreateLabel(taskFilterRow.transform, "Task:", 14, TextAlignmentOptions.Left, 50);
            taskDropdown = CreateDropdown(taskFilterRow.transform);
            taskDropdown.onValueChanged.AddListener(OnTaskFilterChanged);

            // Playback controls row
            var controlsRow = CreateRow(panelObject.transform);
            playButton = CreateButton(controlsRow.transform, "Play", 70);
            playButton.onClick.AddListener(OnPlayClicked);

            pauseButton = CreateButton(controlsRow.transform, "Pause", 70);
            pauseButton.onClick.AddListener(OnPauseClicked);

            stopButton = CreateButton(controlsRow.transform, "Stop", 70);
            stopButton.onClick.AddListener(OnStopClicked);

            // Progress slider
            var progressRow = CreateRow(panelObject.transform);
            CreateLabel(progressRow.transform, "Progress:", 14, TextAlignmentOptions.Left, 70);
            progressSlider = CreateSlider(progressRow.transform);
            progressSlider.onValueChanged.AddListener(OnProgressChanged);

            // Track interaction so the playback-driven auto-update doesn't
            // fight a drag/click. While the slider is held, OnProgressChanged
            // is suppressed and the slider value is not overwritten by the
            // playback head; on release one final seek follows the chosen
            // position.
            var progressTracker = progressSlider.gameObject.AddComponent<SliderInteractionTracker>();
            progressTracker.InteractionStarted += () => isDraggingSlider = true;
            progressTracker.InteractionEnded += () =>
            {
                isDraggingSlider = false;
                if (replayController != null && replayController.IsReady)
                {
                    replayController.SeekToProgress(progressSlider.value);
                }
            };

            // Speed slider
            var speedRow = CreateRow(panelObject.transform);
            CreateLabel(speedRow.transform, "Speed:", 14, TextAlignmentOptions.Left, 70);
            speedSlider = CreateSlider(speedRow.transform, 0.1f, 3f, 1f);
            speedSlider.onValueChanged.AddListener(OnSpeedChanged);

            // Loop toggle
            var loopRow = CreateRow(panelObject.transform);
            CreateLabel(loopRow.transform, "Loop:", 14, TextAlignmentOptions.Left, 70);
            loopToggle = CreateToggle(loopRow.transform);
            loopToggle.onValueChanged.AddListener(OnLoopChanged);

            // Status display
            CreateLabel(panelObject.transform, "Status", 14, TextAlignmentOptions.Left);
            statusText = CreateLabel(panelObject.transform, "Not loaded", 12, TextAlignmentOptions.Left);
            statusText.color = Color.gray;

            // Time display
            timeText = CreateLabel(panelObject.transform, "Time: 0:00 / 0:00", 12, TextAlignmentOptions.Left);

            // Current task display (during playback)
            currentTaskText = CreateLabel(panelObject.transform, "Current: -", 12, TextAlignmentOptions.Left);

            // Keyboard shortcuts info
            CreateLabel(panelObject.transform, "Space=Play/Pause, Arrows=Skip", 10, TextAlignmentOptions.Center);

            // Initialize task dropdown as disabled until data loads
            taskDropdown.interactable = false;
        }

        #endregion

        #region UI Helpers

        private GameObject CreatePanel(string name, Transform parent)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);

            var rect = obj.AddComponent<RectTransform>();
            var image = obj.AddComponent<Image>();
            image.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);

            return obj;
        }

        private GameObject CreateRow(Transform parent)
        {
            var obj = new GameObject("Row");
            obj.transform.SetParent(parent, false);

            var rect = obj.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(0, 30);

            var layout = obj.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 5;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            layout.childAlignment = TextAnchor.MiddleLeft;

            var fitter = obj.AddComponent<LayoutElement>();
            fitter.preferredHeight = 30;

            return obj;
        }

        private TextMeshProUGUI CreateLabel(Transform parent, string text, int fontSize = 14,
            TextAlignmentOptions alignment = TextAlignmentOptions.Left, float width = 0)
        {
            var obj = new GameObject("Label");
            obj.transform.SetParent(parent, false);

            var rect = obj.AddComponent<RectTransform>();
            var tmp = obj.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.alignment = alignment;
            tmp.color = Color.white;

            if (width > 0)
            {
                var fitter = obj.AddComponent<LayoutElement>();
                fitter.preferredWidth = width;
                fitter.flexibleWidth = 0;
            }
            else
            {
                var fitter = obj.AddComponent<LayoutElement>();
                fitter.flexibleWidth = 1;
            }

            return tmp;
        }

        private Button CreateButton(Transform parent, string text, float width = 80)
        {
            var obj = new GameObject("Button");
            obj.transform.SetParent(parent, false);

            var rect = obj.AddComponent<RectTransform>();
            var image = obj.AddComponent<Image>();
            image.color = new Color(0.3f, 0.3f, 0.3f, 1f);

            var button = obj.AddComponent<Button>();
            var colors = button.colors;
            colors.highlightedColor = new Color(0.4f, 0.4f, 0.4f, 1f);
            colors.pressedColor = new Color(0.2f, 0.2f, 0.2f, 1f);
            button.colors = colors;

            // Text
            var textObj = new GameObject("Text");
            textObj.transform.SetParent(obj.transform, false);
            var textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;

            var tmp = textObj.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 12;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;

            var fitter = obj.AddComponent<LayoutElement>();
            fitter.preferredWidth = width;

            return button;
        }

        private Slider CreateSlider(Transform parent, float min = 0f, float max = 1f, float value = 0f)
        {
            var obj = new GameObject("Slider");
            obj.transform.SetParent(parent, false);

            var rect = obj.AddComponent<RectTransform>();
            var slider = obj.AddComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = value;

            // Background
            var bg = new GameObject("Background");
            bg.transform.SetParent(obj.transform, false);
            var bgRect = bg.AddComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0, 0.25f);
            bgRect.anchorMax = new Vector2(1, 0.75f);
            bgRect.sizeDelta = Vector2.zero;
            var bgImage = bg.AddComponent<Image>();
            bgImage.color = new Color(0.2f, 0.2f, 0.2f, 1f);

            // Fill area
            var fillArea = new GameObject("FillArea");
            fillArea.transform.SetParent(obj.transform, false);
            var fillAreaRect = fillArea.AddComponent<RectTransform>();
            fillAreaRect.anchorMin = new Vector2(0, 0.25f);
            fillAreaRect.anchorMax = new Vector2(1, 0.75f);
            fillAreaRect.sizeDelta = Vector2.zero;

            var fill = new GameObject("Fill");
            fill.transform.SetParent(fillArea.transform, false);
            var fillRect = fill.AddComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.sizeDelta = Vector2.zero;
            var fillImage = fill.AddComponent<Image>();
            fillImage.color = new Color(0.3f, 0.6f, 0.9f, 1f);

            // Handle
            var handleArea = new GameObject("HandleSlideArea");
            handleArea.transform.SetParent(obj.transform, false);
            var handleAreaRect = handleArea.AddComponent<RectTransform>();
            handleAreaRect.anchorMin = Vector2.zero;
            handleAreaRect.anchorMax = Vector2.one;
            handleAreaRect.sizeDelta = Vector2.zero;

            var handle = new GameObject("Handle");
            handle.transform.SetParent(handleArea.transform, false);
            var handleRect = handle.AddComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(20, 20);
            var handleImage = handle.AddComponent<Image>();
            handleImage.color = Color.white;

            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handleImage;

            var fitter = obj.AddComponent<LayoutElement>();
            fitter.flexibleWidth = 1;
            fitter.preferredHeight = 20;

            return slider;
        }

        private TMP_Dropdown CreateDropdown(Transform parent)
        {
            var obj = new GameObject("Dropdown");
            obj.transform.SetParent(parent, false);

            var rect = obj.AddComponent<RectTransform>();
            var image = obj.AddComponent<Image>();
            image.color = new Color(0.25f, 0.25f, 0.25f, 1f);

            var dropdown = obj.AddComponent<TMP_Dropdown>();

            // Label
            var label = new GameObject("Label");
            label.transform.SetParent(obj.transform, false);
            var labelRect = label.AddComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(10, 0);
            labelRect.offsetMax = new Vector2(-25, 0);
            var labelTmp = label.AddComponent<TextMeshProUGUI>();
            labelTmp.fontSize = 12;
            labelTmp.alignment = TextAlignmentOptions.Left;
            labelTmp.color = Color.white;

            dropdown.captionText = labelTmp;

            // Template (basic)
            var template = new GameObject("Template");
            template.transform.SetParent(obj.transform, false);
            template.SetActive(false);
            var templateRect = template.AddComponent<RectTransform>();
            templateRect.anchorMin = new Vector2(0, 0);
            templateRect.anchorMax = new Vector2(1, 0);
            templateRect.pivot = new Vector2(0.5f, 1);
            templateRect.sizeDelta = new Vector2(0, 150);
            var templateImage = template.AddComponent<Image>();
            templateImage.color = new Color(0.2f, 0.2f, 0.2f, 1f);

            var viewport = new GameObject("Viewport");
            viewport.transform.SetParent(template.transform, false);
            var viewportRect = viewport.AddComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.sizeDelta = Vector2.zero;
            viewport.AddComponent<Mask>().showMaskGraphic = false;
            viewport.AddComponent<Image>();

            var content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);
            var contentRect = content.AddComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0.5f, 1);
            contentRect.sizeDelta = new Vector2(0, 28);

            var item = new GameObject("Item");
            item.transform.SetParent(content.transform, false);
            var itemRect = item.AddComponent<RectTransform>();
            itemRect.anchorMin = new Vector2(0, 0.5f);
            itemRect.anchorMax = new Vector2(1, 0.5f);
            itemRect.sizeDelta = new Vector2(0, 28);
            var itemToggle = item.AddComponent<Toggle>();

            var itemLabel = new GameObject("ItemLabel");
            itemLabel.transform.SetParent(item.transform, false);
            var itemLabelRect = itemLabel.AddComponent<RectTransform>();
            itemLabelRect.anchorMin = Vector2.zero;
            itemLabelRect.anchorMax = Vector2.one;
            itemLabelRect.offsetMin = new Vector2(10, 0);
            itemLabelRect.offsetMax = new Vector2(-10, 0);
            var itemLabelTmp = itemLabel.AddComponent<TextMeshProUGUI>();
            itemLabelTmp.fontSize = 12;
            itemLabelTmp.alignment = TextAlignmentOptions.Left;
            itemLabelTmp.color = Color.white;

            dropdown.template = templateRect;
            dropdown.itemText = itemLabelTmp;

            var fitter = obj.AddComponent<LayoutElement>();
            fitter.flexibleWidth = 1;
            fitter.preferredHeight = 28;

            return dropdown;
        }

        private Toggle CreateToggle(Transform parent)
        {
            var obj = new GameObject("Toggle");
            obj.transform.SetParent(parent, false);

            var rect = obj.AddComponent<RectTransform>();
            var toggle = obj.AddComponent<Toggle>();

            var bg = new GameObject("Background");
            bg.transform.SetParent(obj.transform, false);
            var bgRect = bg.AddComponent<RectTransform>();
            bgRect.sizeDelta = new Vector2(20, 20);
            var bgImage = bg.AddComponent<Image>();
            bgImage.color = new Color(0.3f, 0.3f, 0.3f, 1f);

            var checkmark = new GameObject("Checkmark");
            checkmark.transform.SetParent(bg.transform, false);
            var checkRect = checkmark.AddComponent<RectTransform>();
            checkRect.anchorMin = Vector2.zero;
            checkRect.anchorMax = Vector2.one;
            checkRect.sizeDelta = new Vector2(-4, -4);
            var checkImage = checkmark.AddComponent<Image>();
            checkImage.color = new Color(0.3f, 0.7f, 0.3f, 1f);

            toggle.targetGraphic = bgImage;
            toggle.graphic = checkImage;

            var fitter = obj.AddComponent<LayoutElement>();
            fitter.preferredWidth = 20;

            return toggle;
        }

        #endregion

        #region Event Handlers

        private void SubscribeToEvents()
        {
            if (replayManager != null)
            {
                replayManager.OnDataLoaded += OnDataLoaded;
                replayManager.OnLoadError += OnLoadError;
            }

            if (replayController != null)
            {
                replayController.OnStateChanged += OnReplayStateChanged;
                replayController.OnPhaseChanged += OnPhaseChangedHandler;
            }
        }

        private void UnsubscribeFromEvents()
        {
            if (replayManager != null)
            {
                replayManager.OnDataLoaded -= OnDataLoaded;
                replayManager.OnLoadError -= OnLoadError;
            }

            if (replayController != null)
            {
                replayController.OnStateChanged -= OnReplayStateChanged;
                replayController.OnPhaseChanged -= OnPhaseChangedHandler;
            }
        }

        private void OnLoadClicked()
        {
            if (fileDropdown == null || availableFiles.Count == 0)
            {
                Debug.LogWarning("[ReplayUI] No file selected");
                return;
            }

            int index = fileDropdown.value;
            if (index >= 0 && index < availableFiles.Count)
            {
                string fileName = availableFiles[index];
                string fullPath = Path.Combine(Application.streamingAssetsPath, fileName);

                if (statusText != null)
                {
                    statusText.text = "Loading...";
                    statusText.color = Color.yellow;
                }

                if (replayManager != null)
                {
                    replayManager.LoadData(fullPath);
                }
                else if (replayController != null)
                {
                    replayController.LoadFromStreamingAssets(fileName);
                }
            }
        }

        private void OnPlayClicked()
        {
            if (replayManager != null)
            {
                replayManager.Play();
            }
            else
            {
                replayController?.Play();
            }
        }

        private void OnPauseClicked()
        {
            if (replayManager != null)
            {
                replayManager.Pause();
            }
            else
            {
                replayController?.Pause();
            }
        }

        private void OnStopClicked()
        {
            if (replayManager != null)
            {
                replayManager.Stop();
            }
            else
            {
                replayController?.Stop();
            }
        }

        private void OnProgressChanged(float value)
        {
            if (replayController != null && replayController.IsReady && !isDraggingSlider)
            {
                replayController.SeekToProgress(value);
            }
        }

        private void OnSpeedChanged(float value)
        {
            if (replayController != null)
            {
                replayController.playbackSpeed = value;
            }
        }

        private void OnLoopChanged(bool value)
        {
            if (replayController != null)
            {
                replayController.loopPlayback = value;
            }
        }

        private void OnTaskFilterChanged(int index)
        {
            if (replayManager == null || taskDropdown == null) return;

            if (index == 0)
            {
                // "All Tasks" selected - clear filter
                replayManager.SetFilter(ReplayManager.PlaybackScope.EntireRecording);
            }
            else
            {
                // Filter to specific task (phase)
                string selectedTask = replayManager.availablePhases[index - 1];
                replayManager.SetFilter(ReplayManager.PlaybackScope.SpecificPhase, selectedTask);
            }
        }

        private void OnReplayStateChanged(ReplayState state)
        {
            UpdateButtonStates(state);
        }

        private void OnDataLoaded(ReplaySession session)
        {
            dataLoaded = true;

            if (statusText != null)
            {
                statusText.text = $"Loaded: {session.totalFrames} frames, {session.totalDuration:F1}s";
                statusText.color = Color.green;
            }

            // Populate task dropdown
            PopulateTaskDropdown();

            // Enable task filter dropdown
            if (taskDropdown != null) taskDropdown.interactable = true;
        }

        private void OnLoadError(string error)
        {
            if (statusText != null)
            {
                statusText.text = $"Error: {error}";
                statusText.color = Color.red;
            }
        }

        private void OnPhaseChangedHandler(string phase)
        {
            if (currentTaskText != null)
            {
                currentTaskText.text = $"Current: {phase}";
            }
        }

        private void PopulateTaskDropdown()
        {
            if (taskDropdown == null || replayManager == null) return;

            taskDropdown.ClearOptions();

            var options = new List<string> { "All Tasks" };
            options.AddRange(replayManager.availablePhases);
            taskDropdown.AddOptions(options);
        }

        #endregion

        #region UI Updates

        private void UpdateUI()
        {
            if (replayController == null) return;

            // Update progress slider
            if (progressSlider != null && !isDraggingSlider)
            {
                progressSlider.SetValueWithoutNotify(replayController.Progress);
            }

            // Update time display
            if (timeText != null)
            {
                float current = replayController.CurrentTime;
                float total = replayController.TotalDuration;
                timeText.text = $"Time: {FormatTime(current)} / {FormatTime(total)}";
            }

            // Update current task display
            if (currentTaskText != null && !string.IsNullOrEmpty(replayController.CurrentPhase))
            {
                currentTaskText.text = $"Current: {replayController.CurrentPhase}";
            }

            // Update status
            if (statusText != null && replayController.Session == null)
            {
                statusText.text = $"State: {replayController.State}";
            }
        }

        private void UpdateButtonStates(ReplayState state)
        {
            if (playButton != null)
            {
                playButton.interactable = state == ReplayState.Ready ||
                                          state == ReplayState.Paused ||
                                          state == ReplayState.Complete;
            }

            if (pauseButton != null)
            {
                pauseButton.interactable = state == ReplayState.Playing;
            }

            if (stopButton != null)
            {
                stopButton.interactable = state == ReplayState.Playing ||
                                          state == ReplayState.Paused;
            }
        }

        private string FormatTime(float seconds)
        {
            int mins = Mathf.FloorToInt(seconds / 60f);
            int secs = Mathf.FloorToInt(seconds % 60f);
            return $"{mins}:{secs:D2}";
        }

        #endregion

        #region File Management

        public void RefreshFileList()
        {
            availableFiles.Clear();

            string streamingPath = Application.streamingAssetsPath;

            if (Directory.Exists(streamingPath))
            {
                string[] csvFiles = Directory.GetFiles(streamingPath, "*.csv");

                foreach (string file in csvFiles)
                {
                    availableFiles.Add(Path.GetFileName(file));
                }
            }

            // Update dropdown
            if (fileDropdown != null)
            {
                fileDropdown.ClearOptions();
                fileDropdown.AddOptions(availableFiles);
            }

            Debug.Log($"[ReplayUI] Found {availableFiles.Count} CSV files in StreamingAssets");
        }

        #endregion
    }
}
