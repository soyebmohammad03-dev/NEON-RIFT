using System.IO;
using UnityEditor;
using UnityEngine;

namespace NeonRift.HarborRain.EditorTools
{
    /// <summary>
    /// Harbor Rain's surface materials (Harbor Rain 2.0), built from CC0 Poly Haven texture sets in Imported/PolyHaven:
    /// wet asphalt for the roads, worn concrete for the yards, corrugated cladding (four paint colours) for the sheds and
    /// warehouses, precast concrete for walls, kerbs and plinths. Wetness comes from each set's own roughness map
    /// (smoothness = a wet range remapped from 1 − roughness), so the ground shows aggregate, cracks, patches and drier
    /// high spots instead of one uniform mirror; standing water stays in the separate puddle and flood meshes. These are
    /// new Harbor Rain materials — Night Run's district materials are not modified.
    /// </summary>
    public static partial class HarborRainBuilder
    {
        private const string PolyTex = Root + "/Imported/PolyHaven/Textures";
        private static Material claddingRed, claddingGreen, claddingWhite;

        private static Texture2D LoadTex(string id, string kind, bool normal)
        {
            string path = $"{PolyTex}/{id}/{id}_{kind}_1k.jpg";
            if (AssetImporter.GetAtPath(path) is TextureImporter ti)
            {
                bool dirty = false;
                if (normal && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; dirty = true; }
                if (!normal && kind != "diff" && ti.sRGBTexture) { ti.sRGBTexture = false; dirty = true; }
                if (ti.maxTextureSize != 1024) { ti.maxTextureSize = 1024; dirty = true; }
                if (dirty) ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Metallic (R = 0) + smoothness (A) map from a roughness map: A = lerp(wetMax, wetMin, roughness^shape).</summary>
        private static Texture2D GlossFromRoughness(string id, float wetMin, float wetMax, float metal = 0f)
        {
            string src = $"{PolyTex}/{id}/{id}_rough_1k.jpg";
            string dst = $"{Gen}/Materials/HR_{id}_MetalGloss.png";
            var t = new Texture2D(2, 2);
            t.LoadImage(File.ReadAllBytes(src));
            var px = t.GetPixels();
            for (int i = 0; i < px.Length; i++)
            {
                float r = px[i].r;
                float a = Mathf.Lerp(wetMax, wetMin, Mathf.Pow(r, 0.8f));
                px[i] = new Color(metal, 0f, 0f, a);
            }
            t.SetPixels(px);
            File.WriteAllBytes(dst, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(dst);
            if (AssetImporter.GetAtPath(dst) is TextureImporter ti && (ti.sRGBTexture || ti.maxTextureSize != 1024))
            {
                ti.sRGBTexture = false; ti.maxTextureSize = 1024; ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(dst);
        }

        private static Material Surface(string name, string id, Color tint, float scale, float wetMin, float wetMax, float bump, float metal = 0f)
        {
            var m = Asset(name, Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", tint);
            m.SetTexture("_BaseMap", LoadTex(id, "diff", false));
            m.SetTextureScale("_BaseMap", Vector2.one * scale);
            m.SetTexture("_BumpMap", LoadTex(id, "nor_gl", true));
            m.SetFloat("_BumpScale", bump);
            m.EnableKeyword("_NORMALMAP");
            m.SetTexture("_MetallicGlossMap", GlossFromRoughness(id, wetMin, wetMax, metal));
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.SetFloat("_Smoothness", 1f);
            m.SetFloat("_SmoothnessTextureChannel", 0f);
            m.SetFloat("_Metallic", metal);
            var ao = LoadTex(id, "ao", false);
            if (ao != null) { m.SetTexture("_OcclusionMap", ao); m.SetFloat("_OcclusionStrength", 0.8f); m.EnableKeyword("_OCCLUSIONMAP"); }
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        private static void BuildSurfaceMaterials()
        {
            // Road UVs are world / 6 m, yard ground world / 8 m, walls and boxes world / 2–4 m.
            // 2.2: the yard's wet range was reflecting the storm sky as a pale sheen across every open area at grazing angles;
            // drier, darker concrete keeps the wet look in the textured lows and leaves sheen to the puddle / flood meshes.
            // Bump kept low: the aggregate normals at high wet smoothness aliased into gold "glitter" under the sodium lamps
            // at grazing angles (no specular anti-aliasing in URP Lit).
            wetAsphalt = Surface("HR_RoadAsphalt", "asphalt_02", new Color(0.44f, 0.44f, 0.45f), 2.4f, 0.3f, 0.66f, 0.28f);
            wetConcrete = Surface("HR_YardConcrete", "concrete_floor_worn_001", new Color(0.4f, 0.395f, 0.385f), 2.6f, 0.2f, 0.54f, 0.3f);
            concreteDark = Surface("HR_PrecastConcrete", "precast_concrete_wall", new Color(0.42f, 0.42f, 0.42f), 1.2f, 0.2f, 0.55f, 0.8f);
            cladding = Surface("HR_CladdingGrey", "corrugated_iron_02", new Color(0.55f, 0.6f, 0.64f), 2f, 0.3f, 0.6f, 1f, 0.35f);
            claddingDark = Surface("HR_CladdingNavy", "corrugated_iron_02", new Color(0.22f, 0.28f, 0.36f), 2f, 0.3f, 0.6f, 1f, 0.35f);
            claddingRed = Surface("HR_CladdingRed", "corrugated_iron_02", new Color(0.5f, 0.18f, 0.12f), 2f, 0.3f, 0.6f, 1f, 0.3f);
            claddingGreen = Surface("HR_CladdingGreen", "corrugated_iron_02", new Color(0.2f, 0.36f, 0.3f), 2f, 0.3f, 0.6f, 1f, 0.3f);
            claddingWhite = Surface("HR_CladdingWhite", "corrugated_iron_02", new Color(0.78f, 0.78f, 0.76f), 2f, 0.3f, 0.6f, 1f, 0.3f);
            stainedConcrete = stainedConcrete != null ? stainedConcrete : concreteDark;
            report.AppendLine("surfaces: textured wet asphalt, worn yard concrete, precast concrete, corrugated cladding ×5 colours (Poly Haven CC0 sets; smoothness from each roughness map)");
        }
    }
}
