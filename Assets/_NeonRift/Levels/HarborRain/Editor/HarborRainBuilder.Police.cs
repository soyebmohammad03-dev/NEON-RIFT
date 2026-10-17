using System.Collections.Generic;
using System.Linq;
using NeonRift.Gameplay;
using NeonRift.Vehicles;
using NeonRift.World;
using UnityEditor;
using UnityEngine;

namespace NeonRift.HarborRain.EditorTools
{
    /// <summary>
    /// The port police: Harbor Rain's road rectangles turned into a stock <see cref="RoadNetwork"/> asset (a node at
    /// every junction and road end, plus extra nodes at the edges of every area that can close, so each closable stretch
    /// is its own edge with a blocker id), a <see cref="CityNavigation"/> with one invisible <see cref="SecurityBarrier"/>
    /// per closure, a <see cref="RacerProfile"/> for the police drivers, and the <see cref="HarborPolice"/> units.
    /// </summary>
    public static partial class HarborRainBuilder
    {
        /// <summary>Closable areas the police plan round: (id, area). Ids match the mission's closure / hazard labels.</summary>
        private static List<(string id, Rect area)> PoliceClosures(Coast coast)
        {
            var list = new List<(string, Rect)>
            {
                ("QUAYSIDE WEST", new Rect(330f, 221f, 140f, 18f)),
                ("QUAYSIDE EAST", new Rect(510f, 221f, 100f, 18f)),
                ("LANE 80 CHECKPOINT", new Rect(514f, 72f, 12f, 16f)),
                ("SOUTH GATE", new Rect(-11f, -336f, 22f, 12f)),
                ("SECURITY GATE", new Rect(212f, 90f, 26f, 12f)),
                ("NORTH GATE", new Rect(212f, 210f, 26f, 12f)),
                ("EAST GATE", new Rect(654f, 220f, 12f, 20f)),
            };
            for (int i = 0; i < coast.BlockadeNames.Count; i++) list.Add((coast.BlockadeNames[i], coast.BlockadeAreas[i]));
            string[] labels = { "SEAWALL WEST", "SEAWALL MID", "SEAWALL EAST" };
            for (int k = 0; k < WaveCentres.Length; k++)
                list.Add(("WAVE:" + labels[k], new Rect(WaveCentres[k] - WaveHalf, CoastZ0 - 1f, WaveHalf * 2f, CoastZ1 - CoastZ0 + 2f)));
            return list;
        }

        private static RoadNetwork BuildRoadNetwork(List<(string id, Rect area)> closures, out string summary)
        {
            var rects = Roads.Select(r => new Rect(r.min, r.max - r.min)).ToArray();
            bool AlongZ(int i) => rects[i].height > rects[i].width;
            Vector3 P(int i, float s) => AlongZ(i) ? new Vector3(rects[i].center.x, 0f, s) : new Vector3(s, 0f, rects[i].center.y);
            static Rect Grow(Rect r, float m) => new(r.x - m, r.y - m, r.width + m * 2f, r.height + m * 2f);

            var stations = new List<float>[rects.Length];
            for (int i = 0; i < rects.Length; i++)
                stations[i] = new List<float> { AlongZ(i) ? rects[i].yMin : rects[i].xMin, AlongZ(i) ? rects[i].yMax : rects[i].xMax };
            for (int i = 0; i < rects.Length; i++)
            for (int j = i + 1; j < rects.Length; j++)
            {
                if (AlongZ(i) == AlongZ(j) || !Grow(rects[i], 1.5f).Overlaps(Grow(rects[j], 1.5f))) continue;
                int v = AlongZ(i) ? i : j, h = v == i ? j : i;
                stations[v].Add(rects[h].center.y);
                stations[h].Add(rects[v].center.x);
            }
            // Split roads at the edges of closable areas so a closure only removes its own stretch.
            foreach (var (_, area) in closures)
                for (int i = 0; i < rects.Length; i++)
                {
                    if (!rects[i].Overlaps(area)) continue;
                    float a = AlongZ(i) ? area.yMin : area.xMin, b = AlongZ(i) ? area.yMax : area.xMax;
                    float lo = AlongZ(i) ? rects[i].yMin : rects[i].xMin, hi = AlongZ(i) ? rects[i].yMax : rects[i].xMax;
                    foreach (float s in new[] { a - 1f, b + 1f }) if (s > lo + 2f && s < hi - 2f) stations[i].Add(s);
                }

            var nodes = new List<Vector3>();
            int Node(Vector3 p)
            {
                for (int k = 0; k < nodes.Count; k++) if ((nodes[k] - p).sqrMagnitude < 9f) return k;
                nodes.Add(p);
                return nodes.Count - 1;
            }
            var raw = new List<(int a, int b, int road)>();
            for (int i = 0; i < rects.Length; i++)
            {
                var sorted = stations[i].Distinct().OrderBy(s => s).ToList();
                var merged = new List<float>();
                foreach (float s in sorted) if (merged.Count == 0 || s - merged[^1] > 3f) merged.Add(s);
                int prev = -1;
                foreach (float s in merged)
                {
                    int n = Node(P(i, s));
                    if (prev >= 0 && prev != n) raw.Add((prev, n, i));
                    prev = n;
                }
            }
            // Prune short dead-end stubs (a road end a few metres past a junction, e.g. the square corners of the coast bends).
            bool pruned = true;
            int removed = 0;
            while (pruned)
            {
                pruned = false;
                var degree = new int[nodes.Count];
                foreach (var e in raw) { degree[e.a]++; degree[e.b]++; }
                for (int k = raw.Count - 1; k >= 0; k--)
                {
                    var e = raw[k];
                    if ((degree[e.a] == 1 || degree[e.b] == 1) && Vector3.Distance(nodes[e.a], nodes[e.b]) < 12f) { raw.RemoveAt(k); pruned = true; removed++; break; }
                }
            }
            // Compact the node list to the nodes still used.
            var used = raw.SelectMany(e => new[] { e.a, e.b }).Distinct().OrderBy(k => k).ToList();
            var remap = new Dictionary<int, int>();
            var roadNodes = new List<RoadNode>();
            foreach (int k in used) { remap[k] = roadNodes.Count; roadNodes.Add(new RoadNode { position = nodes[k] }); }
            var edges = new List<RoadEdge>();
            int blocked = 0;
            foreach (var (a, b, road) in raw)
            {
                var r = rects[road];
                float width = AlongZ(road) ? r.width : r.height;
                Vector3 mid = (nodes[a] + nodes[b]) * 0.5f;
                string blocker = "";
                foreach (var (id, area) in closures) if (area.Contains(new Vector2(mid.x, mid.z))) { blocker = id; break; }
                if (blocker.Length > 0) blocked++;
                bool coastal = road >= 20;
                float limit = coastal ? 31f : width >= 14f ? 27f : road == 18 ? 13f : 19f;
                edges.Add(new RoadEdge
                {
                    a = remap[a], b = remap[b], halfWidth = width * 0.5f, lanesPerDirection = 1, median = 0f,
                    roadClass = width >= 14f ? RoadClass.Arterial : RoadClass.Street, street = RoadName(road), speedLimit = limit, blockerId = blocker
                });
            }
            var net = AssetDatabase.LoadAssetAtPath<RoadNetwork>($"{Gen}/HR_RoadNetwork.asset");
            if (net == null) { net = ScriptableObject.CreateInstance<RoadNetwork>(); AssetDatabase.CreateAsset(net, $"{Gen}/HR_RoadNetwork.asset"); }
            net.EditorConfigure("Kowloon Bay Port", roadNodes, edges, new List<CityDistrict>(), new Rect(-720f, -660f, 3100f, 1300f));
            EditorUtility.SetDirty(net);
            var problems = net.Validate();
            var graph = new RoadGraph(net);
            int reach = graph.CountReachable(0);
            summary = $"police road network: {roadNodes.Count} nodes, {edges.Count} edges ({blocked} closable, {removed} stubs pruned), {reach}/{roadNodes.Count} reachable{(problems.Count > 0 ? ", issues: " + string.Join("; ", problems.Take(3)) : "")}";
            return net;
        }

        private static string RoadName(int i) => i switch
        {
            0 => "ENTRY ROAD", 1 => "SOUTH PERIMETER ROAD", 2 => "THE SPINE", 3 => "WEST ROAD", 4 => "EAST ROAD", 5 => "CROSS ROAD", 6 => "TERMINAL ROAD",
            7 => "LANE 80", 8 => "QUAY ROAD", 9 => "LANE W450", 10 => "LANE W150", 11 => "LANE E90", 12 => "LANE E450", 13 => "FREIGHT CONNECTOR",
            14 => "INDUSTRIAL CONNECTOR", 15 => "FREIGHT NORTH EXIT", 16 => "FREIGHT SERVICE ROAD", 17 => "INDUSTRIAL SERVICE ROAD", 18 => "RESTRICTED LANE",
            19 => "INDUSTRIAL NORTH EXIT", 20 => "COAST JUNCTION", 21 => "COAST ROAD", 22 => "SERVICE ROAD", 23 => "EAST LINK", 24 => "HEADLAND ROAD", 25 => "PIER ROAD",
            _ => "ROAD " + i
        };

        private static HarborPolice BuildPolice(Transform rig, Coast coast)
        {
            var root = new GameObject("PortPolice").transform;
            root.SetParent(rig, false);
            var closures = PoliceClosures(coast);
            var net = BuildRoadNetwork(closures, out string summary);
            var barriers = new List<HarborPolice.Closure>();
            var blockers = new List<CityNavigation.Blocker>();
            var proxies = new GameObject("RoadClosureProxies").transform;
            proxies.SetParent(root, false);
            foreach (var (id, area) in closures)
            {
                var go = new GameObject("Closure_" + id.Replace(' ', '_').Replace(':', '_'));
                go.transform.SetParent(proxies, false);
                go.transform.position = new Vector3(area.center.x, 0f, area.center.y);
                var b = go.AddComponent<SecurityBarrier>();
                b.EditorConfigure(id, false, null, null, 0f, 0f, 0f, 0.05f, 0.1f, new SecurityBarrier.Panel[0], null, null, null, null, null, null);
                barriers.Add(new HarborPolice.Closure { id = id, barrier = b });
                blockers.Add(new CityNavigation.Blocker { id = id, barrier = b });
            }
            var nav = root.gameObject.AddComponent<CityNavigation>();
            nav.EditorConfigure(net, blockers.ToArray());

            var profile = AssetDatabase.LoadAssetAtPath<RacerProfile>($"{Gen}/HR_PoliceDriver.asset");
            if (profile == null) { profile = ScriptableObject.CreateInstance<RacerProfile>(); AssetDatabase.CreateAsset(profile, $"{Gen}/HR_PoliceDriver.asset"); }
            profile.EditorConfigure("PORT POLICE", 0.92f, 8f, 1.5f, 0.55f, 0f, 0f);
            Configure(profile, so =>
            {
                so.FindProperty("maxSpeed").floatValue = 50f;
                so.FindProperty("stuckSeconds").floatValue = 2f;
                so.FindProperty("reversesBeforeReset").intValue = 6;   // try hard before the stock off-screen reset
            });

            var car = AssetDatabase.LoadAssetAtPath<VehicleDefinition>(HarborRainPolice.DefinitionPath);
            var police = root.gameObject.AddComponent<HarborPolice>();
            police.EditorConfigure(car, profile, nav, new[]
            {
                new HarborPolice.UnitSetup { callsign = "KILO-1", role = HarborPolice.Role.Chase, patrol = new[] { new Vector3(100f, 0.05f, -50f), new Vector3(450f, 0f, -50f), new Vector3(450f, 0f, 80f), new Vector3(90f, 0f, 80f) } },
                new HarborPolice.UnitSetup { callsign = "KILO-2", role = HarborPolice.Role.Flank, patrol = new[] { new Vector3(-150f, 0.05f, -50f), new Vector3(-150f, 0f, 230f), new Vector3(-450f, 0f, 230f), new Vector3(-450f, 0f, -50f) } },
                new HarborPolice.UnitSetup { callsign = "KILO-3", role = HarborPolice.Role.Intercept, patrol = new[] { new Vector3(380f, 0.05f, -240f), new Vector3(380f, 0f, -180f), new Vector3(641f, 0f, -180f), new Vector3(641f, 0f, -240f) } },
            }, barriers.ToArray());
            Configure(police, so => Array(so.FindProperty("parkedKits"), parkedPoliceKits.ToArray()));
            report.AppendLine(summary);
            report.AppendLine($"police: 3 units ({(car != null ? car.DisplayName : "MISSING POLICE CAR — run Build Police Car")}) on the stock RacerDriver, {closures.Count} closable areas mirrored as road barriers");
            return police;
        }
    }
}
