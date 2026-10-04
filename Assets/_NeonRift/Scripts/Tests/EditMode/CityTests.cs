using System.Collections.Generic;
using System.Linq;
using NeonRift.EditorTools.District;
using NeonRift.Game;
using NeonRift.Gameplay;
using NeonRift.Vehicles;
using NeonRift.World;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NeonRift.Tests
{
    /// <summary>City layout and road graph (pure data/logic), racing lines, the rubber band and the rival field.</summary>
    public class CityTests
    {
        private readonly List<Object> created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in created) Object.DestroyImmediate(o);
            created.Clear();
        }

        private RoadNetwork Network()
        {
            var n = ScriptableObject.CreateInstance<RoadNetwork>();
            created.Add(n);
            return CityLayout.BuildNetwork(n);
        }

        [Test]
        public void CityNetwork_IsValidAndConnected()
        {
            var network = Network();
            Assert.That(network.Validate(), Is.Empty);
            Assert.Greater(network.Nodes.Count, 60);
            Assert.Greater(network.Edges.Count, 90);
        }

        [Test]
        public void CityIsMuchLargerThanTheSlice()
        {
            var b = CityLayout.CityBounds;
            Assert.GreaterOrEqual(b.width * b.height, 6f * 340f * 720f);
        }

        [Test]
        public void EveryBlockHasAZoneAndPavement()
        {
            var d = CityLayout.Decompose();
            Assert.Greater(d.Blocks.Count, 30);
            foreach (var cell in d.Cells.Where(c => !c.Outer))
                Assert.IsTrue(cell.Slab.width > 0f && cell.Slab.height > 0f, $"cell {cell.Grid} has no pavement");
        }

        [Test]
        public void HeroBuildingsAvoidRoadsAndReservedAreas()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<BuildingCatalog>(AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("t:BuildingCatalog")[0]));
            foreach (var h in CityLayout.Heroes)
            {
                var def = catalog.Buildings.First(b => b != null && b.Id == h.Id);
                var e = Quaternion.Euler(0f, h.Yaw, 0f) * new Vector3(def.Size.x, 0f, def.Size.z) * h.Scale;
                var rect = new Rect(h.Position.x - Mathf.Abs(e.x) * 0.5f, h.Position.y - Mathf.Abs(e.z) * 0.5f, Mathf.Abs(e.x), Mathf.Abs(e.z));
                Assert.IsFalse(CityLayout.Blocked(rect, includeLots: false), $"{h.Id} at {h.Position} overlaps a road");
            }
        }

        [Test]
        public void Path_SpawnToCore_FollowsRoads()
        {
            var network = Network();
            var graph = new RoadGraph(network);
            var path = new RoadPath();
            Assert.IsTrue(graph.FindPath(NightRunBuilder.SpawnPosition, NightRunBuilder.CorePosition, null, path));
            // The core is inside the compound: the straight line is ~440 m, any road route is longer.
            Assert.Greater(path.Length, Vector3.Distance(NightRunBuilder.SpawnPosition, NightRunBuilder.CorePosition));
            Assert.Less(path.Length, 1600f);
            Assert.AreEqual(path.Points.Count - 1, path.Edges.Count);
        }

        [Test]
        public void ClosedGates_ChangeTheRoute()
        {
            var network = Network();
            var graph = new RoadGraph(network);
            var open = new RoadPath();
            var closed = new RoadPath();
            // Start on the compound's south driveway (the edge a car stands on is never treated as closed).
            Vector3 core = new(160f, 0f, 100f), extraction = new(320f, 0f, -440f);
            Assert.IsTrue(graph.FindPath(core, extraction, null, open));
            string[] gates = { CityLayout.AlleyGateId, CityLayout.CheckpointId, CityLayout.CompoundGateId };
            // With the alley and the compound gate both sealed there is no way out of the compound.
            Assert.IsFalse(graph.FindPath(core, extraction, e => gates.Contains(network.Edges[e].blockerId) ? float.PositiveInfinity : 0f, closed));
            // With only the checkpoint sealed the route still exists.
            Assert.IsTrue(graph.FindPath(core, extraction, e => network.Edges[e].blockerId == CityLayout.CheckpointId ? float.PositiveInfinity : 0f, closed));
            Assert.IsTrue(closed.Edges.All(e => network.Edges[e].blockerId != CityLayout.CheckpointId));
        }

        [Test]
        public void RememberedBlockedEdge_IsRoutedAround()
        {
            // RacerDriver adds a finite penalty to an edge it got stuck on; the grid always offers a detour.
            var network = Network();
            var graph = new RoadGraph(network);
            var first = new RoadPath();
            var second = new RoadPath();
            Vector3 start = new(3.5f, 0f, -292f), goal = new(160f, 0f, 300f);
            Assert.IsTrue(graph.FindPath(start, goal, null, first));
            int blocked = first.Edges[first.Edges.Count / 2];
            Assert.IsTrue(graph.FindPath(start, goal, e => e == blocked ? 250f : 0f, second));
            CollectionAssert.DoesNotContain(second.Edges, blocked);
            Assert.Less(second.Length - first.Length, 250f, "the detour is cheaper than pushing through the blocked edge");
        }

        [Test]
        public void Skyway_IsElevatedAndReachable()
        {
            var network = Network();
            var deck = network.Edges.Where(e => e.roadClass == RoadClass.Skyway).ToList();
            Assert.AreEqual(3, deck.Count);
            Assert.IsTrue(deck.Any(e => network.NodePosition(e.a).y > 7f && network.NodePosition(e.b).y > 7f));
            var path = new RoadPath();
            Assert.IsTrue(new RoadGraph(network).FindPath(new Vector3(400f, 8f, 60f), new Vector3(700f, 8f, 60f), null, path));
            Assert.IsTrue(path.Edges.All(e => network.Edges[e].roadClass == RoadClass.Skyway));
        }

        [Test]
        public void RacingLine_SlowsForCornersAndStops()
        {
            var network = Network();
            var path = new RoadPath();
            new RoadGraph(network).FindPath(new Vector3(3.5f, 0f, -250f), new Vector3(120f, 0f, -97f), null, path);   // W Avenue, right onto Market St
            var line = new RacingLine();
            line.Build(path, network, 9f, 70f, 1.3f, stopAtEnd: true);
            Assert.Greater(line.Count, 50);
            float straight = line.SpeedAt(10), corner = Enumerable.Range(0, line.Count).Min(i => i < line.Count - 3 ? line.SpeedAt(i) : float.MaxValue);
            Assert.Greater(straight, corner + 5f);
            Assert.AreEqual(0f, line.SpeedAt(line.Count - 1));
            for (int i = 0; i < line.Count; i++) Assert.LessOrEqual(line.MinOffset(i), line.MaxOffset(i));
        }

        [Test]
        public void RubberBand_IsBoundedAndSymmetricInSign()
        {
            var p = ScriptableObject.CreateInstance<RacerProfile>();
            created.Add(p);
            p.EditorConfigure("T", 1f, 8f, 1.2f, 0.5f, 0.05f, 0.06f);
            Assert.AreEqual(1f, p.RubberBand(0f), 1e-5f);
            Assert.AreEqual(1.05f, p.RubberBand(10000f), 1e-5f);
            Assert.AreEqual(0.94f, p.RubberBand(-10000f), 1e-5f);
            Assert.Greater(p.RubberBand(50f), 1f);
        }

        [Test]
        public void Session_RivalsAreTheCarsNotPicked()
        {
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("t:GameConfig")[0]));
            var catalog = config.VehicleCatalog;
            Assume.That(catalog.Count, Is.GreaterThanOrEqualTo(2));
            var session = new RunSession();
            var picked = catalog.Vehicles[1];
            session.SelectVehicle(picked, catalog);
            Assert.AreEqual(picked, session.SelectedVehicle);
            Assert.AreEqual(catalog.Count - 1, session.Rivals.Count);
            Assert.IsFalse(session.Rivals.Contains(picked));
            session.SelectVehicle(picked);
            Assert.AreEqual(0, session.Rivals.Count);
        }

        [Test]
        public void NightRun_CityLifeIsLightweight()
        {
            var scene = EditorSceneManager.OpenScene("Assets/_NeonRift/Scenes/NightRun.unity", OpenSceneMode.Additive);
            try
            {
                var roots = scene.GetRootGameObjects();
                var traffic = roots.SelectMany(r => r.GetComponentsInChildren<AmbientTraffic>(true)).Single();
                Assert.GreaterOrEqual(traffic.transform.childCount, 30, "traffic pool");
                Assert.IsEmpty(traffic.GetComponentsInChildren<Collider>(true), "traffic cars must not collide");
                Assert.IsEmpty(traffic.GetComponentsInChildren<Rigidbody>(true), "traffic cars must not simulate physics");
                var crowd = roots.SelectMany(r => r.GetComponentsInChildren<CrowdGroups>(true)).Single();
                Assert.Greater(crowd.Count, 20, "crowd groups");
                Assert.IsEmpty(crowd.GetComponentsInChildren<Collider>(true), "pedestrians must not collide");
                Assert.AreEqual(1, roots.SelectMany(r => r.GetComponentsInChildren<SecurityDrones>(true)).Count());
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
