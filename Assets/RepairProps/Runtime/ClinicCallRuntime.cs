using System.Collections.Generic;
using System.Linq;
using BorderRepair.RepairProps.Dialogue;
using BorderRepair.TwoNight;
using BorderRepair.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Call = BorderRepair.RepairProps.RepairPropStoryText.Call;

namespace BorderRepair.RepairProps
{
    // Phone calls that carry the story between orders, in the visor like a holo-call: a card on the left with the
    // caller's live portrait (ClinicNpcRuntime's studio), name and a typed subtitle line; the player's own lines in
    // hot amber. Non-blocking: it never takes the cursor or pauses work. It rings first (T or a click answers; it picks
    // up by itself after a few seconds), lines advance on their own (T skips), and it hangs up after the last line.
    // Each call plays once per run, rung by its trigger condition in calls.json (checked in file order). A line with
    // choices holds the call until the player picks a reply (number keys or a click); DialogueRunner walks the lines.
    // Edit the calls in Border Repair > Repair Props > Dialogue Editor.
    [AddComponentMenu("")]
    public sealed class ClinicCallRuntime : MonoBehaviour
    {
        public const string ScenePath = RepairBenchV2Runtime.ScenePath;

        // Original lines for the NSP port setting (Docs/Integration/RepairPropsCyberBench/SETTING_PROPS.md).
        // The text is in Assets/RepairProps/Story/Resources/RepairPropsStory/calls.json (RepairPropStoryText).
        public static IReadOnlyList<Call> Calls => RepairPropStoryText.Calls;

        static readonly HashSet<string> played = new HashSet<string>();
        static readonly HashSet<string> flags = new HashSet<string>();
        static TwoNightState playedFor;

        /// <summary>Flags the calls have set this run (choices and lines with "set"); conditions read them as flag:name.</summary>
        public static IReadOnlyCollection<string> Flags => flags;
        public static bool HasFlag(string flag) => flags.Contains(flag);
        public static void SetFlag(string flag, bool on) { if (on) flags.Add(flag); else flags.Remove(flag); }
        public static bool Played(string id) => played.Contains(id);

        public static ClinicCallRuntime Current { get; private set; }
        public static Call Find(string id) => Calls.FirstOrDefault(c => c.id == id);

        enum Mode { Idle, Ringing, Talking, Ending }
        Mode mode;
        Call call;
        DialogueRunner runner;
        bool linePlayer;
        string lineText = "";
        int linesShown;
        RectTransform choicePanel;
        readonly List<(Button button, Text label)> choiceButtons = new List<(Button, Text)>();
        const int MaxChoices = 4;
        const float CardHeight = 206, ChoiceRow = 30;
        float modeAt, lineAt, lineHold, sceneStart, lastHangup;
        UnifiedClinicDirector clinic;
        Font font, mono, kana;
        Canvas canvas;
        RectTransform card;
        RawImage portrait;
        Image frame, ringBar;
        Text header, nameLine, subtitle, footer, timer;
        Button answer;
        AudioSource audioSource;
        AudioClip ring, blip;

        public bool Active => mode != Mode.Idle;
        public bool Ringing => mode == Mode.Ringing;
        public string CallId => call != null ? call.id : null;
        public string Subtitle => subtitle != null ? subtitle.text : "";
        public RectTransform Card => card;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetHooks() { SceneManager.sceneLoaded -= OnSceneLoaded; Current = null; played.Clear(); flags.Clear(); playedFor = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install() => SceneManager.sceneLoaded += OnSceneLoaded;

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!string.Equals(scene.path, ScenePath, System.StringComparison.OrdinalIgnoreCase)) return;
            if (UnityEngine.XR.XRSettings.isDeviceActive) return;
            var host = new GameObject("ClinicCalls (runtime only)");
            SceneManager.MoveGameObjectToScene(host, scene);
            host.AddComponent<ClinicCallRuntime>();
        }

        void Awake()
        {
            Current = this;
            // While replies are up, 1-4 pick a reply and must not also switch the dock's fixed cameras.
            BorderRepair.FirstOrder.FirstOrderInput.NumberKeysClaimed = () => Current != null && Current.Choosing;
            sceneStart = Time.unscaledTime;
            font = RepairPropCyberStyle.UiFontOr(UIFontProvider.GetCjkFont(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")));
            mono = RepairPropCyberStyle.Mono != null ? RepairPropCyberStyle.Mono : font;
            kana = RepairPropCyberStyle.Kana != null ? RepairPropCyberStyle.Kana : font;
            BuildUi();
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false; audioSource.spatialBlend = 0; audioSource.volume = .35f;
            ring = Tone("CallRing", 2.0f, t => (t % 2f < .9f && (t % .2f) < .1f ? 1f : 0f) * (Mathf.Sin(2 * Mathf.PI * 880 * t) * .5f + Mathf.Sin(2 * Mathf.PI * 1320 * t) * .3f));
            blip = Tone("CallBlip", .12f, t => Mathf.Sin(2 * Mathf.PI * (1200 - 3000 * t) * t) * (1 - t / .12f));
        }

        void Start() => clinic = FindObjectsByType<UnifiedClinicDirector>(FindObjectsSortMode.None).FirstOrDefault(c => c.gameObject.scene == gameObject.scene);

        static AudioClip Tone(string name, float seconds, System.Func<float, float> wave)
        {
            int rate = 22050, n = Mathf.CeilToInt(seconds * rate);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(wave(i / (float)rate), -1, 1) * .6f;
            var clip = AudioClip.Create(name, n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        void BuildUi()
        {
            var host = new GameObject("CallCanvas", typeof(RectTransform));
            host.transform.SetParent(transform, false);
            canvas = host.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 80;
            var scaler = host.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = .5f;
            host.AddComponent<GraphicRaycaster>();
            card = Rect("CallCard", host.transform, 16, -186, 500, 206);
            var bg = card.gameObject.AddComponent<Image>(); bg.raycastTarget = false;
            frame = RepairPropCyberStyle.Framed(bg, RepairPropCyberStyle.Field, RepairPropCyberStyle.Amber);
            RepairPropCyberStyle.Texture(card, RepairPropCyberStyle.Dots, new Color(1f, .62f, .16f, .08f), "DotGrid");
            RepairPropCyberStyle.Texture(card, RepairPropCyberStyle.Dither, new Color(1f, .42f, .10f, .06f), "Dither");
            HudMotion.Boot(card.gameObject);
            HudWindowDrag.Attach(RepairPropCyberStyle.TitleBar(card, "COMM.LINK // 通信", font), card, "call");
            var feed = Rect("Feed", card, 10, -10, 148, 186);
            var feedBg = feed.gameObject.AddComponent<Image>(); feedBg.color = new Color(.035f, .022f, .012f, 1); feedBg.raycastTarget = false;
            portrait = Rect("Portrait", feed, 0, 0, 148, 186).gameObject.AddComponent<RawImage>();
            portrait.raycastTarget = false; portrait.color = new Color(1f, .93f, .82f, 1);
            RepairPropCyberStyle.Texture(feed, RepairPropCyberStyle.Scan, new Color(0, 0, 0, .35f), "Scanlines").transform.SetAsLastSibling();
            var feedFrame = Rect("FeedFrame", feed, 0, 0, 148, 186).gameObject.AddComponent<Image>();
            feedFrame.sprite = RepairPropCyberStyle.Bracket; feedFrame.type = Image.Type.Sliced; feedFrame.color = RepairPropCyberStyle.Amber; feedFrame.raycastTarget = false;
            timer = Label(feed, "", mono, 10, 4, -170, 140, 14, RepairPropCyberStyle.Amber); timer.alignment = TextAnchor.MiddleLeft;
            header = Label(card, "", kana, 12, 170, -10, 320, 18, RepairPropCyberStyle.AmberDim);
            nameLine = Label(card, "", font, 18, 170, -30, 320, 26, RepairPropCyberStyle.Hot);
            var rule = Rect("Rule", card, 170, -60, 316, 1).gameObject.AddComponent<Image>(); rule.color = RepairPropCyberStyle.AmberDim; rule.raycastTarget = false;
            subtitle = Label(card, "", font, 15, 170, -68, 316, 100, RepairPropCyberStyle.Ink);
            subtitle.verticalOverflow = VerticalWrapMode.Overflow; subtitle.alignment = TextAnchor.UpperLeft; subtitle.lineSpacing = 1.1f;
            HudMotion.TypeOn(subtitle, 34);
            footer = Label(card, "", mono, 10, 170, -184, 316, 14, RepairPropCyberStyle.AmberDim);
            ringBar = Rect("RingBar", card, 170, -176, 316, 3).gameObject.AddComponent<Image>(); ringBar.color = RepairPropCyberStyle.Amber; ringBar.raycastTarget = false;
            var answerRect = Rect("Answer", card, 380, -136, 106, 34);
            var answerImage = answerRect.gameObject.AddComponent<Image>();
            RepairPropCyberStyle.Framed(answerImage, new Color(1f, .62f, .16f, .85f), RepairPropCyberStyle.Hot);
            answer = answerRect.gameObject.AddComponent<Button>(); answer.targetGraphic = answerImage;
            answer.onClick.AddListener(Pickup);
            var answerText = Label(answerRect, "应答 [T]", font, 14, 0, 0, 106, 34, Color.black); answerText.alignment = TextAnchor.MiddleCenter;
            // Replies hang under the card, which grows to fit them.
            choicePanel = Rect("Choices", card, 170, -CardHeight + 4, 316, ChoiceRow * MaxChoices);
            for (int i = 0; i < MaxChoices; i++)
            {
                int index = i;
                var row = Rect("Choice" + (i + 1), choicePanel, 0, -i * ChoiceRow, 316, ChoiceRow - 4);
                var image = row.gameObject.AddComponent<Image>();
                RepairPropCyberStyle.Framed(image, new Color(.06f, .035f, .015f, .92f), RepairPropCyberStyle.Amber);
                var button = row.gameObject.AddComponent<Button>(); button.targetGraphic = image;
                button.onClick.AddListener(() => Choose(index));
                var label = Label(row, "", font, 14, 8, 0, 300, ChoiceRow - 4, RepairPropCyberStyle.Hot);
                choiceButtons.Add((button, label));
            }
            choicePanel.gameObject.SetActive(false);
            card.gameObject.SetActive(false);
        }

        RectTransform Rect(string name, Transform parent, float x, float y, float w, float h)
        {
            var r = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1); r.anchoredPosition = new Vector2(x, y); r.sizeDelta = new Vector2(w, h);
            return r;
        }

        Text Label(Transform parent, string value, Font face, int size, float x, float y, float w, float h, Color color)
        {
            var text = Rect("Label", parent, x, y, w, h).gameObject.AddComponent<Text>();
            text.font = face; text.fontSize = size; text.color = color; text.text = value; text.raycastTarget = false; text.supportRichText = false;
            text.alignment = TextAnchor.MiddleLeft; text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        /// <summary>Starts a call now (also used by tests and the capture tour). False if one is already up or it already played.</summary>
        public bool Begin(string id, bool force = false)
        {
            var next = Find(id);
            if (next == null || mode != Mode.Idle || (!force && played.Contains(id))) return false;
            played.Add(id);
            call = next; mode = Mode.Ringing; modeAt = Time.unscaledTime;
            runner = new DialogueRunner(call, new World(this), SetFlag);
            linePlayer = false; lineText = ""; linesShown = 0;
            ShowChoices();
            card.gameObject.SetActive(true);
            nameLine.text = call.name + "　·　" + call.title;
            subtitle.text = "";
            if (audioSource != null) { audioSource.clip = ring; audioSource.loop = true; audioSource.Play(); }
            return true;
        }

        public void Pickup()
        {
            if (mode != Mode.Ringing) return;
            mode = Mode.Talking; modeAt = Time.unscaledTime;
            if (audioSource != null) { audioSource.Stop(); audioSource.loop = false; audioSource.PlayOneShot(blip); }
            Advance();
        }

        /// <summary>True while the call waits for the player to pick a reply.</summary>
        public bool Choosing => mode == Mode.Talking && runner != null && runner.AwaitingChoice;
        /// <summary>The replies on offer right now (empty unless Choosing).</summary>
        public IReadOnlyList<string> ChoiceTexts => runner != null ? runner.Choices.Select(c => c.text).ToList() : new List<string>();

        public void Advance()
        {
            if (mode != Mode.Talking || Choosing) return;
            if (!runner.Advance()) { mode = Mode.Ending; modeAt = Time.unscaledTime; ShowChoices(); if (audioSource != null) audioSource.PlayOneShot(blip, .7f); return; }
            var current = runner.Line;
            // A choice-only line (no text) keeps the previous subtitle up while the replies wait.
            if (!string.IsNullOrEmpty(current.text)) Show(current.player, current.text);
            ShowChoices();
        }

        /// <summary>Picks reply i (0-based): the player says it, then the call follows where it leads.</summary>
        public void Choose(int i)
        {
            if (!Choosing) return;
            var choice = runner.Choose(i);
            if (choice == null) return;
            ShowChoices();
            if (audioSource != null) audioSource.PlayOneShot(blip, .5f);
            if (!string.IsNullOrEmpty(choice.text)) Show(true, choice.text);
            else Advance();
        }

        void Show(bool player, string text)
        {
            linePlayer = player; lineText = text ?? ""; linesShown++;
            subtitle.text = (player ? "你　" : call.name + "　") + lineText;
            subtitle.color = player ? RepairPropCyberStyle.Hot : RepairPropCyberStyle.Ink;
            lineAt = Time.unscaledTime;
            lineHold = 1.6f + lineText.Length * .085f;
        }

        void ShowChoices()
        {
            int n = Choosing ? Mathf.Min(runner.Choices.Count, MaxChoices) : 0;
            choicePanel.gameObject.SetActive(n > 0);
            for (int i = 0; i < choiceButtons.Count; i++)
            {
                bool on = i < n;
                choiceButtons[i].button.gameObject.SetActive(on);
                if (on) choiceButtons[i].label.text = (i + 1) + "  " + runner.Choices[i].text;
            }
            card.sizeDelta = new Vector2(card.sizeDelta.x, CardHeight + (n > 0 ? n * ChoiceRow + 4 : 0));
        }

        static int PressedChoice(Keyboard keyboard)
        {
            if (keyboard == null) return -1;
            if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame) return 0;
            if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame) return 1;
            if (keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame) return 2;
            if (keyboard.digit4Key.wasPressedThisFrame || keyboard.numpad4Key.wasPressedThisFrame) return 3;
            return -1;
        }

        void Update()
        {
            if (TwoNightRun.Current != playedFor) { playedFor = TwoNightRun.Current; played.Clear(); flags.Clear(); }
            if (mode == Mode.Idle) { Trigger(); return; }
            float now = Time.unscaledTime;
            var keyboard = Keyboard.current;
            bool key = !ClinicStartScreenRuntime.Blocking && keyboard != null && keyboard.tKey.wasPressedThisFrame
                && !BorderRepair.FirstOrder.FirstOrderInput.TextInputFocused;   // typing a t in a field is not answering
            switch (mode)
            {
                case Mode.Ringing:
                    if (key || now - modeAt > 6f) Pickup();
                    break;
                case Mode.Talking:
                    if (Choosing)
                    {
                        // The reply waits for the player; only the line on screen finishes typing.
                        int pick = ClinicStartScreenRuntime.Blocking || BorderRepair.FirstOrder.FirstOrderInput.TextInputFocused ? -1 : PressedChoice(keyboard);
                        if (pick >= 0) Choose(pick);
                    }
                    else if (key || now - lineAt > lineHold) Advance();
                    break;
                case Mode.Ending:
                    if (now - modeAt > 1.1f) { mode = Mode.Idle; card.gameObject.SetActive(false); lastHangup = now; call = null; runner = null; return; }
                    break;
            }
            Paint(now);
        }

        void Paint(float now)
        {
            bool ringing = mode == Mode.Ringing, ending = mode == Mode.Ending;
            answer.gameObject.SetActive(ringing);
            ringBar.gameObject.SetActive(ringing);
            if (ringing) ringBar.rectTransform.sizeDelta = new Vector2(316 * (1 - Mathf.Clamp01((now - modeAt) / 6f)), 3);
            header.text = ringing ? "着信 // INCOMING CALL" : ending ? "通話終了 // LINE CLOSED" : "通話中 // ON LINE";
            frame.color = ringing && Mathf.Repeat(now, .8f) < .4f ? RepairPropCyberStyle.Hot : ending ? RepairPropCyberStyle.AmberDim : RepairPropCyberStyle.Amber;
            float talkTime = mode == Mode.Ringing ? 0 : now - modeAt;
            timer.text = (ringing ? "RING " : "REC ") + ((int)talkTime / 60).ToString("00") + ":" + ((int)talkTime % 60).ToString("00");
            footer.text = ringing ? "NSP-LINE " + RepairPropCyberStyle.Code(call.id, 6) + "  //  [T] ANSWER"
                : Choosing ? "[1-" + Mathf.Min(runner.Choices.Count, MaxChoices) + "] 选择回答  //  " + linesShown
                : "[T] NEXT  //  " + linesShown;
            var npcs = ClinicNpcRuntime.Current;
            bool callerTalking = mode == Mode.Talking && linesShown > 0 && !linePlayer
                && now - lineAt < Mathf.Min(lineHold - .4f, lineText.Length / 34f + .3f);
            var feed = npcs != null && !ringing ? npcs.Portrait(call.caller, callerTalking) : null;
            portrait.texture = feed;
            // While ringing (and on hang-up) the feed is a blank with a flicker, the way the reference terminals boot.
            portrait.color = feed == null ? new Color(1f, .62f, .16f, ringing && Mathf.Repeat(now * 7, 1) < .5f ? .10f : .04f)
                : ending ? new Color(1f, .93f, .82f, Mathf.Clamp01(1 - (now - modeAt) / .6f)) : new Color(1f, .93f, .82f, 1);
        }

        void Trigger()
        {
            // Nothing rings behind the title page, and the baseline moves with the clock while it is up, so every
            // "seconds since the scene started" delay below is measured from the moment the page closes.
            // First line on purpose: the early return below must not stop the baseline from following.
            if (ClinicStartScreenRuntime.Blocking) { sceneStart = Time.unscaledTime; return; }
            var state = TwoNightRun.Current;
            if (state == null || clinic == null || !clinic.Initialized) return;
            float now = Time.unscaledTime;
            if (now - lastHangup < 20f) return;
            // Calls ring in file order: the first whose trigger holds and has not played this run.
            foreach (var c in Calls)
                if (!string.IsNullOrWhiteSpace(c.trigger) && !played.Contains(c.id) && DialogueCondition.Evaluate(c.trigger, new World(this, now)) && Begin(c.id)) return;
        }

        /// <summary>The live night as conditions see it.</summary>
        sealed class World : IDialogueWorld
        {
            readonly ClinicCallRuntime calls;
            readonly float now;
            public World(ClinicCallRuntime calls, float now = -1) { this.calls = calls; this.now = now; }

            public bool Has(string kind, string arg)
            {
                var state = TwoNightRun.Current;
                switch (kind)
                {
                    case "flag": return flags.Contains(arg);
                    case "played": return played.Contains(arg);
                    case "phase": return state != null && state.phase.ToString() == arg;
                    case "delivered":
                        return state != null && state.customerTrades != null
                            && state.customerTrades.Any(t => t.state == ClinicTradeState.Delivered && t.caseId != null && t.caseId.EndsWith(arg));
                    default: return false;
                }
            }

            public bool TryNumber(string name, out float value)
            {
                var state = TwoNightRun.Current;
                value = 0;
                switch (name)
                {
                    case "time": value = (now >= 0 ? now : Time.unscaledTime) - calls.sceneStart; return true;
                    case "cash": if (state == null) return false; value = state.Cash; return true;
                    case "delivered":
                        if (state == null) return false;
                        value = state.customerTrades == null ? 0 : state.customerTrades.Count(t => t.state == ClinicTradeState.Delivered);
                        return true;
                    default: return false;
                }
            }
        }

        void OnDestroy() { if (Current == this) Current = null; }
    }
}
