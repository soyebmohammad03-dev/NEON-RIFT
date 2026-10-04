using System;
using UnityEngine;
using UnityEngine.Playables;

namespace NeonRift.Intro
{
    /// <summary>
    /// Street and sky life for the opening cinematic, driven by the intro Timeline's clock so every shot looks the
    /// same on every play (and after a Skip): lanes of traffic (visual-only copies of the pack cars, wheels turning)
    /// on the avenues the shots look down, patrol drones sweeping searchlights over the streets, drones circling the
    /// Data Core and sky traffic across the skyline. Each element is only active inside its shot window, so the
    /// lights and skinned drones cost nothing outside it. Built by the IntroBuilder; dormant during missions.
    /// </summary>
    public sealed class IntroCityLife : MonoBehaviour
    {
        [Serializable]
        public struct Lane
        {
            public Vector3 from, to;
            [Min(0.1f)] public float speed;
            [Tooltip("Gap between the cars on the lane, m.")]
            [Min(1f)] public float spacing;
            [Tooltip("Timeline window the lane is live in, s.")]
            public float start, end;
            public Transform[] cars;
        }

        [Serializable]
        public struct Flight
        {
            public Transform drone;
            public Light searchlight;
            public Transform beam;
            public Vector3 from, to;
            public float start, end;
        }

        [Serializable]
        public struct Orbit
        {
            public Transform drone;
            public Light searchlight;
            public Transform beam;
            public Vector3 centre;
            public float radius, height, period, phase, start, end;
        }

        [SerializeField] private PlayableDirector director;
        [SerializeField] private Lane[] lanes = Array.Empty<Lane>();
        [SerializeField] private Flight[] flights = Array.Empty<Flight>();
        [SerializeField] private Orbit[] orbits = Array.Empty<Orbit>();
        [SerializeField, Min(0.05f)] private float wheelRadius = 0.34f;

        private Transform[][] wheels = Array.Empty<Transform[]>();
        private Quaternion[][] wheelRest = Array.Empty<Quaternion[]>();
        private int[] laneOffset = Array.Empty<int>();

        private void Awake()
        {
            // Wheels of every lane car, in lane order (the pack prefabs name their wheel pivots "Wheel").
            int count = 0;
            laneOffset = new int[lanes.Length];
            for (int l = 0; l < lanes.Length; l++) { laneOffset[l] = count; count += lanes[l].cars?.Length ?? 0; }
            wheels = new Transform[count][];
            wheelRest = new Quaternion[count][];
            for (int l = 0; l < lanes.Length; l++)
                for (int k = 0; k < (lanes[l].cars?.Length ?? 0); k++)
                {
                    var car = lanes[l].cars[k];
                    var list = new System.Collections.Generic.List<Transform>();
                    if (car != null) foreach (var t in car.GetComponentsInChildren<Transform>(true)) if (t.name == "Wheel") list.Add(t);
                    int i = laneOffset[l] + k;
                    wheels[i] = list.ToArray();
                    wheelRest[i] = list.ConvertAll(w => w.localRotation).ToArray();
                }
            Apply(-1f);
        }

        private void OnDisable() => Apply(-1f);

        private void LateUpdate()
        {
            if (director == null) return;
            Apply((float)director.time);
        }

        /// <summary>Places everything for Timeline time <paramref name="t"/> (negative = all hidden).</summary>
        private void Apply(float t)
        {
            for (int l = 0; l < lanes.Length; l++)
            {
                var lane = lanes[l];
                if (lane.cars == null) continue;
                bool live = t >= lane.start && t <= lane.end;
                Vector3 dir = lane.to - lane.from;
                float length = dir.magnitude;
                if (length < 1f) continue;
                dir /= length;
                var rotation = Quaternion.LookRotation(dir);
                for (int k = 0; k < lane.cars.Length; k++)
                {
                    var car = lane.cars[k];
                    if (car == null) continue;
                    if (car.gameObject.activeSelf != live) car.gameObject.SetActive(live);
                    if (!live) continue;
                    float d = Mathf.Repeat(lane.speed * (t - lane.start) + k * lane.spacing, length);
                    car.SetPositionAndRotation(lane.from + dir * d, rotation);
                    int i = laneOffset[l] + k;
                    var w = wheels[i];
                    var angle = Quaternion.Euler(d / wheelRadius * Mathf.Rad2Deg, 0f, 0f);
                    for (int j = 0; j < w.Length; j++) w[j].localRotation = wheelRest[i][j] * angle;
                }
            }
            for (int f = 0; f < flights.Length; f++)
            {
                var fl = flights[f];
                if (fl.drone == null) continue;
                bool live = t >= fl.start && t <= fl.end;
                if (fl.drone.gameObject.activeSelf != live) fl.drone.gameObject.SetActive(live);
                if (!live) continue;
                float k = Mathf.InverseLerp(fl.start, fl.end, t);
                Vector3 dir = fl.to - fl.from;
                Vector3 p = fl.from + dir * k + Vector3.up * Mathf.Sin(t * 1.3f + f) * 0.4f;
                Vector3 flat = new(dir.x, 0f, dir.z);
                fl.drone.SetPositionAndRotation(p, Quaternion.LookRotation(flat.sqrMagnitude > 0.01f ? flat : Vector3.forward) * Quaternion.Euler(12f, 0f, 0f));
                // The searchlight sweeps the street ahead and to the sides.
                Vector3 ground = new Vector3(p.x, 0f, p.z) + flat.normalized * 8f
                                 + Vector3.Cross(Vector3.up, flat.normalized) * Mathf.Sin(t * 1.1f + f * 2f) * 7f;
                Aim(fl.searchlight, fl.beam, ground);
            }
            for (int o = 0; o < orbits.Length; o++)
            {
                var ob = orbits[o];
                if (ob.drone == null) continue;
                bool live = t >= ob.start && t <= ob.end;
                if (ob.drone.gameObject.activeSelf != live) ob.drone.gameObject.SetActive(live);
                if (!live) continue;
                float a = ob.phase + t / Mathf.Max(1f, ob.period) * Mathf.PI * 2f;
                Vector3 radial = new(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 p = ob.centre + radial * ob.radius + Vector3.up * (ob.height + Mathf.Sin(t + o) * 0.6f);
                Vector3 tangent = new(-radial.z, 0f, radial.x);
                ob.drone.SetPositionAndRotation(p, Quaternion.LookRotation(tangent) * Quaternion.Euler(10f, 0f, -8f));
                // Light on the ground inside the circle, drifting: the compound is being searched.
                Aim(ob.searchlight, ob.beam, ob.centre + radial * (ob.radius * 0.45f) + tangent * Mathf.Sin(t * 0.9f + o) * 6f);
            }
        }

        private static void Aim(Light light, Transform beam, Vector3 at)
        {
            if (light != null) light.transform.rotation = Quaternion.LookRotation(at - light.transform.position);
            if (beam == null) return;
            Vector3 from = beam.parent != null ? beam.parent.position : beam.position;
            beam.position = from;
            beam.rotation = Quaternion.LookRotation(at - from) * Quaternion.Euler(90f, 0f, 0f);
            beam.localScale = new Vector3(1f, Mathf.Min(60f, Vector3.Distance(from, at)), 1f);
        }

#if UNITY_EDITOR
        public void EditorConfigure(PlayableDirector playableDirector, Lane[] trafficLanes, Flight[] droneFlights, Orbit[] droneOrbits)
        {
            director = playableDirector;
            lanes = trafficLanes;
            flights = droneFlights;
            orbits = droneOrbits;
        }
#endif
    }
}
