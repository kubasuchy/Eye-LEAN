using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Navlab.Planners
{
    public static class Serde
    {
        public static PlanRequest PlanRequestFromJson(string json)
        {
            var root = JObject.Parse(json);
            return PlanRequestFromElement(root);
        }

        public static PlanRequest PlanRequestFromElement(JObject root)
        {
            var gd = (JObject)root["grid"];
            int width = gd["width"].Value<int>();
            int height = gd["height"].Value<int>();
            float res = gd["resolution_m"].Value<float>();
            float ox = gd["origin_world_x"].Value<float>();
            float oy = gd["origin_world_y"].Value<float>();
            var data = new bool[height, width];
            var rows = (JArray)gd["data"];
            for (int r = 0; r < height; r++)
            {
                var row = (JArray)rows[r];
                for (int c = 0; c < width; c++)
                    data[r, c] = row[c].Value<int>() != 0;
            }
            var grid = new OccupancyGrid(width, height, res, ox, oy, data);

            var start = (JArray)root["start_world"];
            var goal = (JArray)root["goal_world"];
            var startWorld = (start[0].Value<float>(), start[1].Value<float>());
            var goalWorld = (goal[0].Value<float>(), goal[1].Value<float>());
            float agentRadius = root["agent_radius_m"].Value<float>();

            var dynList = new List<DynamicObstacle>();
            if (root["dynamic_obstacles"] is JArray dynArr)
            {
                foreach (JObject dEl in dynArr)
                {
                    string objId = dEl["object_id"].Value<string>();
                    float fr = dEl["footprint_radius_m"].Value<float>();
                    var poses = new List<(float, float, float)>();
                    foreach (JArray pEl in (JArray)dEl["poses"])
                        poses.Add((pEl[0].Value<float>(),
                                   pEl[1].Value<float>(),
                                   pEl[2].Value<float>()));
                    dynList.Add(new DynamicObstacle(objId, fr, poses));
                }
            }

            double maxT = root["max_planning_time_ms"]?.Value<double>() ?? 5000.0;

            return new PlanRequest(grid, startWorld, goalWorld, agentRadius,
                                   dynList, maxT);
        }

        public static string PlanResultToJson(PlanResult res)
        {
            var waypoints = new JArray();
            foreach (var (x, y) in res.WaypointsWorld)
                waypoints.Add(new JArray(x, y));
            var obj = new JObject
            {
                ["success"] = res.Success,
                ["waypoints_world"] = waypoints,
                ["runtime_ms"] = res.RuntimeMs,
                ["nodes_expanded"] = res.NodesExpanded,
                ["error_message"] = res.ErrorMessage,
                ["extra"] = new JObject(),
            };
            return obj.ToString(Formatting.Indented);
        }
    }
}
