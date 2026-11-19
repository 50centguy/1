using System.Collections;
using System.Collections.Generic;
using BorderRepair.Controls;
using BorderRepair.FirstOrder;
using UnityEngine;
using UnityEngine.UI;

namespace BorderRepair.RepairProps
{
    public enum B1FiddleKind { CagedBulb, FestiveBulbs, Heater, WaterDispenser, Newspaper, Thermos, Radio }

    // Small things in the Berth 04 B1 the player can fiddle with while walking: aim the crosshair, left click. Nothing here
    // touches the trade (the director's zones): each fiddle has its own solid box on the default layer, so the director's
    // pick stops on it and does nothing, while B1FiddleRuntime picks it, shows its hint and uses it. Wired by
    // ExteriorDistrictPass.B1Fiddles in the playable district copy (the items are made non-static there so they can move).
    public sealed class B1Fiddle : MonoBehaviour
    {
        public B1FiddleKind kind;
        public Transform item;             // what moves (the thermos, the paper, the bulb group, the tap)
        public Transform second;           // the thermos cup; the water bottle
        public Light lamp;
        public Renderer[] glows = new Renderer[0];
        public AudioSource speaker;

        public int State { get; private set; }
        public int Uses { get; private set; }
        public bool Busy => busy != null;

        Coroutine busy;
        Color[] glowBase;
        MaterialPropertyBlock block;
        GameObject tea;
        Vector3 itemPos, secondPos; Quaternion itemRot, secondRot;
        static readonly int Emission = Shader.PropertyToID("_EmissionColor");

        static readonly string[] Headlines =
        {
            "港区夜报｜四号泊位夜间限电延长至月底，修理铺获准保留一路照明。",
            "港区夜报｜二手义体配件价格本周再涨一成，旧件回收站排起长队。",
            "港区夜报｜气象台：寒潮滞留，明晨码头最低零下十九度。",
            "港区夜报｜无照诊所整治暂缓，街道办称“先过完这个冬天”。",
        };
        static readonly string[] RadioLines = { "收音机：……沙沙……深夜点歌台，下一首送给还在值夜班的人。", "收音机：……港务频道：三号起重机停用，请绕行……沙沙……" };

        void Awake()
        {
            block = new MaterialPropertyBlock();
            glowBase = new Color[glows.Length];
            for (int i = 0; i < glows.Length; i++)
                glowBase[i] = glows[i] != null && glows[i].sharedMaterial != null && glows[i].sharedMaterial.HasProperty(Emission) ? glows[i].sharedMaterial.GetColor(Emission) : Color.black;
            if (item != null) { itemPos = item.position; itemRot = item.rotation; }
            if (second != null) { secondPos = second.position; secondRot = second.rotation; }
            // The radio starts off; the rest as the room was dressed (bulb, heater and string lit).
            if (kind == B1FiddleKind.Radio) SetGlow(0);
            if (kind == B1FiddleKind.Heater) Loop(B1FiddleSound.Hum, .16f);
            B1FiddleRuntime.Ensure();
        }

        public string Hint
        {
            get
            {
                switch (kind)
                {
                    case B1FiddleKind.CagedBulb: return State == 0 ? "左键：拉灯绳（关灯）" : "左键：拉灯绳（开灯）";
                    case B1FiddleKind.FestiveBulbs: return State == 0 ? "左键：彩灯换成闪烁" : State == 1 ? "左键：关掉彩灯" : "左键：打开彩灯";
                    case B1FiddleKind.Heater: return State == 0 ? "左键：关掉电暖器" : "左键：打开电暖器";
                    case B1FiddleKind.WaterDispenser: return "左键：接一杯凉水";
                    case B1FiddleKind.Newspaper: return "左键：翻看报纸";
                    case B1FiddleKind.Thermos: return State == 0 ? "左键：倒一杯热茶" : "左键：把杯盖拧回去";
                    case B1FiddleKind.Radio: return State == 0 ? "左键：打开收音机" : State == 1 ? "左键：换个频道" : "左键：关掉收音机";
                }
                return null;
            }
        }

        // Returns a line for the toast (or null).
        public string Use()
        {
            if (busy != null) return null;
            Uses++;
            switch (kind)
            {
                case B1FiddleKind.CagedBulb:
                    State = 1 - State;
                    if (lamp != null) lamp.enabled = State == 0;
                    SetGlow(State == 0 ? 1 : 0);
                    Play(B1FiddleSound.Click, .7f);
                    busy = StartCoroutine(Sway());
                    return null;
                case B1FiddleKind.FestiveBulbs:
                    State = (State + 1) % 3;
                    SetGlow(State == 2 ? 0 : 1);
                    Play(B1FiddleSound.Click, .4f);
                    return State == 1 ? "彩灯一闪一闪的，像过节。" : null;
                case B1FiddleKind.Heater:
                    State = 1 - State;
                    SetGlow(State == 0 ? 1 : 0);
                    Play(B1FiddleSound.Click, .6f);
                    if (State == 0) Loop(B1FiddleSound.Hum, .16f); else if (speaker != null) speaker.Stop();
                    return State == 1 ? "电暖器停了，屋里很快就凉下来。" : null;
                case B1FiddleKind.WaterDispenser:
                    Play(B1FiddleSound.Gurgle, .8f);
                    busy = StartCoroutine(Pour());
                    return "接了一杯凉水，桶里咕咚冒了几个泡。";
                case B1FiddleKind.Newspaper:
                    Play(B1FiddleSound.Rustle, .8f);
                    busy = StartCoroutine(Lift());
                    return Headlines[(Uses - 1) % Headlines.Length];
                case B1FiddleKind.Thermos:
                    State = 1 - State;
                    busy = StartCoroutine(State == 1 ? PourTea() : CapBack());
                    return State == 1 ? "倒了半杯热茶，还烫手。" : null;
                case B1FiddleKind.Radio:
                    State = (State + 1) % 3;
                    Play(B1FiddleSound.Click, .5f);
                    SetGlow(State == 0 ? 0 : 1);
                    if (State == 0) { if (speaker != null) speaker.Stop(); return null; }
                    Loop(State == 1 ? B1FiddleSound.RadioSong : B1FiddleSound.RadioTalk, .5f);
                    return RadioLines[State - 1];
            }
            return null;
        }

        void Update()
        {
            if (kind != B1FiddleKind.FestiveBulbs || State != 1) return;
            float t = Time.time;
            for (int i = 0; i < glows.Length; i++)
            {
                float k = .15f + .85f * Mathf.Clamp01(Mathf.Sin(t * 2.3f + i * 1.7f) * 1.4f + .3f * Mathf.Sin(t * 7.1f + i));
                SetGlow(i, k);
            }
        }

        void SetGlow(float k) { for (int i = 0; i < glows.Length; i++) SetGlow(i, k); }

        void SetGlow(int i, float k)
        {
            var r = glows[i];
            if (r == null) return;
            r.GetPropertyBlock(block);
            block.SetColor(Emission, glowBase[i] * k);
            r.SetPropertyBlock(block);
        }

        void Play(B1FiddleSound s, float volume)
        {
            if (speaker == null) return;
            speaker.PlayOneShot(B1FiddleSounds.Get(s), volume);
        }

        void Loop(B1FiddleSound s, float volume)
        {
            if (speaker == null) return;
            speaker.clip = B1FiddleSounds.Get(s); speaker.loop = true; speaker.volume = volume; speaker.Play();
        }

        // The bulb swings on its cord from the ceiling, damped.
        IEnumerator Sway()
        {
            var pivot = itemPos + Vector3.up * .45f;
            if (item.GetComponentInChildren<Renderer>() is Renderer any)
            {
                float top = float.MinValue;
                foreach (var r in item.GetComponentsInChildren<Renderer>()) top = Mathf.Max(top, r.bounds.max.y);
                pivot = new Vector3(any.bounds.center.x, top, any.bounds.center.z);
            }
            for (float t = 0; t < 2.4f; t += Time.deltaTime)
            {
                var q = Quaternion.Euler(Mathf.Sin(t * 7f) * 9f * Mathf.Exp(-t * 1.6f), 0, Mathf.Sin(t * 5.3f + 1) * 5f * Mathf.Exp(-t * 1.6f));
                item.SetPositionAndRotation(pivot + q * (itemPos - pivot), q * itemRot);
                yield return null;
            }
            item.SetPositionAndRotation(itemPos, itemRot);
            busy = null;
        }

        IEnumerator Pour()
        {
            for (float t = 0; t < 1.4f; t += Time.deltaTime)
            {
                if (item != null) item.position = itemPos + Vector3.down * (t < 1.1f ? .008f : 0);
                if (second != null) second.position = secondPos + Vector3.up * Mathf.Sin(t * 30) * .002f * (1 - t / 1.4f);
                yield return null;
            }
            if (item != null) item.position = itemPos;
            if (second != null) second.position = secondPos;
            busy = null;
        }

        IEnumerator Lift()
        {
            var up = itemPos + Vector3.up * .06f;
            var rot = itemRot * Quaternion.Euler(-14, 8, 0);
            for (float t = 0; t < .35f; t += Time.deltaTime) { item.SetPositionAndRotation(Vector3.Lerp(itemPos, up, t / .35f), Quaternion.Slerp(itemRot, rot, t / .35f)); yield return null; }
            yield return new WaitForSeconds(2.2f);
            for (float t = 0; t < .35f; t += Time.deltaTime) { item.SetPositionAndRotation(Vector3.Lerp(up, itemPos, t / .35f), Quaternion.Slerp(rot, itemRot, t / .35f)); yield return null; }
            item.SetPositionAndRotation(itemPos, itemRot);
            busy = null;
        }

        // The cup comes off the top and stands beside the flask, the flask tips over it about its base and back, the cup has tea.
        IEnumerator PourTea()
        {
            var aside = itemPos + Vector3.right * .1f + Vector3.forward * .02f;
            float baseY = item.GetComponent<Renderer>().bounds.min.y;
            var cupAt = new Vector3(aside.x, baseY + (secondPos.y - second.GetComponent<Renderer>().bounds.min.y), aside.z);
            Play(B1FiddleSound.Click, .35f);
            for (float t = 0; t < .4f; t += Time.deltaTime) { second.position = Arc(secondPos, cupAt, t / .4f); yield return null; }
            second.position = cupAt;
            var foot = new Vector3(itemPos.x, baseY, itemPos.z) + Vector3.right * .035f;
            Play(B1FiddleSound.Pour, .7f);
            for (float t = 0; t < 1.6f; t += Time.deltaTime)
            {
                float a = Mathf.Sin(Mathf.Clamp01(t / 1.6f) * Mathf.PI) * 62f;
                var q = Quaternion.Euler(0, 0, -a);
                item.SetPositionAndRotation(foot + q * (itemPos - foot), q * itemRot);
                yield return null;
            }
            item.SetPositionAndRotation(itemPos, itemRot);
            if (tea == null)
            {
                tea = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(tea.GetComponent<Collider>());
                tea.name = "Tea";
                var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                m.SetColor("_BaseColor", new Color(.36f, .2f, .07f)); m.SetFloat("_Smoothness", .9f);
                tea.GetComponent<Renderer>().sharedMaterial = m;
            }
            tea.SetActive(true);
            tea.transform.SetParent(second, true);
            var cupB = second.GetComponent<Renderer>().bounds;
            tea.transform.position = new Vector3(cupB.center.x, cupB.max.y - .012f, cupB.center.z);
            tea.transform.localRotation = Quaternion.identity;
            tea.transform.localScale = new Vector3(.88f, .004f / Mathf.Max(.001f, cupB.size.y) * 2, .88f);
            busy = null;
        }

        IEnumerator CapBack()
        {
            if (tea != null) tea.SetActive(false);
            var from = second.position;
            Play(B1FiddleSound.Click, .35f);
            for (float t = 0; t < .4f; t += Time.deltaTime) { second.position = Arc(from, secondPos, t / .4f); yield return null; }
            second.SetPositionAndRotation(secondPos, secondRot);
            busy = null;
        }

        static Vector3 Arc(Vector3 a, Vector3 b, float t) => Vector3.Lerp(a, b, t) + Vector3.up * Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI) * .05f;
    }

    // One per scene: the walking pick (ten times a second), the hint under the crosshair and the toast.
    public sealed class B1FiddleRuntime : MonoBehaviour
    {
        public static B1FiddleRuntime Instance { get; private set; }
        public B1Fiddle Aimed { get; private set; }
        public string HintText => hint != null && hintBox.activeSelf ? hint.text : null;
        public string ToastText => toastBox != null && toastBox.activeSelf ? toast.text : null;

        FirstPersonWalker walker;
        float lastDistance;
        // Small things are taken at arm's length, a little past the trade reach (a paper on a chair, a bulb overhead).
        float Reach => walker.Reach + .35f;
        float nextProbe, nextFind, toastUntil;
        GameObject hintBox, toastBox; Text hint, toast;
        readonly RaycastHit[] hits = new RaycastHit[16];

        public static void Ensure()
        {
            if (Instance != null) return;
            Instance = new GameObject("B1 fiddles - pick, hint, toast").AddComponent<B1FiddleRuntime>();
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start() => BuildUi();

        void Update()
        {
            if (walker == null && Time.unscaledTime >= nextFind) { nextFind = Time.unscaledTime + 1; walker = FindFirstObjectByType<FirstPersonWalker>(); }
            if (toastBox.activeSelf && Time.unscaledTime > toastUntil) toastBox.SetActive(false);
            bool aiming = walker != null && walker.Aiming && walker.Eye != null;
            if (!aiming) { Show(null, false); Aimed = null; return; }
            if (Time.unscaledTime >= nextProbe)
            {
                nextProbe = Time.unscaledTime + .1f;
                Aimed = Pick(out float distance); lastDistance = distance;
                if (Aimed == null) Show(null, false);
                else if (distance > Reach) Show("够不着：走近一点", false);
                else Show(Aimed.Hint, true);
            }
            if (Aimed != null && RepairInput.LeftPressed && !walker.ClickConsumedThisFrame && lastDistance <= Reach)
            {
                var line = Aimed.Use();
                if (line != null) Toast(line);
                nextProbe = 0;
            }
        }

        // The first thing on the line that is not the player, an invisible walking blocker or one of the B1's generated
        // solids (Ignore Raycast: they box whole pieces of furniture, the paper on the chairs included).
        B1Fiddle Pick(out float distance)
        {
            distance = 0;
            int n = Physics.RaycastNonAlloc(walker.Eye.position, walker.Eye.forward, hits, 6, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, 0, n, HitOrder.Instance);
            for (int i = 0; i < n; i++)
            {
                var c = hits[i].collider;
                if (c is CharacterController || c.gameObject.layer == 2 || c.GetComponent<FirstPersonBlocker>() != null) continue;
                var f = c.GetComponentInParent<B1Fiddle>();
                distance = hits[i].distance;
                return f;
            }
            return null;
        }

        sealed class HitOrder : IComparer<RaycastHit>
        {
            public static readonly HitOrder Instance = new HitOrder();
            public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
        }

        public void Toast(string line)
        {
            toast.text = line;
            ((RectTransform)toastBox.transform).sizeDelta = new Vector2(Mathf.Min(900, toast.preferredWidth + 40), 40);
            toastBox.SetActive(true);
            toastUntil = Time.unscaledTime + 4.5f;
        }

        void Show(string text, bool can)
        {
            if (hintBox == null) return;
            bool show = text != null;
            if (hintBox.activeSelf != show) hintBox.SetActive(show);
            if (!show) return;
            hint.color = can ? new Color(.6f, .9f, .95f) : new Color(1f, .68f, .32f);
            if (hint.text == text) return;
            hint.text = text;
            ((RectTransform)hintBox.transform).sizeDelta = new Vector2(hint.preferredWidth + 24, 28);
        }

        void BuildUi()
        {
            var canvasGo = new GameObject("B1 fiddle UI", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 31;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1600, 900); scaler.matchWidthOrHeight = .5f;
            var font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Microsoft YaHei UI", "SimHei", "PingFang SC", "Noto Sans CJK SC", "Arial" }, 16);
            (hintBox, hint) = Box(canvasGo.transform, "Fiddle hint", font, 15, new Vector2(.5f, .5f), new Vector2(0, -22), new Vector2(.5f, 1f));
            (toastBox, toast) = Box(canvasGo.transform, "Fiddle toast", font, 17, new Vector2(.5f, 0), new Vector2(0, 150), new Vector2(.5f, 0));
            toast.color = new Color(.93f, .9f, .82f);
            hintBox.SetActive(false); toastBox.SetActive(false);
        }

        static (GameObject, Text) Box(Transform parent, string name, Font font, int size, Vector2 anchor, Vector2 pos, Vector2 pivot)
        {
            var box = new GameObject(name, typeof(RectTransform), typeof(Image));
            box.transform.SetParent(parent, false);
            var rect = (RectTransform)box.transform;
            rect.anchorMin = rect.anchorMax = anchor; rect.pivot = pivot; rect.anchoredPosition = pos; rect.sizeDelta = new Vector2(240, 28);
            var back = box.GetComponent<Image>(); back.raycastTarget = false; back.color = new Color(0, 0, 0, .62f);
            var label = new GameObject("Text", typeof(RectTransform), typeof(Text));
            label.transform.SetParent(box.transform, false);
            var lr = (RectTransform)label.transform;
            lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one; lr.offsetMin = lr.offsetMax = Vector2.zero;
            var text = label.GetComponent<Text>();
            text.font = font; text.fontSize = size; text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            return (box, text);
        }
    }

    public enum B1FiddleSound { Click, Hum, Gurgle, Rustle, Pour, RadioSong, RadioTalk }

    // Small synthesised sounds, made once on first use (a few hundred kB in all).
    public static class B1FiddleSounds
    {
        const int Rate = 22050;
        static readonly Dictionary<B1FiddleSound, AudioClip> clips = new Dictionary<B1FiddleSound, AudioClip>();

        public static AudioClip Get(B1FiddleSound s)
        {
            if (clips.TryGetValue(s, out var c) && c != null) return c;
            float[] d;
            var rng = new System.Random(17 + (int)s);
            float N() => (float)rng.NextDouble() * 2 - 1;
            switch (s)
            {
                case B1FiddleSound.Click:
                    d = new float[Rate / 12];
                    for (int i = 0; i < d.Length; i++) { float t = i / (float)Rate; d[i] = (N() * .6f + Mathf.Sin(t * 2 * Mathf.PI * 1900) * .5f) * Mathf.Exp(-t * 160) + N() * .25f * Mathf.Exp(-(t - .03f) * (t - .03f) * 4e5f); }
                    break;
                case B1FiddleSound.Hum:
                    // 2 s loop: mains hum and a little fan hiss; whole cycles of 50 Hz so it loops clean.
                    d = new float[Rate * 2];
                    { float lp = 0; for (int i = 0; i < d.Length; i++) { float t = i / (float)Rate; lp += (N() - lp) * .05f; d[i] = Mathf.Sin(t * 2 * Mathf.PI * 50) * .35f + Mathf.Sin(t * 2 * Mathf.PI * 100) * .2f + lp * .5f; } }
                    break;
                case B1FiddleSound.Gurgle:
                    d = new float[Rate * 3 / 2];
                    { float lp = 0; for (int i = 0; i < d.Length; i++) { float t = i / (float)Rate; lp += (N() - lp) * .08f; d[i] = lp * .5f * Mathf.Clamp01(1.2f - t); } }
                    for (int b = 0; b < 5; b++)
                    {
                        int at = (int)(Rate * (.35f + b * .2f + (float)rng.NextDouble() * .08f)); float f0 = 300 + (float)rng.NextDouble() * 250;
                        for (int i = 0; i < Rate / 10 && at + i < d.Length; i++) { float t = i / (float)Rate; d[at + i] += Mathf.Sin(2 * Mathf.PI * (f0 * t + 1800 * t * t)) * .5f * Mathf.Exp(-t * 40); }
                    }
                    break;
                case B1FiddleSound.Rustle:
                    d = new float[Rate * 7 / 10];
                    { float hp = 0, prev = 0; for (int i = 0; i < d.Length; i++) { float t = i / (float)Rate, x = N(); hp = .9f * (hp + x - prev); prev = x; float env = Mathf.Abs(Mathf.Sin(t * 19)) * Mathf.Clamp01(1 - t / .7f); d[i] = hp * .5f * env * (rng.NextDouble() < .3 ? 1.5f : 1); } }
                    break;
                case B1FiddleSound.Pour:
                    d = new float[Rate * 8 / 5];
                    { float lp = 0; for (int i = 0; i < d.Length; i++) { float t = i / (float)Rate; float a = Mathf.Lerp(.05f, .3f, t / 1.6f); lp += (N() - lp) * a; d[i] = lp * .7f * Mathf.Sin(Mathf.Clamp01(t / 1.6f) * Mathf.PI); } }
                    break;
                case B1FiddleSound.RadioSong:
                    // 8 s loop: a slow pentatonic tune on a soft square-ish tone, a bass note, through a little crackle.
                    d = new float[Rate * 8];
                    {
                        int[] notes = { 0, 3, 5, 7, 10, 7, 5, 3, 0, 5, 3, 0, -2, 0, 3, 0 };
                        float lp = 0;
                        for (int i = 0; i < d.Length; i++)
                        {
                            float t = i / (float)Rate; int k = (int)(t * 2) % notes.Length; float nt = t * 2 - Mathf.Floor(t * 2);
                            float f = 330 * Mathf.Pow(2, notes[k] / 12f), fb = 110 * Mathf.Pow(2, (k / 4 % 2 == 0 ? 0 : -2) / 12f);
                            float tone = Mathf.Sin(2 * Mathf.PI * f * t) + .3f * Mathf.Sin(2 * Mathf.PI * 3 * f * t) + .5f * Mathf.Sin(2 * Mathf.PI * fb * t);
                            lp += (N() - lp) * .3f;
                            float crackle = rng.NextDouble() < .0015 ? N() * 2 : 0;
                            d[i] = tone * .22f * Mathf.Exp(-nt * 2.2f) * (.85f + .15f * Mathf.Sin(t * 31)) + lp * .08f + crackle * .3f;
                        }
                    }
                    break;
                default: // RadioTalk: a voice-like murmur (formant-ish buzz in syllables) under static
                    d = new float[Rate * 6];
                    {
                        float lp = 0, ph = 0;
                        for (int i = 0; i < d.Length; i++)
                        {
                            float t = i / (float)Rate; float syl = Mathf.Max(0, Mathf.Sin(t * 2 * Mathf.PI * 3.1f)) * Mathf.Max(0, Mathf.Sin(t * 2 * Mathf.PI * .45f) + .3f);
                            ph += 2 * Mathf.PI * (120 + 25 * Mathf.Sin(t * 1.3f)) / Rate;
                            float buzz = Mathf.Sin(ph) * .5f + Mathf.Sin(ph * 5.3f) * .25f + Mathf.Sin(ph * 9.1f) * .12f;
                            lp += (N() - lp) * .4f;
                            d[i] = buzz * syl * .3f + lp * .12f;
                        }
                    }
                    break;
            }
            c = AudioClip.Create("B1 fiddle - " + s, d.Length, 1, Rate, false);
            c.SetData(d, 0);
            clips[s] = c;
            return c;
        }
    }
}
