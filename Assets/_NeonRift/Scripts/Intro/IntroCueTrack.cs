using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NeonRift.Intro
{
    /// <summary>Timeline track of intro cues, bound to the object that handles them.</summary>
    [TrackColor(0.1f, 0.75f, 0.9f)]
    [TrackClipType(typeof(IntroCueClip))]
    [TrackBindingType(typeof(IntroSceneEntry))]
    public sealed class IntroCueTrack : TrackAsset
    {
    }
}
