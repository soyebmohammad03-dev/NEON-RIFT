using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace NeonRift.Game
{
    /// <summary>
    /// The campaign's presentation layer (UI Toolkit, built in code, created by the campaign bootstrap's
    /// <see cref="GameRoot"/>; it sits above every HUD and below the loading fade). Layers, back to front: the
    /// intercepted-transmission teaser, white flash and black fade, letterbox, message card, choice menu, subtitles.
    /// Spoken lines play here (<see cref="Speak"/>) from the campaign's <see cref="VoiceLine"/> table, so a line's audio and
    /// its subtitle always come from the same record. Everything transient is cleared on every scene change.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class CampaignOverlay : MonoBehaviour
    {
        private static readonly Color Cyan = new(0.33f, 0.91f, 1f), Magenta = new(0.93f, 0.25f, 0.82f), Text = new(0.91f, 0.93f, 1f),
                                      Dim = new(0.62f, 0.65f, 0.78f), Amber = new(1f, 0.69f, 0.25f);

        private GameContext context;
        private CampaignSession session;
        private AudioSource voice;
        private VisualElement root, teaser, teaserImage, scan, noise, flash, fade, barTop, barBottom, card, cardLines, subtitleBox, menuRoot;
        private Label teaserCaption, teaserCode, teaserTag, cardKicker, cardTitle, cardFooter, subSpeaker, subText, hint, captionKicker, captionTitle;
        private VisualElement caption, subHolder;
        private Texture2D noiseTex, scanTex;
        private readonly List<Label> revealLines = new();
        private float revealStart, revealInterval;
        private float subtitleUntil = -1f;
        private OptionsMenu menu;
        private Color32[] noisePixels;
        private float teaserGlitch;
        private System.Random rng = new(7);

        public bool CardVisible => card != null && card.style.display == DisplayStyle.Flex;
        public bool MenuOpen => menu != null && menu.IsOpen;
        public bool TeaserVisible => teaser != null && teaser.style.display == DisplayStyle.Flex;
        public bool SubtitleVisible => subtitleBox != null && subtitleBox.style.display == DisplayStyle.Flex;
        public string SubtitleText => SubtitleVisible ? subText.text : null;
        public string CardTitle => CardVisible ? cardTitle.text : null;
        /// <summary>The line currently playing (null when silent).</summary>
        public string SpeakingId { get; private set; }

        public void Initialize(GameContext gameContext, CampaignSession campaign)
        {
            context = gameContext;
            session = campaign;
            session.Overlay = this;
            voice = gameObject.AddComponent<AudioSource>();
            voice.playOnAwake = false;
            voice.spatialBlend = 0f;
            voice.ignoreListenerPause = true;
            voice.priority = 0;
            if (context.Config.AudioMixer != null) voice.outputAudioMixerGroup = context.Config.AudioMixer.UI;
            Build();
            Subtitles.Shown += OnSubtitle;
            Subtitles.Cleared += OnSubtitlesCleared;
            if (context.Flow != null) { context.Flow.StateChanged += OnFlowState; context.Flow.ContentUnloading += OnUnloading; }
        }

        private void OnDestroy()
        {
            Subtitles.Shown -= OnSubtitle;
            Subtitles.Cleared -= OnSubtitlesCleared;
            if (context?.Flow != null) { context.Flow.StateChanged -= OnFlowState; context.Flow.ContentUnloading -= OnUnloading; }
            if (noiseTex != null) Destroy(noiseTex);
            if (scanTex != null) Destroy(scanTex);
        }

        // ---------------- Voice ----------------

        /// <summary>Plays the campaign line <paramref name="lineId"/> with its subtitle. Returns its length (0 if unknown).</summary>
        public float Speak(string lineId)
        {
            if (session == null || !session.Definition.TryGetLine(lineId, out var line))
            {
                Debug.LogWarning($"[Campaign] unknown voice line '{lineId}'");
                return 0f;
            }
            voice.Stop();
            if (line.clip != null)
            {
                voice.clip = line.clip;
                voice.Play();
            }
            SpeakingId = lineId;
            float seconds = line.Seconds;
            Subtitles.Show(line.speaker, line.text, seconds + 0.25f, line.Voiced);
            return seconds;
        }

        /// <summary>Stops the current line and removes its subtitle (skips).</summary>
        public void StopVoice()
        {
            if (voice != null) voice.Stop();
            SpeakingId = null;
            Subtitles.Clear();
        }

        public bool VoicePlaying => voice != null && voice.isPlaying;

        // ---------------- Layers ----------------

        public void SetFlash(float alpha) => SetAlpha(flash, alpha);
        public void SetFade(float alpha) => SetAlpha(fade, alpha);

        public void SetLetterbox(bool on)
        {
            float h = on ? 11f : 0f;
            barTop.style.height = Length.Percent(h);
            barBottom.style.height = Length.Percent(h);
        }

        /// <summary>Lower-left shot caption for cinematics (kicker small, title large); null hides it.</summary>
        public void SetCaption(string kicker, string title, float alpha = 1f)
        {
            bool on = !string.IsNullOrEmpty(title) && alpha > 0.001f;
            caption.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (!on) return;
            captionKicker.text = kicker ?? string.Empty;
            captionTitle.text = title;
            caption.style.opacity = Mathf.Clamp01(alpha);
        }

        /// <summary>The shared menu (pause menus that need the Settings and Controls pages).</summary>
        public OptionsMenu Menu => menu ??= new OptionsMenu(menuRoot, context);

        public void SetHint(string text)
        {
            hint.text = text ?? string.Empty;
            hint.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        /// <summary>One frame of the intercepted transmission: a captured Harbor Rain frame with slow drift, static and glitches.</summary>
        public void SetTeaser(Texture2D frame, float scale, Vector2 drift, float alpha, float glitch, string caption, string code)
        {
            teaser.style.display = DisplayStyle.Flex;
            teaser.style.opacity = Mathf.Clamp01(alpha);
            if (frame != null) teaserImage.style.backgroundImage = new StyleBackground(frame);
            teaserGlitch = glitch;
            float jitter = glitch > 0.01f && rng.NextDouble() < glitch ? (float)(rng.NextDouble() * 2 - 1) * 40f * glitch : 0f;
            teaserImage.style.scale = new Scale(new Vector3(scale, scale, 1f));
            teaserImage.style.translate = new Translate(Length.Percent(drift.x), Length.Percent(drift.y));
            teaserImage.style.left = jitter;
            teaserImage.style.unityBackgroundImageTintColor = Color.Lerp(new Color(0.78f, 0.95f, 1f), new Color(1f, 0.55f, 0.9f), glitch * (float)rng.NextDouble());
            teaserCaption.text = caption ?? string.Empty;
            teaserCode.text = code ?? string.Empty;
        }

        public void HideTeaser()
        {
            teaser.style.display = DisplayStyle.None;
            teaserImage.style.backgroundImage = StyleKeyword.None;
            teaserGlitch = 0f;
        }

        /// <summary>
        /// Centre card: a kicker, a title and lines that appear one after another (<paramref name="interval"/> s apart),
        /// with an optional footer prompt.
        /// </summary>
        public void ShowCard(string kicker, string title, IList<string> lines, string footer = null, float interval = 0.55f, Color? accent = null)
        {
            card.style.display = DisplayStyle.Flex;
            cardKicker.text = kicker ?? string.Empty;
            cardTitle.text = title ?? string.Empty;
            cardTitle.style.color = accent ?? Cyan;
            cardFooter.text = footer ?? string.Empty;
            cardFooter.style.display = string.IsNullOrEmpty(footer) ? DisplayStyle.None : DisplayStyle.Flex;
            cardLines.Clear();
            revealLines.Clear();
            if (lines != null)
                foreach (var l in lines)
                {
                    var label = new Label(l);
                    bool head = l.StartsWith("▸", StringComparison.Ordinal) || l == l.ToUpperInvariant() && l.Length < 48;
                    label.style.fontSize = head ? 24 : 21;
                    label.style.color = l.StartsWith("NEW OPERATION", StringComparison.Ordinal) ? Magenta : head ? Text : Dim;
                    label.style.unityFontStyleAndWeight = head ? FontStyle.Bold : FontStyle.Normal;
                    label.style.letterSpacing = head ? 3 : 1;
                    label.style.whiteSpace = WhiteSpace.Normal;
                    label.style.marginTop = 7;
                    label.style.opacity = interval <= 0f ? 1f : 0f;
                    cardLines.Add(label);
                    revealLines.Add(label);
                }
            revealStart = Time.unscaledTime;
            revealInterval = interval;
        }

        /// <summary>All of the card's lines are visible.</summary>
        public bool CardRevealed => revealInterval <= 0f || Time.unscaledTime - revealStart >= revealLines.Count * revealInterval + 0.3f;

        public void RevealCardNow() => revealInterval = 0f;

        public void HideCard()
        {
            card.style.display = DisplayStyle.None;
            revealLines.Clear();
        }

        /// <summary>A menu of buttons (keyboard / gamepad / mouse / touch), using the game's shared menu overlay.</summary>
        public void ShowChoices(string title, IList<(string label, Action action)> choices, string footer = null)
        {
            menu ??= new OptionsMenu(menuRoot, context);
            var rows = new List<OptionsMenu.Row>();
            foreach (var (label, action) in choices)
            {
                var a = action;
                rows.Add(menu.Button(label, () => { menu.Close(); a?.Invoke(); }));
            }
            menu.Open(title, rows, footer);
        }

        public void HideChoices()
        {
            if (menu != null && menu.IsOpen) menu.Close();
        }

        /// <summary>Removes every transient layer (scene changes, skips).</summary>
        public void ClearAll()
        {
            HideTeaser();
            HideCard();
            HideChoices();
            SetFlash(0f);
            SetFade(0f);
            SetLetterbox(false);
            SetHint(null);
            SetCaption(null, null);
            StopVoice();
        }

        /// <summary>
        /// Leaving a scene (under the black fade): everything goes except a full white flash, which a portal crossing carries
        /// over the cut on purpose (the next scene clears it).
        /// </summary>
        private void OnUnloading()
        {
            bool white = flash.style.display == DisplayStyle.Flex && flash.resolvedStyle.opacity > 0.9f;
            ClearAll();
            if (white) SetFlash(1f);
        }

        private void OnFlowState(GameState state)
        {
            // The loading fade is fully up whenever the state changes: nothing from the previous scene may remain.
            ClearAll();
        }

        // ---------------- Subtitles ----------------

        private void OnSubtitle(string speaker, string text, float seconds)
        {
            subSpeaker.text = string.IsNullOrEmpty(speaker) ? string.Empty : speaker.ToUpperInvariant();
            subSpeaker.style.display = string.IsNullOrEmpty(speaker) ? DisplayStyle.None : DisplayStyle.Flex;
            subText.text = text;
            subtitleBox.style.display = DisplayStyle.Flex;
            subtitleUntil = Time.unscaledTime + seconds;
        }

        private void OnSubtitlesCleared()
        {
            subtitleBox.style.display = DisplayStyle.None;
            subtitleUntil = -1f;
        }

        private void Update()
        {
            // The UI is built in code; after a script reload in Play Mode it is gone until the next Initialize.
            if (subHolder == null || subtitleBox == null) return;
            if (subtitleUntil > 0f && Time.unscaledTime >= subtitleUntil)
            {
                subtitleBox.style.display = DisplayStyle.None;
                subtitleUntil = -1f;
                Subtitles.Expire();
            }
            if (SpeakingId != null && voice != null && !voice.isPlaying && subtitleUntil < 0f) SpeakingId = null;
            for (int i = 0; i < revealLines.Count; i++)
            {
                float a = revealInterval <= 0f ? 1f : Mathf.Clamp01((Time.unscaledTime - revealStart - i * revealInterval) / 0.35f);
                revealLines[i].style.opacity = a;
            }
            if (TeaserVisible && Time.frameCount % 2 == 0) StirNoise();
            // A centre card leaves the lower band free: the subtitle drops below it instead of covering its footer.
            subHolder.style.paddingBottom = Length.Percent(CardVisible ? 2.5f : 13f);
        }

        private void StirNoise()
        {
            for (int i = 0; i < noisePixels.Length; i++)
            {
                byte v = (byte)rng.Next(256);
                noisePixels[i] = new Color32(v, v, v, 255);
            }
            // Torn rows during a glitch.
            if (teaserGlitch > 0.2f)
                for (int k = 0; k < 4; k++)
                {
                    int y = rng.Next(noiseTex.height);
                    for (int x = 0; x < noiseTex.width; x++) noisePixels[y * noiseTex.width + x] = new Color32(200, 240, 255, 160);
                }
            noiseTex.SetPixels32(noisePixels);
            noiseTex.Apply(false);
            // Light grain normally, heavy static on glitches.
            noise.style.opacity = 0.07f + teaserGlitch * 0.55f;
        }

        // ---------------- Build ----------------

        private static void SetAlpha(VisualElement e, float alpha)
        {
            e.style.opacity = Mathf.Clamp01(alpha);
            e.style.display = alpha > 0.001f ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static VisualElement Full(string name, Color? colour = null)
        {
            var e = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
            e.style.position = Position.Absolute;
            e.style.left = e.style.top = e.style.right = e.style.bottom = 0;
            if (colour.HasValue) e.style.backgroundColor = colour.Value;
            return e;
        }

        private static Label MakeLabel(string text, int size, Color colour, FontStyle style = FontStyle.Normal, int spacing = 2)
        {
            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            l.style.fontSize = size;
            l.style.color = colour;
            l.style.unityFontStyleAndWeight = style;
            l.style.letterSpacing = spacing;
            l.style.whiteSpace = WhiteSpace.Normal;
            return l;
        }

        private void Build()
        {
            var doc = GetComponent<UIDocument>();
            root = doc.rootVisualElement;
            root.Clear();
            root.pickingMode = PickingMode.Ignore;
            root.style.flexGrow = 1;

            // Teaser: the captured frame, scanlines and live static.
            teaser = Full("campaign-teaser", Color.black);
            teaser.style.overflow = Overflow.Hidden;
            teaserImage = Full("teaser-image");
            teaserImage.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
            teaser.Add(teaserImage);
            scanTex = new Texture2D(2, 540, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
            for (int y = 0; y < scanTex.height; y++)
                for (int x = 0; x < 2; x++) scanTex.SetPixel(x, y, new Color(0f, 0.02f, 0.04f, y % 3 == 0 ? 0.42f : 0.05f));
            scanTex.Apply();
            scan = Full("teaser-scan");
            scan.style.backgroundImage = new StyleBackground(scanTex);
            scan.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
            teaser.Add(scan);
            noiseTex = new Texture2D(320, 180, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            noisePixels = new Color32[noiseTex.width * noiseTex.height];
            noise = Full("teaser-noise");
            noise.style.backgroundImage = new StyleBackground(noiseTex);
            noise.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
            teaser.Add(noise);
            var vignette = Full("teaser-frame");
            vignette.style.borderTopWidth = vignette.style.borderBottomWidth = vignette.style.borderLeftWidth = vignette.style.borderRightWidth = 2;
            vignette.style.borderTopColor = vignette.style.borderBottomColor = vignette.style.borderLeftColor = vignette.style.borderRightColor = new Color(0.33f, 0.91f, 1f, 0.35f);
            vignette.style.left = vignette.style.right = 34;
            vignette.style.top = vignette.style.bottom = 30;
            teaser.Add(vignette);
            teaserTag = MakeLabel("● INTERCEPTED SIGNAL", 20, new Color(1f, 0.3f, 0.3f), FontStyle.Bold, 4);
            teaserTag.style.position = Position.Absolute;
            teaserTag.style.left = 64;
            teaserTag.style.top = 52;
            teaser.Add(teaserTag);
            teaserCode = MakeLabel("", 18, Cyan, FontStyle.Normal, 3);
            teaserCode.style.position = Position.Absolute;
            teaserCode.style.right = 64;
            teaserCode.style.top = 54;
            teaser.Add(teaserCode);
            teaserCaption = MakeLabel("", 30, Text, FontStyle.Bold, 8);
            teaserCaption.style.position = Position.Absolute;
            teaserCaption.style.left = 64;
            teaserCaption.style.bottom = 64;
            teaser.Add(teaserCaption);
            root.Add(teaser);

            fade = Full("campaign-fade", Color.black);
            root.Add(fade);
            flash = Full("campaign-flash", new Color(0.92f, 0.98f, 1f));
            root.Add(flash);

            barTop = Full("campaign-bar-top", Color.black);
            barTop.style.bottom = StyleKeyword.Auto;
            barTop.style.height = 0;
            barBottom = Full("campaign-bar-bottom", Color.black);
            barBottom.style.top = StyleKeyword.Auto;
            barBottom.style.height = 0;
            root.Add(barTop);
            root.Add(barBottom);

            // Message card.
            var cardHolder = Full("campaign-card-holder");
            cardHolder.style.alignItems = Align.Center;
            cardHolder.style.justifyContent = Justify.Center;
            card = new VisualElement { name = "campaign-card", pickingMode = PickingMode.Ignore };
            card.style.width = Length.Percent(62);
            card.style.maxWidth = 1100;
            card.style.paddingLeft = card.style.paddingRight = 48;
            card.style.paddingTop = 30;
            card.style.marginBottom = Length.Percent(8);
            card.style.paddingBottom = 34;
            card.style.backgroundColor = new Color(0.03f, 0.03f, 0.08f, 0.86f);
            card.style.borderLeftWidth = 4;
            card.style.borderLeftColor = Cyan;
            card.style.borderTopWidth = card.style.borderBottomWidth = card.style.borderRightWidth = 1;
            card.style.borderTopColor = card.style.borderBottomColor = card.style.borderRightColor = new Color(0.33f, 0.91f, 1f, 0.25f);
            cardKicker = MakeLabel("", 18, Dim, FontStyle.Bold, 6);
            cardTitle = MakeLabel("", 44, Cyan, FontStyle.Bold, 6);
            cardTitle.style.marginTop = 6;
            cardTitle.style.marginBottom = 12;
            cardLines = new VisualElement { pickingMode = PickingMode.Ignore };
            cardFooter = MakeLabel("", 19, Amber, FontStyle.Bold, 4);
            cardFooter.style.marginTop = 24;
            card.Add(cardKicker);
            card.Add(cardTitle);
            card.Add(cardLines);
            card.Add(cardFooter);
            card.style.display = DisplayStyle.None;
            cardHolder.Add(card);
            root.Add(cardHolder);

            menuRoot = Full("campaign-menu");
            menuRoot.pickingMode = PickingMode.Ignore;
            root.Add(menuRoot);

            // Subtitles: bottom centre, clear of the HUD's bottom corners (speed, minimap and touch controls).
            subHolder = Full("campaign-subtitles");
            subHolder.style.alignItems = Align.Center;
            subHolder.style.justifyContent = Justify.FlexEnd;
            subHolder.style.paddingBottom = Length.Percent(13);
            subtitleBox = new VisualElement { name = "subtitle-box", pickingMode = PickingMode.Ignore };
            subtitleBox.style.maxWidth = Length.Percent(56);
            subtitleBox.style.backgroundColor = new Color(0f, 0f, 0f, 0.72f);
            subtitleBox.style.paddingLeft = subtitleBox.style.paddingRight = 22;
            subtitleBox.style.paddingTop = 10;
            subtitleBox.style.paddingBottom = 12;
            subtitleBox.style.borderTopLeftRadius = subtitleBox.style.borderTopRightRadius =
                subtitleBox.style.borderBottomLeftRadius = subtitleBox.style.borderBottomRightRadius = 4;
            subtitleBox.style.alignItems = Align.Center;
            subSpeaker = MakeLabel("", 17, Cyan, FontStyle.Bold, 4);
            subText = MakeLabel("", 26, Color.white, FontStyle.Normal, 0);
            subText.style.unityTextAlign = TextAnchor.MiddleCenter;
            subtitleBox.Add(subSpeaker);
            subtitleBox.Add(subText);
            subtitleBox.style.display = DisplayStyle.None;
            subHolder.Add(subtitleBox);
            root.Add(subHolder);

            caption = new VisualElement { name = "campaign-caption", pickingMode = PickingMode.Ignore };
            caption.style.position = Position.Absolute;
            caption.style.left = 72;
            caption.style.top = Length.Percent(15);
            caption.style.borderLeftWidth = 3;
            caption.style.borderLeftColor = Magenta;
            caption.style.paddingLeft = 16;
            captionKicker = MakeLabel("", 17, Cyan, FontStyle.Bold, 6);
            captionTitle = MakeLabel("", 40, Text, FontStyle.Bold, 8);
            caption.Add(captionKicker);
            caption.Add(captionTitle);
            root.Add(caption);

            hint = MakeLabel("", 17, Dim, FontStyle.Bold, 4);
            hint.style.position = Position.Absolute;
            hint.style.right = 48;
            hint.style.bottom = 30;
            hint.style.display = DisplayStyle.None;
            root.Add(hint);

            ClearAll();
        }
    }
}
