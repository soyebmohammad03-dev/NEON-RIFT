using System;
using NeonRift.World;
using UnityEngine;

namespace NeonRift.Gameplay
{
    /// <summary>
    /// The scene's view of the <see cref="RoadNetwork"/>: plans routes that respect the current state of security
    /// gates (a closed gate removes its edge; a pending or closing one is avoided unless nothing else is close), and
    /// answers "where am I" (street, district). <see cref="Version"/> changes whenever a gate changes state so
    /// drivers and the HUD know to re-plan. One per mission scene; referenced, not looked up globally.
    /// </summary>
    public sealed class CityNavigation : MonoBehaviour
    {
        [Serializable]
        public struct Blocker
        {
            public string id;
            public SecurityBarrier barrier;
        }

        [SerializeField] private RoadNetwork network;
        [SerializeField] private Blocker[] blockers = Array.Empty<Blocker>();
        [Tooltip("Extra route cost for an edge whose gate is counting down to close, m.")]
        [SerializeField, Min(0f)] private float pendingPenalty = 450f;

        private RoadGraph graph;
        private SecurityBarrier[] edgeBarrier;
        private int[] lastState;
        private RoadGraph.EdgeCost costFunction;

        public RoadNetwork Network => network;
        public RoadGraph Graph => EnsureGraph();
        /// <summary>Incremented whenever any gate opens, starts closing or closes.</summary>
        public int Version { get; private set; }

        private RoadGraph EnsureGraph()
        {
            if (graph != null || network == null) return graph;
            graph = new RoadGraph(network);
            edgeBarrier = new SecurityBarrier[network.Edges.Count];
            for (int i = 0; i < network.Edges.Count; i++)
            {
                string id = network.Edges[i].blockerId;
                if (string.IsNullOrEmpty(id)) continue;
                foreach (var b in blockers)
                    if (b.id == id) edgeBarrier[i] = b.barrier;
            }
            lastState = new int[blockers.Length];
            costFunction = EdgeCost;
            return graph;
        }

        private void Update()
        {
            if (EnsureGraph() == null) return;
            for (int i = 0; i < blockers.Length; i++)
            {
                int state = State(blockers[i].barrier);
                if (state == lastState[i]) continue;
                lastState[i] = state;
                Version++;
            }
        }

        private static int State(SecurityBarrier b) => b == null ? 0 : b.IsOpen ? 0 : b.IsClosed ? 2 : b.SecondsUntilClosing > 0f ? 1 : 3;

        /// <summary>Extra cost for an edge right now: closed gates are impassable, closing ones expensive.</summary>
        public float EdgeCost(int edge)
        {
            var b = edgeBarrier[edge];
            if (b == null) return 0f;
            return State(b) switch { 0 => 0f, 1 => pendingPenalty, _ => float.PositiveInfinity };
        }

        public bool Plan(Vector3 from, Vector3 to, RoadPath result) =>
            EnsureGraph() != null && graph.FindPath(from, to, costFunction, result);

        public bool IsBlocked(int edge) => EnsureGraph() != null && float.IsPositiveInfinity(EdgeCost(edge));

        public string StreetAt(Vector3 position) =>
            network != null && network.TryGetNearestEdge(position, out int e, out _, out float d) && d < 30f ? network.Edges[e].street : null;

        public bool TryGetDistrict(Vector3 position, out CityDistrict district)
        {
            district = default;
            return network != null && network.TryGetDistrict(position, out district);
        }

#if UNITY_EDITOR
        public void EditorConfigure(RoadNetwork roadNetwork, Blocker[] gates)
        {
            network = roadNetwork;
            blockers = gates ?? Array.Empty<Blocker>();
            graph = null;
        }
#endif
    }
}
