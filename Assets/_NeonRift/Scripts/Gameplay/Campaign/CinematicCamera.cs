using Unity.Cinemachine;
using UnityEngine;

namespace NeonRift.Gameplay
{
    /// <summary>
    /// A runtime Cinemachine camera that outranks the chase camera while a campaign cinematic plays. Poses are set
    /// directly each frame (cuts are instant; moves are whatever the caller animates), with an optional shake.
    /// </summary>
    public sealed class CinematicCamera : MonoBehaviour
    {
        private CinemachineCamera cam;
        private float shake;

        public static CinematicCamera Create(string name, float fov = 50f)
        {
            var go = new GameObject(name);
            var c = go.AddComponent<CinematicCamera>();
            c.cam = go.AddComponent<CinemachineCamera>();
            c.cam.Priority.Value = 1000;
            c.cam.Lens.FieldOfView = fov;
            c.cam.Lens.NearClipPlane = 0.1f;
            c.cam.Lens.FarClipPlane = 3000f;
            Cut();
            return c;
        }

        /// <summary>
        /// The next camera change is a hard cut (no blend from the chase camera into the first shot, nor back out of the
        /// last one): the brain's default blend is restored two frames later.
        /// </summary>
        public static void Cut()
        {
            var brain = Camera.main != null ? Camera.main.GetComponent<CinemachineBrain>() : null;
            if (brain == null) return;
            if (!cutting) saved = brain.DefaultBlend;
            cutting = true;
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
            restoreAt = Time.frameCount + 3;
            if (restorer == null) restorer = new GameObject("CinematicCutRestorer").AddComponent<CutRestorer>();
        }

        private static bool cutting;
        private static CinemachineBlendDefinition saved;
        private static int restoreAt;
        private static CutRestorer restorer;

        private sealed class CutRestorer : MonoBehaviour
        {
            private void Update()
            {
                if (!cutting || Time.frameCount < restoreAt) return;
                var brain = Camera.main != null ? Camera.main.GetComponent<CinemachineBrain>() : null;
                if (brain != null) brain.DefaultBlend = saved;
                cutting = false;
                Destroy(gameObject);
                restorer = null;
            }
        }

        private void OnDestroy() => Cut();

        public float FieldOfView { get => cam.Lens.FieldOfView; set => cam.Lens.FieldOfView = value; }

        public void Pose(Vector3 position, Vector3 lookAt, float fov = -1f)
        {
            Vector3 dir = lookAt - position;
            if (dir.sqrMagnitude < 1e-4f) dir = transform.forward;
            Vector3 jitter = shake > 0f ? Random.insideUnitSphere * shake * 0.18f : Vector3.zero;
            transform.SetPositionAndRotation(position + jitter, Quaternion.LookRotation(dir, Vector3.up));
            if (fov > 0f) cam.Lens.FieldOfView = fov;
        }

        public void Shake(float amount) => shake = Mathf.Max(shake, amount);

        private void Update() => shake = Mathf.MoveTowards(shake, 0f, Time.unscaledDeltaTime * 1.5f);
    }
}
