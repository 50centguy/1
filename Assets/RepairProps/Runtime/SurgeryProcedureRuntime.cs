using System;
using System.Collections.Generic;
using System.Linq;
using BorderRepair.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BorderRepair.RepairProps
{
    // The operation on the strapped-in surgery patient (罗亚): his leased right forearm was remotely locked when the
    // contract ran out, and it is replaced part by part in the close-up surgery view (SurgeryCloseupRuntime), the way
    // the bench props are taken apart and put back:
    //   scan (Scanner) -> two service cover screws (Drill) -> the cover (Gripper) -> the battery service plug (Probe: the
    //   arm goes dead) -> the neural plug (Probe) -> cut his tape off the third clamp bolt (Cutter) -> four clamp bolts
    //   (Drill) -> old forearm onto the instrument tray (Gripper) -> clean the socket contacts (Suction) -> replacement
    //   from the tray onto the socket (Gripper) -> four bolts back (Drill) -> neural plug in (Probe) -> link test, a fist
    //   each time, three passes (Probe) -> confirm.
    // Each command lowers its gantry tool arm with a work beam onto its part and moves the part. Tool commands need the
    // patient secured and strapped, the lamp on and the arms deployed; taking the forearm off and fitting the new one
    // also need the tray out (those table switches are commands too). From the first command until the confirmation the
    // patient stays on the table even with the straps open. Visual and state only; nothing is saved.
    [AddComponentMenu("")]
    public sealed class SurgeryProcedureRuntime : MonoBehaviour
    {
        public sealed class Command
        {
            public string id, label, target, tool, done;
            public float seconds;
            /// <summary>Needs the instrument tray swung out.</summary>
            public bool tray;
            /// <summary>A table switch (lamp, arms, tray, straps): instant, no gantry tool, allowed at any time.</summary>
            public bool table;
            public readonly List<(string key, int value)> requires = new List<(string, int)>();
            public readonly List<(string key, int value)> effects = new List<(string, int)>();
        }

        public enum CommandState { Available, Blocked, Done }

        public const string Whole = SurgeryCyberArm.Whole, TableTarget = "Table";
        public const int LinkPasses = 3;
        public static readonly string[] PhaseLabels = { "诊断", "断电", "拆卸", "更换", "复装", "联调", "完成" };
        /// <summary>The gantry tools in toolbar order.</summary>
        public static readonly string[] ToolOrder = { "Scanner", "Probe", "Drill", "Gripper", "Suction", "Cutter" };
        static readonly string[] LinkResultText =
        {
            "联调 1/3：拇指和食指跟上了，无名指慢半拍（延迟 140 毫秒）。再校一次。",
            "联调 2/3：五指都跟上了，握力 62%。",
            "联调 3/3：绿灯常亮。罗亚慢慢攥紧拳头，又松开。“……这只手是我自己的了。”",
        };

        public static SurgeryProcedureRuntime Current { get; private set; }

        readonly List<Command> commands = new List<Command>();
        readonly Dictionary<string, int> state = new Dictionary<string, int>();
        readonly Queue<string> queue = new Queue<string>();
        readonly List<string> inspected = new List<string>();
        readonly List<string> links = new List<string>();
        Command running;
        float runTime;
        bool started;
        Transform oldArm, newArm;
        Vector3 carryFrom;
        Quaternion carryFromRot;
        LineRenderer beam;
        Material beamMaterial;

        public IReadOnlyList<Command> Commands => commands;
        public Command Find(string id) => commands.Find(c => c.id == id);
        public bool Running => running != null;
        public Command RunningCommand => running;
        public float Progress => running != null ? Mathf.Clamp01(runTime / Mathf.Max(.01f, running.seconds)) : 0;
        public bool Complete => Get("done") == 1;
        /// <summary>The operation has begun and is not confirmed: the patient may not leave.</summary>
        public bool Holding => started && !Complete || Running;
        public string LastResult { get; private set; }
        public string LastMessage { get; private set; }
        public FeedbackKind LastKind { get; private set; }
        public IReadOnlyList<string> Inspected => inspected;
        public IReadOnlyList<string> LinkResults => links;
        public Transform OldArm => oldArm;
        public Transform NewArm => newArm;
        public bool Queued => queue.Count > 0;
        public event Action Changed;
        public event Action<Command> StepDone;
        public event Action<string, FeedbackKind> Feedback;

        static SurgeryPatientRuntime Patient => SurgeryPatientRuntime.Current;
        static SurgeryTableV2Runtime Table => SurgeryTableV2Runtime.Current;
        static SurgeryGantryV2Runtime Gantry => SurgeryGantryV2Runtime.Current;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetHooks() { SceneManager.sceneLoaded -= OnSceneLoaded; Current = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install() => SceneManager.sceneLoaded += OnSceneLoaded;

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!string.Equals(scene.path, SurgeryTableV2Runtime.ScenePath, StringComparison.OrdinalIgnoreCase)) return;
            if (UnityEngine.XR.XRSettings.isDeviceActive) return;
            var host = new GameObject("SurgeryProcedure (runtime only)");
            SceneManager.MoveGameObjectToScene(host, scene);
            host.AddComponent<SurgeryProcedureRuntime>();
        }

        void Awake()
        {
            Current = this;
            SurgeryPatientRuntime.HoldOnTable = () => Current != null && Current.Holding;
            Author();
            state["arm"] = 1;
        }

        void Start()
        {
            if (Patient != null) Patient.PhaseChanged += OnPatientPhase;
            beam = new GameObject("SurgeryBeam").AddComponent<LineRenderer>();
            beam.transform.SetParent(transform, false);
            beamMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "SurgeryBeam" };
            beam.sharedMaterial = beamMaterial;
            beam.positionCount = 2; beam.widthMultiplier = .0035f; beam.useWorldSpace = true;
            beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            beam.enabled = false;
        }

        // ------------------------------------------------------------------------------------------------ authoring
        void Author()
        {
            Command Add(string id, string label, string target, string tool, float seconds, string done = null)
            {
                var c = new Command { id = id, label = label, target = target, tool = tool, seconds = seconds, done = done };
                commands.Add(c);
                return c;
            }
            var bolts = SurgeryCyberArm.Bolts;

            var scan = Add("scan", "扫描右臂义体", Whole, "Scanner", 2.2f,
                "扫描：前臂的租赁锁模块被远程锁死（“雇主已释放”）。神经接口完好。锁模块带熔断封签，强拆会烧掉整只前臂的驱动——只能整段更换。替换前臂已从库存取出，放在器械托盘上。");
            scan.requires.Add(("scanned", 0)); scan.effects.Add(("scanned", 1));

            foreach (var screw in SurgeryCyberArm.CoverScrews)
            {
                var c = Add("remove." + screw, "卸下" + SurgeryCyberArm.Label(screw), screw, "Drill", .9f);
                c.requires.AddRange(new[] { ("scanned", 1), ("arm", 1), ("cover", 0), ("out." + screw, 0) });
                c.effects.Add(("out." + screw, 1));
            }
            var cover = Add("cover.remove", "取下检修盖板", "ServiceCover", "Gripper", 1.3f,
                "盖板取下：红色熔断封签完好。锁模块旁边就是电池服务插头。");
            cover.requires.AddRange(new[] { ("arm", 1), ("cover", 0) });
            foreach (var screw in SurgeryCyberArm.CoverScrews) cover.requires.Add(("out." + screw, 1));
            cover.effects.Add(("cover", 1));

            var isolate = Add("battery.isolate", "拔下电池服务插头", "BatteryPlug", "Probe", 1.2f,
                "前臂断电，锁死的红灯灭了。罗亚：“没事，早就没感觉了。”");
            isolate.requires.AddRange(new[] { ("arm", 1), ("cover", 1), ("battery", 0) });
            isolate.effects.Add(("battery", 1));

            var release = Add("neural.release", "拔下神经接头", "NeuralPlug", "Probe", 1.2f, "神经接头拔下。罗亚的肩膀抽了一下：“……麻。”");
            release.requires.AddRange(new[] { ("arm", 1), ("battery", 1), ("neural", 0) });
            release.effects.Add(("neural", 1));

            var cut = Add("tape.cut", "切开螺栓3上的胶布", "ClampTape", "Cutter", 1.2f, "胶布切开。里层是他自己用圆珠笔写的字：吊机 3 组 · 罗。");
            cut.requires.AddRange(new[] { ("scanned", 1), ("tape", 0) });
            cut.effects.Add(("tape", 1));

            for (int i = 0; i < bolts.Length; i++)
            {
                var c = Add("remove." + bolts[i], "卸下" + SurgeryCyberArm.Label(bolts[i]), bolts[i], "Drill", 1.0f);
                c.requires.AddRange(new[] { ("arm", 1), ("battery", 1), ("neural", 1), ("out." + bolts[i], 0) });
                if (i + 1 == SurgeryCyberArm.TapedBolt) c.requires.Add(("tape", 1));
                c.effects.Add(("out." + bolts[i], 1));
            }
            var off = Add("forearm.remove", "取下旧前臂，放上托盘", SurgeryCyberArm.Old, "Gripper", 2.6f, "旧前臂放上托盘。锁模块上还贴着租赁合同的资产编号。");
            off.tray = true;
            off.requires.AddRange(new[] { ("arm", 1), ("neural", 1) });
            foreach (var b in bolts) off.requires.Add(("out." + b, 1));
            off.effects.Add(("arm", 0));

            var clean = Add("socket.clean", "清理接口触点", "Socket", "Suction", 1.8f, "接口触点清理干净，发亮。");
            clean.requires.AddRange(new[] { ("arm", 0), ("clean", 0) });
            clean.effects.Add(("clean", 1));

            var fit = Add("forearm.fit", "装上替换前臂", SurgeryCyberArm.New, "Gripper", 2.6f, "替换前臂插进接口环。");
            fit.tray = true;
            fit.requires.AddRange(new[] { ("scanned", 1), ("arm", 0), ("clean", 1) });
            fit.effects.Add(("arm", 2));

            foreach (var b in bolts)
            {
                var c = Add("install." + b, "拧紧" + SurgeryCyberArm.Label(b), b, "Drill", 1.0f);
                c.requires.AddRange(new[] { ("arm", 2), ("out." + b, 1) });
                c.effects.Add(("out." + b, 0));
            }
            var connect = Add("neural.connect", "插上神经接头", "NeuralPlug", "Probe", 1.2f, "神经接头插进新前臂。");
            connect.requires.AddRange(new[] { ("arm", 2), ("neural", 1) });
            foreach (var b in bolts) connect.requires.Add(("out." + b, 0));
            connect.effects.Add(("neural", 0));

            var test = Add("test.link", "神经链路联调（握拳）", Whole, "Probe", 2.8f);
            test.requires.AddRange(new[] { ("arm", 2), ("neural", 0), ("linked", 0) });
            foreach (var b in bolts) test.requires.Add(("out." + b, 0));

            var confirm = Add("complete", "确认手术完成", Whole, null, 0, "手术完成。解开三条束带，罗亚就可以起身了。");
            confirm.requires.AddRange(new[] { ("linked", 1), ("done", 0) });
            confirm.effects.Add(("done", 1));

            // Table switches (live keys: lamp, arms, tray, straps).
            void Switch(string id, string label, string key, int to)
            {
                var c = Add(id, label, TableTarget, null, 0);
                c.table = true; c.requires.Add((key, 1 - to)); c.effects.Add((key, to));
            }
            Switch("lamp.on", "打开手术灯", "lamp", 1); Switch("lamp.off", "关闭手术灯", "lamp", 0);
            Switch("arms.deploy", "展开机械臂", "arms", 1); Switch("arms.park", "收回机械臂", "arms", 0);
            Switch("tray.out", "摆出器械托盘", "tray", 1); Switch("tray.in", "收回器械托盘", "tray", 0);
            Switch("straps.close", "扣上全部束带", "straps", 1);
        }

        // ------------------------------------------------------------------------------------------------ state
        public int Get(string key)
        {
            switch (key)
            {
                case "lamp": return Gantry != null && Gantry.LampOn ? 1 : 0;
                case "arms": return Gantry != null && Gantry.ArmsDeployed ? 1 : 0;
                case "tray": return Table != null && Table.TrayOut ? 1 : 0;
                case "straps": return Table != null && Table.StrapCount > 0 && Table.StrapsClosed == Table.StrapCount ? 1 : 0;
                case "link": return links.Count;
            }
            return state.TryGetValue(key, out var v) ? v : 0;
        }

        /// <summary>0 diagnose, 1 isolate, 2 take apart, 3 replace, 4 reassemble, 5 link test, 6 done.</summary>
        public int Phase
        {
            get
            {
                if (Get("linked") == 1) return 6;
                if (Get("scanned") == 0) return 0;
                int arm = Get("arm");
                if (arm == 1) return Get("battery") == 1 && Get("neural") == 1 ? 2 : 1;
                if (arm == 0) return 3;
                return SurgeryCyberArm.Bolts.All(b => Get("out." + b) == 0) && Get("neural") == 0 ? 5 : 4;
            }
        }

        public bool IsDone(Command c) =>
            c.id == "test.link" ? Get("linked") == 1 : c.effects.Count > 0 && c.effects.All(e => Get(e.key) == e.value);

        public CommandState StateOf(Command c, out string reason)
        {
            reason = Why(c);
            if (reason == null) return CommandState.Available;
            return IsDone(c) ? CommandState.Done : CommandState.Blocked;
        }

        /// <summary>Why a command cannot run now (null when it can).</summary>
        public string Why(Command c)
        {
            if (c == null) return "没有这项操作";
            if (Running) return "机械臂正在工作：" + running.label;
            if (c.table)
            {
                if (Table == null || Gantry == null) return "手术台不可用";
                foreach (var r in c.requires) if (Get(r.key) != r.value) return Describe(r, c);
                return null;
            }
            if (Complete) return "手术已完成";
            if (Patient == null || Patient.PhaseNow != SurgeryPatientRuntime.Phase.Secured) return "先让病人躺好并扣上束带";
            if (IsDone(c)) return "无需操作：已经" + c.label;
            if (Table == null || Get("straps") == 0) return "先扣上全部束带";
            if (Gantry == null || !Gantry.LampOn) return "先打开手术灯";
            if (!Gantry.ArmsDeployed) return "先展开机械臂";
            if (Gantry.DeployAmount < .98f) return "机械臂正在展开";
            foreach (var r in c.requires) if (Get(r.key) != r.value) return Describe(r, c);
            if (c.tray && !Table.TrayOut) return "先摆出器械托盘";
            return null;
        }

        string Describe((string key, int value) r, Command c)
        {
            bool on = r.value == 1;
            if (r.key.StartsWith("out.", StringComparison.Ordinal))
            {
                bool bolt = r.key.Contains("ClampBolt");
                string family = bolt ? "ClampBolt" : "CoverScrew";
                int remaining = c.requires.Count(q => q.key.StartsWith("out.", StringComparison.Ordinal) && q.key.Contains(family) && Get(q.key) != q.value);
                string what = bolt ? "卡箍螺栓" : "检修盖螺钉";
                return remaining > 1 ? "还有 " + remaining + " 颗" + what + (on ? "没卸下" : "没拧紧")
                    : (on ? "先卸下" : "先拧紧") + SurgeryCyberArm.Label(r.key.Substring(4));
            }
            switch (r.key)
            {
                case "scanned": return on ? "先扫描右臂义体，确认故障" : "已经扫描过";
                case "arm":
                    if (r.value == 1) return "旧前臂已经取下";
                    if (r.value == 2) return "先装上替换前臂";
                    return Get("arm") == 1 ? "先取下旧前臂" : "替换前臂已经装上";
                case "cover": return on ? "先取下检修盖板" : "检修盖板已经取下";
                case "battery": return on ? "先拔下电池服务插头：带电拔神经接头会烧触点" : "电池服务插头已经拔下";
                case "neural": return on ? "先拔下神经接头" : "先插上神经接头";
                case "tape": return on ? "螺栓3缠着胶布：先用切割臂切开" : "胶布已经切开";
                case "clean": return on ? "先清理接口触点" : "接口已经清理过";
                case "linked": return on ? "先完成神经链路联调（握拳 " + LinkPasses + " 次）" : "联调已经完成";
                case "done": return "手术已完成";
                case "lamp": return on ? "手术灯已经关了" : "手术灯已经开着";
                case "arms": return on ? "机械臂已经收起" : "机械臂已经展开";
                case "tray": return on ? "器械托盘已经收回" : "器械托盘已经摆出";
                case "straps": return "束带已经全部扣上";
                default: return "前置步骤未完成";
            }
        }

        /// <summary>The suggested next command: the first authored step not yet done whose own requirements are met, or
        /// the table switch that stands in its way (straps, lamp, arms, tray).</summary>
        public Command Next()
        {
            if (Complete || Patient == null || Patient.PhaseNow != SurgeryPatientRuntime.Phase.Secured) return null;
            foreach (var c in commands)
            {
                if (c.table || IsDone(c) || !c.requires.All(r => Get(r.key) == r.value)) continue;
                if (Get("straps") == 0) return Find("straps.close");
                if (Gantry != null && !Gantry.LampOn) return Find("lamp.on");
                if (Gantry != null && !Gantry.ArmsDeployed) return Find("arms.deploy");
                if (c.tray && Table != null && !Table.TrayOut) return Find("tray.out");
                return c;
            }
            return null;
        }

        /// <summary>One sentence for the guide: what to do next.</summary>
        public string NextStepText
        {
            get
            {
                if (Complete) return "手术完成。离开手术台，解开三条束带，罗亚就能起身。";
                if (Running) return "进行中：" + running.label + "（" + ToolName(running.tool) + "）";
                if (Patient == null || Patient.PhaseNow != SurgeryPatientRuntime.Phase.Secured) return "先让病人躺好并扣上束带。";
                var next = Next();
                if (next == null) return "没有可执行的步骤。";
                return next.table ? "先" + next.label + "。" : "下一步：" + next.label + (next.tool != null ? "（" + ToolName(next.tool) + "）" : "") + "。";
            }
        }

        /// <summary>Under the room crosshair, on the patient's arm.</summary>
        public string ClickHint => Complete ? "左键：近景查看（手术已完成）" : started ? "左键：回到近景手术" : "左键：近景手术";

        /// <summary>Top-centre line in the room while the patient is secured.</summary>
        public string Hint => Complete ? "手术完成：解开三条束带，让罗亚起身"
            : started ? "手术进行中：瞄准罗亚的右臂，左键回到近景手术" : "罗亚已固定：瞄准他的右臂，左键开始近景手术";

        public static string ToolName(string tool)
        {
            switch (tool)
            {
                case "Scanner": return "扫描臂";
                case "Probe": return "探针臂";
                case "Drill": return "钻头臂";
                case "Gripper": return "夹爪臂";
                case "Suction": return "吸引臂";
                case "Cutter": return "切割臂";
                case null: return "检查";
                default: return tool;
            }
        }

        /// <summary>What close inspection shows of a part, in its current state.</summary>
        public string Finding(string id)
        {
            switch (id)
            {
                case Whole: return Get("scanned") == 0 ? "罗亚的右前臂：码头公司租给吊机工的标准作业臂。手指僵在半握的位置，状态灯红色常亮。"
                    : "租赁锁远程锁死（“雇主已释放”）。神经接口完好，可以整段更换前臂。";
                case SurgeryCyberArm.Old: return "旧前臂：浅灰外壳，护板是租赁公司的黄色。手腕上有一圈黄色租赁标识带。";
                case SurgeryCyberArm.New: return "替换前臂：二手的石墨色作业臂，没有租赁锁。关节还没和他的神经校准。";
                case "SocketRing": return "接口环（身体侧，保留）：长在他上臂末端，只清理、不更换。";
                case "Socket": return Get("clean") == 1 ? "接口触点：清理过，发亮。" : "接口触点：有油泥，还有一圈铁锈色的汗渍。";
                case "ClampTape": return "胶布缠了好几层。最里层是圆珠笔写的字：吊机 3 组 · 罗。";
                case "NeuralPlug": return "神经接头（身体侧）：连着他自己的神经。拔插前先给前臂断电。";
                case "ServiceCover": return "检修盖板：租赁公司的黄色涂装，角上贴着资产编号。两颗螺钉固定。";
                case "LeaseLock": return "租赁锁模块：远程锁死。红色熔断封签——强拆会烧掉整只前臂的驱动。";
                case "BatteryPlug": return Get("battery") == 1 ? "电池服务插头：已拔下，前臂断电。" : "电池服务插头：拔下就能给整只前臂断电。";
                case "BatteryCell": return "前臂电池：电量还剩 71%，没坏。";
                case "StatusLamp": return Get("battery") == 1 ? "状态灯：灭了（断电）。" : "状态灯：红色常亮 = 锁死。";
                case "StatusLamp_New": return Get("linked") == 1 ? "状态灯：绿色 = 已和他的神经联机。" : Get("link") > 0 ? "状态灯：琥珀色，联调中。" : "状态灯：灭（尚未联机）。";
                case "Hand": return "旧前臂的手：手指僵在半握的位置，锁死那天夜里就这样了。";
                case "Hand_New": return Get("linked") == 1 ? "替换件的手：五指都跟得上。" : "替换件的手：还没校准，五指松着。";
            }
            if (id != null && id.StartsWith("ClampBolt_", StringComparison.Ordinal)) return SurgeryCyberArm.Label(id) + "：把前臂卡在接口环上。"
                + (id == SurgeryCyberArm.Bolts[SurgeryCyberArm.TapedBolt - 1] && Get("tape") == 0 ? "螺栓头缠着胶布。" : "");
            if (id != null && id.StartsWith("CoverScrew_", StringComparison.Ordinal)) return SurgeryCyberArm.Label(id) + "：普通十字槽螺钉。";
            return SurgeryCyberArm.Label(id);
        }

        public void Inspect(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (!inspected.Contains(id)) inspected.Add(id);
            Say(SurgeryCyberArm.Label(id) + "：" + Finding(id), FeedbackKind.Info);
            Changed?.Invoke();
        }

        public void Say(string text, FeedbackKind kind)
        {
            LastMessage = text; LastKind = kind;
            Feedback?.Invoke(text, kind);
        }

        // ------------------------------------------------------------------------------------------------ running
        /// <summary>Runs a command if nothing blocks it (otherwise says why).</summary>
        public bool Run(string id)
        {
            var c = Find(id);
            string why = Why(c);
            if (why != null) { Say(why, FeedbackKind.Warning); return false; }
            if (c.table) { Switch(c); Say(c.label, FeedbackKind.Info); Changed?.Invoke(); return true; }
            Begin(c);
            return true;
        }

        /// <summary>Runs commands one after another (a set of bolts), each when its turn comes and it can run.</summary>
        public void RunAll(IEnumerable<string> ids)
        {
            foreach (var id in ids) queue.Enqueue(id);
            Pump();
        }

        public void CancelQueue() => queue.Clear();

        void Pump()
        {
            while (queue.Count > 0 && running == null)
            {
                var c = Find(queue.Dequeue());
                if (c != null && Why(c) == null) Begin(c);
            }
        }

        void Switch(Command c)
        {
            switch (c.id)
            {
                case "lamp.on": case "lamp.off": Gantry.Activate(SurgeryControlKind.Lamp); break;
                case "arms.deploy": case "arms.park": Gantry.Activate(SurgeryControlKind.Arms); break;
                case "tray.out": case "tray.in": Table.Activate(Table.Controls.First(k => k.Kind == SurgeryControlKind.Tray)); break;
                case "straps.close":
                    foreach (var strap in Table.Controls.Where(k => k.Kind == SurgeryControlKind.Strap).ToList())
                        if (!Table.StrapClosed(strap)) Table.Activate(strap);
                    break;
            }
        }

        void Begin(Command c)
        {
            running = c; runTime = 0; started = true;
            if (c.tool != null && Gantry != null) Gantry.Work(c.tool, true);
            switch (c.id)
            {
                case "forearm.remove":
                    oldArm = Patient.DetachCyberArm();
                    carryFrom = oldArm.position; carryFromRot = oldArm.rotation;
                    Show("Socket", true);
                    break;
                case "forearm.fit":
                    if (newArm == null) newArm = SpareOnTray();
                    newArm.SetParent(null, true);
                    carryFrom = newArm.position; carryFromRot = newArm.rotation;
                    break;
            }
            Changed?.Invoke();
            if (c.seconds <= 0) Finish();
        }

        void Update()
        {
            if (running == null) { if (beam != null && beam.enabled) beam.enabled = false; Pump(); return; }
            runTime += Time.deltaTime;
            float t = Progress;
            Animate(running, t);
            Beam(running, t);
            if (t >= 1) Finish();
        }

        void Finish()
        {
            var c = running;
            running = null;
            if (c.tool != null && Gantry != null) Gantry.Work(c.tool, false);
            foreach (var e in c.effects) state[e.key] = e.value;
            string result = c.done;
            switch (c.id)
            {
                case "scan": if (newArm == null) newArm = SpareOnTray(); break;
                case "forearm.remove": Place(oldArm, TraySlot(-1), TrayRotation()); break;
                case "forearm.fit": Patient.FitCyberArm(newArm); break;
                case "test.link":
                    result = LinkResultText[Mathf.Min(links.Count, LinkResultText.Length - 1)];
                    links.Add(result);
                    if (links.Count >= LinkPasses) state["linked"] = 1;
                    break;
            }
            Apply();
            LastResult = result ?? c.label;
            Say(LastResult, c.id == "test.link" && Get("linked") == 0 ? FeedbackKind.Info : FeedbackKind.Success);
            StepDone?.Invoke(c);
            Changed?.Invoke();
            Pump();
        }

        // ------------------------------------------------------------------------------------------------ parts
        /// <summary>A part of his arm by id: on him (body side or the fitted forearm), or on a forearm on the tray.</summary>
        public SurgeryArmPart Part(string id)
        {
            var bone = Patient != null ? Patient.BoneTransform(SurgeryPatientRuntime.Bone.RightLowerArm) : null;
            return SurgeryCyberArm.FindPart(bone, id) ?? SurgeryCyberArm.FindPart(oldArm, id) ?? SurgeryCyberArm.FindPart(newArm, id);
        }

        /// <summary>The transform whose renderers show a target (the whole arm: everything on the forearm bone).</summary>
        public Transform TargetTransform(string id)
        {
            if (id == Whole) return Patient != null ? Patient.BoneTransform(SurgeryPatientRuntime.Bone.RightLowerArm) : null;
            var part = Part(id);
            return part != null ? part.transform : null;
        }

        void Show(string id, bool on)
        {
            var part = Part(id);
            if (part != null && part.gameObject.activeSelf != on) part.gameObject.SetActive(on);
        }

        // Puts every part where the state says it is (after each command, and when an operation starts over).
        void Apply()
        {
            if (Patient == null) return;
            foreach (var b in SurgeryCyberArm.Bolts)
            {
                var bolt = Part(b);
                if (bolt == null) continue;
                bool fitted = Get("out." + b) == 0;
                bolt.gameObject.SetActive(fitted);
                if (fitted) bolt.Restore();
            }
            var tape = Part("ClampTape");
            if (tape != null) { tape.gameObject.SetActive(Get("tape") == 0); tape.Restore(); }
            var plug = Part("NeuralPlug");
            if (plug != null)
            {
                bool pulled = Get("neural") == 1;
                plug.transform.localPosition = pulled ? SurgeryCyberArm.PlugParked : SurgeryCyberArm.PlugSeated;
                plug.transform.localRotation = pulled ? SurgeryCyberArm.PlugParkedRotation : Quaternion.identity;
            }
            var face = Part("Socket");
            if (face != null)
            {
                face.gameObject.SetActive(Get("arm") == 0);
                face.Exposed = Get("arm") == 0;
                SurgeryCyberArm.SetSocketClean(face, Get("clean"));
            }
            if (oldArm != null)
            {
                foreach (var s in SurgeryCyberArm.CoverScrews)
                {
                    var screw = SurgeryCyberArm.FindPart(oldArm, s);
                    if (screw == null) continue;
                    screw.gameObject.SetActive(Get("out." + s) == 0);
                    if (screw.gameObject.activeSelf) screw.Restore();
                }
                var cover = SurgeryCyberArm.FindPart(oldArm, "ServiceCover");
                if (cover != null) { cover.gameObject.SetActive(Get("cover") == 0); cover.Restore(); }
                foreach (var id in SurgeryCyberArm.Internals)
                {
                    var inside = SurgeryCyberArm.FindPart(oldArm, id);
                    if (inside != null) inside.Exposed = Get("cover") == 1;
                }
                var battery = SurgeryCyberArm.FindPart(oldArm, "BatteryPlug");
                if (battery != null)
                {
                    battery.Restore();
                    if (Get("battery") == 1) battery.transform.localPosition += SurgeryCyberArm.BatteryPlugPulled;
                }
                bool powered = Get("battery") == 0;
                SurgeryPatientRuntime.SetArmLamp(oldArm, powered ? SurgeryCyberArm.LampRed : SurgeryCyberArm.LampDark);
                SurgeryCyberArm.SetLockLed(oldArm, powered);
                SurgeryCyberArm.SetCurl(oldArm, SurgeryCyberArm.LockedCurl);
            }
            if (newArm != null)
            {
                SurgeryPatientRuntime.SetArmLamp(newArm, Get("linked") == 1 ? SurgeryCyberArm.LampGreen : links.Count > 0 ? SurgeryCyberArm.LampAmber : SurgeryCyberArm.LampDark);
                SurgeryCyberArm.SetCurl(newArm, 0);
            }
        }

        // ------------------------------------------------------------------------------------------------ motion
        static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3 - 2 * t); }

        void Animate(Command c, float t)
        {
            float e = Smooth(t);
            bool removing = c.id.StartsWith("remove.", StringComparison.Ordinal);
            if (removing || c.id.StartsWith("install.", StringComparison.Ordinal))
            {
                // Screwed out along its axis (two turns), then into the parts bin; or back in from there.
                var part = Part(c.target);
                if (part == null) return;
                if (!part.gameObject.activeSelf) part.gameObject.SetActive(true);
                float travel = c.target.StartsWith("ClampBolt", StringComparison.Ordinal) ? .022f : .014f, k = removing ? e : 1 - e;
                part.transform.localPosition = part.RestPosition + part.Travel * (travel * k);
                part.transform.localRotation = part.RestRotation * Quaternion.AngleAxis(-720f * k, Vector3.up);
                part.transform.localScale = Vector3.one * (removing ? 1 - Smooth((t - .8f) / .2f) : Smooth(t / .2f));
                return;
            }
            switch (c.id)
            {
                case "cover.remove":
                {
                    var cover = Part("ServiceCover");
                    if (cover == null) return;
                    float lift = HudMotion.EaseOut(t / .45f), slide = Smooth((t - .4f) / .6f);
                    cover.transform.localPosition = cover.RestPosition + Vector3.forward * (.028f * lift + .02f * slide) + Vector3.right * (.07f * slide);
                    cover.transform.localRotation = cover.RestRotation * Quaternion.Euler(0, 0, 25f * slide);
                    cover.transform.localScale = Vector3.one * (1 - Smooth((t - .75f) / .25f));
                    break;
                }
                case "battery.isolate":
                {
                    var plug = Part("BatteryPlug");
                    if (plug == null) return;
                    var pulled = SurgeryCyberArm.BatteryPlugPulled;
                    plug.transform.localPosition = plug.RestPosition + Vector3.forward * (pulled.z * Smooth(t / .5f)) + Vector3.right * (pulled.x * Smooth((t - .5f) / .5f));
                    break;
                }
                case "neural.release":
                case "neural.connect":
                {
                    var plug = Part("NeuralPlug");
                    if (plug == null) return;
                    float k = c.id == "neural.release" ? e : 1 - e;
                    plug.transform.localPosition = Vector3.Lerp(SurgeryCyberArm.PlugSeated, SurgeryCyberArm.PlugParked, k) + Vector3.forward * (.012f * Mathf.Sin(k * Mathf.PI));
                    plug.transform.localRotation = Quaternion.Slerp(Quaternion.identity, SurgeryCyberArm.PlugParkedRotation, k);
                    break;
                }
                case "tape.cut":
                {
                    var tape = Part("ClampTape");
                    if (tape == null) return;
                    float fall = Smooth((t - .55f) / .45f);
                    tape.transform.localPosition = tape.RestPosition + tape.Travel * (.012f * fall);
                    tape.transform.localScale = Vector3.one * (1 - fall);
                    break;
                }
                case "forearm.remove": Carry(oldArm, Seat(TraySlot(-1), TrayRotation()), TrayRotation(), t); break;
                case "forearm.fit":
                {
                    var bone = Patient.BoneTransform(SurgeryPatientRuntime.Bone.RightLowerArm);
                    Carry(newArm, bone.position, bone.rotation, t);
                    if (t > .9f) Show("Socket", false);
                    break;
                }
                case "socket.clean": SurgeryCyberArm.SetSocketClean(Part("Socket"), Smooth((t - .2f) / .7f)); break;
                case "test.link":
                {
                    // A fist and open again, stronger each pass; the lamp blinks amber while it calibrates.
                    float grip = links.Count == 0 ? .55f : links.Count == 1 ? .85f : 1f;
                    Patient.RightHandCurl = grip * Mathf.Sin(Mathf.Clamp01((t - .3f) / .7f) * Mathf.PI);
                    SurgeryPatientRuntime.SetArmLamp(Patient.CyberArm, Mathf.Repeat(t * 6f, 1f) < .5f ? SurgeryCyberArm.LampAmber : SurgeryCyberArm.LampDark);
                    break;
                }
            }
        }

        void Beam(Command c, float t)
        {
            bool on = c.tool != null && Gantry != null && t > .12f && t < .92f;
            beam.enabled = on;
            if (!on) return;
            Color color = c.tool == "Scanner" ? new Color(.3f, 1f, .9f) : c.tool == "Probe" ? new Color(.45f, .65f, 1f)
                : c.tool == "Drill" ? new Color(1f, .62f, .2f) : c.tool == "Suction" ? new Color(.7f, .9f, .7f)
                : c.tool == "Cutter" ? new Color(1f, .35f, .25f) : new Color(1f, .9f, .6f);
            beamMaterial.color = color * (.75f + .25f * Mathf.PerlinNoise(Time.time * 18f, 0));
            var target = TargetPoint(c.target);
            if (c.tool == "Scanner" && Table != null) target += Table.HeadDir * (.11f * Mathf.Sin(t * Mathf.PI * 3));   // sweeps along the arm
            beam.SetPosition(0, Gantry.TipPoint(c.tool));
            beam.SetPosition(1, target);
        }

        /// <summary>World centre of what a target shows (the forearm's middle for the whole arm).</summary>
        public Vector3 TargetPoint(string id)
        {
            var bone = Patient != null ? Patient.BoneTransform(SurgeryPatientRuntime.Bone.RightLowerArm) : null;
            if (id == Whole) return bone != null ? bone.TransformPoint(new Vector3(0, -.14f, .03f)) : Vector3.zero;
            var part = Part(id);
            if (part == null) return bone != null ? bone.position : Vector3.zero;
            var renderers = part.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return part.transform.position;
            var b = renderers[0].bounds;
            foreach (var r in renderers.Skip(1)) b.Encapsulate(r.bounds);
            return b.center;
        }

        // Two slots side by side across the tray top: the old forearm on one, the replacement on the other.
        Vector3 TraySlot(int side)
        {
            var b = Table.Tray.GetComponent<Renderer>().bounds;
            var along = TrayLong();
            return new Vector3(b.center.x, b.max.y + .052f, b.center.z) + Vector3.Cross(Vector3.up, along) * (.065f * side);
        }

        Vector3 TrayLong()
        {
            var b = Table.Tray.GetComponent<Renderer>().bounds;
            return b.size.x >= b.size.z ? Vector3.right : Vector3.forward;
        }

        // Laid along the tray, its top (the service bay) up.
        Quaternion TrayRotation() => Quaternion.LookRotation(Vector3.up, -TrayLong());

        // The assembly's origin is its elbow end; the slot is where its middle rests.
        static Vector3 Seat(Vector3 slot, Quaternion rotation) => slot - rotation * new Vector3(0, -.2f, 0);

        Transform SpareOnTray()
        {
            var arm = Patient.BuildCyberArm(true);
            Place(arm, TraySlot(1), TrayRotation());
            return arm;
        }

        void Place(Transform arm, Vector3 slot, Quaternion rotation)
        {
            arm.SetPositionAndRotation(Seat(slot, rotation), rotation);
            arm.SetParent(Table.Tray, true);
        }

        // Lifted along an arc from where it was to its destination (origin to origin), turning on the way.
        void Carry(Transform arm, Vector3 to, Quaternion toRotation, float t)
        {
            if (arm == null) return;
            float e = Smooth(t);
            arm.position = Vector3.Lerp(carryFrom, to, e) + Vector3.up * (.18f * Mathf.Sin(e * Mathf.PI));
            arm.rotation = Quaternion.Slerp(carryFromRot, toRotation, e);
        }

        // ------------------------------------------------------------------------------------------------ lifecycle
        // A new patient walking in starts a new operation; the last one's parts are cleared off the tray.
        void OnPatientPhase(SurgeryPatientRuntime.Phase phase)
        {
            if (phase != SurgeryPatientRuntime.Phase.WalkIn) return;
            if (running != null && running.tool != null && Gantry != null) Gantry.Work(running.tool, false);
            running = null; started = false; LastResult = null; LastMessage = null;
            queue.Clear(); inspected.Clear(); links.Clear(); state.Clear();
            state["arm"] = 1;
            if (oldArm != null && oldArm != Patient.CyberArm) Destroy(oldArm.gameObject);
            if (newArm != null && newArm != Patient.CyberArm) Destroy(newArm.gameObject);
            oldArm = Patient.CyberArm; newArm = null;
            Apply();
            Changed?.Invoke();
        }

        void OnDestroy()
        {
            if (Patient != null) Patient.PhaseChanged -= OnPatientPhase;
            if (oldArm != null && Patient != null && oldArm != Patient.CyberArm) Destroy(oldArm.gameObject);
            if (beamMaterial != null) Destroy(beamMaterial);
            if (Current == this) { Current = null; SurgeryPatientRuntime.HoldOnTable = null; }
        }
    }
}
