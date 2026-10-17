using System.IO;
using UnityEngine;

namespace NeonRift.HarborRain.EditorTools
{
    /// <summary>Development aid (Harbor Rain 2.2): tiles review screenshots into one labelled-by-order contact sheet PNG.</summary>
    public static class HarborContactSheet
    {
        public static string Make(string[] files, string output, int columns = 3, int cellWidth = 640)
        {
            int n = files.Length, rows = (n + columns - 1) / columns;
            int cellHeight = cellWidth * 9 / 16;
            var sheet = new Texture2D(columns * cellWidth, rows * cellHeight, TextureFormat.RGB24, false);
            var black = new Color32[sheet.width * sheet.height];
            sheet.SetPixels32(black);
            var rt = RenderTexture.GetTemporary(cellWidth, cellHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var cell = new Texture2D(cellWidth, cellHeight, TextureFormat.RGB24, false);
            for (int i = 0; i < n; i++)
            {
                if (!File.Exists(files[i])) continue;
                var src = new Texture2D(2, 2);
                src.LoadImage(File.ReadAllBytes(files[i]));
                Graphics.Blit(src, rt);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                cell.ReadPixels(new Rect(0, 0, cellWidth, cellHeight), 0, 0);
                cell.Apply();
                RenderTexture.active = prev;
                int cx = i % columns, cy = rows - 1 - i / columns;
                sheet.SetPixels(cx * cellWidth, cy * cellHeight, cellWidth, cellHeight, cell.GetPixels());
                Object.DestroyImmediate(src);
            }
            RenderTexture.ReleaseTemporary(rt);
            sheet.Apply();
            File.WriteAllBytes(output, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);
            Object.DestroyImmediate(cell);
            return output;
        }
    }
}
