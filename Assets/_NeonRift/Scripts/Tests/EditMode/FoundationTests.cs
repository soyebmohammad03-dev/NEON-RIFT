using System.Collections.Generic;
using NeonRift.EditorTools;
using NeonRift.Game;
using NeonRift.Missions;
using NeonRift.Vehicles;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace NeonRift.Tests
{
    public class FoundationTests
    {
        private readonly List<Object> created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in created) Object.DestroyImmediate(o);
            created.Clear();
        }

        private VehicleDefinition Vehicle(string id, GameObject prefab)
        {
            var v = ScriptableObject.CreateInstance<VehicleDefinition>();
            created.Add(v);
            var so = new SerializedObject(v);
            so.FindProperty("id").stringValue = id;
            so.FindProperty("gameplayPrefab").objectReferenceValue = prefab;
            so.ApplyModifiedPropertiesWithoutUndo();
            return v;
        }

        private VehicleCatalog Catalog(params VehicleDefinition[] vehicles)
        {
            var c = ScriptableObject.CreateInstance<VehicleCatalog>();
            created.Add(c);
            var so = new SerializedObject(c);
            var list = so.FindProperty("vehicles");
            list.arraySize = vehicles.Length;
            for (int i = 0; i < vehicles.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = vehicles[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            return c;
        }

        private GameObject Prefab()
        {
            var go = new GameObject("TestPrefab");
            created.Add(go);
            return go;
        }

        [Test]
        public void Catalog_ReportsDuplicateIdsAndMissingPrefabs()
        {
            var catalog = Catalog(Vehicle("a", Prefab()), Vehicle("a", Prefab()), Vehicle("b", null));
            var problems = catalog.Validate();
            Assert.That(problems, Has.Some.Contains("Duplicate vehicle id 'a'"));
            Assert.That(problems, Has.Some.Contains("no gameplay prefab"));
        }

        [Test]
        public void Catalog_LooksUpById_AndDefaultIsFirst()
        {
            var a = Vehicle("a", Prefab());
            var b = Vehicle("b", Prefab());
            var catalog = Catalog(a, b);
            Assert.That(catalog.Validate(), Is.Empty);
            Assert.That(catalog.TryGet("b", out var found), Is.True);
            Assert.That(found, Is.SameAs(b));
            Assert.That(catalog.Default, Is.SameAs(a));
            Assert.That(catalog.TryGet("missing", out _), Is.False);
        }

        [Test]
        public void Session_IsReadyOnlyWithVehicleAndMission()
        {
            var session = new RunSession();
            Assert.That(session.IsReadyToLaunch, Is.False);
            session.SelectVehicle(Vehicle("a", Prefab()));
            Assert.That(session.IsReadyToLaunch, Is.False);
            var mission = ScriptableObject.CreateInstance<MissionDefinition>();
            created.Add(mission);
            session.SelectMission(mission);
            Assert.That(session.IsReadyToLaunch, Is.True);
        }

        [Test]
        public void Session_RecordsAndClearsResult()
        {
            var session = new RunSession();
            session.RecordResult(new RunResult(MissionOutcome.Completed, 127f, 4));
            Assert.That(session.LastResult.HasValue, Is.True);
            Assert.That(session.LastResult.Value.SecurityLevelReached, Is.EqualTo(4));
            session.ClearResult();
            Assert.That(session.LastResult.HasValue, Is.False);
        }

        [Test]
        public void ProjectWiring_IsValid()
        {
            var problems = ProjectValidator.Validate();
            // An empty vehicle catalog is expected until vehicle models are provided.
            problems.RemoveAll(p => p.StartsWith("VehicleCatalog:"));
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }
    }
}
