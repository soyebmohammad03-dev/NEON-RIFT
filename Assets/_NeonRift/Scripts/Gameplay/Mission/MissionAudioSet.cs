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

        [Header("Interaction stages")]
        [SerializeField] private AudioClip stepStart;
        [SerializeField] private AudioClip stepDone;
        [SerializeField] private AudioClip miss;
        [SerializeField] private AudioClip interference;
        [SerializeField] private AudioClip resync;
        [SerializeField] private AudioClip lockout;
        [SerializeField] private AudioClip cancel;
        [Tooltip("Builds under long interactions (extraction); level and pitch follow progress.")]
        [SerializeField] private AudioClip tensionLoop;
        [SerializeField, Range(0f, 1f)] private float tensionVolume = 0.7f;
        [Header("Story")]
        [Tooltip("Stingers played when a world event fires (data acquired, breach detected …).")]
        [SerializeField] private EventStinger[] eventStingers = System.Array.Empty<EventStinger>();

        [System.Serializable]
        public struct EventStinger
        {
            public string eventId;
            public AudioClip clip;
            [Range(0f, 1f)] public float volume;
        }

        public AudioClip TensionLoop => tensionLoop;
        public float TensionVolume => tensionVolume;
        public EventStinger[] EventStingers => eventStingers ?? System.Array.Empty<EventStinger>();

        public AudioClip Feedback(Interactable.Feedback feedback) => feedback switch
        {
            Interactable.Feedback.Started or Interactable.Feedback.StepStarted => stepStart,
            Interactable.Feedback.StepCompleted => stepDone,
            Interactable.Feedback.Miss => miss,
            Interactable.Feedback.Interference => interference,
            Interactable.Feedback.Resynced => resync,
            Interactable.Feedback.LinkDropped => miss,
            Interactable.Feedback.Failed => lockout,
            Interactable.Feedback.Cancelled => cancel,
            _ => null
        };

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
        public void EditorConfigureStages(AudioClip start, AudioClip done, AudioClip missCue, AudioClip interferenceCue, AudioClip resyncCue,
                                          AudioClip lockoutCue, AudioClip cancelCue, AudioClip tension, EventStinger[] stingers)
        {
            stepStart = start;
            stepDone = done;
            miss = missCue;
            interference = interferenceCue;
            resync = resyncCue;
            lockout = lockoutCue;
            cancel = cancelCue;
            tensionLoop = tension;
            eventStingers = stingers;
        }

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
