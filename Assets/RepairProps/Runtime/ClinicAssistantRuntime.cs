using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BorderRepair.Controls;
using BorderRepair.Dock;
using BorderRepair.TwoNight;
using BorderRepair.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BorderRepair.RepairProps
{
    // UNIT 07 off the dock: while the counter is open on night 1, the port's leased robot is the repairer's assistant.
    // - It hauls purchased parts from the north end of the counter's pass-through to the bench on its parts tray (the
    //   carry pose and tray the night-1 tray incident uses), flying the north lane between the surgery ring and the bench.
    //   It never enters the customer strip west of the counter (x 1.22..1.56).
    // - Its screen shows its mood (faces drawn here, not the RobotV4 textures) and it talks in a small window: after the
    //   night's calls and deliveries, and when the player clicks it.
    // Runtime only. The robot's pose, arm overlay, tray and screen are put back exactly the moment the counter closes
    // (before the incident takes the robot over) and when the scene unloads; RobotV4 assets are not touched.
    [DefaultExecutionOrder(1000)]   // after the Animator and TrayCarryOverlay have written the bones this frame
    public sealed class ClinicAssistantRuntime : MonoBehaviour
    {
        public const string ScenePath = RepairBenchV2Runtime.ScenePath;
        public enum Face { Smile, Blink, Talk, Happy, Worry, Sleep, Signal, Carry }

        const string RobotName = "UNIT07_RobotV4_DockReady";
        const float Cruise = 1.15f, Speed = .9f, SleepAfter = 50f;
        // Route (root positions, metres; checked against the runtime floor plan, shot 39-assistant-plan). The north lane
        // (z 1.35..1.45) runs between the surgery ring (z <= .9) and the bench clutter (z >= 1.90) with the robot's
        // 1.02 m span sideways; at the counter it hovers north of the pass-through, facing south, clear of customers
        // standing at the pads (z <= .85) and over the counter top (.95).
        static readonly Vector3 DockOut = new Vector3(-2.10f, .95f, 0f), WestLane = new Vector3(-1.60f, Cruise, 1.35f),
            EastLane = new Vector3(1.20f, Cruise, 1.35f), CounterLane = new Vector3(1.85f, Cruise, 1.45f),
            CounterHover = new Vector3(1.85f, 1.00f, 1.35f), BenchLane = new Vector3(.55f, Cruise, 1.40f),
            BenchHover = new Vector3(.55f, 1.05f, 1.85f);
        static readonly Vector3 CounterSpot = new Vector3(1.85f, .95f, .92f), BenchSpot = new Vector3(.55f, .92f, 2.50f),
            CrateSize = new Vector3(.22f, .12f, .16f);

        public static ClinicAssistantRuntime Current { get; private set; }

        UnifiedClinicDirector clinic;
        Transform robot, holdAnchor;
        Renderer screen;
        TrayCarryOverlay overlay;
        DockPickable tray;
        Renderer trayRenderer;
        Material crateMaterial, stripeMaterial;
        BoxCollider hitbox;
        Vector3 homePosition; Quaternion homeRotation;
        float[] savedOverlay;
        MaterialPropertyBlock block, savedBlock;
        bool engaged, trayTaken, hadBlock;
        GameObject crate, benchCrate;
        bool crateOnTray;
        float engagedAt, lastActivity, blinkAt, benchCrateAt;
        int seenReceipts = -1, pendingHauls, retestsSeen;
        RepairPropFlow watchedFlow;
        string lastCallId;
        readonly HashSet<string> said = new HashSet<string>();
        readonly Dictionary<(Face, int), Texture2D> faces = new Dictionary<(Face, int), Texture2D>();

        // Speech
        readonly Queue<(string text, Face face)> lines = new Queue<(string, Face)>();
        (string text, Face face) line;
        bool speaking;
        float lineAt, lineHold, quietAt;
        Canvas canvas;
        RectTransform card;
        RawImage portrait;
        Text speech, footer;
        Font font, mono;
        int talkCount;

        public bool Engaged => engaged;
        public bool Busy { get; private set; }
        public Face CurrentFace { get; private set; }
        public Transform Robot => robot;
        public Vector3 HomePosition => homePosition;
        public RectTransform Card => card;
        public string Line => speaking ? line.text : "";
        public GameObject BenchCrate => benchCrate;
        public int Hauls { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetHooks() { SceneManager.sceneLoaded -= OnSceneLoaded; Current = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install() => SceneManager.sceneLoaded += OnSceneLoaded;

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!string.Equals(scene.path, ScenePath, System.StringComparison.OrdinalIgnoreCase)) return;
            if (UnityEngine.XR.XRSettings.isDeviceActive) return;
            var host = new GameObject("ClinicAssistant UNIT07 (runtime only)");
            SceneManager.MoveGameObjectToScene(host, scene);
            host.AddComponent<ClinicAssistantRuntime>();
        }

        void Awake()
        {
            Current = this;
            font = RepairPropCyberStyle.UiFontOr(UIFontProvider.GetCjkFont(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")));
            mono = RepairPropCyberStyle.Mono != null ? RepairPropCyberStyle.Mono : font;
            BuildUi();
        }

        void Start()
        {
            clinic = FindObjectsByType<UnifiedClinicDirector>(FindObjectsSortMode.None).FirstOrDefault(c => c.gameObject.scene == gameObject.scene);
            robot = gameObject.scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == RobotName);
            if (robot == null) { enabled = false; return; }
            screen = robot.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.name == "ScreenContent_Animated");
            overlay = robot.GetComponent<TrayCarryOverlay>();
            var incident = gameObject.scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<TrayIncident>(true)).FirstOrDefault();
            if (incident != null)
            {
                tray = incident.Tray;
                holdAnchor = typeof(TrayIncident).GetField("holdAnchor", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(incident) as Transform;
            }
            trayRenderer = tray != null ? tray.GetComponentInChildren<Renderer>() : null;
            crateMaterial = trayRenderer != null ? trayRenderer.sharedMaterial : null;
            stripeMaterial = robot.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials)
                .FirstOrDefault(m => m != null && m.name.Contains("WarningYellow"));
        }

        bool ShouldEngage => clinic != null && clinic.Initialized && clinic.State != null && clinic.State.phase == TwoNightPhase.Night1Counter;

        void Update()
        {
            if (robot == null) return;
            if (ShouldEngage != engaged) { if (engaged) Release(); else Engage(); }
            if (!engaged) return;
            float now = Time.unscaledTime;
            if ((Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame) || RepairInput.LeftPressed) lastActivity = now;
            Watch(now);
            Click();
            Speak(now);
            if (!Busy && pendingHauls > 0) { pendingHauls--; StartCoroutine(Haul()); }
            if (benchCrate != null && !Busy && now - benchCrateAt > 60f) Destroy(benchCrate);
        }

        void LateUpdate()
        {
            if (!engaged) return;
            if (crate != null && crateOnTray && trayRenderer != null)
            {
                var b = trayRenderer.bounds;
                var forward = Vector3.ProjectOnPlane(robot.forward, Vector3.up);
                crate.transform.SetPositionAndRotation(new Vector3(b.center.x, b.max.y + CrateSize.y * .5f, b.center.z),
                    Quaternion.LookRotation(forward.sqrMagnitude > 1e-4f ? forward : Vector3.forward));
            }
            if (hitbox != null) hitbox.transform.SetPositionAndRotation(robot.position, robot.rotation);
            PaintFace(Time.unscaledTime);
        }

        // ---- engage / release ---------------------------------------------------------------------------------

        void Engage()
        {
            engaged = true;
            engagedAt = lastActivity = Time.unscaledTime;
            homePosition = robot.position; homeRotation = robot.rotation;
            if (overlay != null) savedOverlay = new[] { overlay.RightWeight, overlay.LeftWeight, overlay.JawOpen, overlay.UpperJawExtra, overlay.SteadyWeight };
            if (screen != null)
            {
                hadBlock = screen.HasPropertyBlock();
                savedBlock = new MaterialPropertyBlock();
                if (hadBlock) screen.GetPropertyBlock(savedBlock);
            }
            seenReceipts = Receipts();
            // A solid box the size of the robot: the room's click raycast stops on it instead of reaching the counter behind.
            var bounds = robot.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled).Select(r => r.bounds)
                .Aggregate((a, b) => { a.Encapsulate(b); return a; });
            hitbox = new GameObject("UNIT07_AssistantHitbox").AddComponent<BoxCollider>();
            SceneManager.MoveGameObjectToScene(hitbox.gameObject, gameObject.scene);
            var local = new Bounds(robot.InverseTransformPoint(bounds.center), Vector3.zero);
            for (int i = 0; i < 8; i++)
                local.Encapsulate(robot.InverseTransformPoint(bounds.center + Vector3.Scale(bounds.extents, new Vector3(i & 1, (i >> 1) & 1, (i >> 2) & 1) * 2 - Vector3.one)));
            hitbox.center = local.center; hitbox.size = local.size;
            hitbox.transform.SetPositionAndRotation(robot.position, robot.rotation);
        }

        void Release()
        {
            if (!engaged) return;
            engaged = false;
            StopAllCoroutines();
            Busy = false; pendingHauls = 0; crateOnTray = false;
            robot.SetPositionAndRotation(homePosition, homeRotation);
            if (trayTaken && tray != null) tray.PutBack();
            trayTaken = false;
            if (overlay != null && savedOverlay != null)
            {
                overlay.RightWeight = savedOverlay[0]; overlay.LeftWeight = savedOverlay[1]; overlay.JawOpen = savedOverlay[2];
                overlay.UpperJawExtra = savedOverlay[3]; overlay.SteadyWeight = savedOverlay[4];
            }
            if (screen != null) screen.SetPropertyBlock(hadBlock ? savedBlock : null);
            if (crate != null) Destroy(crate);
            if (benchCrate != null) Destroy(benchCrate);
            if (hitbox != null) Destroy(hitbox.gameObject);
            lines.Clear(); speaking = false;
            if (card != null) card.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (robot != null) Release();
            foreach (var texture in faces.Values) if (texture != null) Destroy(texture);
            if (Current == this) Current = null;
        }

        // ---- what happens in the clinic -----------------------------------------------------------------------

        static int Receipts() => TwoNightRun.Current == null ? 0
            : TwoNightRun.Current.transactions.Count(t => ClinicSupplies.IsReceipt(t.id) && t.amount < 0);

        void Watch(float now)
        {
            var state = clinic.State;
            // Purchases: one haul per purchase order (all of an order's lines are posted in the same frame).
            int receipts = Receipts();
            if (receipts > seenReceipts)
            {
                var orders = state.transactions.Where(t => ClinicSupplies.IsReceipt(t.id) && t.amount < 0)
                    .Skip(seenReceipts).Select(t => t.id.Substring(0, Mathf.Min(t.id.Length, ClinicSupplies.ReceiptPrefix.Length + 32))).Distinct().Count();   // prefix + order guid
                pendingHauls += Mathf.Max(1, orders);
                seenReceipts = receipts;
                Say("采购单收到。货到了柜台交付口，我去搬。", Face.Happy);
            }

            var calls = ClinicCallRuntime.Current;
            bool onCall = calls != null && calls.Active;
            if (onCall) { lastCallId = calls.CallId; return; }
            if (lastCallId != null) { AfterCall(lastCallId); lastCallId = null; }

            var trades = state.customerTrades ?? new List<ClinicCustomerTrade>();
            int delivered = trades.Count(t => t.state == ClinicTradeState.Delivered);
            if (delivered == 0 && now - engagedAt > 6f)
                Once("greet", ("七号上线。港务租赁单元 NSP-07，今晚归这间铺子调度。", Face.Happy),
                    ("零件我来搬，修理你来。有事就看我一眼——左键点我。", Face.Smile));
            if (clinic.TradeState == ClinicTradeState.InRepair)
                Once("first-item", ("第一件上台了。我在这边待命。拆下来的螺丝别乱放。", Face.Smile));

            var bridge = clinic.Station != null && clinic.Station.Inspector != null && clinic.Station.Inspector.CurrentItem != null
                ? clinic.Station.Inspector.CurrentItem.GetComponent<RepairPropStationBridge>() : null;
            var flow = bridge != null ? bridge.Flow : null;
            if (flow != watchedFlow) { watchedFlow = flow; retestsSeen = flow != null ? flow.FailedRetests : 0; }
            else if (flow != null && flow.FailedRetests > retestsSeen)
            {
                retestsSeen = flow.FailedRetests;
                Say("复测没过。别急，看看是不是哪里没接好，或者螺丝少了一颗。", Face.Worry);
            }

            if (state.customerQueueCount > 0 && delivered >= state.customerQueueCount && !Busy && now - quietAt > 4f)
                Once("all-done", ("今晚的单子都清了。", Face.Happy),
                    ("……左侧引擎有点热，比平时高 3°C。还在容差内。", Face.Worry),
                    ("别看我。去记账吧。", Face.Smile));
        }

        // The robot's own thread: it is one of the port's leased units, and the night's stories brush against it.
        void AfterCall(string id)
        {
            switch (id)
            {
                case "rent-n1":
                    Once(id, ("霞说的封条，我身上一共七张。一张都没破。我数过。", Face.Smile)); break;
                case "dealer-intro":
                    Once(id, ("源的货……我扫过序列号。有一半登记在港务的报废清单上。", Face.Worry), ("“不问来路”，是吧。", Face.Smile)); break;
                case "patrol-receiver":
                    Once(id, ("……02:10 那个自动应答，我在调度频段上也收到了。", Face.Signal),
                        ("巡检 07-B 和我是同一批出厂的。编号挨着。", Face.Signal),
                        ("我不知道它记不记得他。我只知道，它每晚还在走那一圈。", Face.Smile)); break;
                case "dealer-cartridge":
                    Once(id, ("港务也在换自动吊臂。", Face.Worry),
                        ("……要是哪天我的租约到期，会不会也有人把我的校准拆下来卖？", Face.Worry),
                        ("开玩笑的。先把今晚的单子做完。", Face.Smile)); break;
                case "care-omamori":
                    Once(id, ("小绿的型号比我还早两代。厂家关了服务器，它还亮着。", Face.Smile),
                        ("鳶的“晚安”……如果哪天我也只能替别人说话，我希望能说得像她那样。", Face.Happy)); break;
            }
        }

        // Clicking the robot (aimed crosshair while walking, the pointer otherwise).
        void Click()
        {
            if (!RepairInput.LeftPressed || clinic.ConsoleOpen) return;
            var rig = clinic.Robot != null ? clinic.Robot.Flow.Rig : null;
            var cam = rig != null && rig.Cam != null ? rig.Cam : Camera.main;
            if (cam == null) return;
            bool walking = clinic.RoomWalking;
            if (walking && (rig.Walker == null || !rig.Walker.Aiming)) return;
            if (!walking && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            Vector2 pointer = walking ? new Vector2(Screen.width * .5f, Screen.height * .5f) : RepairInput.PointerPosition;
            foreach (var hit in Physics.RaycastAll(cam.ScreenPointToRay(pointer), 8f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
            {
                if (hit.collider is CharacterController) continue;
                if (hit.collider == hitbox || hit.transform.IsChildOf(robot)) Talk();
                break;
            }
        }

        /// <summary>What UNIT 07 says when the player turns to it: the next thing to do, then some small talk.</summary>
        public void Talk()
        {
            if (!engaged) return;
            lastActivity = Time.unscaledTime;
            if (speaking) { lineHold = 0; return; }   // a click skips the current line
            var state = clinic.State;
            string hint = Busy ? "我在搬货，马上回来。" :
                clinic.Carrying ? "放到维修台上就行。我帮你看着路。" :
                clinic.HasPendingCase ? "柜台接收区有新单子。" :
                clinic.TradeState == ClinicTradeState.InRepair ? "台上那件还没修完。缺配件就开采购单，我去搬。" :
                clinic.TradeState == ClinicTradeState.ReadyForDelivery ? "修好的东西该交给客人了。去柜台交付区。" : null;
            var banter = new List<string>
            {
                "我的托盘承重四公斤。今晚最重的是那台电源。",
                "转子转速正常。你的手很稳。",
                "屏幕上的笑脸是出厂设置。不过我挺喜欢的。",
                "港务的调度频段今晚很安静。安静得有点不对。",
            };
            if (said.Contains("patrol-receiver")) banter.Add("02:10 快到了吗？……我只是在看时间。");
            if (said.Contains("care-omamori")) banter.Add("小绿刚才在照护站的频段上发了一个心跳包。它还好。");
            if (hint != null) Say(hint, Busy ? Face.Carry : Face.Smile);
            Say(banter[talkCount++ % banter.Count], Face.Happy);
        }

        // ---- hauling ------------------------------------------------------------------------------------------

        /// <summary>Queues a parts run (also used by tests and the capture tour).</summary>
        public void RequestHaul() { if (engaged) pendingHauls++; }

        IEnumerator Haul()
        {
            Busy = true;
            lastActivity = Time.unscaledTime;
            crate = MakeCrate("UNIT07_PartsCrate");
            crate.transform.position = new Vector3(CounterSpot.x, CounterSpot.y + CrateSize.y * .5f, CounterSpot.z);
            // Take the tray off the dock shelf into the carry pose (as the incident does), stow the left arm.
            if (tray != null && holdAnchor != null && !tray.Taken)
            {
                tray.HoldPoint = holdAnchor; tray.Take();
                tray.transform.localPosition = Vector3.zero; tray.transform.localRotation = Quaternion.identity;
                trayTaken = true;
            }
            yield return Blend(v => { if (overlay != null) { overlay.RightWeight = v; overlay.LeftWeight = v; } }, 0, 1, .9f);
            var southRotation = homeRotation * Quaternion.Euler(0, 90, 0);    // face the counter's pass-through (-Z)
            var benchRotation = homeRotation * Quaternion.Euler(0, -90, 0);   // face the bench (+Z)
            yield return Fly(homeRotation, homePosition, DockOut, WestLane, EastLane, CounterLane);
            yield return Turn(homeRotation, southRotation, CounterLane);
            yield return Fly(southRotation, CounterLane, CounterHover);
            yield return Jaw(1);
            // Lift the crate off the counter onto the tray.
            for (float t = 0; t < .4f && trayRenderer != null; t += Time.deltaTime)
            {
                var b = trayRenderer.bounds;
                crate.transform.position = Vector3.Lerp(crate.transform.position, new Vector3(b.center.x, b.max.y + CrateSize.y * .5f, b.center.z), t / .4f);
                yield return null;
            }
            crateOnTray = true;
            yield return Jaw(0);
            yield return Fly(southRotation, CounterHover, CounterLane);
            yield return Turn(southRotation, homeRotation, CounterLane);
            yield return Turn(homeRotation, benchRotation, CounterLane);
            yield return Fly(benchRotation, CounterLane, BenchLane, BenchHover);
            yield return Jaw(1);
            crateOnTray = false;
            float top = clinic.BenchTopWorldY > .5f ? clinic.BenchTopWorldY : BenchSpot.y;
            var rest = new Vector3(BenchSpot.x, top + CrateSize.y * .5f, BenchSpot.z);
            var from = crate.transform.position; var fromRotation = crate.transform.rotation;
            for (float t = 0; t < .35f; t += Time.deltaTime)
            {
                crate.transform.SetPositionAndRotation(Vector3.Lerp(from, rest, Mathf.SmoothStep(0, 1, t / .35f)),
                    Quaternion.Slerp(fromRotation, Quaternion.identity, t / .35f));
                yield return null;
            }
            crate.transform.SetPositionAndRotation(rest, Quaternion.identity);
            if (benchCrate != null) Destroy(benchCrate);   // the newest delivery replaces the last one on the bench
            benchCrate = crate; crate = null; benchCrateAt = Time.unscaledTime;
            Hauls++;
            Say("配件放到维修台右手边了。签收：七号。", Face.Happy);
            yield return Jaw(0);
            yield return Fly(benchRotation, BenchHover, BenchLane);
            yield return Turn(benchRotation, homeRotation, BenchLane);
            yield return Fly(homeRotation, BenchLane, WestLane, DockOut, homePosition);   // BenchLane -> WestLane at z 1.40..1.35
            if (trayTaken && tray != null) { tray.PutBack(); trayTaken = false; }
            yield return Blend(v => { if (overlay != null) { overlay.RightWeight = v; overlay.LeftWeight = v; } }, 1, 0, .9f);
            robot.SetPositionAndRotation(homePosition, homeRotation);
            Busy = false;
            lastActivity = Time.unscaledTime;
        }

        // Along the polyline at a steady speed, easing in and out over the whole run; the body banks a little into the motion.
        IEnumerator Fly(Quaternion facing, params Vector3[] points)
        {
            float length = 0;
            for (int i = 1; i < points.Length; i++) length += Vector3.Distance(points[i - 1], points[i]);
            if (length < 1e-3f) yield break;
            float duration = length / Speed + .6f;
            Vector3 last = points[0];
            for (float t = 0; t < duration; t += Time.deltaTime)
            {
                var at = Along(points, length * Mathf.SmoothStep(0, 1, t / duration));
                var velocity = (at - last) / Mathf.Max(Time.deltaTime, 1e-4f);
                last = at;
                var flat = Vector3.ProjectOnPlane(velocity, Vector3.up);
                var bank = flat.sqrMagnitude > 1e-4f ? Quaternion.AngleAxis(Mathf.Min(flat.magnitude * 7f, 7f), Vector3.Cross(Vector3.up, flat.normalized)) : Quaternion.identity;
                robot.SetPositionAndRotation(at, bank * facing);
                yield return null;
            }
            robot.SetPositionAndRotation(points[points.Length - 1], facing);
        }

        static Vector3 Along(Vector3[] points, float distance)
        {
            for (int i = 1; i < points.Length; i++)
            {
                float segment = Vector3.Distance(points[i - 1], points[i]);
                if (distance <= segment) return Vector3.Lerp(points[i - 1], points[i], segment > 0 ? distance / segment : 1);
                distance -= segment;
            }
            return points[points.Length - 1];
        }

        IEnumerator Turn(Quaternion from, Quaternion to, Vector3 at)
        {
            for (float t = 0; t < .7f; t += Time.deltaTime) { robot.SetPositionAndRotation(at, Quaternion.Slerp(from, to, Mathf.SmoothStep(0, 1, t / .7f))); yield return null; }
            robot.SetPositionAndRotation(at, to);
        }

        IEnumerator Jaw(float to) => Blend(v => { if (overlay != null) overlay.JawOpen = v; }, overlay != null ? overlay.JawOpen : 0, to, .35f);

        static IEnumerator Blend(System.Action<float> set, float from, float to, float seconds)
        {
            for (float t = 0; t < seconds; t += Time.deltaTime) { set(Mathf.Lerp(from, to, Mathf.SmoothStep(0, 1, t / seconds))); yield return null; }
            set(to);
        }

        GameObject MakeCrate(string name)
        {
            var root = new GameObject(name);
            SceneManager.MoveGameObjectToScene(root, gameObject.scene);
            void Part(Vector3 size, Vector3 offset, Material material)
            {
                var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(part.GetComponent<Collider>());
                part.transform.SetParent(root.transform, false);
                part.transform.localScale = size; part.transform.localPosition = offset;
                if (material != null) part.GetComponent<Renderer>().sharedMaterial = material;
            }
            Part(CrateSize, Vector3.zero, crateMaterial);
            Part(new Vector3(CrateSize.x + .004f, .022f, CrateSize.z + .004f), new Vector3(0, .02f, 0), stripeMaterial);   // hazard band
            return root;
        }

        // ---- speech -------------------------------------------------------------------------------------------

        void Say(string text, Face face) { lines.Enqueue((text, face)); lastActivity = Time.unscaledTime; }

        void Once(string key, params (string text, Face face)[] said_)
        {
            if (!said.Add(key)) return;
            foreach (var entry in said_) Say(entry.text, entry.face);
        }

        void Speak(float now)
        {
            bool onCall = ClinicCallRuntime.Current != null && ClinicCallRuntime.Current.Active;
            if (speaking && now - lineAt > lineHold) { speaking = false; quietAt = now; }
            if (!speaking && lines.Count > 0 && !onCall)
            {
                line = lines.Dequeue();
                speaking = true; lineAt = now; lineHold = 1.8f + line.text.Length * .08f;
                card.gameObject.SetActive(true);
                speech.text = line.text;
                footer.text = "NSP-07  //  " + (Busy ? "CARRY" : "STANDBY") + "  //  [左键] 下一句";
            }
            if (!speaking && lines.Count == 0 && card.gameObject.activeSelf && now - quietAt > 2.5f) card.gameObject.SetActive(false);
        }

        void BuildUi()
        {
            var host = new GameObject("AssistantCanvas", typeof(RectTransform));
            host.transform.SetParent(transform, false);
            canvas = host.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 79;
            var scaler = host.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = .5f;
            host.AddComponent<GraphicRaycaster>();
            card = (RectTransform)new GameObject("AssistantCard", typeof(RectTransform)).transform;
            card.SetParent(host.transform, false);
            card.anchorMin = card.anchorMax = card.pivot = Vector2.zero; card.anchoredPosition = new Vector2(16, 96); card.sizeDelta = new Vector2(380, 92);
            var bg = card.gameObject.AddComponent<Image>(); bg.raycastTarget = false;
            RepairPropCyberStyle.Framed(bg, RepairPropCyberStyle.Field, RepairPropCyberStyle.AmberDim);
            RepairPropCyberStyle.Texture(card, RepairPropCyberStyle.Dots, new Color(1f, .62f, .16f, .06f), "DotGrid");
            HudMotion.Boot(card.gameObject);
            HudWindowDrag.Attach(RepairPropCyberStyle.TitleBar(card, "UNIT 07 // 助手", font), card, "assistant");
            var feed = Child("Face", card, 8, -8, 100, 76);
            var feedBg = feed.gameObject.AddComponent<Image>(); feedBg.color = new Color(.02f, .06f, .035f, 1); feedBg.raycastTarget = false;
            portrait = Child("Screen", feed, 2, -2, 96, 72).gameObject.AddComponent<RawImage>(); portrait.raycastTarget = false;
            var feedFrame = Child("Frame", feed, 0, 0, 100, 76).gameObject.AddComponent<Image>();
            feedFrame.sprite = RepairPropCyberStyle.Bracket; feedFrame.type = Image.Type.Sliced; feedFrame.color = RepairPropCyberStyle.Amber; feedFrame.raycastTarget = false;
            speech = Child("Speech", card, 118, -8, 254, 62).gameObject.AddComponent<Text>();
            speech.font = font; speech.fontSize = 14; speech.color = RepairPropCyberStyle.Ink; speech.raycastTarget = false; speech.supportRichText = false;
            speech.alignment = TextAnchor.UpperLeft; speech.horizontalOverflow = HorizontalWrapMode.Wrap; speech.verticalOverflow = VerticalWrapMode.Overflow; speech.lineSpacing = 1.08f;
            HudMotion.TypeOn(speech, 34);
            footer = Child("Footer", card, 118, -72, 254, 14).gameObject.AddComponent<Text>();
            footer.font = mono; footer.fontSize = 10; footer.color = RepairPropCyberStyle.AmberDim; footer.raycastTarget = false;
            footer.alignment = TextAnchor.MiddleLeft; footer.horizontalOverflow = HorizontalWrapMode.Overflow;
            card.gameObject.SetActive(false);
        }

        static RectTransform Child(string name, Transform parent, float x, float y, float w, float h)
        {
            var r = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1); r.anchoredPosition = new Vector2(x, y); r.sizeDelta = new Vector2(w, h);
            return r;
        }

        // ---- the screen ---------------------------------------------------------------------------------------

        void PaintFace(float now)
        {
            Face face; int frame = 0;
            bool typing = speaking && now - lineAt < line.text.Length / 34f + .2f;
            if (speaking && typing && line.face != Face.Signal) { face = Face.Talk; frame = (int)(now / .11f) % 2; }
            else if (speaking) face = line.face;
            else if (Busy) face = Face.Carry;
            else if (now - lastActivity > SleepAfter) face = Face.Sleep;
            else face = Face.Smile;
            if (face == Face.Signal) frame = (int)(now / .09f) % 4;
            if (face == Face.Smile || face == Face.Carry)
            {
                if (now > blinkAt + .13f && now - blinkAt > 3.2f + Mathf.PerlinNoise(blinkAt, 0) * 2.5f) blinkAt = now;
                if (now - blinkAt < .13f) face = Face.Blink;
            }
            CurrentFace = face;
            var texture = FaceTexture(face, frame);
            if (portrait != null) portrait.texture = texture;
            if (screen == null) return;
            block ??= new MaterialPropertyBlock();
            screen.GetPropertyBlock(block);
            block.SetTexture("_BaseMap", texture); block.SetTexture("_MainTex", texture); block.SetTexture("_EmissionMap", texture);
            block.SetVector("_BaseMap_ST", new Vector4(1, 1, 0, 0));
            screen.SetPropertyBlock(block);
        }

        Texture2D FaceTexture(Face face, int frame)
        {
            if (faces.TryGetValue((face, frame), out var texture)) return texture;
            texture = FaceArt.Draw(face, frame);
            faces[(face, frame)] = texture;
            return texture;
        }

        // Pixel faces on the robot's 4:3 screen, phosphor green like its factory smile.
        static class FaceArt
        {
            const int W = 64, H = 48;
            static readonly Color32 Back = new Color32(8, 30, 17, 255), Ink = new Color32(118, 255, 150, 255), Soft = new Color32(52, 150, 84, 255);

            public static Texture2D Draw(Face face, int frame)
            {
                var px = new Color32[W * H];
                for (int i = 0; i < px.Length; i++) px[i] = Back;
                void Dot(int x, int y, Color32 c) { if (x >= 0 && x < W && y >= 0 && y < H) px[y * W + x] = c; }
                void Box(int x, int y, int w, int h, Color32 c) { for (int i = 0; i < w; i++) for (int j = 0; j < h; j++) Dot(x + i, y + j, c); }
                void Line(int x0, int y0, int x1, int y1, Color32 c)
                {
                    int dx = Mathf.Abs(x1 - x0), dy = -Mathf.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
                    while (true)
                    {
                        Box(x0, y0, 2, 2, c);
                        if (x0 == x1 && y0 == y1) break;
                        int e2 = 2 * err;
                        if (e2 >= dy) { err += dy; x0 += sx; }
                        if (e2 <= dx) { err += dx; y0 += sy; }
                    }
                }
                void Arc(int from, int to, float baseY, float k, Color32 c)
                {
                    int prevY = Mathf.RoundToInt(baseY + k * (from - 32) * (from - 32));
                    for (int x = from + 1; x <= to; x++) { int y = Mathf.RoundToInt(baseY + k * (x - 32) * (x - 32)); Line(x - 1, prevY, x, y, c); prevY = y; }
                }
                void Eyes(int y, int w, int h) { Box(20 - w / 2, y, w, h, Ink); Box(44 - w / 2, y, w, h, Ink); }

                switch (face)
                {
                    case Face.Smile: Eyes(25, 4, 8); Arc(22, 42, 13, .022f, Ink); break;
                    case Face.Blink: Box(17, 28, 7, 2, Ink); Box(41, 28, 7, 2, Ink); Arc(22, 42, 13, .022f, Ink); break;
                    case Face.Talk:
                        Eyes(25, 4, 8);
                        if (frame == 0) { Box(27, 10, 10, 2, Ink); Box(27, 17, 10, 2, Ink); Box(26, 12, 2, 5, Ink); Box(36, 12, 2, 5, Ink); Box(28, 12, 8, 5, Soft); }
                        else Box(28, 13, 8, 2, Ink);
                        break;
                    case Face.Happy: Line(16, 26, 20, 31, Ink); Line(20, 31, 24, 26, Ink); Line(40, 26, 44, 31, Ink); Line(44, 31, 48, 26, Ink); Arc(19, 45, 11, .03f, Ink); break;
                    case Face.Worry:
                        Eyes(23, 4, 6); Line(15, 33, 24, 36, Ink); Line(40, 36, 49, 33, Ink);
                        for (int x = 24; x <= 40; x += 2) Box(x, 13 + ((x / 2) % 2), 2, 2, Ink);
                        break;
                    case Face.Sleep:
                        Box(16, 26, 8, 2, Ink); Box(40, 26, 8, 2, Ink); Box(30, 13, 4, 2, Soft);
                        Line(48, 38, 52, 38, Soft); Line(52, 38, 48, 34, Soft); Line(48, 34, 52, 34, Soft);
                        Line(54, 44, 57, 44, Soft); Line(57, 44, 54, 41, Soft); Line(54, 41, 57, 41, Soft);
                        break;
                    case Face.Signal:
                        for (int x = 4; x < 60; x++)
                        {
                            float envelope = Mathf.Sin(Mathf.PI * (x - 4) / 56f);
                            int y = Mathf.RoundToInt(23 + 11 * envelope * Mathf.Sin(x * .42f + frame * 1.5f) * (.6f + .4f * Mathf.Sin(x * .11f)));
                            Box(x, y, 1, 2, Ink);
                        }
                        Box(4, 23, 56, 1, Soft);
                        break;
                    case Face.Carry: Eyes(22, 4, 5); Box(27, 13, 10, 2, Ink); break;
                }
                // CRT: darker alternate rows and a dim border, like the factory faces.
                for (int y = 0; y < H; y += 2) for (int x = 0; x < W; x++) { var c = px[y * W + x]; px[y * W + x] = new Color32((byte)(c.r * .78f), (byte)(c.g * .78f), (byte)(c.b * .78f), 255); }
                var texture = new Texture2D(W, H, TextureFormat.RGBA32, false) { name = "UNIT07_Face_" + face + frame, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                texture.SetPixels32(px);
                texture.Apply(false, true);
                return texture;
            }
        }
    }
}
