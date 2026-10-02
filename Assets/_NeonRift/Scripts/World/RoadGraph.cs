using System;
using System.Collections.Generic;
using UnityEngine;

namespace NeonRift.World
{
    /// <summary>A route through the road network: polyline points and, per segment, the edge it runs along.</summary>
    public sealed class RoadPath
    {
        public readonly List<Vector3> Points = new();
        /// <summary>Edge index of segment i (Points[i] → Points[i + 1]).</summary>
        public readonly List<int> Edges = new();
        public float Length;
        public bool IsValid => Points.Count >= 2;

        public void Clear()
        {
            Points.Clear();
            Edges.Clear();
            Length = 0f;
        }
    }

    /// <summary>
    /// Shortest paths over a <see cref="RoadNetwork"/> between arbitrary positions (each end is projected onto its
    /// nearest edge). Edges can be blocked or penalised per query, so routes follow gates as they open and close.
    /// Pure C#; allocation-free after construction apart from the result path.
    /// </summary>
    public sealed class RoadGraph
    {
        /// <summary>Returns extra cost in metres for an edge, or <see cref="float.PositiveInfinity"/> if it is closed.</summary>
        public delegate float EdgeCost(int edgeIndex);

        private readonly RoadNetwork network;
        private readonly List<(int edge, int other)>[] adjacency;
        private readonly float[] edgeLength;
        private readonly float[] cost;
        private readonly int[] previousNode, previousEdge;
        private readonly bool[] settled;

        public RoadNetwork Network => network;

        public RoadGraph(RoadNetwork roadNetwork)
        {
            network = roadNetwork ?? throw new ArgumentNullException(nameof(roadNetwork));
            int n = network.Nodes.Count;
            adjacency = new List<(int, int)>[n];
            for (int i = 0; i < n; i++) adjacency[i] = new List<(int, int)>(4);
            edgeLength = new float[network.Edges.Count];
            for (int i = 0; i < network.Edges.Count; i++)
            {
                var e = network.Edges[i];
                adjacency[e.a].Add((i, e.b));
                adjacency[e.b].Add((i, e.a));
                edgeLength[i] = Vector3.Distance(network.NodePosition(e.a), network.NodePosition(e.b));
            }
            cost = new float[n];
            previousNode = new int[n];
            previousEdge = new int[n];
            settled = new bool[n];
        }

        public float EdgeLength(int edge) => edgeLength[edge];

        public int CountReachable(int start)
        {
            var seen = new bool[adjacency.Length];
            var stack = new Stack<int>();
            stack.Push(start);
            seen[start] = true;
            int count = 0;
            while (stack.Count > 0)
            {
                int node = stack.Pop();
                count++;
                foreach (var (_, other) in adjacency[node])
                    if (!seen[other]) { seen[other] = true; stack.Push(other); }
            }
            return count;
        }

        /// <summary>
        /// Shortest route from <paramref name="from"/> to <paramref name="to"/>. The edge the start lies on is never treated
        /// as closed (a car already past a gate can always drive away from it). Returns false if no open route exists.
        /// </summary>
        public bool FindPath(Vector3 from, Vector3 to, EdgeCost extraCost, RoadPath result)
        {
            result.Clear();
            if (!network.TryGetNearestEdge(from, out int startEdge, out float startT, out _)) return false;
            if (!network.TryGetNearestEdge(to, out int goalEdge, out float goalT, out _)) return false;
            var se = network.Edges[startEdge];
            var ge = network.Edges[goalEdge];
            Vector3 start = Vector3.Lerp(network.NodePosition(se.a), network.NodePosition(se.b), startT);
            Vector3 goal = Vector3.Lerp(network.NodePosition(ge.a), network.NodePosition(ge.b), goalT);

            float Extra(int edge) => extraCost == null || edge == startEdge ? 0f : extraCost(edge);

            // Same edge: drive straight along it (unless it is closed between the two points).
            float direct = float.PositiveInfinity;
            if (startEdge == goalEdge) direct = Mathf.Abs(goalT - startT) * edgeLength[startEdge];

            int n = adjacency.Length;
            for (int i = 0; i < n; i++)
            {
                cost[i] = float.PositiveInfinity;
                previousNode[i] = -1;
                previousEdge[i] = -1;
                settled[i] = false;
            }
            cost[se.a] = startT * edgeLength[startEdge];
            cost[se.b] = Mathf.Min(cost[se.b], (1f - startT) * edgeLength[startEdge]);
            previousEdge[se.a] = startEdge;
            previousEdge[se.b] = startEdge;

            float goalExtra = Extra(goalEdge);
            float best = direct;
            int bestEnd = -1;
            while (true)
            {
                int node = -1;
                float min = float.PositiveInfinity;
                for (int i = 0; i < n; i++)
                    if (!settled[i] && cost[i] < min) { min = cost[i]; node = i; }
                if (node < 0 || min >= best) break;
                settled[node] = true;

                // Finish onto the goal edge from either of its ends.
                if (node == ge.a || node == ge.b)
                {
                    float tail = (node == ge.a ? goalT : 1f - goalT) * edgeLength[goalEdge] + goalExtra;
                    if (min + tail < best) { best = min + tail; bestEnd = node; }
                }
                foreach (var (edge, other) in adjacency[node])
                {
                    if (settled[other]) continue;
                    float extra = Extra(edge);
                    if (float.IsPositiveInfinity(extra)) continue;
                    float c = min + edgeLength[edge] + extra;
                    if (c < cost[other])
                    {
                        cost[other] = c;
                        previousNode[other] = node;
                        previousEdge[other] = edge;
                    }
                }
            }
            if (float.IsPositiveInfinity(best)) return false;

            result.Points.Add(start);
            if (bestEnd < 0)
            {
                result.Points.Add(goal);
                result.Edges.Add(startEdge);
            }
            else
            {
                var chain = new List<int>();
                for (int node = bestEnd; node >= 0; node = previousNode[node]) chain.Add(node);
                chain.Reverse();
                // First leg: from the start point along the start edge to the first node.
                result.Points.Add(network.NodePosition(chain[0]));
                result.Edges.Add(startEdge);
                for (int i = 1; i < chain.Count; i++)
                {
                    result.Points.Add(network.NodePosition(chain[i]));
                    result.Edges.Add(previousEdge[chain[i]]);
                }
                result.Points.Add(goal);
                result.Edges.Add(goalEdge);
                // Drop zero-length legs (start or goal exactly on a node).
                for (int i = result.Points.Count - 1; i > 0; i--)
                    if ((result.Points[i] - result.Points[i - 1]).sqrMagnitude < 0.01f)
                    {
                        result.Points.RemoveAt(i);
                        result.Edges.RemoveAt(i - 1);
                    }
            }
            for (int i = 1; i < result.Points.Count; i++) result.Length += Vector3.Distance(result.Points[i - 1], result.Points[i]);
            return result.Points.Count >= 2;
        }
    }
}
