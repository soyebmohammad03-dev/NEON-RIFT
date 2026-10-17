using System;
using System.Collections.Generic;
using UnityEngine;

namespace NeonRift.HarborRain.EditorTools
{
    /// <summary>
    /// Port vehicles (Harbor Rain 2.1): one terminal tractor + skeletal trailer model shared by the parked trucks (batched)
    /// and the moving ones (separate wheels that turn, steer, brake lights, beacon, a headlight on the wet ground), and a
    /// forklift with a real mast, carriage, forks, overhead guard, counterweight, wheels, beacon and work lights; two of
    /// them work loading bays (HarborForklift). Local frame: +z forward, ground at y 0, cab front at z +6.4.
    /// </summary>
    public static partial class HarborRainBuilder
    {
        private static Mesh truckWheel, forkWheel;
        private static Material truckPaintRed, truckPaintBlue, truckPaintWhite, tyreTread, tailLamp, ambLamp, headLamp;

        private static void VehicleMaterials()
        {
            truckPaintRed = Lit("HR_TruckRed", new Color(0.42f, 0.07f, 0.05f), 0.55f, 0.3f, null);
            truckPaintBlue = Lit("HR_TruckBlue", new Color(0.08f, 0.16f, 0.32f), 0.55f, 0.3f, null);
            truckPaintWhite = Lit("HR_TruckWhite", new Color(0.62f, 0.62f, 0.6f), 0.55f, 0.25f, null);
            tyreTread = Lit("HR_Tyre", new Color(0.03f, 0.03f, 0.03f), 0.35f, 0f, null);
            tailLamp = Lit("HR_TailLamp", new Color(0.2f, 0.01f, 0.01f), 0.7f, 0f, new Color(1.2f, 0.05f, 0.03f));
            ambLamp = Lit("HR_AmberLamp", new Color(0.2f, 0.1f, 0f), 0.7f, 0f, new Color(1.6f, 0.7f, 0.05f));
            headLamp = Lit("HR_HeadLamp", new Color(0.3f, 0.3f, 0.3f), 0.9f, 0f, new Color(3.2f, 3.1f, 2.8f));
        }

        /// <summary>A wheel (tyre tube, side walls, hub) on the x axis, written through <paramref name="W"/>.</summary>
        private static void WheelGeo(Func<Vector3, Vector3> W, Vector3 c, float r, float width, int segs = 14)
        {
            for (int i = 0; i < segs; i++)
            {
                float a0 = i / (float)segs * Mathf.PI * 2f, a1 = (i + 1) / (float)segs * Mathf.PI * 2f;
                Vector3 p0 = new(0f, Mathf.Cos(a0) * r, Mathf.Sin(a0) * r), p1 = new(0f, Mathf.Cos(a1) * r, Mathf.Sin(a1) * r);
                Vector3 h = Vector3.right * (width * 0.5f);
                B(tyreTread).Quad(W(c + p0 - h), W(c + p1 - h), W(c + p1 + h), W(c + p0 + h), new Vector2(0, i), new Vector2(0, i + 1), new Vector2(1, i + 1), new Vector2(1, i));
                foreach (float s in new[] { -1f, 1f })
                {
                    B(tyreTread).Tri(W(c + h * s), W(c + p0 * (s > 0 ? 1f : 1f) + h * s), W(c + p1 + h * s), Vector2.zero, Vector2.up, Vector2.one);
                    B(tyreTread).Tri(W(c + h * s), W(c + p1 + h * s), W(c + p0 + h * s), Vector2.zero, Vector2.one, Vector2.up);
                }
                Vector3 q0 = p0 * 0.55f, q1 = p1 * 0.55f, o = Vector3.right * (width * 0.5f + 0.01f);
                B(metal).Tri(W(c + o), W(c + q0 + o), W(c + q1 + o), Vector2.zero, Vector2.up, Vector2.one);
                B(metal).Tri(W(c - o), W(c + q1 - o), W(c + q0 - o), Vector2.zero, Vector2.one, Vector2.up);
            }
        }

        private static Mesh WheelMesh(ref Mesh cache, string name, float r, float width)
        {
            if (cache != null) return cache;
            var saved = batch;
            Begin();
            WheelGeo(v => v, Vector3.zero, r, width);
            // One mesh per material is how Flush works; for a wheel object we want a single mesh with two materials → keep the tyre only (the hub reads through the tread at range).
            var tyre = batch.TryGetValue(tyreTread, out var mb) ? mb : null;
            cache = SaveMesh(tyre.ToMesh(name, false), name);
            batch = saved;
            return cache;
        }

        /// <summary>
        /// A moving port truck (Harbor Rain 2.2): an instance of the shared PF_HR_PortTruck (cab-over tractor + skeletal
        /// trailer) carrying a container in <paramref name="box"/>, driven by a HarborLife mover. HarborVehicleDressing turns
        /// the 16 wheels, steers the front pair, lights the brake lamps, flashes the roof beacon and pitches the body.
        /// </summary>
        private static GameObject TerminalTractor(Transform parent, string name, Material box, AudioClip machinery, UnityEngine.Audio.AudioMixerGroup amb, int paint)
        {
            var rig = PlaceTruck(parent, name, Vector3.zero, Quaternion.identity, paint, box, true);
            var dressing = rig.Root.AddComponent<HarborVehicleDressing>();
            dressing.EditorConfigure(rig.Spin.ToArray(), rig.Steer.ToArray(), TractorWheelR, rig.Brakes.ToArray(), rig.Beacon, rig.Body);
            var hum = Spatial(rig.Root.transform, "Engine", machinery, true, amb, 0.35f, 6f, 60f);
            hum.pitch = 1.25f;
            return rig.Root;
        }
    }
}
