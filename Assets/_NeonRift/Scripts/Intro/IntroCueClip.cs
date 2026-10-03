using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NeonRift.Intro
{
    [Serializable]
    public sealed class IntroCueClip : PlayableAsset, ITimelineClipAsset
    {
        public IntroCueKind kind;
        public string text;
        public string subtitle;

        public ClipCaps clipCaps => ClipCaps.None;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<IntroCueBehaviour>.Create(graph);
            var b = playable.GetBehaviour();
            b.kind = kind;
            b.text = text;
            b.subtitle = subtitle;
            return playable;
        }
    }
}
