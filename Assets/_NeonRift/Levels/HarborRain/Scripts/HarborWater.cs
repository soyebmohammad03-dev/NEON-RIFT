using UnityEngine;

namespace NeonRift.HarborRain
{
    /// <summary>
    /// CPU mirror of the shared water maths in <c>Shaders/HarborWater.hlsl</c> (noise and the four Gerstner swells), and the
    /// globals both sides read. Keeping the two identical lets gameplay (spray emitters, camera, validation) know where
    /// the rendered water actually is.
    /// </summary>
    public static class HarborWater
    {
        public static float Time, Storm, Level = -2.2f, ShoreZ = 300f;

        private static readonly int TimeId = Shader.PropertyToID("_HR_Time");
        private static readonly int StormId = Shader.PropertyToID("_HR_Storm");
        private static readonly int LevelId = Shader.PropertyToID("_HR_Level");
        private static readonly int ShoreId = Shader.PropertyToID("_HR_ShoreZ");
        private static readonly int LightningId = Shader.PropertyToID("_HR_Lightning");

        public static void Push(float storm, float lightning, float level, float shoreZ)
        {
            Time = UnityEngine.Time.time;
            Storm = storm;
            Level = level;
            ShoreZ = shoreZ;
            Shader.SetGlobalFloat(TimeId, Time);
            Shader.SetGlobalFloat(StormId, storm);
            Shader.SetGlobalFloat(LevelId, level);
            Shader.SetGlobalFloat(ShoreId, shoreZ);
            Shader.SetGlobalFloat(LightningId, lightning);
        }

        private static float Frac(float x) => x - Mathf.Floor(x);

        public static float Hash(Vector2 p)
        {
            p = new Vector2(Frac(p.x * 123.34f), Frac(p.y * 456.21f));
            float d = p.x * (p.x + 45.32f) + p.y * (p.y + 45.32f);
            p += new Vector2(d, d);
            return Frac(p.x * p.y);
        }

        public static float Noise(Vector2 p)
        {
            var i = new Vector2(Mathf.Floor(p.x), Mathf.Floor(p.y));
            var f = p - i;
            var u = new Vector2(f.x * f.x * (3f - 2f * f.x), f.y * f.y * (3f - 2f * f.y));
            float a = Hash(i), b = Hash(i + new Vector2(1f, 0f)), c = Hash(i + new Vector2(0f, 1f)), d = Hash(i + Vector2.one);
            return Mathf.Lerp(Mathf.Lerp(a, b, u.x), Mathf.Lerp(c, d, u.x), u.y);
        }

        private static Vector3 Gerstner(Vector4 wave, Vector2 p, float t, float amp)
        {
            float k = 6.28318f / wave.w;
            float c = Mathf.Sqrt(9.8f / k);
            var d = new Vector2(wave.x, wave.y).normalized;
            float f = k * (Vector2.Dot(d, p) - c * t);
            float a = wave.z / k * amp;
            return new Vector3(d.x * a * Mathf.Cos(f), a * Mathf.Sin(f), d.y * a * Mathf.Cos(f));
        }

        /// <summary>Sea surface displacement at a still-water point (x, z) — same as SeaDisplace() in the shader.</summary>
        public static Vector3 SeaDisplace(Vector2 p)
        {
            float t = Time;
            float shore = p.y - ShoreZ;
            float amp = Mathf.Lerp(0.5f, 0.95f, Mathf.Clamp01(Storm)) * Mathf.Lerp(0.38f, 1f, Mathf.Clamp01(shore / 170f));
            var o = Gerstner(new Vector4(0.15f, -1f, 0.2f, 90f), p, t, amp);
            o += Gerstner(new Vector4(-0.35f, -1f, 0.18f, 52f), p, t * 1.05f, amp);
            o += Gerstner(new Vector4(0.6f, -1f, 0.15f, 23f), p, t, amp);
            o += Gerstner(new Vector4(-0.8f, -0.6f, 0.12f, 11f), p, t * 1.1f, amp * 0.8f);
            float near = Mathf.Clamp01(1f - shore / 40f);
            o.y += near * (Mathf.Sin(p.x * 0.31f + t * 2.3f) * Mathf.Sin(p.y * 0.47f - t * 1.9f)) * 0.3f * amp;
            return o;
        }

        /// <summary>Sea surface height (world y) near a world point (ignores the small horizontal shift).</summary>
        public static float SurfaceY(Vector3 world) => Level + SeaDisplace(new Vector2(world.x, world.z)).y;
    }
}
