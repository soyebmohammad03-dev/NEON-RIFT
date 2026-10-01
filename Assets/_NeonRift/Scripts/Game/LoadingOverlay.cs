using UnityEngine;
using UnityEngine.UIElements;

namespace NeonRift.Game
{
    /// <summary>Full-screen fade that hides scene loads. Starts opaque so the empty Bootstrap scene is never seen.</summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class LoadingOverlay : MonoBehaviour
    {
        private const string OverlayName = "loading-overlay";

        private VisualElement overlay;

        private VisualElement Overlay
        {
            get
            {
                if (overlay == null)
                {
                    var root = GetComponent<UIDocument>().rootVisualElement;
                    overlay = root.Q(OverlayName) ?? root;
                }
                return overlay;
            }
        }

        public async Awaitable ShowAsync(float seconds)
        {
            Overlay.style.display = DisplayStyle.Flex;
            Overlay.pickingMode = PickingMode.Position;
            await FadeAsync(1f, seconds);
        }

        public async Awaitable HideAsync(float seconds)
        {
            await FadeAsync(0f, seconds);
            Overlay.pickingMode = PickingMode.Ignore;
            Overlay.style.display = DisplayStyle.None;
        }

        private async Awaitable FadeAsync(float target, float seconds)
        {
            float start = Overlay.resolvedStyle.opacity;
            if (seconds <= 0f || Mathf.Approximately(start, target))
            {
                Overlay.style.opacity = target;
                return;
            }
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                Overlay.style.opacity = Mathf.Lerp(start, target, t / seconds);
                await Awaitable.NextFrameAsync();
            }
            Overlay.style.opacity = target;
        }
    }
}
