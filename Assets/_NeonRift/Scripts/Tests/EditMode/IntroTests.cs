using System.Linq;
using NeonRift.Game;
using NeonRift.Intro;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NeonRift.Tests
{
    public sealed class IntroTests
    {
        private const string Key = "NeonRift.Tests.IntroSeen";

        private static IntroSettings Settings(IntroSettings.Policy policy)
        {
            var s = ScriptableObject.CreateInstance<IntroSettings>();
            s.EditorConfigure(policy, canSkip: true);
            var so = new SerializedObject(s);
            so.FindProperty("seenKey").stringValue = Key;
            so.ApplyModifiedPropertiesWithoutUndo();
            return s;
        }

        [TearDown]
        public void TearDown() => PlayerPrefs.DeleteKey(Key);

        [Test]
        public void FirstLaunchOnly_PlaysUntilSeen()
        {
            var s = Settings(IntroSettings.Policy.FirstLaunchOnly);
            s.ResetSeen();
            Assert.IsTrue(s.ShouldPlayOnBoot());
            s.MarkSeen();
            Assert.IsFalse(s.ShouldPlayOnBoot());
            Object.DestroyImmediate(s);
        }

        [Test]
        public void AlwaysAndNever_IgnoreTheSeenFlag()
        {
            var always = Settings(IntroSettings.Policy.Always);
            var never = Settings(IntroSettings.Policy.Never);
            always.MarkSeen();
            Assert.IsTrue(always.ShouldPlayOnBoot());
            never.ResetSeen();
            Assert.IsFalse(never.ShouldPlayOnBoot());
            Object.DestroyImmediate(always);
            Object.DestroyImmediate(never);
        }

        [Test]
        public void VideoModeWithoutAClip_FallsBackToRealtime()
        {
            var s = Settings(IntroSettings.Policy.Always);
            var so = new SerializedObject(s);
            so.FindProperty("mode").enumValueIndex = (int)IntroSettings.Mode.Video;
            so.ApplyModifiedPropertiesWithoutUndo();
            Assert.AreEqual(IntroSettings.Mode.Realtime, s.PlaybackMode);
            Object.DestroyImmediate(s);
        }

        [Test]
        public void GameConfig_PointsAtTheIntro()
        {
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("t:GameConfig")[0]));
            Assert.IsNotNull(config.Intro, "GameConfig has IntroSettings");
            Assert.AreEqual("NightRun", config.IntroScene);
        }

        [Test]
        public void NightRunScene_HasAWiredIntro()
        {
            var scene = EditorSceneManager.OpenScene("Assets/_NeonRift/Scenes/NightRun.unity", OpenSceneMode.Additive);
            try
            {
                var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Intro");
                Assert.IsNotNull(root, "Intro root");
                Assert.IsNotNull(root.GetComponent<IntroSceneEntry>());
                var director = root.GetComponent<PlayableDirector>();
                Assert.IsFalse(director.playOnAwake, "the intro must not start on its own (missions share the scene)");
                var timeline = (TimelineAsset)director.playableAsset;
                Assert.IsNotNull(timeline);
                var cm = timeline.GetOutputTracks().OfType<CinemachineTrack>().Single();
                Assert.GreaterOrEqual(cm.GetClips().Count(), 15, "shots");
                foreach (var clip in cm.GetClips())
                {
                    var shot = (CinemachineShot)clip.asset;
                    Assert.IsNotNull(director.GetReferenceValue(shot.VirtualCamera.exposedName, out _), $"shot {clip.displayName} has a camera");
                }
                var cues = timeline.GetOutputTracks().OfType<IntroCueTrack>().Single();
                Assert.IsTrue(cues.GetClips().Any(c => ((IntroCueClip)c.asset).kind == IntroCueKind.Title));
                Assert.AreSame(root.GetComponent<IntroSceneEntry>(), director.GetGenericBinding(cues));
                foreach (var audio in timeline.GetOutputTracks().OfType<AudioTrack>())
                    Assert.IsNotNull(director.GetGenericBinding(audio), $"audio track {audio.name} is bound");
                // Inert during missions: shot cameras, overlay and stage lights are off until the intro enters.
                Assert.IsFalse(root.transform.Find("Shots").gameObject.activeSelf);
                Assert.IsFalse(root.transform.Find("IntroOverlay").gameObject.activeSelf);
                Assert.IsTrue(root.GetComponentsInChildren<Light>(true).All(l => !l.enabled));
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
