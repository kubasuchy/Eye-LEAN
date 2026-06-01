// unity-component/Runtime/ExperimentConfigExporter.cs
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Navlab.ExperimentRuntime
{
    public static class ExperimentConfigExporter
    {
        /// <summary>Serialize an ExperimentConfig (with its EnvironmentConfig
        /// resolved post-procedural) to the JSON sidecar format the workbench
        /// reads.</summary>
        public static string ToJson(ExperimentConfig exp)
        {
            var env = exp.environment;
            var envJson = new JObject
            {
                ["boundsMin"] = new JArray(env.boundsMin.x, env.boundsMin.y),
                ["boundsMax"] = new JArray(env.boundsMax.x, env.boundsMax.y),
                ["walls"] = SerializeList(env.walls, w => new JObject {
                    ["startXZ"] = new JArray(w.startXZ.x, w.startXZ.y),
                    ["endXZ"]   = new JArray(w.endXZ.x,   w.endXZ.y),
                    ["thickness"] = w.thickness,
                }),
                ["obstacles"] = SerializeList(env.obstacles, o => new JObject {
                    ["centerXZ"] = new JArray(o.centerXZ.x, o.centerXZ.y),
                    ["radius"] = o.radius,
                }),
                ["spawnPoints"] = SerializeList(env.spawnPoints, p => new JObject {
                    ["name"] = p.name,
                    ["positionXZ"] = new JArray(p.positionXZ.x, p.positionXZ.y),
                    ["headingDeg"] = p.headingDeg,
                }),
                ["goals"] = SerializeList(env.goals, p => new JObject {
                    ["name"] = p.name,
                    ["positionXZ"] = new JArray(p.positionXZ.x, p.positionXZ.y),
                    ["headingDeg"] = p.headingDeg,
                }),
                ["proceduralDelegateId"] = env.proceduralDelegate != null
                    ? env.proceduralDelegate.DelegateId : null,
            };

            var agents = SerializeList(exp.agents, a => new JObject {
                ["name"] = a.name,
                ["agentType"] = a.agentType.ToString().ToLowerInvariant(),
                ["skinAsset"] = a.skin != null ? a.skin.name : null,
                ["planner"] = a.planner switch {
                    PlannerType.None      => (string)null,
                    PlannerType.AStar     => "astar",
                    PlannerType.DStarLite => "dstar_lite",
                    _                     => throw new System.ArgumentException(
                                                 $"Unknown PlannerType: {a.planner}")
                },
                ["spawnRef"] = a.spawnRef,
                ["goalRef"] = a.goalRef,
            });

            var trials = SerializeList(exp.trials, t => {
                var overrides = new JObject();
                foreach (var ov in t.goalOverrides)
                    overrides[ov.agentName] = ov.goalRef;
                return new JObject {
                    ["trialId"] = t.trialId,
                    ["activeAgentNames"] = new JArray(t.activeAgentNames),
                    ["goalOverrides"] = overrides,
                    ["durationSeconds"] = t.durationSeconds,
                    ["trialSeed"] = t.trialSeed,
                };
            });

            var root = new JObject
            {
                ["experimentName"] = exp.experimentName,
                ["version"] = exp.version,
                ["environment"] = envJson,
                ["agents"] = agents,
                ["trials"] = trials,
                ["randomSeed"] = exp.randomSeed,
            };
            return root.ToString(Formatting.Indented);
        }

        public static void WriteToFile(ExperimentConfig exp, string path)
        {
            File.WriteAllText(path, ToJson(exp));
        }

        private static JArray SerializeList<T>(System.Collections.Generic.List<T> items,
                                                 System.Func<T, JToken> fn)
        {
            var arr = new JArray();
            if (items == null) return arr;
            foreach (var item in items) arr.Add(fn(item));
            return arr;
        }
    }
}
