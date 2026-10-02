using UnityEngine;

namespace NeonRift.Gameplay
{
    /// <summary>Non-vehicle sounds of a mission: ambience bed, interaction loop and HUD/story cues.</summary>
    [CreateAssetMenu(menuName = "Neon Rift/Missions/Mission Audio Set", fileName = "MissionAudio")]
    public sealed class MissionAudioSet : ScriptableObject
    {
        [Header("Loops")]
        [SerializeField] private AudioClip ambience;
        [SerializeField, Range(0f, 1f)] private float ambienceVolume = 0.55f;
        [Tooltip("Plays while an interaction is held; pitch rises with progress.")]
        [SerializeField] private AudioClip interactLoop;
        [SerializeField, Range(0f, 1f)] private float interactVolume = 0.6f;

        [Header("Cues")]
        [SerializeField] private AudioClip objective;
        [SerializeField] private AudioClip interactComplete;
        [SerializeField] private AudioClip heat;
        [SerializeField] private AudioClip lockdown;
        [SerializeField] private AudioClip countdownTick;
        [SerializeField] private AudioClip message;
        [SerializeField] private AudioClip success;
        [SerializeField] private AudioClip failure;

        public AudioClip Ambience => ambience;
        public float AmbienceVolume => ambienceVolume;
        public AudioClip InteractLoop => interactLoop;
        public float InteractVolume => interactVolume;

        public AudioClip Cue(MissionAudio.Cue cue) => cue switch
        {
            MissionAudio.Cue.Objective => objective,
            MissionAudio.Cue.InteractComplete => interactComplete,
            MissionAudio.Cue.Heat => heat,
            MissionAudio.Cue.Lockdown => lockdown,
            MissionAudio.Cue.CountdownTick => countdownTick,
            MissionAudio.Cue.Message => message,
            MissionAudio.Cue.Success => success,
            MissionAudio.Cue.Failure => failure,
            _ => null
        };

#if UNITY_EDITOR
        public void EditorConfigure(AudioClip ambienceLoop, AudioClip interact, AudioClip objectiveCue, AudioClip interactCompleteCue,
                                    AudioClip heatCue, AudioClip lockdownCue, AudioClip tickCue, AudioClip messageCue,
                                    AudioClip successCue, AudioClip failureCue)
        {
            ambience = ambienceLoop;
            interactLoop = interact;
            objective = objectiveCue;
            interactComplete = interactCompleteCue;
            heat = heatCue;
            lockdown = lockdownCue;
            countdownTick = tickCue;
            message = messageCue;
            success = successCue;
            failure = failureCue;
        }
#endif
    }
}
