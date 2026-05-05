using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using EyeTracking.Components;
using EyeTracking.Core;

/// <summary>
/// Generates a basic VR test room with static and dynamic gaze targets.
/// Simplified version focused on basic eye tracking testing.
/// Implements <see cref="EyeTracking.Core.IRoomFrameProvider"/> so
/// EyeLean.Core consumers (e.g. CalibrationTestRunner) can parent
/// scene-local content without importing the EyeLean.SampleDemo asmdef.
/// </summary>
public class EnvironmentGenerator : MonoBehaviour, EyeTracking.Core.IRoomFrameProvider
{
    [Header("Room Structure")]
    [SerializeField] private float roomLength = 6f;
    [SerializeField] private float roomWidth = 6f;
    [SerializeField] private float roomHeight = 3f;
    [SerializeField] private float wallThickness = 0.1f;

    [Header("Object Generation")]
    [SerializeField] private int staticObjectCount = 12;
    [SerializeField] private int dynamicObjectCount = 4;
    [SerializeField] private float objectSize = 0.3f;

    // Public properties for external configuration
    public int StaticObjectCount { get => staticObjectCount; set => staticObjectCount = value; }
    public int DynamicObjectCount { get => dynamicObjectCount; set => dynamicObjectCount = value; }

    // Room bounds for object positioning constraints
    public float RoomWidth => roomWidth;
    public float RoomLength => roomLength;
    public float RoomHeight => roomHeight;

    /// <summary>
    /// Room's world-space transform. Experiment task spawners (ChangeDetection
    /// / VisualSearch / Counting) parent their objects under this so their
    /// room-local positions land in the rotated+offset world frame instead of
    /// world-axis-aligned coords. Returns null before GenerateRoomStructure
    /// has run.
    /// </summary>
    public Transform RoomTransform => roomStructure != null ? roomStructure.transform : null;

    /// <summary>
    /// Convert a room-local position (X centered at 0, Z forward from 0) into
    /// world space. Falls back to identity if the room hasn't been generated
    /// yet (returns the input unchanged).
    /// </summary>
    public Vector3 RoomLocalToWorld(Vector3 localPos)
    {
        var t = RoomTransform;
        return t != null ? t.TransformPoint(localPos) : localPos;
    }
    public float MinHeight => minHeight;
    public float MaxHeight => maxHeight;
    public float MinDepth => minDepth;

    /// <summary>
    /// Get safe bounds for spawning objects (with margins from walls).
    /// </summary>
    public Bounds GetSafeSpawnBounds(float margin = 0.5f)
    {
        Vector3 center = new Vector3(0, (minHeight + maxHeight) / 2f, roomLength / 2f);
        Vector3 size = new Vector3(
            roomWidth - margin * 2,
            maxHeight - minHeight,
            roomLength - margin * 2 - minDepth
        );
        return new Bounds(center, size);
    }

    [Header("Object Distribution")]
    [SerializeField] private float minHeight = 0.5f;
    [SerializeField] private float maxHeight = 2.5f;
    [SerializeField] private float pathwayWidth = 4f;
    [SerializeField] private float minDepth = 1f;

    [Header("Dynamic Movement")]
    [SerializeField] private float moveSpeed = 1f;
    [SerializeField] private float rotationSpeed = 30f;

    [Header("Eye Tracking Setup")]
    [Tooltip("Automatically configure eye tracker on Start")]
    [SerializeField] private bool configureEyeTrackerOnStart = true;
    [Tooltip("Set coordinate origin to camera position on Start")]
    [SerializeField] private bool setCoordinateOriginOnStart = true;
    [Tooltip("Initial session phase name for test scenes (use 'Idle' or descriptive name)")]
    [SerializeField] private string initialSessionPhase = "Idle";
    [Tooltip("Reference to HMDDataCollector — owner of SetCoordinateOrigin (auto-finds if not set).")]
    [SerializeField] private HMDDataCollector hmdCollector;
    [Tooltip("Reference to SessionRecorder — owner of SetSessionContext (auto-finds if not set).")]
    [SerializeField] private SessionRecorder sessionRecorder;

    [Header("User Spawn Settings")]
    [Tooltip("Spawn room around user's starting position (VR mode)")]
    [SerializeField] private bool spawnAroundUser = true;
    [Tooltip("Show standing position marker on floor")]
    [SerializeField] private bool showStandingMarker = true;
    [Tooltip("Distance from back wall to user spawn point")]
    [SerializeField] private float userDistanceFromBackWall = 0.5f;

    // Generated objects
    private List<GameObject> staticObjects = new List<GameObject>();
    private List<GameObject> dynamicObjects = new List<GameObject>();
    private List<DynamicTarget> dynamicTargets = new List<DynamicTarget>();
    private GameObject roomStructure;
    private GameObject standingMarker;

    // User spawn position (captured at start)
    private Vector3 userSpawnPosition;
    private float userSpawnYaw; // Y-axis rotation at spawn

    // Opt-in override that bypasses CaptureUserSpawnPosition's Camera.main
    // probe and uses an externally-supplied pose instead.
    // DemoReplayBootstrapper sets this to the recorded session's first-frame
    // head pose so the replay scene's room lands at the recording's
    // hardware-world anchor and recorded gaze rays point at the same wall
    // positions. Off by default.
    private bool overrideUserSpawn;
    private Vector3 overrideUserSpawnPosition;
    private float overrideUserSpawnYaw;

    /// <summary>
    /// Inject a user-spawn pose to use instead of the live Camera.main probe.
    /// Must be called BEFORE GenerateBasicRoom; sets the spawn fields directly
    /// since GenerateBasicRoom does not re-call CaptureUserSpawnPosition.
    /// </summary>
    public void SetUserSpawnOverride(Vector3 worldPosition, float yawDegrees)
    {
        overrideUserSpawn = true;
        overrideUserSpawnPosition = new Vector3(worldPosition.x, 0f, worldPosition.z);
        overrideUserSpawnYaw = yawDegrees;
        userSpawnPosition = overrideUserSpawnPosition;
        userSpawnYaw = overrideUserSpawnYaw;
    }

    public void ClearUserSpawnOverride()
    {
        overrideUserSpawn = false;
    }

    void Start()
    {
        // Wait for XR initialization before capturing position. Under replay
        // the auto-generated room is REGENERATED by DemoReplayBootstrapper on
        // OnLoadComplete using the recorded coord-origin (via
        // SetUserSpawnOverride + GenerateBasicRoom). GenerateBasicRoom calls
        // ClearEnvironment first, so the duplicate generation is harmless and
        // the final result is correctly anchored.
        StartCoroutine(InitializeWithDelay());
    }

    /// <summary>
    /// Wait for XR system to initialize before capturing user position and generating room.
    /// </summary>
    private IEnumerator InitializeWithDelay()
    {
        Debug.Log("[EnvironmentGenerator] Waiting for VR camera to begin tracking...");

        // VRReadinessService polls until camPos.y > 0.5f && magnitude > 0.1f
        // (otherwise an OpenXR cold-start that runs longer than a fixed wait
        // captures the spawn at floor level), capped at 5s. Continues on
        // timeout so the room still generates with a fallback.
        var readiness = VRReadinessService.Instance;
        if (readiness != null) yield return readiness.WaitForCameraReady(5f);
        else { yield return null; yield return null; yield return new WaitForSeconds(0.5f); }

        // Apply ambient lighting BEFORE any geometry is built so wall/floor
        // materials sample the trilight ambient on first instantiation.
        ConfigureAmbientLighting();

        // Capture user's spawn position
        CaptureUserSpawnPosition();

        ClearEnvironment();
        GenerateBasicRoom();
        Debug.Log($"[EnvironmentGenerator] Basic room generated around user at {userSpawnPosition}");

        // Configure eye tracker for test scenes
        if (configureEyeTrackerOnStart)
        {
            ConfigureEyeTracker();
        }
    }

    /// <summary>
    /// Capture the user's position and facing direction at app start; the
    /// room is generated around this position. Yaw is derived from
    /// atan2(forward.x, forward.z) on the XZ-projected forward vector — head-
    /// tilt independent. Reading eulerAngles.y instead would gimbal-flip when
    /// the head is pitched or rolled at startup and rotate the entire room.
    /// </summary>
    private void CaptureUserSpawnPosition()
    {
        if (overrideUserSpawn)
        {
            userSpawnPosition = overrideUserSpawnPosition;
            userSpawnYaw = overrideUserSpawnYaw;
            Debug.Log($"[EnvironmentGenerator] Using override user spawn: position={userSpawnPosition}, yaw={userSpawnYaw:F2}° (set by DemoReplayBootstrapper or equivalent)");
            return;
        }
        Camera mainCamera = Camera.main;
        if (mainCamera != null && mainCamera.transform.position.sqrMagnitude > 0.01f)
        {
            // Floor-level XZ.
            userSpawnPosition = new Vector3(
                mainCamera.transform.position.x,
                0f,
                mainCamera.transform.position.z
            );

            // Robust yaw: project forward to XZ plane and take atan2.
            // Independent of head pitch/roll, so a tilted head at startup
            // doesn't skew the room. atan2 returns radians; convert to deg.
            // If forward is nearly straight up/down (degenerate; user looking
            // at the floor or ceiling), fall back to 0° yaw rather than
            // producing NaN.
            Vector3 fwd = mainCamera.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f)
            {
                userSpawnYaw = 0f;
                Debug.LogWarning("[EnvironmentGenerator] Camera looking nearly straight up/down at spawn; defaulting yaw to 0°.");
            }
            else
            {
                fwd.Normalize();
                userSpawnYaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            }

            Debug.Log($"[EnvironmentGenerator] Captured user spawn: position={userSpawnPosition}, yaw={userSpawnYaw:F2}° (head-tilt-independent)");
        }
        else
        {
            // Fallback to world origin - camera not found or at origin
            userSpawnPosition = Vector3.zero;
            userSpawnYaw = 0f;
            Debug.LogWarning($"[EnvironmentGenerator] Camera not ready (pos={mainCamera?.transform.position}), using world origin");
        }
    }

    /// <summary>
    /// Configure the eye tracker with test-scene defaults. Calls
    /// HMDDataCollector for SetCoordinateOrigin and SessionRecorder for
    /// SetSessionContext.
    /// </summary>
    private void ConfigureEyeTracker()
    {
        if (hmdCollector == null) hmdCollector = FindFirstObjectByType<HMDDataCollector>();
        if (sessionRecorder == null) sessionRecorder = FindFirstObjectByType<SessionRecorder>();

        if (sessionRecorder != null && !string.IsNullOrEmpty(initialSessionPhase))
        {
            sessionRecorder.SetSessionContext(0, initialSessionPhase, "EnvironmentTest");
            Debug.Log($"[EnvironmentGenerator] Set session context: phase={initialSessionPhase}");
        }
        else if (sessionRecorder == null)
        {
            Debug.LogWarning("[EnvironmentGenerator] SessionRecorder not found - session context will not be set on the CSV stream.");
        }

        if (hmdCollector != null && setCoordinateOriginOnStart)
        {
            bool ok = hmdCollector.SetCoordinateOrigin();
            Debug.Log(ok
                ? "[EnvironmentGenerator] Set coordinate origin to camera position"
                : "[EnvironmentGenerator] HMDDataCollector.SetCoordinateOrigin failed (no live camera).");
        }
        else if (hmdCollector == null)
        {
            Debug.LogWarning("[EnvironmentGenerator] HMDDataCollector not found - coordinate origin will not be set.");
        }

        Debug.Log("[EnvironmentGenerator] Eye tracker configured for test scene");
    }

    /// <summary>
    /// Generate only the room structure (walls, floor, ceiling).
    /// </summary>
    public void GenerateBasicRoom()
    {
        ClearTestObjects();
        GenerateRoomStructure();
        Debug.Log("[EnvironmentGenerator] Basic room structure generated");
    }

    /// <summary>
    /// Generate complete test environment with static and dynamic objects.
    /// </summary>
    public void GenerateTestEnvironment()
    {
        EyeLean.SceneState.SceneEventRecorder.RecordKV("SpawnTestEnvironment", "",
            ("static", staticObjectCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("dynamic", dynamicObjectCount.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        ClearTestObjects();
        GenerateStaticObjects();
        GenerateDynamicObjects();
        Debug.Log($"[EnvironmentGenerator] Test environment generated: {staticObjects.Count} static, {dynamicObjects.Count} dynamic objects");
    }

    /// <summary>
    /// Apply the calm-research-room palette + lighting: subdued matte tones
    /// under a soft trilight ambient + low-intensity warm directional. Called
    /// once per scene from InitializeWithDelay before GenerateRoomStructure.
    /// </summary>
    private void ConfigureAmbientLighting()
    {
        try
        {
            UnityEngine.RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            UnityEngine.RenderSettings.ambientSkyColor      = new Color(0.55f, 0.55f, 0.58f);
            UnityEngine.RenderSettings.ambientEquatorColor  = new Color(0.42f, 0.42f, 0.45f);
            UnityEngine.RenderSettings.ambientGroundColor   = new Color(0.22f, 0.22f, 0.25f);
            UnityEngine.RenderSettings.ambientIntensity     = 1f;
            // Soften any directional light already in the scene rather than
            // replacing it — preserves the scene author's placement.
            var lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
            foreach (var l in lights)
            {
                if (l.type != LightType.Directional) continue;
                l.intensity = Mathf.Min(l.intensity, 0.55f);
                l.color = new Color(1.00f, 0.96f, 0.90f);    // warm tungsten-tinted
                l.shadows = LightShadows.Soft;
                l.shadowStrength = 0.55f;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[EnvironmentGenerator] ConfigureAmbientLighting failed: {e.Message}");
        }
    }

    /// <summary>
    /// Build a wall/floor/ceiling material via VRMaterialProvider (Unlit/Color).
    /// URP/Lit is NOT viable here: on Android the build pipeline strips
    /// shaders that aren't in Always Included Shaders, leaving the Material
    /// with a non-null but broken shader object and surfaces render magenta
    /// or invisible. Unlit/Color is in Always Included and renders reliably.
    /// Per-surface tone differences (floor / ceiling / walls) substitute for
    /// real ambient occlusion. ConfigureAmbientLighting still runs for any
    /// downstream lit objects added later.
    /// </summary>
    private Material CreateLitMaterial(Color color, float smoothness = 0.15f)
    {
        return VRMaterialProvider.GetMaterial(color);
    }

    /// <summary>
    /// Creates the physical room structure using tinted primitives for reliable rendering.
    /// Room is generated around the user's spawn position when spawnAroundUser is enabled.
    /// </summary>
    void GenerateRoomStructure()
    {
        Debug.Log("[EnvironmentGenerator] GenerateRoomStructure() starting...");

        if (roomStructure != null)
        {
            DestroyImmediate(roomStructure);
        }

        roomStructure = new GameObject("RoomStructure");

        // Calculate offset so user is at userDistanceFromBackWall from the back wall
        // In local room coordinates: back wall is at z=0, user should be at z=userDistanceFromBackWall
        // So we offset the room structure so that position (0, 0, userDistanceFromBackWall) aligns with userSpawnPosition
        Vector3 roomOffset = Vector3.zero;
        Quaternion roomRotation = Quaternion.identity;

        if (spawnAroundUser)
        {
            // Rotate room to match user's facing direction
            roomRotation = Quaternion.Euler(0, userSpawnYaw, 0);

            // Offset so the user's spawn point (in local room space at z=userDistanceFromBackWall)
            // aligns with their actual world position
            Vector3 localUserPosition = new Vector3(0, 0, userDistanceFromBackWall);
            Vector3 rotatedLocalPosition = roomRotation * localUserPosition;
            roomOffset = userSpawnPosition - rotatedLocalPosition;

            Debug.Log($"[EnvironmentGenerator] Room offset: {roomOffset}, rotation: {userSpawnYaw}°");
        }

        // Apply transform to room structure
        roomStructure.transform.position = roomOffset;
        roomStructure.transform.rotation = roomRotation;

        float roomCenterZ = roomLength / 2f;

        // Wall / floor / ceiling colors are warm-neutral so calibration target
        // colors (red / blue / green) still pop against them.

        // Floor + ceiling extend to (roomWidth + 2*wallThickness) x
        // (roomLength + 2*wallThickness) to cover the wider footprint created
        // by the overlapping wall slabs below. Without this the floor/ceiling
        // visibly stop short of the side-wall outer faces.
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Floor";
        floor.transform.parent = roomStructure.transform;
        floor.transform.localPosition = new Vector3(0, -wallThickness / 2, roomCenterZ);
        floor.transform.localScale = new Vector3(roomWidth + 2f * wallThickness, wallThickness, roomLength + 2f * wallThickness);
        floor.GetComponent<Renderer>().material = CreateLitMaterial(new Color(0.42f, 0.40f, 0.37f), smoothness: 0.05f); // warm matte gray

        GameObject ceiling = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ceiling.name = "Ceiling";
        ceiling.transform.parent = roomStructure.transform;
        ceiling.transform.localPosition = new Vector3(0, roomHeight + wallThickness / 2, roomCenterZ);
        ceiling.transform.localScale = new Vector3(roomWidth + 2f * wallThickness, wallThickness, roomLength + 2f * wallThickness);
        ceiling.GetComponent<Renderer>().material = CreateLitMaterial(new Color(0.78f, 0.76f, 0.72f)); // light cream

        // Walls extend in every axis by wallThickness so they overlap
        // adjacent walls horizontally AND floor/ceiling vertically. The back
        // wall, for example, extends from y=-t to y=h+t.
        float fullWidth = roomWidth + 2f * wallThickness;
        float fullLength = roomLength + 2f * wallThickness;
        float fullHeight = roomHeight + 2f * wallThickness;

        // Generous corner overlap on BOTH dimensions: back/front walls extend
        // 2*wallThickness past each side, side walls extend 2*wallThickness
        // past each end. Each pair of adjacent walls' meshes coexist inside
        // the corner cuboid, but their user-facing inner faces are
        // perpendicular and don't z-fight. A flush-edge contact (e.g. side-
        // wall end meeting back-wall outer plane exactly) renders as a
        // visible light-colored sliver under URP shadow cascades +
        // screen-space AA, even when geometrically airtight.
        float backFrontExtend = 2f * wallThickness;
        float sideExtend = 2f * wallThickness;
        GameObject backWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        backWall.name = "BackWall";
        backWall.transform.parent = roomStructure.transform;
        backWall.transform.localPosition = new Vector3(0, roomHeight / 2, -wallThickness / 2);
        backWall.transform.localScale = new Vector3(roomWidth + 2f * backFrontExtend, fullHeight, wallThickness);
        backWall.GetComponent<Renderer>().material = CreateLitMaterial(new Color(0.46f, 0.55f, 0.55f)); // muted teal

        GameObject frontWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        frontWall.name = "FrontWall";
        frontWall.transform.parent = roomStructure.transform;
        frontWall.transform.localPosition = new Vector3(0, roomHeight / 2, roomLength + wallThickness / 2);
        frontWall.transform.localScale = new Vector3(roomWidth + 2f * backFrontExtend, fullHeight, wallThickness);
        frontWall.GetComponent<Renderer>().material = CreateLitMaterial(new Color(0.66f, 0.62f, 0.55f)); // warm beige

        GameObject leftWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        leftWall.name = "LeftWall";
        leftWall.transform.parent = roomStructure.transform;
        leftWall.transform.localPosition = new Vector3(-roomWidth / 2 - wallThickness / 2, roomHeight / 2, roomCenterZ);
        leftWall.transform.localScale = new Vector3(wallThickness, fullHeight, roomLength + 2f * sideExtend);
        leftWall.GetComponent<Renderer>().material = CreateLitMaterial(new Color(0.70f, 0.68f, 0.65f)); // light warm gray

        GameObject rightWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rightWall.name = "RightWall";
        rightWall.transform.parent = roomStructure.transform;
        rightWall.transform.localPosition = new Vector3(roomWidth / 2 + wallThickness / 2, roomHeight / 2, roomCenterZ);
        rightWall.transform.localScale = new Vector3(wallThickness, fullHeight, roomLength + 2f * sideExtend);
        rightWall.GetComponent<Renderer>().material = CreateLitMaterial(new Color(0.70f, 0.68f, 0.65f)); // matches left

        // Create standing position marker
        if (showStandingMarker)
        {
            CreateStandingMarker();
        }

        // Decorative props: away from the standing marker + the calibrator's
        // target spawn lane (centered front-of-user) so they don't interfere
        // with calibration / change-detection trials. Just a couple of low-
        // poly cues so the room reads as "research office," not "Unity demo."
        CreateDecorativeProps();
    }

    /// <summary>
    /// Spawn a small set of decorative props for room ambiance. All props sit
    /// along the side walls + corner, clear of the front-of-user lane so they
    /// don't interfere with the calibration target spawn area.
    /// </summary>
    private void CreateDecorativeProps()
    {
        // Left-wall "desk": low rectangular prism, dark wood tone.
        GameObject desk = GameObject.CreatePrimitive(PrimitiveType.Cube);
        desk.name = "Decor_Desk";
        desk.transform.parent = roomStructure.transform;
        desk.transform.localPosition = new Vector3(-roomWidth / 2 + 0.4f, 0.4f, roomLength * 0.65f);
        desk.transform.localScale = new Vector3(0.7f, 0.78f, 1.4f);
        var deskCol = desk.GetComponent<Collider>();
        if (deskCol != null) DestroyImmediate(deskCol);
        desk.GetComponent<Renderer>().material = CreateLitMaterial(new Color(0.34f, 0.27f, 0.22f), smoothness: 0.20f);

        // Right-back-corner "plant": cylindrical pot + sphere foliage. Pushed
        // inward to give the foliage sphere (radius 0.275) ~0.3m clearance
        // from the right wall's inner face — less clearance reads as visual
        // clipping on the foliage silhouette through the headset.
        GameObject pot = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pot.name = "Decor_PlantPot";
        pot.transform.parent = roomStructure.transform;
        pot.transform.localPosition = new Vector3(roomWidth / 2 - 0.65f, 0.18f, roomLength * 0.85f);
        pot.transform.localScale = new Vector3(0.32f, 0.18f, 0.32f);
        var potCol = pot.GetComponent<Collider>();
        if (potCol != null) DestroyImmediate(potCol);
        pot.GetComponent<Renderer>().material = CreateLitMaterial(new Color(0.45f, 0.32f, 0.25f), smoothness: 0.10f);

        GameObject foliage = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        foliage.name = "Decor_PlantFoliage";
        foliage.transform.parent = roomStructure.transform;
        foliage.transform.localPosition = new Vector3(roomWidth / 2 - 0.65f, 0.55f, roomLength * 0.85f);
        foliage.transform.localScale = new Vector3(0.55f, 0.55f, 0.55f);
        var folCol = foliage.GetComponent<Collider>();
        if (folCol != null) DestroyImmediate(folCol);
        foliage.GetComponent<Renderer>().material = CreateLitMaterial(new Color(0.35f, 0.48f, 0.28f), smoothness: 0.05f);

        // Right-wall "poster": flat thin rectangle with a soft accent color.
        GameObject poster = GameObject.CreatePrimitive(PrimitiveType.Cube);
        poster.name = "Decor_Poster";
        poster.transform.parent = roomStructure.transform;
        poster.transform.localPosition = new Vector3(roomWidth / 2 - wallThickness, 1.55f, roomLength * 0.45f);
        poster.transform.localScale = new Vector3(0.04f, 0.7f, 0.5f);
        var posCol = poster.GetComponent<Collider>();
        if (posCol != null) DestroyImmediate(posCol);
        poster.GetComponent<Renderer>().material = CreateLitMaterial(new Color(0.62f, 0.50f, 0.38f), smoothness: 0.15f);
    }

    /// <summary>
    /// Creates a visual marker on the floor showing where the participant should stand.
    /// Simple target pattern with concentric circles.
    /// </summary>
    void CreateStandingMarker()
    {
        if (standingMarker != null)
        {
            DestroyImmediate(standingMarker);
        }

        standingMarker = new GameObject("StandingMarker");
        standingMarker.transform.parent = roomStructure.transform;

        // Position at user spawn point (in local room coordinates)
        standingMarker.transform.localPosition = new Vector3(0, 0.01f, userDistanceFromBackWall);

        // Create outer circle (bright green ring)
        GameObject outerCircle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        outerCircle.name = "OuterRing";
        outerCircle.transform.parent = standingMarker.transform;
        outerCircle.transform.localPosition = Vector3.zero;
        outerCircle.transform.localScale = new Vector3(0.8f, 0.005f, 0.8f);
        Collider outerCollider = outerCircle.GetComponent<Collider>();
        if (outerCollider != null) DestroyImmediate(outerCollider);
        outerCircle.GetComponent<Renderer>().material = VRMaterialProvider.GetMaterial(new Color(0.2f, 0.9f, 0.2f));

        // Middle ring (white)
        GameObject middleCircle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        middleCircle.name = "MiddleRing";
        middleCircle.transform.parent = standingMarker.transform;
        middleCircle.transform.localPosition = new Vector3(0, 0.006f, 0);
        middleCircle.transform.localScale = new Vector3(0.6f, 0.005f, 0.6f);
        Collider middleCollider = middleCircle.GetComponent<Collider>();
        if (middleCollider != null) DestroyImmediate(middleCollider);
        middleCircle.GetComponent<Renderer>().material = VRMaterialProvider.GetMaterial(Color.white);

        // Inner circle (green center dot)
        GameObject innerCircle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        innerCircle.name = "InnerDot";
        innerCircle.transform.parent = standingMarker.transform;
        innerCircle.transform.localPosition = new Vector3(0, 0.007f, 0);
        innerCircle.transform.localScale = new Vector3(0.3f, 0.005f, 0.3f);
        Collider innerCollider = innerCircle.GetComponent<Collider>();
        if (innerCollider != null) DestroyImmediate(innerCollider);
        innerCircle.GetComponent<Renderer>().material = VRMaterialProvider.GetMaterial(new Color(0.1f, 0.7f, 0.1f));

        Debug.Log($"[EnvironmentGenerator] Standing marker (target pattern) created at local position (0, 0, {userDistanceFromBackWall})");
    }

    /// <summary>
    /// Generate static gaze targets distributed in the room.
    /// </summary>
    void GenerateStaticObjects()
    {
        int nearCount = staticObjectCount / 3;
        int midCount = staticObjectCount / 3;
        int farCount = staticObjectCount - nearCount - midCount;

        GenerateObjectsInDepthZone(nearCount, minDepth, 2f, "Static_Near", false);
        GenerateObjectsInDepthZone(midCount, 2f, 4f, "Static_Mid", false);
        GenerateObjectsInDepthZone(farCount, 4f, roomLength - 0.5f, "Static_Far", false);
    }

    /// <summary>
    /// Generate dynamic (moving) gaze targets.
    /// </summary>
    void GenerateDynamicObjects()
    {
        for (int i = 0; i < dynamicObjectCount; i++)
        {
            Vector3 position = GetRandomPositionInPathway();
            GameObject dynamicObj = CreateTargetObject(position, $"Dynamic_{i}", true);

            DynamicTarget dynamicTarget = dynamicObj.AddComponent<DynamicTarget>();
            MovementPattern pattern = (MovementPattern)(i % System.Enum.GetValues(typeof(MovementPattern)).Length);
            dynamicTarget.Initialize(pattern, moveSpeed, rotationSpeed, roomLength);

            dynamicObjects.Add(dynamicObj);
            dynamicTargets.Add(dynamicTarget);
        }
    }

    void GenerateObjectsInDepthZone(int count, float minZ, float maxZ, string namePrefix, bool isDynamic)
    {
        for (int i = 0; i < count; i++)
        {
            Vector3 position = GetRandomPositionInDepthZone(minZ, maxZ);
            GameObject obj = CreateTargetObject(position, $"{namePrefix}_{i}", isDynamic);

            if (!isDynamic)
            {
                staticObjects.Add(obj);
            }
        }
    }

    /// <summary>
    /// Creates a target object with collider and gaze feedback.
    /// </summary>
    GameObject CreateTargetObject(Vector3 position, string objectName, bool isDynamic)
    {
        GameObject obj = new GameObject(objectName);
        obj.transform.position = position;
        obj.transform.localScale = Vector3.one * objectSize;

        MeshFilter meshFilter = obj.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = obj.AddComponent<MeshRenderer>();

        bool isSphere = Random.value > 0.5f;

        if (isSphere)
        {
            meshFilter.mesh = CreateSphereMesh();
            SphereCollider collider = obj.AddComponent<SphereCollider>();
            collider.radius = 0.5f;
        }
        else
        {
            meshFilter.mesh = CreateCubeMesh();
            BoxCollider collider = obj.AddComponent<BoxCollider>();
            collider.size = Vector3.one;
        }

        GazeTarget gazeTarget = obj.AddComponent<GazeTarget>();
        gazeTarget.Initialize(isDynamic);

        Color objectColor = isDynamic ? new Color(1f, 0f, 0f, 0.7f) : new Color(0f, 0f, 1f, 0.7f);
        meshRenderer.material = GetMaterial(objectColor, true);  // transparent = true for gaze targets

        // Opt the gaze target into the scene-state sidecar via the
        // "Recordable" tag (defined in TagManager.asset). SceneStateRecorder
        // picks it up via OrTag discovery + runtime rescan. Try/catch in case
        // the tag wasn't added to a downstream researcher's project.
        try { obj.tag = "Recordable"; } catch (UnityException) { /* tag not defined; recording disabled for this scene */ }

        // Seed the Recordable id deterministically from the spawn name
        // (MD5-derived: same name -> same GUID across sessions and machines).
        // Without it, every session mints fresh GUIDs and the recording's ids
        // don't line up with a replay scene's ids — SceneStateReplayer would
        // fall through to placeholders.
        EyeLean.SceneState.SceneStateRecorder.MarkRecordableSeeded(obj, objectName);

        return obj;
    }

    /// <summary>
    /// Gets a material with the specified color using VRMaterialProvider.
    /// For primitives, prefer using VRMaterialProvider.TintPrimitiveMaterial() instead.
    /// </summary>
    Material GetMaterial(Color color, bool transparent)
    {
        return VRMaterialProvider.GetMaterial(color, transparent);
    }

    Vector3 GetRandomPositionInDepthZone(float minZ, float maxZ)
    {
        float x = Random.Range(-pathwayWidth / 2, pathwayWidth / 2);
        float z = Random.Range(minZ, maxZ);
        float y = Random.Range(minHeight, maxHeight);
        return new Vector3(x, y, z);
    }

    Vector3 GetRandomPositionInPathway()
    {
        float x = Random.Range(-pathwayWidth / 2, pathwayWidth / 2);
        float z = Random.Range(minDepth, roomLength - 0.5f);
        float y = Random.Range(minHeight, maxHeight);
        return new Vector3(x, y, z);
    }

    /// <summary>
    /// Clear all generated objects including room.
    /// </summary>
    public void ClearEnvironment()
    {
        ClearTestObjects();

        if (standingMarker != null)
        {
            DestroyImmediate(standingMarker);
            standingMarker = null;
        }

        if (roomStructure != null)
        {
            DestroyImmediate(roomStructure);
            roomStructure = null;
        }
    }

    /// <summary>
    /// Clear test objects but keep room structure.
    /// </summary>
    public void ClearTestObjects()
    {
        // Skip the recording event when there's nothing to clear: defensive
        // pre-spawn cleanup calls would otherwise emit phantom rows.
        if (staticObjects.Count > 0 || dynamicObjects.Count > 0)
        {
            EyeLean.SceneState.SceneEventRecorder.Record("ClearTestObjects");
        }
        foreach (GameObject obj in staticObjects)
        {
            if (obj != null) DestroyImmediate(obj);
        }
        staticObjects.Clear();

        foreach (GameObject obj in dynamicObjects)
        {
            if (obj != null) DestroyImmediate(obj);
        }
        dynamicObjects.Clear();
        dynamicTargets.Clear();
    }

    Mesh CreateSphereMesh()
    {
        GameObject tempSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Mesh sphereMesh = tempSphere.GetComponent<MeshFilter>().mesh;

        Mesh meshCopy = new Mesh();
        meshCopy.vertices = sphereMesh.vertices;
        meshCopy.triangles = sphereMesh.triangles;
        meshCopy.normals = sphereMesh.normals;
        meshCopy.uv = sphereMesh.uv;
        meshCopy.name = "SphereMesh";

        DestroyImmediate(tempSphere);
        return meshCopy;
    }

    Mesh CreateCubeMesh()
    {
        GameObject tempCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Mesh cubeMesh = tempCube.GetComponent<MeshFilter>().mesh;

        Mesh meshCopy = new Mesh();
        meshCopy.vertices = cubeMesh.vertices;
        meshCopy.triangles = cubeMesh.triangles;
        meshCopy.normals = cubeMesh.normals;
        meshCopy.uv = cubeMesh.uv;
        meshCopy.name = "CubeMesh";

        DestroyImmediate(tempCube);
        return meshCopy;
    }

    /// <summary>
    /// Editor utility to regenerate environment.
    /// </summary>
    [ContextMenu("Generate Test Environment")]
    public void RegenerateTestEnvironment()
    {
        GenerateTestEnvironment();
    }

    [ContextMenu("Generate Basic Room Only")]
    public void RegenerateBasicRoom()
    {
        GenerateBasicRoom();
    }
}
