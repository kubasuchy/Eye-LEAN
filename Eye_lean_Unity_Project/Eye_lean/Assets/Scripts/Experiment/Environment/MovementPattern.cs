/// <summary>
/// Defines different movement patterns for dynamic targets.
/// Each pattern tests different aspects of eye tracking.
/// </summary>
public enum MovementPattern
{
    Linear,         // Back and forth movement - tests smooth pursuit
    Circular,       // Circular motion - tests continuous smooth pursuit
    Figure8,        // Figure-8 pattern - tests complex smooth pursuit
    Random,         // Random movement with pauses - tests saccadic tracking
    Oscillating,    // Side-to-side oscillation - tests predictable tracking
    Spiral          // Expanding/contracting spiral - tests depth tracking
}
