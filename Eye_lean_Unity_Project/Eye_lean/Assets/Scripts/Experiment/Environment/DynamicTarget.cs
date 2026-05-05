using UnityEngine;

/// <summary>
/// Controls movement behavior for dynamic gaze targets.
/// Supports multiple movement patterns for testing different aspects of eye tracking.
/// </summary>
public class DynamicTarget : MonoBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private MovementPattern movementPattern = MovementPattern.Linear;
    [SerializeField] private float moveSpeed = 1f;
    [SerializeField] private float rotationSpeed = 30f;

    [Header("Movement Boundaries")]
    [SerializeField] private float movementRadius = 2f;
    [SerializeField] private float roomBoundary = 5f;

    [Header("Pattern-Specific Settings")]
    [SerializeField] private float oscillationFrequency = 1f;
    [SerializeField] private float randomChangeInterval = 2f;
    [SerializeField] private float spiralAmplitude = 1f;

    // Movement state
    private Vector3 startPosition;
    private Vector3 movementCenter;
    private float timer = 0f;
    private float randomTimer = 0f;

    // Pattern-specific
    private float phaseOffset;
    private Vector3 randomDirection;

    /// <summary>
    /// Initialize the dynamic target with movement parameters.
    /// Called by EnvironmentGenerator when creating objects.
    /// </summary>
    public void Initialize(MovementPattern pattern, float speed, float rotSpeed, float roomSize)
    {
        movementPattern = pattern;
        moveSpeed = speed;
        rotationSpeed = rotSpeed;
        roomBoundary = roomSize;

        startPosition = transform.position;
        movementCenter = startPosition;

        phaseOffset = Random.Range(0f, Mathf.PI * 2f);
        randomDirection = Random.onUnitSphere;
        randomDirection.y = Mathf.Abs(randomDirection.y);

        SetInitialTarget();

        Debug.Log($"[DynamicTarget] {gameObject.name} initialized with {pattern} pattern");
    }

    void Update()
    {
        timer += Time.deltaTime;

        switch (movementPattern)
        {
            case MovementPattern.Linear:
                UpdateLinearMovement();
                break;
            case MovementPattern.Circular:
                UpdateCircularMovement();
                break;
            case MovementPattern.Figure8:
                UpdateFigure8Movement();
                break;
            case MovementPattern.Random:
                UpdateRandomMovement();
                break;
            case MovementPattern.Oscillating:
                UpdateOscillatingMovement();
                break;
            case MovementPattern.Spiral:
                UpdateSpiralMovement();
                break;
        }

        transform.Rotate(0, rotationSpeed * Time.deltaTime, 0);
        EnforceRoomBoundaries();
    }

    /// <summary>
    /// Linear back-and-forth movement - ideal for testing basic smooth pursuit.
    /// </summary>
    void UpdateLinearMovement()
    {
        float distancePerSecond = moveSpeed;
        float totalDistance = movementRadius * 2f;
        float cycleDuration = totalDistance / distancePerSecond;

        float cycleProgress = (timer % cycleDuration) / cycleDuration;
        float smoothProgress = Mathf.Sin(cycleProgress * Mathf.PI * 2f - Mathf.PI * 0.5f) * 0.5f + 0.5f;

        Vector3 direction = new Vector3(1f, 0f, 0f);
        Vector3 targetPos = movementCenter + direction * movementRadius * (smoothProgress * 2f - 1f);

        transform.position = targetPos;

        Vector3 movementDirection = Vector3.right * Mathf.Sign(Mathf.Cos(cycleProgress * Mathf.PI * 2f));
        if (movementDirection.magnitude > 0.1f)
        {
            transform.rotation = Quaternion.LookRotation(movementDirection);
        }
    }

    /// <summary>
    /// Circular movement - tests continuous smooth pursuit tracking.
    /// </summary>
    void UpdateCircularMovement()
    {
        float angularVelocity = moveSpeed / movementRadius;
        float angle = timer * angularVelocity + phaseOffset;

        Vector3 offset = new Vector3(
            Mathf.Cos(angle) * movementRadius,
            Mathf.Sin(angle * 0.2f) * 0.1f,
            Mathf.Sin(angle) * movementRadius * 0.7f
        );

        transform.position = movementCenter + offset;

        if (moveSpeed > 0.1f)
        {
            Vector3 velocity = new Vector3(
                -Mathf.Sin(angle) * angularVelocity * movementRadius,
                0f,
                Mathf.Cos(angle) * angularVelocity * movementRadius * 0.7f
            );

            if (velocity.magnitude > 0.1f)
            {
                transform.rotation = Quaternion.LookRotation(velocity.normalized);
            }
        }
    }

    /// <summary>
    /// Figure-8 pattern - tests complex smooth pursuit with direction changes.
    /// </summary>
    void UpdateFigure8Movement()
    {
        float t = timer * moveSpeed + phaseOffset;
        Vector3 offset = new Vector3(
            Mathf.Sin(t) * movementRadius,
            Mathf.Sin(t * 0.5f) * 0.2f,
            Mathf.Sin(2f * t) * movementRadius * 0.5f
        );

        transform.position = movementCenter + offset;
    }

    /// <summary>
    /// Random movement with pauses - tests saccadic tracking and fixation.
    /// </summary>
    void UpdateRandomMovement()
    {
        randomTimer += Time.deltaTime;

        if (randomTimer >= randomChangeInterval)
        {
            randomTimer = 0f;
            randomDirection = Random.onUnitSphere;
            randomDirection.y = Mathf.Abs(randomDirection.y * 0.3f);

            if (Random.value < 0.3f)
            {
                moveSpeed = 0.1f;
            }
            else
            {
                moveSpeed = Random.Range(0.5f, 2f);
            }
        }

        Vector3 targetPos = movementCenter + randomDirection * movementRadius;
        transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);
    }

    /// <summary>
    /// Side-to-side oscillation - tests predictable smooth pursuit.
    /// </summary>
    void UpdateOscillatingMovement()
    {
        float oscillation = Mathf.Sin(timer * oscillationFrequency + phaseOffset);
        Vector3 offset = new Vector3(
            oscillation * movementRadius,
            Mathf.Sin(timer * oscillationFrequency * 0.3f) * 0.2f,
            0f
        );

        transform.position = movementCenter + offset;
    }

    /// <summary>
    /// Spiral movement - tests tracking with changing distance/depth.
    /// </summary>
    void UpdateSpiralMovement()
    {
        float angle = timer * moveSpeed + phaseOffset;
        float radius = spiralAmplitude * (1f + 0.5f * Mathf.Sin(timer * 0.5f));

        Vector3 offset = new Vector3(
            Mathf.Cos(angle) * radius,
            Mathf.Sin(angle * 0.7f) * 0.3f,
            Mathf.Sin(angle) * radius
        );

        transform.position = movementCenter + offset;
    }

    void SetInitialTarget()
    {
        if (movementPattern == MovementPattern.Random)
        {
            randomDirection = Random.onUnitSphere;
            randomDirection.y = Mathf.Abs(randomDirection.y * 0.3f);
        }
    }

    /// <summary>
    /// Ensures the object stays within the room boundaries.
    /// </summary>
    void EnforceRoomBoundaries()
    {
        Vector3 pos = transform.position;
        bool needsAdjustment = false;

        if (Mathf.Abs(pos.x) > roomBoundary / 2f - 0.5f)
        {
            pos.x = Mathf.Clamp(pos.x, -roomBoundary / 2f + 0.5f, roomBoundary / 2f - 0.5f);
            needsAdjustment = true;
        }

        if (pos.z < 0.5f || pos.z > roomBoundary - 0.5f)
        {
            pos.z = Mathf.Clamp(pos.z, 0.5f, roomBoundary - 0.5f);
            needsAdjustment = true;
        }

        if (pos.y < 0.3f || pos.y > 2.7f)
        {
            pos.y = Mathf.Clamp(pos.y, 0.3f, 2.7f);
            needsAdjustment = true;
        }

        if (needsAdjustment)
        {
            transform.position = pos;
            movementCenter = pos;
        }
    }

    /// <summary>
    /// Change movement pattern during runtime.
    /// </summary>
    public void ChangeMovementPattern(MovementPattern newPattern)
    {
        movementPattern = newPattern;
        timer = 0f;
        movementCenter = transform.position;
        SetInitialTarget();

        Debug.Log($"[DynamicTarget] {gameObject.name} changed to {newPattern} pattern");
    }

    /// <summary>
    /// Pause or resume movement.
    /// </summary>
    public void SetMovementEnabled(bool enabled)
    {
        this.enabled = enabled;
    }

    /// <summary>
    /// Get current movement information for debugging.
    /// </summary>
    public string GetMovementInfo()
    {
        return $"Pattern: {movementPattern}, Speed: {moveSpeed:F1}, Center: {movementCenter}";
    }

    /// <summary>
    /// Set the center point for movement patterns.
    /// </summary>
    public void SetCenter(Vector3 center)
    {
        movementCenter = center;
        startPosition = center;
    }

    /// <summary>
    /// Set the movement speed.
    /// </summary>
    public float speed
    {
        get => moveSpeed;
        set => moveSpeed = value;
    }

    /// <summary>
    /// Set the movement radius.
    /// </summary>
    public float radius
    {
        get => movementRadius;
        set => movementRadius = value;
    }
}
