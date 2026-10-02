using System.IO;
using UnityEngine;

namespace NeonRift.EditorTools.District
{
    /// <summary>Renders the scene's main camera from a given pose into a PNG (look-dev and review from tools).</summary>
    public static class ViewCapture
    {
        public static string Capture(Vector3 position, Vector3 euler, string file, int width = 1600, int height = 900, float fieldOfView = 60f)
        {
            var camera = Camera.main;
            if (camera == null) return "no main camera";
            var t = camera.transform;
            var (oldPosition, oldRotation, oldFov) = (t.position, t.rotation, camera.fieldOfView);
            var brain = camera.GetComponent<Unity.Cinemachine.CinemachineBrain>();
            bool brainEnabled = brain != null && brain.enabled;
            if (brain != null) brain.enabled = false;
            var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            try
            {
                t.SetPositionAndRotation(position, Quaternion.Euler(euler));
                camera.fieldOfView = fieldOfView;
                camera.targetTexture = rt;
                camera.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllBytes(file, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                return file;
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
                t.SetPositionAndRotation(oldPosition, oldRotation);
                camera.fieldOfView = oldFov;
                if (brain != null) brain.enabled = brainEnabled;
            }
        }
    }
}
