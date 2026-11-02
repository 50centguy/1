using System;
using System.Collections.Generic;
using System.Linq;
using BorderRepair.Cleaning;
using BorderRepair.Controls;
using BorderRepair.Core;
using BorderRepair.Narrative;
using BorderRepair.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BorderRepair.TwoNight
{
    /// <summary>Optional customer-item cleaning. Adds no mechanical repair step, income or save-format changes.</summary>
    [DefaultExecutionOrder(-120)]
    public sealed class ClinicCleaningController : MonoBehaviour
    {
        public UnifiedClinicDirector Clinic { get; private set; }
        public bool Cleaning { get; private set; }
        // 手感数值（改动前 .045 / 2.4 / .97）。笔刷面积 ×1.8、最厚的污垢约 0.3 秒擦掉，整件外壳一两分钟内擦得完；
        // 单次局部擦拭仍然只能清掉一小块。质量进度过线后剩下的零星污点一次收尾，不用满壳找最后几个像素。
        [Range(.01f,.2f)] public float brushRadiusUV = .06f;
        [Range(.1f,8)] public float strengthPerSecond = 3.2f;
        [Range(.5f,1)] public float completionThreshold = .93f;
        const float HeavyDirt = .55f, LightDirt = .18f, NoEffectDeny = .35f;
        public enum WipeResult { None, Removed, AlreadyClean, Blocked, Miss }
        public WipeResult LastWipe { get; private set; }
        public IReadOnlyList<CleanableSurface> Surfaces => surfaces;
        CleanableSurface[] surfaces = Array.Empty<CleanableSurface>();
        CleaningOccluder[] occluders = Array.Empty<CleaningOccluder>();
        GameObject mounted, ui;
        Button toggle;
        Text text, buttonText, hint, banner;
        Image progressFill, ring, burst;
        readonly Image[] specks = new Image[8];
        readonly Vector2[] speckVelocity = new Vector2[8];
        readonly float[] speckLife = new float[8];
        RectTransform uiRect;
        bool previousScan;
        bool previousScannedTint;
        CleanableSurface previousSurface;
        Vector2 previousUv;
        bool stroke;
        // Feedback state. Sound follows real progress events and per-stroke timers, never individual brush updates.
        readonly Dictionary<RepairSoundSynth.Cue, AudioClip> clips = new Dictionary<RepairSoundSynth.Cue, AudioClip>();
        readonly List<Texture2D> ownedTextures = new List<Texture2D>();
        AudioSource wipeLoop, oneShot;
        CleanableSurface hitSurface;
        Vector3 lastHitPoint;
        float lastRemaining, wipeGain, noEffectTime, nextGrain, nextTierSound, nextDeny, nextSpeck;
        float tierFlash, hintUntil, bannerUntil, burstAt = -10, strokeAt = -10;
        bool wasHeld, pendingDone;
        public float Progress => surfaces.Length == 0 ? 0 : surfaces.Average(s => s != null ? s.Progress : 0);
        public bool AllClean => surfaces.Length > 0 && surfaces.All(s => s == null || s.IsClean);
        public bool Available => Clinic != null && Clinic.Initialized && Clinic.ConsoleOpen &&
            Clinic.Station != null && Clinic.Station.Session != null && Clinic.Station.Session.Stage == RepairStage.Inspect &&
            Clinic.Station.Inspector.isActiveAndEnabled && Clinic.Station.Inspector.InteractionEnabled &&
            !Clinic.Station.InputLocked && !Clinic.Counter.DialogueVisible && surfaces.Length > 0 &&
            (Cleaning || !Clinic.Station.CleaningActive);   // appearance editing and the shop hold the same claim

        /// <summary>现在不能清洁的原因；能清洁时为 null。</summary>
        public string UnavailableReason
        {
            get
            {
                if (Available) return null;
                if (surfaces.Length == 0) return "这件物品没有可清洁的外壳。";
                if (Clinic == null || !Clinic.Initialized || !Clinic.ConsoleOpen || Clinic.Station == null) return "先回到维修台。";
                if (Clinic.Counter.DialogueVisible) return "先听完顾客说话。";
                if (Clinic.Station.Session == null || Clinic.Station.Session.Stage != RepairStage.Inspect) return "只有检查阶段才能清洁。";
                if (Clinic.Station.InputLocked) return "工具正在动作，等它结束。";
                if (Clinic.Station.CleaningActive) return "外观编辑或商店还开着，先关掉。";
                return "现在不能清洁。";
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            SceneManager.sceneLoaded -= SceneLoaded;
            SceneManager.sceneLoaded += SceneLoaded;
        }
        static void SceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.path != "Assets/BorderRepair/Scenes/UnifiedClinic/UnifiedClinic.unity") return;
            var clinic = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<UnifiedClinicDirector>(true)).FirstOrDefault();
            if (clinic == null || scene.GetRootGameObjects().Any(g => g.GetComponent<ClinicCleaningController>() != null)) return;
            var root = new GameObject("ClinicCleaning_Runtime"); SceneManager.MoveGameObjectToScene(root, scene);
            root.AddComponent<ClinicCleaningController>().Configure(clinic);
        }

        public void Configure(UnifiedClinicDirector clinic) { Clinic = clinic; }

        void OnEnable() => CleanableSurface.SetOverlayHost(true);

        void Start()
        {
            if (Clinic == null) Clinic = FindFirstObjectByType<UnifiedClinicDirector>();
            BuildUi();
            BuildAudio();
        }

        void Update()
        {
            if (Clinic == null || Clinic.Station == null) return;
            var item = Clinic.Station.Inspector.CurrentItem;
            if (item != mounted)
            {
                SetCleaning(false);
                Bind(item);
            }
            if (pendingDone) { pendingDone = false; PlayCompletion(); }
            if (!Available && Cleaning) SetCleaning(false);
            bool textFocused = EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null &&
                EventSystem.current.currentSelectedGameObject.GetComponent<InputField>() != null;
            if (!textFocused && Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame)
            {
                bool wanted = !Cleaning;
                // In the room C is the walker's crouch: only refuse out loud at the bench, on an item that can be cleaned.
                if (!SetCleaning(wanted) && wanted && Clinic.ConsoleOpen && surfaces.Length > 0) Deny(UnavailableReason);
            }
            bool held = Cleaning && RepairInput.LeftHeld && !RepairInput.RightHeld && !RepairInput.PointerOverUI;
            bool pressed = held && !wasHeld;
            wasHeld = held;
            if (held) WipeAt(RepairInput.PointerPosition, Mathf.Min(Time.unscaledDeltaTime, .1f));
            else { stroke = false; previousSurface = null; }
            UpdateStrokeFeedback(held, pressed);
            UpdatePresentation(held);
            if (ui != null)
            {
                ui.SetActive(Clinic.ConsoleOpen && mounted != null && surfaces.Length > 0);
                toggle.interactable = Available;
                buttonText.text = Cleaning ? "退出清洁 [C]" : "清洁工具 [C]";
                string detail = !Available && !Cleaning ? UnavailableReason : AllClean ? "已全部清洁" + (Cleaning ? " · 按 C 退出" : "")
                    : Cleaning ? "按住左键擦拭 · 右键转动 · 滚轮缩放" : "局部清洁，不改变维修步骤与费用";
                text.text = $"污垢清除 {Progress:P0}\n{detail}";
            }
        }

        void Bind(GameObject item)
        {
            foreach (var surface in surfaces) Unsubscribe(surface);
            mounted = item;
            surfaces = item != null ? item.GetComponentsInChildren<CleanableSurface>(true)
                .Where(s => s.Ready && s.Target.gameObject.activeInHierarchy).ToArray() : Array.Empty<CleanableSurface>();
            occluders = item != null ? item.GetComponentsInChildren<CleaningOccluder>(true) : Array.Empty<CleaningOccluder>();
            foreach (var surface in surfaces)
            {
                surface.SetCompletionThreshold(completionThreshold);
                surface.ProgressTier += OnSurfaceTier;
                surface.Cleaned += OnSurfaceCleaned;
            }
            pendingDone = false;
        }

        void Unsubscribe(CleanableSurface surface)
        {
            if (surface == null) return;
            surface.ProgressTier -= OnSurfaceTier;
            surface.Cleaned -= OnSurfaceCleaned;
        }

        public bool SetCleaning(bool value)
        {
            if (value == Cleaning) return Cleaning;
            if (value && !Available) return false;
            if (Clinic == null || Clinic.Station == null) return false;
            var station = Clinic.Station;
            LastWipe = WipeResult.None;
            noEffectTime = 0;
            if (value)
            {
                previousScan = station.Session.ScanModeActive;
                previousScannedTint = station.Scanner.ShowScannedTint;
                station.SelectTool(null);
                station.Session.SetScanMode(false);
            }
            Cleaning = value; stroke = false; previousSurface = null;
            station.CleaningActive = value;
            station.Inspector.LeftPointerReserved = value;
            station.Scanner.InteractionSuppressed = value;
            station.Scanner.ShowScannedTint = value ? false : previousScannedTint;
            foreach (var surface in surfaces) if (surface != null) surface.SetProbeEnabled(value);
            foreach (var occluder in occluders) if (occluder != null) occluder.SetProbeEnabled(value);
            if (!value && station.Session != null && station.Session.Stage == RepairStage.Inspect)
                station.Session.SetScanMode(previousScan);
            return Cleaning;
        }

        /// <summary>Same path used by real input and acceptance tests. No cleaning through UI or room occluders.</summary>
        public bool WipeAt(Vector2 screen, float deltaSeconds)
        {
            if (!float.IsFinite(deltaSeconds) || deltaSeconds <= 0 || !TryGetCleaningHit(screen,out var closest,out var hit))
            { stroke = false; previousSurface = null; LastWipe = Classify(screen); return false; }
            Vector2 uv = hit.textureCoord2;
            hitSurface = closest; lastHitPoint = hit.point; lastRemaining = closest.RemainingAt(uv);
            float amount = strengthPerSecond * Mathf.Min(deltaSeconds, .1f);
            bool changed = closest.Wipe(uv, brushRadiusUV, amount);
            // Fill fast mouse strokes, but do not bridge distant atlas islands or a change of surface.
            if (stroke && closest == previousSurface && closest.SameIsland(uv, previousUv) && Vector2.Distance(uv, previousUv) < .25f)
            {
                int steps = Mathf.Clamp(Mathf.CeilToInt(Vector2.Distance(uv, previousUv) / (brushRadiusUV * .4f)), 1, 24);
                for (int i = 1; i < steps; i++) changed |= closest.Wipe(Vector2.Lerp(previousUv, uv, i / (float)steps), brushRadiusUV, amount);
            }
            previousUv = uv; previousSurface = closest; stroke = true;
            LastWipe = changed ? WipeResult.Removed : WipeResult.AlreadyClean;
            return changed;
        }

        // Why a ray found nothing to clean: on the item but not a cleanable part (screen, label, seal) or simply off it.
        WipeResult Classify(Vector2 screen)
        {
            if (!Cleaning || !Available || !float.IsFinite(screen.x) || !float.IsFinite(screen.y) || IsOverUi(screen)) return WipeResult.None;
            var camera = Clinic.Station.Inspector.ViewCamera;
            if (!camera.pixelRect.Contains(screen)) return WipeResult.None;
            Ray ray = camera.ScreenPointToRay(screen);
            foreach (var surface in surfaces)
                if (surface != null && surface.Raycast(ray, 10, out _)) return WipeResult.Blocked;
            foreach (var occluder in occluders)
                if (occluder != null && occluder.Blocks(ray, 10)) return WipeResult.Blocked;
            return WipeResult.Miss;
        }

        WipeResult Hover(Vector2 screen)
        {
            if (!TryGetCleaningHit(screen, out var closest, out var hit)) return Classify(screen);
            return closest.RemainingAt(hit.textureCoord2) > .01f ? WipeResult.Removed : WipeResult.AlreadyClean;
        }

        // ---------------------------------------------------------------- feedback
        void OnSurfaceTier(int tier)
        {
            if (tier >= 10) return;   // the last tier is the completion cue's
            tierFlash = 1;
            if (Time.unscaledTime < nextTierSound) return;
            nextTierSound = Time.unscaledTime + .09f;
            OneShot(RepairSoundSynth.Cue.CleanDust, .3f, Mathf.Lerp(.9f, 1.45f, tier / 10f));
        }

        // Deferred one frame: F4 and multi-part wipes clean several surfaces at once and should sound as one finish.
        void OnSurfaceCleaned() => pendingDone = true;

        void PlayCompletion()
        {
            if (oneShot != null && Time.unscaledTime - strokeAt > .3f && mounted != null) oneShot.transform.position = mounted.transform.position;
            bool all = AllClean;
            OneShot(RepairSoundSynth.Cue.CleanDone, all ? .95f : .55f, all ? 1f : 1.22f);
            tierFlash = 1;
            if (all)
            {
                burstAt = Time.unscaledTime;
                bannerUntil = Time.unscaledTime + 1.1f;
            }
        }

        void Deny(string reason)
        {
            if (hint != null && !string.IsNullOrEmpty(reason))
            {
                hint.text = reason;
                hint.color = new Color(1f, .62f, .3f);
                hintUntil = Time.unscaledTime + 1.8f;
            }
            if (Time.unscaledTime < nextDeny) return;
            nextDeny = Time.unscaledTime + .7f;
            OneShot(RepairSoundSynth.Cue.Deny, .7f, 1f);
        }

        void UpdateStrokeFeedback(bool held, bool pressed)
        {
            float now = Time.unscaledTime, dt = Time.unscaledDeltaTime;
            bool removing = held && LastWipe == WipeResult.Removed;
            if (removing)
            {
                noEffectTime = 0;
                strokeAt = now;
                if (oneShot != null) oneShot.transform.position = lastHitPoint;
                // Thick dirt scrapes, ordinary dirt wipes (the loop), the last dust sweeps away.
                if (now >= nextGrain && lastRemaining > HeavyDirt)
                {
                    OneShot(RepairSoundSynth.Cue.CleanScrape, .5f, UnityEngine.Random.Range(.9f, 1.12f));
                    nextGrain = now + .2f;
                }
                else if (now >= nextGrain && lastRemaining < LightDirt)
                {
                    OneShot(RepairSoundSynth.Cue.CleanDust, .3f, UnityEngine.Random.Range(.95f, 1.15f));
                    nextGrain = now + .32f;
                }
            }
            else if (held)
            {
                noEffectTime += dt;
                if (pressed && AllClean) Deny("已经全部干净了。按 C 退出清洁。");
                else if (LastWipe == WipeResult.Blocked && (pressed || noEffectTime > NoEffectDeny)) Deny("这里不能擦：屏幕、标签和封条要保留。");
                else if (LastWipe == WipeResult.AlreadyClean && noEffectTime > NoEffectDeny) Deny("这里已经干净了，换个地方擦。");
                else if (LastWipe == WipeResult.Miss && pressed) Deny("对准物品外壳再擦。");
            }
            else noEffectTime = 0;

            if (wipeLoop == null) return;
            float target = removing ? (lastRemaining < LightDirt ? .22f : .5f) : 0;
            wipeGain = Mathf.MoveTowards(wipeGain, target, dt * (target > wipeGain ? 7f : 4f));
            wipeLoop.volume = wipeGain;
            // The cloth runs freer as the part comes clean.
            wipeLoop.pitch = Mathf.Lerp(.82f, 1.28f, hitSurface != null ? hitSurface.Progress : Progress);
            if (wipeGain > 0 && !wipeLoop.isPlaying) wipeLoop.Play();
            else if (wipeGain <= 0 && wipeLoop.isPlaying) wipeLoop.Stop();
        }

        void UpdatePresentation(bool held)
        {
            if (ring == null) return;
            float now = Time.unscaledTime, dt = Time.unscaledDeltaTime;
            bool show = Cleaning && !RepairInput.PointerOverUI;
            ring.gameObject.SetActive(show);
            Vector2 local = Vector2.zero;
            if (show)
            {
                Vector2 pointer = RepairInput.PointerPosition;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(uiRect, pointer, null, out local);
                var state = held ? LastWipe : Hover(pointer);
                bool removing = held && state == WipeResult.Removed;
                // Green: wiping works here. Amber: this part must not be wiped. Pale: already clean. Faint: off the item.
                ring.color = state == WipeResult.Removed ? new Color(.62f, .96f, .58f, .95f)
                    : state == WipeResult.Blocked ? new Color(1f, .62f, .3f, .95f)
                    : state == WipeResult.AlreadyClean ? new Color(.85f, .88f, .85f, .6f) : new Color(.8f, .8f, .8f, .22f);
                // The hand's work: the brush trembles and shrinks a touch while it bites.
                var tremble = removing ? new Vector2(Mathf.Sin(now * 71f), Mathf.Cos(now * 59f)) * 1.6f : Vector2.zero;
                ring.rectTransform.anchoredPosition = local + tremble;
                ring.rectTransform.localScale = Vector3.one * (removing ? .9f + .05f * Mathf.Sin(now * 34f) : 1f);
                if (removing && now >= nextSpeck) { nextSpeck = now + .055f; SpawnSpeck(local); }
                if (now >= hintUntil && hint != null)
                {
                    hint.color = new Color(.92f, .94f, .89f, .85f);
                    hint.text = state == WipeResult.Blocked ? "这里不能擦" : state == WipeResult.AlreadyClean ? "这里已经干净" : "";
                }
            }
            else if (now >= hintUntil && hint != null) hint.text = "";

            for (int i = 0; i < specks.Length; i++)
            {
                if (speckLife[i] <= 0) continue;
                speckLife[i] -= dt;
                var rect = specks[i].rectTransform;
                speckVelocity[i] += Vector2.down * (260f * dt);
                rect.anchoredPosition += speckVelocity[i] * dt;
                var c = specks[i].color; c.a = Mathf.Clamp01(speckLife[i] / .3f) * .85f; specks[i].color = c;
                if (speckLife[i] <= 0) specks[i].gameObject.SetActive(false);
            }

            tierFlash = Mathf.MoveTowards(tierFlash, 0, dt * 4f);
            progressFill.fillAmount = Mathf.Clamp01(Progress);
            progressFill.color = Color.Lerp(AllClean ? new Color(.62f, .96f, .58f) : new Color(.86f, .66f, .28f), Color.white, tierFlash * .75f);

            bool showBanner = now < bannerUntil;
            if (banner.gameObject.activeSelf != showBanner) banner.gameObject.SetActive(showBanner);
            if (showBanner)
            {
                float left = bannerUntil - now;
                var c = banner.color; c.a = Mathf.Clamp01(left / .35f); banner.color = c;
            }
            float u = (now - burstAt) / .45f;
            bool showBurst = u >= 0 && u < 1;
            if (burst.gameObject.activeSelf != showBurst) burst.gameObject.SetActive(showBurst);
            if (showBurst)
            {
                burst.rectTransform.anchoredPosition = banner.rectTransform.anchoredPosition;
                burst.rectTransform.localScale = Vector3.one * Mathf.Lerp(.4f, 3.4f, 1 - (1 - u) * (1 - u));
                var c = burst.color; c.a = (1 - u) * .85f; burst.color = c;
            }
        }

        void SpawnSpeck(Vector2 at)
        {
            for (int i = 0; i < specks.Length; i++)
            {
                if (speckLife[i] > 0) continue;
                speckLife[i] = UnityEngine.Random.Range(.22f, .42f);
                speckVelocity[i] = UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(50f, 130f) + Vector2.up * 40f;
                var rect = specks[i].rectTransform;
                rect.anchoredPosition = at + UnityEngine.Random.insideUnitCircle * 14f;
                float size = UnityEngine.Random.Range(2f, 4.5f);
                rect.sizeDelta = new Vector2(size, size);
                specks[i].gameObject.SetActive(true);
                return;
            }
        }

        AudioClip Clip(RepairSoundSynth.Cue cue)
        {
            if (!clips.TryGetValue(cue, out var clip)) clips[cue] = clip = RepairSoundSynth.Create(cue);
            return clip;
        }

        void OneShot(RepairSoundSynth.Cue cue, float volume, float pitch)
        {
            if (oneShot == null) return;
            oneShot.pitch = pitch;
            oneShot.PlayOneShot(Clip(cue), volume);
        }

        // Mostly positional: the source follows the brush, while a share of plain 2D keeps it audible when the room
        // camera's listener is far from the inspection stage.
        void BuildAudio()
        {
            var host = new GameObject("Cleaning Audio");
            host.transform.SetParent(transform, false);
            AudioSource Source()
            {
                var source = host.AddComponent<AudioSource>();
                source.playOnAwake = false; source.spatialBlend = .7f; source.dopplerLevel = 0;
                source.rolloffMode = AudioRolloffMode.Linear; source.minDistance = 1; source.maxDistance = 14;
                return source;
            }
            oneShot = Source();
            wipeLoop = Source();
            wipeLoop.loop = true; wipeLoop.volume = 0; wipeLoop.clip = Clip(RepairSoundSynth.Cue.CleanWipe);
        }

        public bool TryGetCleaningHit(Vector2 screen,out CleanableSurface closest,out RaycastHit hit)
        {
            closest = null; hit = default;
            if (!Cleaning || !Available || !float.IsFinite(screen.x) || !float.IsFinite(screen.y) || IsOverUi(screen)) return false;
            var camera = Clinic.Station.Inspector.ViewCamera;
            if (!camera.pixelRect.Contains(screen)) return false;
            Physics.SyncTransforms();
            Ray ray = camera.ScreenPointToRay(screen);
            float distance = 10;
            foreach (var surface in surfaces)
                if (surface != null && surface.Raycast(ray, distance, out var candidate))
                { distance = candidate.distance; closest = surface; hit = candidate; }
            if (closest == null || !closest.AcceptsHit(hit)) return false;
            foreach (var occluder in occluders)
                if (occluder != null && occluder.Blocks(ray,Mathf.Max(0,distance-.0001f))) return false;
            if (Physics.Raycast(ray, out var blocker, Mathf.Max(0, distance - .002f), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && !blocker.transform.IsChildOf(mounted.transform)) return false;
            return true;
        }

        static bool IsOverUi(Vector2 screen)
        {
            if (EventSystem.current == null) return false;
            var hits = new System.Collections.Generic.List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = screen }, hits);
            return hits.Count > 0;
        }

        void BuildUi()
        {
            ui = new GameObject("Cleaning UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            ui.transform.SetParent(transform, false);
            var canvas = ui.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 60;
            var scale = ui.GetComponent<CanvasScaler>(); scale.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scale.referenceResolution = new Vector2(1280,720); scale.matchWidthOrHeight = .5f;
            var panel = new GameObject("Cleaning controls", typeof(RectTransform), typeof(Image)); panel.transform.SetParent(ui.transform, false);
            var rect = panel.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(18,18); rect.sizeDelta = new Vector2(465,68);
            var background = panel.GetComponent<Image>(); background.color = new Color(.045f,.06f,.065f,.92f); background.raycastTarget = false;
            var button = new GameObject("Cleaning toggle", typeof(RectTransform), typeof(Image), typeof(Button)); button.transform.SetParent(panel.transform,false);
            var buttonRect = button.GetComponent<RectTransform>(); buttonRect.anchorMin = buttonRect.anchorMax = buttonRect.pivot = Vector2.zero;
            buttonRect.anchoredPosition = new Vector2(8,9); buttonRect.sizeDelta = new Vector2(145,50);
            button.GetComponent<Image>().color = new Color(.25f,.32f,.31f);
            toggle = button.GetComponent<Button>(); toggle.onClick.AddListener(() =>
            { SetCleaning(!Cleaning); EventSystem.current?.SetSelectedGameObject(null); });
            var font = UIFontProvider.GetCjkFont(Clinic.Station.View.GetComponentInChildren<Text>(true)?.font);
            buttonText = Label(button.transform,"Button label",font,new Vector2(0,0),new Vector2(145,50));
            buttonText.alignment=TextAnchor.MiddleCenter;
            text = Label(panel.transform,"Progress",font,new Vector2(163,9),new Vector2(298,50));
            uiRect = (RectTransform)ui.transform;
            var track = new GameObject("Progress track", typeof(RectTransform), typeof(Image)); track.transform.SetParent(panel.transform,false);
            var trackRect = track.GetComponent<RectTransform>(); trackRect.anchorMin = trackRect.anchorMax = trackRect.pivot = Vector2.zero;
            trackRect.anchoredPosition = new Vector2(163,4); trackRect.sizeDelta = new Vector2(294,5);
            var trackImage = track.GetComponent<Image>(); trackImage.color = new Color(1,1,1,.12f); trackImage.raycastTarget = false;
            var fillTexture = new Texture2D(1,1,TextureFormat.RGBA32,false) { name = "CleaningFill_Runtime", hideFlags = HideFlags.DontSave };
            fillTexture.SetPixel(0,0,Color.white); fillTexture.Apply();
            ownedTextures.Add(fillTexture);
            progressFill = Overlay(track.transform,"Progress fill",null,new Vector2(294,5));
            progressFill.sprite = Sprite.Create(fillTexture,new Rect(0,0,1,1),new Vector2(.5f,.5f));
            progressFill.type = Image.Type.Filled; progressFill.fillMethod = Image.FillMethod.Horizontal;
            var ringSprite = RingSprite();
            ring = Overlay(ui.transform,"Brush ring",ringSprite,new Vector2(46,46));
            burst = Overlay(ui.transform,"Finish burst",ringSprite,new Vector2(90,90));
            burst.color = new Color(.62f,.96f,.58f,0); burst.gameObject.SetActive(false);
            ring.gameObject.SetActive(false);
            for (int i = 0; i < specks.Length; i++)
            {
                specks[i] = Overlay(ui.transform,"Dust speck",progressFill.sprite,new Vector2(3,3));
                specks[i].color = new Color(.62f,.48f,.3f,.85f);
                specks[i].gameObject.SetActive(false);
            }
            hint = CenterLabel("Cleaning hint",font,18,new Vector2(0,-230),new Vector2(640,30));
            banner = CenterLabel("Cleaning done",font,30,new Vector2(0,-170),new Vector2(420,48));
            banner.text = "清洁完成"; banner.color = new Color(.62f,.96f,.58f); banner.gameObject.SetActive(false);
            ui.SetActive(false);
        }

        Image Overlay(Transform parent,string name,Sprite sprite,Vector2 size)
        {
            var obj = new GameObject(name,typeof(RectTransform),typeof(Image)); obj.transform.SetParent(parent,false);
            var image = obj.GetComponent<Image>(); image.sprite = sprite; image.raycastTarget = false;
            var r = image.rectTransform; r.sizeDelta = size;
            if (parent == ui.transform) r.anchorMin = r.anchorMax = r.pivot = new Vector2(.5f,.5f);
            else { r.anchorMin = r.anchorMax = r.pivot = Vector2.zero; r.anchoredPosition = Vector2.zero; }
            return image;
        }

        Text CenterLabel(string name,Font font,int size,Vector2 position,Vector2 box)
        {
            var label = Label(ui.transform,name,font,position,box);
            var r = label.rectTransform; r.anchorMin = r.anchorMax = r.pivot = new Vector2(.5f,.5f); r.anchoredPosition = position;
            label.alignment = TextAnchor.MiddleCenter; label.fontSize = size;
            label.gameObject.AddComponent<Shadow>().effectColor = new Color(0,0,0,.85f);
            return label;
        }

        Sprite RingSprite()
        {
            const int size = 64;
            var texture = new Texture2D(size,size,TextureFormat.RGBA32,false) { name = "CleaningRing_Runtime", hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
            float centre = (size - 1) * .5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x,y),new Vector2(centre,centre));
                    texture.SetPixel(x,y,new Color(1,1,1,Mathf.Clamp01(31f - d + .5f) * Mathf.Clamp01(d - 27.5f + .5f)));
                }
            texture.Apply();
            ownedTextures.Add(texture);
            return Sprite.Create(texture,new Rect(0,0,size,size),new Vector2(.5f,.5f));
        }

        static Text Label(Transform parent,string name,Font font,Vector2 position,Vector2 size)
        {
            var obj=new GameObject(name,typeof(RectTransform),typeof(Text)); obj.transform.SetParent(parent,false);
            var r=obj.GetComponent<RectTransform>(); r.anchorMin=r.anchorMax=r.pivot=Vector2.zero; r.anchoredPosition=position; r.sizeDelta=size;
            var label=obj.GetComponent<Text>(); label.font=font; label.fontSize=16; label.color=new Color(.92f,.94f,.89f);
            label.raycastTarget=false; label.alignment=TextAnchor.MiddleLeft; return label;
        }

        void OnDisable()
        {
            CleanableSurface.SetOverlayHost(false);
            SetCleaning(false);
            wipeGain = 0;
            if (wipeLoop != null) { wipeLoop.volume = 0; wipeLoop.Stop(); }
        }

        void OnDestroy()
        {
            foreach (var surface in surfaces) Unsubscribe(surface);
            if (ui != null) Destroy(ui);
            foreach (var clip in clips.Values) if (clip != null) Destroy(clip);
            foreach (var texture in ownedTextures) if (texture != null) Destroy(texture);
        }
    }
}
