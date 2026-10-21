using System.Collections.Generic;
using System.Linq;
using BorderRepair.TwoNight;
using BorderRepair.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BorderRepair.RepairProps
{
    // Phone calls that carry the story between orders, in the visor like a holo-call: a card on the left with the
    // caller's live portrait (ClinicNpcRuntime's studio), name and a typed subtitle line; the player's own lines in
    // hot amber. Non-blocking: it never takes the cursor or pauses work. It rings first (T or a click answers; it picks
    // up by itself after a few seconds), lines advance on their own (T skips), and it hangs up after the last line.
    // Each call plays once per run, triggered by the night's state: opening of night 1, delivered orders, night 2.
    [AddComponentMenu("")]
    public sealed class ClinicCallRuntime : MonoBehaviour
    {
        public const string ScenePath = RepairBenchV2Runtime.ScenePath;

        public sealed class Call
        {
            public string id, caller, name, title;
            public (bool player, string text)[] lines;
        }

        // Original lines for the NSP port setting (Docs/Integration/RepairPropsCyberBench/SETTING_PROPS.md).
        public static readonly Call[] Calls =
        {
            new Call { id = "rent-n1", caller = "Kasumi", name = "霞", title = "房东 · 岸壁三号街", lines = new[]
            {
                (false, "喂，开着门呢？……是我，霞。"),
                (false, "港务又封了两条泊位，夜里来修东西的人只会更多。别嫌累，这是好事。"),
                (true, "房租的事我记着。"),
                (false, "记着就好。一千二，第三晚之前交到我手上。我不收欠条，也不收“下周”。"),
                (false, "还有，七号那台机器人是港务租给你的，封条一张都别撕。他们查起来，连我这块招牌一起摘。"),
                (false, "……别熬太晚。挂了。"),
            } },
            new Call { id = "dealer-intro", caller = "Gen", name = "源", title = "岸壁ジャンク商会", lines = new[]
            {
                (false, "新开的修理铺？我是岸壁那头的源。你们这条街的配件，一半是我这儿出去的。"),
                (false, "缺板子、缺模块，就在终端里点“配件采购”。现货，现结，不问来路。"),
                (true, "不问来路？"),
                (false, "我不问你修的是谁的东西，你也别问我的货是从谁身上拆下来的。这一行能活下来，靠的就是这个。"),
                (false, "对了——客人要你保留原件，就照做。人身上的东西，旧的才是他自己的。"),
            } },
            new Call { id = "patrol-receiver", caller = "Rin", name = "凛", title = "三号管廊 · 夜班巡检", lines = new[]
            {
                (false, "这里是三号管廊巡检，凛。接收器我在泊位底下试过了，信号很干净。"),
                (false, "那台机子里存着我哥的值班呼叫时段。他在七号泊位底下……那次漏压以后，就再没上来。"),
                (false, "我知道那个频段已经没人在听了。可每晚两点十分，我还是想在那儿。"),
                (true, "复测的时候，02:10 有一次自动应答。呼号是「巡检 07-B」。"),
                (false, "……那是我哥的呼号。"),
                (false, "港务说过，出事以后他们用他的巡线记录训了一台自动巡检。原来连呼号都没改。"),
                (false, "它不是他。我知道。……可它还在那条管廊里，替他走那一圈。"),
                (true, "频段和呼叫时段都按原样留着。"),
                (false, "……谢谢。今晚两点十分，我会回它一句。"),
            } },
            new Call { id = "care-omamori", caller = "Tobi", name = "鳶", title = "街区照护站 · 夜班护理", lines = new[]
            {
                (false, "您好，我是照护站的夜班护理员，鳶。御守……还能用了吗？"),
                (false, "里面有一段录音，是我照顾过的一位奶奶。她走之前，每晚都要按一下，听她孙子说一声“晚安”。"),
                (false, "后来那孩子的号码停了。奶奶不知道，我也没告诉她。"),
                (true, "录音没有播放，也没有复制。不过只读核对的时候，看到了最近那些条目的录入终端。"),
                (false, "……照护站 03 号。夜班。"),
                (false, "是我录的。号码停了以后，奶奶还是每晚都按。我不能让它什么都不说。"),
                (false, "我学着那孩子的口气说“晚安”，说了两百多次。奶奶最后一晚也按了。她笑了。"),
                (true, "原来的身份保留了。那些条目也是。"),
                (false, "……谢谢。我想把它挂在照护站门口，值夜班的时候，就像还有人在说晚安。"),
                (false, "对了，电源里那根私接的线，是给小绿的。它是以前的看护机，厂家早就关服务器了，可阿婆们还认得它。"),
                (false, "您没拔掉它。今晚透析机一次都没断，小绿也还亮着。谢谢您。"),
            } },
            new Call { id = "dealer-cartridge", caller = "Gen", name = "源", title = "岸壁ジャンク商会", lines = new[]
            {
                (false, "卡匣修好了？好，那批货总算有一件能出手。"),
                (true, "里面的属主是 LUO.Y，装卸 3 组。状态写着“雇主已释放”。"),
                (false, "……你看得倒仔细。"),
                (false, "罗。吊机三组的。公司换了自动吊臂，把人裁了，义手合同到期就远程收回。手还在他身上的时候，就已经不是他的了。"),
                (false, "那只手干了十一年装卸。新手装上它，第一天就能接住摇晃的集装箱。买家要的就是这个。"),
                (true, "胶布上那句“别格式化”，是谁写的？"),
                (false, "……他自己。我收货那天，他追到船上来，就说了这一句。"),
                (false, "我会把买家的联系方式给他。至少让他知道，自己的手去了哪儿。别跟别人说是我心软。"),
            } },
            new Call { id = "rent-n2", caller = "Kasumi", name = "霞", title = "房东 · 岸壁三号街", lines = new[]
            {
                (false, "第二晚了。账我看过，还差多少你自己清楚。"),
                (false, "七号那台……昨晚的事我听说了。港务的人说是你这条线出的问题，我替你挡回去了。"),
                (true, "我会查清楚。"),
                (false, "这次我信你。别让我下次信不了。明晚之前，一千二。挂了。"),
            } },
        };

        static readonly HashSet<string> played = new HashSet<string>();
        static TwoNightState playedFor;

        public static ClinicCallRuntime Current { get; private set; }
        public static Call Find(string id) => Calls.FirstOrDefault(c => c.id == id);

        enum Mode { Idle, Ringing, Talking, Ending }
        Mode mode;
        Call call;
        int line;
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
        static void ResetHooks() { SceneManager.sceneLoaded -= OnSceneLoaded; Current = null; played.Clear(); playedFor = null; }

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
            call = next; line = -1; mode = Mode.Ringing; modeAt = Time.unscaledTime;
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

        public void Advance()
        {
            if (mode != Mode.Talking) return;
            line++;
            if (line >= call.lines.Length) { mode = Mode.Ending; modeAt = Time.unscaledTime; if (audioSource != null) audioSource.PlayOneShot(blip, .7f); return; }
            var (player, text) = call.lines[line];
            subtitle.text = (player ? "你　" : call.name + "　") + text;
            subtitle.color = player ? RepairPropCyberStyle.Hot : RepairPropCyberStyle.Ink;
            lineAt = Time.unscaledTime;
            lineHold = 1.6f + text.Length * .085f;
        }

        void Update()
        {
            if (TwoNightRun.Current != playedFor) { playedFor = TwoNightRun.Current; played.Clear(); }
            if (mode == Mode.Idle) { Trigger(); return; }
            float now = Time.unscaledTime;
            var keyboard = Keyboard.current;
            bool key = keyboard != null && keyboard.tKey.wasPressedThisFrame;
            switch (mode)
            {
                case Mode.Ringing:
                    if (key || now - modeAt > 6f) Pickup();
                    break;
                case Mode.Talking:
                    if (key || now - lineAt > lineHold) Advance();
                    break;
                case Mode.Ending:
                    if (now - modeAt > 1.1f) { mode = Mode.Idle; card.gameObject.SetActive(false); lastHangup = now; call = null; return; }
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
            footer.text = ringing ? "NSP-LINE " + RepairPropCyberStyle.Code(call.id, 6) + "  //  [T] ANSWER" : "[T] NEXT  //  " + (line + 1) + "/" + call.lines.Length;
            var npcs = ClinicNpcRuntime.Current;
            bool callerTalking = mode == Mode.Talking && line >= 0 && line < call.lines.Length && !call.lines[line].player
                && now - lineAt < Mathf.Min(lineHold - .4f, call.lines[line].text.Length / 34f + .3f);
            var feed = npcs != null && !ringing ? npcs.Portrait(call.caller, callerTalking) : null;
            portrait.texture = feed;
            // While ringing (and on hang-up) the feed is a blank with a flicker, the way the reference terminals boot.
            portrait.color = feed == null ? new Color(1f, .62f, .16f, ringing && Mathf.Repeat(now * 7, 1) < .5f ? .10f : .04f)
                : ending ? new Color(1f, .93f, .82f, Mathf.Clamp01(1 - (now - modeAt) / .6f)) : new Color(1f, .93f, .82f, 1);
        }

        void Trigger()
        {
            var state = TwoNightRun.Current;
            if (state == null || clinic == null || !clinic.Initialized) return;
            float now = Time.unscaledTime;
            if (now - lastHangup < 20f) return;
            if (state.phase == TwoNightPhase.Night1Counter)
            {
                if (now - sceneStart > 25f && Begin("rent-n1")) return;
                var trades = state.customerTrades ?? new List<ClinicCustomerTrade>();
                int delivered = trades.Count(t => t.state == ClinicTradeState.Delivered);
                if ((delivered >= 1 || now - sceneStart > 150f) && played.Contains("rent-n1") && Begin("dealer-intro")) return;
                if (trades.Any(t => t.state == ClinicTradeState.Delivered && t.caseId != null && t.caseId.EndsWith("receiver")) && Begin("patrol-receiver")) return;
                if (trades.Any(t => t.state == ClinicTradeState.Delivered && t.caseId != null && t.caseId.EndsWith("omamori")) && Begin("care-omamori")) return;
                if (trades.Any(t => t.state == ClinicTradeState.Delivered && t.caseId != null && t.caseId.EndsWith("cartridge")) && played.Contains("dealer-intro") && Begin("dealer-cartridge")) return;
            }
            else if (state.phase == TwoNightPhase.Night2Open && now - sceneStart > 8f) Begin("rent-n2");
        }

        void OnDestroy() { if (Current == this) Current = null; }
    }
}
