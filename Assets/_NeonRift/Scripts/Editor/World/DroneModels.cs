using System.Linq;
using NeonRift.EditorTools.District;
using NeonRift.EditorTools.Vehicles;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace NeonRift.EditorTools.World
{
    /// <summary>
    /// Prefabs for the two CC BY drone models (see each folder's ATTRIBUTION.txt): the Mech Drone (security pursuit
    /// drone: textured URP material, its looping hover animation as a legacy clip, thruster flames as additive glow)
    /// and the Buster Drone (a large sentry, posed mid-flight from its lift-off clip). Rebuilt idempotently by the
    /// Night Run builder; the source files are never edited.
    /// </summary>
    public static class DroneModels
    {
        public const string PrefabFolder = "Assets/_NeonRift/Prefabs/Drones";
        public const string MechPrefabPath = PrefabFolder + "/PF_Drone_Mech.prefab";
        public const string BusterPrefabPath = PrefabFolder + "/PF_Drone_Buster.prefab";
        private const string MechFolder = "Assets/ThirdParty/Sketchfab/Drones/Mech_Drone/";
        private const string MechModel = MechFolder + "Drone.FBX";
        private const string BusterModel = "Assets/ThirdParty/Sketchfab/Drones/Buster_Drone/buster_drone.glb";
        /// <summary>Lift-off clip time the sentry is posed at (arms out, hovering), s.</summary>
        private const float BusterPoseTime = 14f;

        /// <summary>Security pursuit drone: model under a "Model" child, forward = +Z, about 2.8 m across.</summary>
        public static GameObject Mech()
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(MechModel);
            if (importer.animationType != ModelImporterAnimationType.Legacy)
            {
                importer.animationType = ModelImporterAnimationType.Legacy;
                var clips = importer.defaultClipAnimations;
                foreach (var c in clips) c.wrapMode = WrapMode.Loop;
                importer.clipAnimations = clips;
                importer.SaveAndReimport();
            }
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(MechModel);
            var clip = AssetDatabase.LoadAllAssetsAtPath(MechModel).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));

            var body = DistrictKit.Lit("Drone_Mech_Body", Color.white, 0.55f, 0.4f, Texture(MechFolder + "Drone_diff.jpg"), Texture(MechFolder + "Drone_normal.jpg", true),
                                       1f, null, Texture(MechFolder + "Drone_emissive.jpg"), new Color(2.2f, 2.2f, 2.2f));
            body.SetTexture("_OcclusionMap", Texture(MechFolder + "Drone_ao.jpg"));
            body.EnableKeyword("_OCCLUSIONMAP");
            var flame = DistrictKit.Glow("Drone_Mech_Flame", new Color(2.4f, 1.1f, 0.35f), Texture(MechFolder + "Fire_Alpha.jpg"), 1f);

            var root = new GameObject("PF_Drone_Mech");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = "Model";
            instance.transform.SetParent(root.transform, false);
            instance.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);   // the model faces -Z
            foreach (var r in instance.GetComponentsInChildren<Renderer>(true))
            {
                r.sharedMaterials = r.sharedMaterials.Select(m => m != null && m.name.StartsWith("Fire") ? flame : body).ToArray();
                r.shadowCastingMode = ShadowCastingMode.Off;
                if (r is SkinnedMeshRenderer s) s.updateWhenOffscreen = false;
            }
            if (clip != null)
            {
                // A legacy-rig import already carries an Animation component.
                var anim = instance.TryGetComponent(out Animation existing) ? existing : instance.AddComponent<Animation>();
                anim.clip = clip;
                anim.AddClip(clip, clip.name);
                anim.wrapMode = WrapMode.Loop;
                anim.playAutomatically = true;
                anim.cullingType = AnimationCullingType.BasedOnRenderers;
            }
            return Save(root, MechPrefabPath);
        }

        /// <summary>Large sentry drone: posed mid-flight, model under a "Model" child, about 4.7 m across.</summary>
        public static GameObject Buster()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(BusterModel);
            var clip = AssetDatabase.LoadAllAssetsAtPath(BusterModel).OfType<AnimationClip>().FirstOrDefault();
            var root = new GameObject("PF_Drone_Buster");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = "Model";
            instance.transform.SetParent(root.transform, false);
            if (clip != null) clip.SampleAnimation(instance, Mathf.Min(BusterPoseTime, clip.length));
            // A posed statue: no animator, no shadows (it hovers high over lit streets).
            foreach (var a in instance.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(a);
            // The source scene includes its display floor ("Boden"): drop it.
            foreach (var r in instance.GetComponentsInChildren<Renderer>(true))
                if (r.sharedMaterials.Any(m => m != null && m.name.StartsWith("Boden"))) Object.DestroyImmediate(r.gameObject);
            // Bake the posed skin into static meshes: a statue should not pay for skinning every frame.
            int baked = 0;
            foreach (var skin in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var bake = new Mesh();
                skin.BakeMesh(bake, true);
                // Overwritten in place on rebuilds (deleting a referenced asset can raise a dialog).
                var mesh = DistrictKit.SaveMesh(bake, $"Drone_Buster_Baked_{baked++}");
                var go = skin.gameObject;
                var materials = skin.sharedMaterials;
                Object.DestroyImmediate(skin);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = materials;
            }
            foreach (var r in instance.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = ShadowCastingMode.Off;
            return Save(root, BusterPrefabPath);
        }

        private static GameObject Save(GameObject root, string path)
        {
            VehiclePrefabBuilder.EnsureFolder(PrefabFolder);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static Texture2D Texture(string path, bool normal = false)
        {
            if (normal && AssetImporter.GetAtPath(path) is TextureImporter ti && ti.textureType != TextureImporterType.NormalMap)
            {
                ti.textureType = TextureImporterType.NormalMap;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
