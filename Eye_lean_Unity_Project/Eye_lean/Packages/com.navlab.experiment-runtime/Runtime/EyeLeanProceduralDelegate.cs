// unity-component/Runtime/EyeLeanProceduralDelegate.cs
using UnityEngine;

namespace Navlab.ExperimentRuntime
{
    /// <summary>Procedural delegate that wraps Eye-LEAN's RoomGenerator.
    ///
    /// At v1, this is a STUB. Researchers integrating with Eye-LEAN should:
    /// 1. Add a reference to Eye-LEAN's assembly in this package's asmdef
    /// 2. Replace the body of Generate() with a call into Eye-LEAN's room
    ///    generator using the provided seed
    /// 3. Populate env.walls / env.obstacles / env.spawnPoints / env.goals
    ///    based on the generated room
    ///
    /// Stub behavior: produces a small empty room with a single goal at the
    /// far end so the package's sample scene works without Eye-LEAN present.
    ///
    /// TODO(eye_lean@EnvironmentGenerator): replace Generate() body with a
    /// call into Eye_lean's `EnvironmentGenerator` so maze geometry is
    /// procedurally seeded from Eye_lean's room generator. Until then, the
    /// Eye_lean maze scene uses a hand-authored EnvironmentConfig asset that
    /// bypasses this delegate.</summary>
    [CreateAssetMenu(menuName = "Navlab/Procedural/Eye-LEAN Wrapper")]
    public class EyeLeanProceduralDelegate : ProceduralEnvironmentDelegate
    {
        public override string DelegateId => "eyelean_room_v1_stub";

        public override void Generate(EnvironmentConfig env, int randomSeed)
        {
            // Stub: 6m x 6m empty room with a goal at the far end
            env.boundsMin = new Vector2(-3, 0);
            env.boundsMax = new Vector2(3, 6);
            env.walls.Clear();
            env.obstacles.Clear();
            env.spawnPoints.Clear();
            env.goals.Clear();

            env.spawnPoints.Add(new NamedPoint {
                name = "S_human", positionXZ = new Vector2(0, 0.3f), headingDeg = 0,
            });
            env.spawnPoints.Add(new NamedPoint {
                name = "S_npc", positionXZ = new Vector2(-1.5f, 0.3f), headingDeg = 0,
            });
            env.goals.Add(new NamedPoint {
                name = "G", positionXZ = new Vector2(0, 5.5f), headingDeg = 180,
            });

            // Use the seed to perturb one obstacle position
            var rng = new System.Random(randomSeed);
            env.obstacles.Add(new CircularObstacle {
                centerXZ = new Vector2(
                    (float)(rng.NextDouble() * 1.0 - 0.5),
                    3.0f),
                radius = 0.4f,
            });
        }
    }
}
