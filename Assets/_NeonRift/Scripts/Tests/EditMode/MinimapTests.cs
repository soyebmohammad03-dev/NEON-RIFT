using NeonRift.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace NeonRift.Tests
{
    /// <summary>North-up minimap projection: the map never rotates, only the markers do.</summary>
    public class MinimapTests
    {
        private static readonly Vector2 Size = new(236f, 236f);

        private static void Near(Vector2 expected, Vector2 actual, float tolerance = 0.01f)
        {
            Assert.That(Vector2.Distance(expected, actual), Is.LessThan(tolerance), $"expected {expected}, got {actual}");
        }

        [Test]
        public void North_IsUp_And_East_IsRight()
        {
            var map = new MinimapProjection(new Vector3(100f, 0f, 50f), Size, 240f);
            var c = Size * 0.5f;
            Near(c, map.ToMap(new Vector3(100f, 0f, 50f)));
            Assert.That(map.ToMap(new Vector3(100f, 0f, 150f)).y, Is.LessThan(c.y), "north (+Z) must be up");
            Assert.That(map.ToMap(new Vector3(200f, 0f, 50f)).x, Is.GreaterThan(c.x), "east (+X) must be right");
            Near(new Vector2(Size.x, c.y), map.ToMap(new Vector3(340f, 0f, 50f)));   // exactly Range east hits the rim
        }

        [Test]
        public void Projection_DoesNotDependOnPlayerHeading()
        {
            // The projection has no heading input at all: a fixed objective maps to the same pixel whichever way
            // the car faces. Drive straight, turn left, turn right, 180: only the centre moves.
            var objective = new Vector3(-80f, 0f, 220f);
            var at = new Vector3(10f, 0f, 20f);
            var map = new MinimapProjection(at, Size, 240f);
            var expected = map.ToMap(objective);
            foreach (float heading in new[] { 0f, -90f, 90f, 180f, 270f })
            {
                Assert.That(MinimapProjection.Heading(Quaternion.Euler(0f, heading, 0f) * Vector3.forward), Is.EqualTo(Mathf.Repeat(heading, 360f)).Within(0.01f));
                Near(expected, new MinimapProjection(at, Size, 240f).ToMap(objective));
            }
        }

        [Test]
        public void ToWorld_RoundTrips()
        {
            var map = new MinimapProjection(new Vector3(-300f, 4f, 812f), Size, 180f);
            var w = new Vector3(-250f, 4f, 700f);
            var back = map.ToWorld(map.ToMap(w));
            Assert.That(Vector3.Distance(w, back), Is.LessThan(0.01f));
        }

        [Test]
        public void Heading_IsCompassClockwiseFromNorth()
        {
            Assert.That(MinimapProjection.Heading(Vector3.forward), Is.EqualTo(0f).Within(0.01f));
            Assert.That(MinimapProjection.Heading(Vector3.right), Is.EqualTo(90f).Within(0.01f));
            Assert.That(MinimapProjection.Heading(Vector3.back), Is.EqualTo(180f).Within(0.01f));
            Assert.That(MinimapProjection.Heading(Vector3.left), Is.EqualTo(270f).Within(0.01f));
            Assert.That(MinimapProjection.Heading(Vector3.up), Is.EqualTo(0f), "degenerate forward falls back to north");
        }

        [Test]
        public void PlayerArrow_PointsAlongWorldHeading()
        {
            var tip = new Vector2(0f, -9f);   // the arrow is drawn pointing up (north)
            foreach (var dir in new[] { Vector3.forward, Vector3.right, Vector3.back, Vector3.left, new Vector3(1f, 0f, 1f) })
            {
                var map = new MinimapProjection(Vector3.zero, Size, 240f);
                var screenDir = (map.ToMap(dir * 50f) - Size * 0.5f).normalized;   // where that heading is on the map
                var arrowDir = MinimapProjection.MarkerRotation(tip, MinimapProjection.Heading(dir)).normalized;
                Near(screenDir, arrowDir, 0.001f);
            }
        }
    }
}
