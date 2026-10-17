using UnityEngine;

namespace NeonRift.HarborRain
{
    /// <summary>A welder working late in a maintenance bay: irregular bursts of sparks with a flickering blue-white arc light.</summary>
    public sealed class HarborWelder : MonoBehaviour
    {
        [SerializeField] private ParticleSystem sparks;
        [SerializeField] private Light arc;
        private float burstEnd, nextBurst;

        private void Update()
        {
            float t = Time.time;
            if (t >= nextBurst) { burstEnd = t + Random.Range(1.2f, 3.5f); nextBurst = burstEnd + Random.Range(1.5f, 5f); }
            bool on = t < burstEnd;
            if (arc != null) arc.intensity = on ? Random.Range(8f, 26f) : Mathf.MoveTowards(arc.intensity, 0f, Time.deltaTime * 80f);
            if (on && sparks != null && Random.value < 0.6f) sparks.Emit(Random.Range(2, 7));
        }
    }
}
