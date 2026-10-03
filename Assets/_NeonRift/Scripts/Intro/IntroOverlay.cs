using UnityEngine;
using UnityEngine.UIElements;

namespace NeonRift.Intro
{
    /// <summary>
    /// The cinematic's 2D layer (UI Toolkit): black fades, white flashes, letterbox, text cards, the title reveal,
    /// the skip hint and the security-camera view. Everything is driven explicitly by the sequence each frame,
    /// so scrubbing the Timeline in the editor and skipping at runtime both land on the right state.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class IntroOverlay : MonoBehaviour
    {
        private VisualElement fade, flash, card, title, titleRule, surveillance, recDot, barTop, barBottom;
        private Label cardText, cardSub, titleMain, titleSub, prompt, skip, cctvClock, cctvAlert;

        private void OnEnable() => Bind();

        private void Bind()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            if (root == null) return;
            fade = root.Q("fade");
            flash = root.Q("flash");
            card = root.Q("card");
            title = root.Q("title");
            titleRule = root.Q("title-rule");
            surveillance = root.Q("surveillance");
            recDot = root.Q("rec-dot");
            barTop = root.Q("letterbox-top");
            barBottom = root.Q("letterbox-bottom");
            cardText = root.Q<Label>("card-text");
            cardSub = root.Q<Label>("card-sub");
            titleMain = root.Q<Label>("title-main");
            titleSub = root.Q<Label>("title-sub");
            prompt = root.Q<Label>("prompt");
            skip = root.Q<Label>("skip");
            cctvClock = root.Q<Label>("cctv-clock");
            cctvAlert = root.Q<Label>("cctv-alert");
            Clear();
        }

        /// <summary>Fully black, nothing else showing.</summary>
        public void Clear()
        {
            if (fade == null) return;
            SetFade(1f);
            SetFlash(0f);
            HideCard();
            SetTitle(0f, 0f, 0f);
            SetPrompt(false);
            SetSkipHint(false);
            SetSurveillance(false, 0f, null);
            SetLetterbox(true);
        }

        public void SetFade(float black) { if (fade != null) fade.style.opacity = Mathf.Clamp01(black); }

        public void SetFlash(float amount) { if (flash != null) flash.style.opacity = Mathf.Clamp01(amount); }

        public void SetLetterbox(bool on)
        {
            if (barTop == null) return;
            barTop.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            barBottom.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void ShowCard(string text, string subtitle, float alpha)
        {
            if (card == null) return;
            cardText.text = text ?? string.Empty;
            cardSub.text = subtitle ?? string.Empty;
            card.style.opacity = Mathf.Clamp01(alpha);
            // A slow drift while the card is up reads as cinematic rather than static.
            card.style.translate = new Translate(Mathf.Lerp(-14f, 0f, Mathf.Clamp01(alpha)), 0f);
        }

        public void HideCard() { if (card != null) card.style.opacity = 0f; }

        /// <summary>
        /// Title reveal: <paramref name="main"/> fades NEON RIFT in while its letter spacing closes, <paramref name="rule"/>
        /// draws the line, <paramref name="sub"/> brings in NIGHT RUN.
        /// </summary>
        public void SetTitle(float main, float rule, float sub)
        {
            if (title == null) return;
            title.style.opacity = Mathf.Clamp01(main * 1.5f);
            titleMain.style.opacity = Mathf.Clamp01(main);
            titleMain.style.letterSpacing = Mathf.Lerp(70f, 30f, Mathf.SmoothStep(0f, 1f, main));
            titleRule.style.width = Length.Percent(Mathf.SmoothStep(0f, 1f, rule) * 46f);
            titleSub.style.opacity = Mathf.Clamp01(sub);
            titleSub.style.letterSpacing = Mathf.Lerp(40f, 22f, Mathf.SmoothStep(0f, 1f, sub));
        }

        public void SetPrompt(bool visible)
        {
            if (prompt == null) return;
            // Slow breathing so it reads as "waiting for you".
            prompt.style.opacity = visible ? 0.45f + 0.45f * Mathf.Sin(Time.unscaledTime * 2.6f) * 0.5f + 0.25f : 0f;
        }

        public void SetSkipHint(bool visible) { if (skip != null) skip.style.opacity = visible ? 0.85f : 0f; }

        public void SetSurveillance(bool on, float seconds, string alert)
        {
            if (surveillance == null) return;
            surveillance.EnableInClassList("nr-intro--off", !on);
            if (!on) return;
            recDot.style.opacity = Mathf.Repeat(Time.unscaledTime, 1f) < 0.55f ? 1f : 0.15f;
            int clock = 2 * 3600 + 13 * 60 + 7 + Mathf.FloorToInt(seconds);
            cctvClock.text = $"{clock / 3600:00}:{clock / 60 % 60:00}:{clock % 60:00}";
            cctvAlert.text = alert ?? string.Empty;
            cctvAlert.style.opacity = string.IsNullOrEmpty(alert) ? 0f : (Mathf.Repeat(Time.unscaledTime * 2f, 1f) < 0.6f ? 1f : 0.25f);
        }
    }
}
