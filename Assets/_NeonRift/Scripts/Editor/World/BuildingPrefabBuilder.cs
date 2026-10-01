using System.Collections.Generic;
using System.IO;
using System.Linq;
using NeonRift.EditorTools.Vehicles;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace NeonRift.EditorTools.World
{
    /// <summary>
    /// Wraps third-party building models in prefabs with a ground-centre pivot, optional box collider,
    /// and URP materials. Source models are referenced, never modified.
    /// </summary>
    public static class BuildingPrefabBuilder
    {
        public const string PrefabFolder = "Assets/_NeonRift/Prefabs/Buildings";
        public const string ArtFolder = "Assets/_NeonRift/Art/Buildings";

        public struct Result
        {
            public string PrefabPath;
            public Vector3 Size;
            public int Triangles;
        }

        /// <summary>Whole model as one building. groundKeyword: renderer whose top surface is street level.</summary>
        public static Result FromModel(GameObject model, string prefabName, float scale, string groundKeyword, bool collider)
        {
            var root = new GameObject(prefabName);
            try
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                inst.name = "Model";
                inst.transform.localScale *= scale;
                foreach (var a in inst.GetComponentsInChildren<Animation>(true)) a.enabled = false;
                var renderers = inst.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled).ToList();
                GltfMaterialFixer.RemapClearcoat(renderers, $"{ArtFolder}/{prefabName}/Materials");
                return Finish(root, inst.transform, renderers, groundKeyword, collider, prefabName);
            }
            finally { Object.DestroyImmediate(root); }
        }

        /// <summary>One child mesh of a multi-building model as its own building, with attached extras (e.g. roof flares).</summary>
        public static Result FromMeshes(string prefabName, IEnumerable<(Transform source, Material[] materials)> parts, bool collider, float scale = 1f)
        {
            var root = new GameObject(prefabName);
            try
            {
                var holder = new GameObject("Model").transform;
                holder.SetParent(root.transform, false);
                holder.localScale = Vector3.one * scale;
                var renderers = new List<Renderer>();
                foreach (var (source, materials) in parts)
                {
                    var go = new GameObject(source.name);
                    go.transform.SetParent(holder, false);
                    go.transform.localPosition = source.position;
                    go.transform.localRotation = source.rotation;
                    go.transform.localScale = source.lossyScale;
                    go.AddComponent<MeshFilter>().sharedMesh = source.GetComponent<MeshFilter>().sharedMesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterials = materials;
                    renderers.Add(mr);
                }
                return Finish(root, holder, renderers, null, collider, prefabName);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static Result Finish(GameObject root, Transform content, List<Renderer> renderers, string groundKeyword, bool collider, string prefabName)
        {
            var b = Bounds(renderers);
            float groundY = b.min.y;
            if (!string.IsNullOrEmpty(groundKeyword))
            {
                var ground = renderers.Where(r => r.name.IndexOf(groundKeyword, System.StringComparison.OrdinalIgnoreCase) >= 0
                                               || r.transform.parent.name.IndexOf(groundKeyword, System.StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                if (ground.Count > 0) groundY = ground.Max(r => r.bounds.max.y);
            }
            content.position -= new Vector3(b.center.x, groundY, b.center.z);
            b = Bounds(renderers);

            if (collider)
            {
                var box = root.AddComponent<BoxCollider>();
                box.center = new Vector3(b.center.x, b.max.y * 0.5f, b.center.z);
                box.size = new Vector3(b.size.x, b.max.y, b.size.z);
                root.layer = LayerMask.NameToLayer("Environment");
            }
            foreach (var r in renderers)
            {
                r.gameObject.layer = LayerMask.NameToLayer("Environment");
                GameObjectUtility.SetStaticEditorFlags(r.gameObject,
                    StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
            }

            VehiclePrefabBuilder.EnsureFolder(PrefabFolder);
            var path = $"{PrefabFolder}/{prefabName}.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            int tris = renderers.Sum(r => { var m = r.GetComponent<MeshFilter>().sharedMesh; int n = 0; for (int i = 0; i < m.subMeshCount; i++) n += (int)m.GetIndexCount(i) / 3; return n; });
            return new Result { PrefabPath = path, Size = new Vector3(b.size.x, b.max.y, b.size.z), Triangles = tris };
        }

        private static Bounds Bounds(List<Renderer> renderers)
        {
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            return b;
        }

        // ---------- Materials ----------

        /// <summary>URP Lit material with optional packed smoothness, normal and emission.</summary>
        public static Material LitMaterial(string path, Texture2D baseMap, Texture2D metallicSmoothness, Texture2D normal,
                                           Texture2D emission, float emissionIntensity, bool alphaClip)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path) ?? new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.shader = Shader.Find("Universal Render Pipeline/Lit");
            mat.SetTexture("_BaseMap", baseMap);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Metallic", 0f);
            if (metallicSmoothness != null)
            {
                mat.SetTexture("_MetallicGlossMap", metallicSmoothness);
                mat.SetFloat("_Smoothness", 1f);
                mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            }
            else mat.SetFloat("_Smoothness", 0.35f);
            if (normal != null) { mat.SetTexture("_BumpMap", normal); mat.EnableKeyword("_NORMALMAP"); }
            if (emission != null)
            {
                mat.SetTexture("_EmissionMap", emission);
                mat.SetColor("_EmissionColor", Color.white * emissionIntensity);
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            }
            else
            {
                mat.SetColor("_EmissionColor", Color.black);
                mat.DisableKeyword("_EMISSION");
            }
            mat.SetFloat("_AlphaClip", alphaClip ? 1f : 0f);
            mat.SetFloat("_Cutoff", 0.5f);
            if (alphaClip) mat.EnableKeyword("_ALPHATEST_ON"); else mat.DisableKeyword("_ALPHATEST_ON");
            mat.enableInstancing = true;
            Save(mat, path);
            return mat;
        }

        /// <summary>Additive unlit material for small glowing sprites such as aircraft warning lights.</summary>
        public static Material AdditiveMaterial(string path, Texture2D rgba, Color tint)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path) ?? new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            mat.shader = Shader.Find("Universal Render Pipeline/Unlit");
            mat.SetTexture("_BaseMap", rgba);
            mat.SetColor("_BaseColor", tint);
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 2f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.One);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_Cull", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
            Save(mat, path);
            return mat;
        }

        private static void Save(Material mat, string path)
        {
            VehiclePrefabBuilder.EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            if (!AssetDatabase.Contains(mat)) AssetDatabase.CreateAsset(mat, path);
            else EditorUtility.SetDirty(mat);
        }

        // ---------- Texture preparation ----------

        /// <summary>Writes a URP metallic/smoothness map (R = metallic 0, A = 1 - roughness).</summary>
        public static Texture2D PackSmoothness(string roughnessFile, string outPath)
        {
            var src = Load(roughnessFile);
            var px = src.GetPixels32();
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(0, 0, 0, (byte)(255 - px[i].r));
            return Write(px, src.width, src.height, outPath, linear: true);
        }

        /// <summary>Writes RGB from one file and A from the red channel of another.</summary>
        public static Texture2D PackAlpha(string rgbFile, string alphaFile, string outPath)
        {
            var rgb = Load(rgbFile);
            var a = Load(alphaFile);
            if (a.width != rgb.width || a.height != rgb.height) a = Resize(a, rgb.width, rgb.height);
            var p = rgb.GetPixels32();
            var q = a.GetPixels32();
            for (int i = 0; i < p.Length; i++) p[i].a = q[i].r;
            return Write(p, rgb.width, rgb.height, outPath, linear: false);
        }

        public static void ConfigureTexture(string path, bool normalMap, bool linear, int maxSize = 2048)
        {
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            imp.sRGBTexture = !linear && !normalMap;
            imp.maxTextureSize = maxSize;
            imp.mipmapEnabled = true;
            imp.anisoLevel = 4;
            imp.textureCompression = TextureImporterCompression.CompressedHQ;
            imp.SaveAndReimport();
        }

        private static Texture2D Load(string file)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            t.LoadImage(File.ReadAllBytes(file));
            return t;
        }

        private static Texture2D Resize(Texture2D src, int w, int h)
        {
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            t.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            t.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return t;
        }

        private static Texture2D Write(Color32[] pixels, int w, int h, string outPath, bool linear)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false, linear);
            t.SetPixels32(pixels);
            t.Apply();
            VehiclePrefabBuilder.EnsureFolder(Path.GetDirectoryName(outPath).Replace('\\', '/'));
            File.WriteAllBytes(outPath, t.EncodeToPNG());
            AssetDatabase.ImportAsset(outPath, ImportAssetOptions.ForceSynchronousImport);
            ConfigureTexture(outPath, false, linear);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
        }
    }
}
