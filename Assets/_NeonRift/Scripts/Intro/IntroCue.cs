using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NeonRift.Intro
{
    /// <summary>What an intro cue clip does while the Timeline plays it.</summary>
    public enum IntroCueKind
    {
        /// <summary>Black fades away over the clip.</summary>
        FadeIn,
        /// <summary>A dip to black and back (a soft cut).</summary>
        Dip,
        /// <summary>Text card: <see cref="IntroCueClip.text"/> and <see cref="IntroCueClip.subtitle"/>, fading in and out.</summary>
        Card,
        /// <summary>NEON RIFT title reveal, then NIGHT RUN.</summary>
        Title,
        /// <summary>Security-camera overlay and grade for the clip's length.</summary>
        Surveillance,
        /// <summary>One-shot: the lineup's headlights switch on.</summary>
        Headlights,
        /// <summary>One-shot: engines start.</summary>
        Ignition,
        /// <summary>One-shot: the rival cars drive away.</summary>
        Departure,
        /// <summary>Short white flash (impact on a cut).</summary>
        Flash,
        /// <summary>"Skip" hint visible for the clip's length.</summary>
        SkipHint,
        /// <summary>Letterbox bars for the clip's length.</summary>
        Letterbox,
        /// <summary>One-shot: the crew garage's lights strike, bank by bank.</summary>
        GarageLights,
        /// <summary>One-shot: the garage's roller door lifts.</summary>
        DoorOpen
    }

    /// <summary>Receives cue clips from the <see cref="IntroCueTrack"/> (implemented by the intro scene entry).</summary>
    public interface IIntroCueHandler
    {
        /// <summary>Every frame a clip is active. <paramref name="progress"/> 0..1 over the clip, <paramref name="seconds"/> = clip local time.</summary>
        void OnCue(IntroCueKind kind, string text, string subtitle, float progress, float seconds, float duration);
        void OnCueStart(IntroCueKind kind);
        void OnCueEnd(IntroCueKind kind);
    }

    [Serializable]
    public sealed class IntroCueBehaviour : PlayableBehaviour
    {
        public IntroCueKind kind;
        public string text;
        public string subtitle;
        private bool started;
        private IIntroCueHandler handler;

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            if (playerData is not IIntroCueHandler h) return;
            handler = h;
            if (!started)
            {
                started = true;
                handler.OnCueStart(kind);
            }
            float duration = (float)playable.GetDuration();
            float seconds = (float)playable.GetTime();
            handler.OnCue(kind, text, subtitle, duration > 0f ? Mathf.Clamp01(seconds / duration) : 1f, seconds, duration);
        }

        public override void OnBehaviourPause(Playable playable, FrameData info)
        {
            if (!started) return;
            started = false;
            // The handler is a MonoBehaviour: use Unity's null check, not ?. (destroyed objects compare equal to null).
            if (handler is UnityEngine.Object o ? o != null : handler != null) handler.OnCueEnd(kind);
        }
    }
}
