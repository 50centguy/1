using System;
using System.Collections.Generic;
using BorderRepair.Core;
using BorderRepair.Data;
using BorderRepair.Inspection;
using UnityEngine;

namespace BorderRepair.RepairProps
{
    public sealed class RepairPropFlow : MonoBehaviour
    {
        [SerializeField] RepairPropFlowDefinition definition;
        readonly Dictionary<string, int> state = new Dictionary<string, int>();
        readonly Dictionary<string, Transform> objects = new Dictionary<string, Transform>();
        readonly Dictionary<string, RepairPart> parts = new Dictionary<string, RepairPart>();
        readonly Dictionary<string, Quaternion> rotations = new Dictionary<string, Quaternion>();
        readonly Dictionary<string, Vector3> positions = new Dictionary<string, Vector3>();
        readonly List<string> history = new List<string>();
        readonly Dictionary<string, Vector3> retainedPositions = new Dictionary<string, Vector3>();
        readonly Dictionary<string, Quaternion> retainedRotations = new Dictionary<string, Quaternion>();
        readonly Dictionary<string, Vector3> retainedScales = new Dictionary<string, Vector3>();
        readonly Dictionary<string, Transform> retainedParents = new Dictionary<string, Transform>();
        RepairShiftData ownedShift;
        Transform modelRoot;
        bool initialized;
        bool executing;

        public RepairPropFlowDefinition Definition => definition;
        public RepairSession Session { get; private set; }
        public IRepairPropTestProbe TestProbe { get; set; }
        public IRepairPropCommandPolicy CommandPolicy { get; set; }
        public bool InventoryVisible(PropNode node) => node.outKey != "new.out" || CommandPolicy == null || CommandPolicy.ReplacementAvailable;
        public IReadOnlyList<string> History => history.AsReadOnly();
        public bool Completed { get; private set; }
        public int FailedRetests { get; private set; }
        public string LastFeedback { get; private set; }
        public bool LastTestPassed { get; private set; }
        public event Action Changed;
        public event Action<string, FeedbackKind> Feedback;
        public int GetState(string key) => state.TryGetValue(key, out var v) ? v : 0;
        public Transform GetObject(string objectName) => objects.TryGetValue(objectName, out var t) ? t : null;
        public Vector3 InstalledLocalPosition(string objectName) => positions[objectName];
        public Quaternion InstalledLocalRotation(string objectName) => rotations[objectName];
        public bool IsAssembled => initialized && Matches(definition.assembled);
        public void Configure(RepairPropFlowDefinition value) { definition = value; }

        // Which of the pool's faults this order carries. 0 rolls one at random; tests pin a fault (1-based).
        public static int FaultOverride;
        public PropFault ActiveFault => definition != null && definition.HasFaultPool
            ? definition.faults[Mathf.Clamp(GetState("fault"), 1, definition.faults.Count) - 1] : null;
        public bool FaultFixed => ActiveFault == null ? GetState("module") == 2 : Matches(ActiveFault.fixedWhen);
        // A repair was attempted since the item was last found faulty: a fix command ran, or a replacement went in.
        public bool RepairAttempted => definition.HasFaultPool ? GetState("repaired") == 1 : GetState("module") == 2;

        /// <summary>Rolls (or applies <see cref="FaultOverride"/>) the order's fault before anything is measured.</summary>
        public void RollFault()
        {
            if (!initialized || !definition.HasFaultPool || history.Count > 0) return;
            int count = definition.faults.Count;
            state["fault"] = FaultOverride > 0 ? Mathf.Clamp(FaultOverride, 1, count) : UnityEngine.Random.Range(1, count + 1);
        }

        /// <summary>The probe's last reading: 0 not measured, 1 normal, 2 abnormal.</summary>
        public int Reading(PropProbe probe) => GetState("meter." + probe.id);

        // The shared station keeps ownership of its queue. Standalone callers get a one-item core session.
        public void Initialize(RepairPropFlowDefinition value, Transform root, RepairSession session = null)
        {
            if (initialized) throw new InvalidOperationException("A flow is bound to one mounted item.");
            if (value == null || root == null) throw new ArgumentNullException(value == null ? nameof(value) : nameof(root));
            var errors = new List<string>();
            if (!value.Validate(errors)) throw new ArgumentException(string.Join("\n", errors));
            definition = value;
            modelRoot = root;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (objects.ContainsKey(t.name)) throw new ArgumentException("Ambiguous model object: " + t.name);
                objects.Add(t.name, t);
            }
            // A Unity model import can name its single root after the FBX file rather than the Blender empty.
            if (!objects.ContainsKey(value.rootObjectName)) objects.Add(value.rootObjectName, root);
            foreach (var n in value.nodes)
                if (!objects.ContainsKey(n.objectName)) errors.Add("Missing model object: " + n.objectName + " (" + n.hierarchyPath + ")");
            if (errors.Count > 0) throw new ArgumentException(string.Join("\n", errors));
            if (session != null && session.CurrentCase != value.repairCase)
                throw new ArgumentException("Session must be on this definition's case.", nameof(session));
            foreach (var entry in value.initialState) state[entry.key] = entry.value;
            Session = session;
            if (Session == null)
            {
                ownedShift = ScriptableObject.CreateInstance<RepairShiftData>();
                ownedShift.cases.Add(value.repairCase);
                Session = new RepairSession(ownedShift, () => Time.time);
                Session.StartShift();
                Session.AcceptItem();
            }
            foreach (var n in value.nodes)
            {
                Transform t = objects[n.objectName];
                rotations[n.objectName] = t.localRotation;
                positions[n.objectName] = t.localPosition;
                if (n.immutable)
                {
                    retainedPositions[n.objectName] = t.localPosition;
                    retainedRotations[n.objectName] = t.localRotation;
                    retainedScales[n.objectName] = t.localScale;
                    retainedParents[n.objectName] = t.parent;
                }
                var point = t.GetComponent<InspectionPoint>();
                if (point == null) point = t.gameObject.AddComponent<InspectionPoint>();
                point.Configure(n.objectName, null, null);
                if (!n.movable) continue;
                var parking = new GameObject("FlowParking_" + n.objectName).transform;
                // Keep tray children in the tray's coordinate frame; never apply tray travel twice.
                parking.SetParent(t.parent, false);
                parking.position = t.position + modelRoot.TransformVector(n.parkingOffset);
                parking.rotation = t.rotation;
                var part = t.GetComponent<RepairPart>();
                if (part == null) part = t.gameObject.AddComponent<RepairPart>();
                part.Configure(n.objectName, parking, null, null, null, null, null);
                parts[n.objectName] = part;
            }
            initialized = true;
            ApplyVisuals();
            RepairPropEconomyController.TryBind(this);
        }

        public bool Matches(IEnumerable<PropCondition> conditions)
        {
            foreach (var c in conditions) if (GetState(c.key) != c.value) return false;
            return true;
        }

        public bool CanExecute(string commandId, string targetObjectName, RepairActionType tool, out string reason)
        {
            reason = null;
            if (!initialized || Session == null) reason = "物品尚未接入维修流程。";
            else if (Completed || Session.Stage != RepairStage.Inspect) reason = "请先接收物品；已完成的物品不能继续操作。";
            var c = definition != null ? definition.FindCommand(commandId) : null;
            if (reason == null && c == null) reason = "没有这项维修操作。";
            if (reason == null && c.target != targetObjectName) reason = "这项操作不适用于当前部件。";
            if (reason == null && c.tool != tool) reason = "请先选择相应的维修工具。";
            if (reason == null && !RetainedPartsIntact()) reason = "原核心或密封部件状态发生变化，请停止操作并保留原件。";
            if (reason == null && !Matches(c.requires)) reason = DescribeBlock(c);
            if (reason == null && (c.kind == PropCommandKind.Test || c.kind == PropCommandKind.Complete) && !IsAssembled)
                reason = "请复装全部接头、固定件、托盘和盖板后再测试。";
            if (reason == null && c.kind == PropCommandKind.Complete)
                foreach (var test in definition.tests)
                    if (GetState("pass." + test.id) != 1) { reason = "功能与原有数据一致性复测必须全部通过。"; break; }
            if (reason == null && CommandPolicy != null) CommandPolicy.CanExecute(this, c, out reason);
            return reason == null;
        }

        // Wording only: names the first unmet requirement. CanExecute alone decides whether a command may run.
        string DescribeBlock(PropCommand c)
        {
            if (c.effects.Count > 0 && Matches(c.effects)) return DescribeCurrent(c);
            if (c.kind == PropCommandKind.Manipulate || c.kind == PropCommandKind.Measure)
            {
                // The initial tests need power and the fixture, so missing diagnosis outranks the isolation advice.
                string diagnosis = DescribeMissingDiagnosis(c.requires);
                if (diagnosis != null) return diagnosis;
                bool powered = GetState("power") != 0, docked = GetState("fixture") != 0;
                if (powered || docked)
                    return powered && docked ? "请先关机并解除外部测试连接，再拆开或移动部件。"
                        : powered ? "请先关机，再拆开或移动部件。" : "请先解除外部测试连接，再拆开或移动部件。";
                if (GetState("isolated") != 1 && c.requires.Exists(r => r.key == "isolated" && r.value == 1))
                    return "请先拔下内部供电服务插头；仅关机不等于完成供电隔离。";
            }
            else
            {
                bool energizes = c.effects.Exists(e => (e.key == "power" || e.key == "fixture") && e.value == 1);
                if ((c.kind == PropCommandKind.Test || c.kind == PropCommandKind.Complete || energizes) && !IsAssembled)
                    return DescribeAssembly();
                if (c.requires.Exists(r => r.key == "fixture" && r.value == 1) && GetState("fixture") != 1)
                    return "外部测试连接未接入：请在关机状态下先“" + Label("fixture.connect") + "”。";
                if (c.requires.Exists(r => r.key == "power" && r.value == 1) && GetState("power") != 1)
                    return "测试供电未开启：请先“" + Label("power.on") + "”。";
                if (c.requires.Exists(r => r.key == "power" && r.value == 0) && GetState("power") != 0)
                    return "请先关机，再更改外部测试连接。";
            }
            foreach (var r in c.requires)
                if (GetState(r.key) != r.value) return DescribeCondition(r, c.requires);
            return "尚不能“" + c.label + "”。";
        }

        string DescribeCurrent(PropCommand c)
        {
            switch (c.id)
            {
                case "power.off": return "已关机。";
                case "power.on": return "测试供电已开启。";
                case "fixture.disconnect": return "外部测试连接已解除。";
                case "fixture.connect": return "外部测试连接已接入。";
                default: return "无需操作：已是“" + c.label + "”后的状态。";
            }
        }

        string DescribeMissingDiagnosis(List<PropCondition> requires)
        {
            var missing = new List<string>();
            foreach (var r in requires)
                if (r.key.StartsWith("diagnosed.", StringComparison.Ordinal) && GetState(r.key) != r.value)
                    missing.Add("“" + definition.FindTest(r.key.Substring(10)).label + "”");
            if (missing.Count == 0) return null;
            string setup = GetState("fixture") != 1 ? "请先“" + Label("fixture.connect") + "”并“" + Label("power.on") + "”，"
                : GetState("power") != 1 ? "请先“" + Label("power.on") + "”，" : "请在当前通电状态下";
            return "初始诊断未完成：还需" + string.Join("", missing) + "。" + setup + "完成初测后再关机拆解。";
        }

        // Reassembly runs inside-out, so the innermost unmet assembled condition is reported first.
        string DescribeAssembly()
        {
            for (int i = definition.assembled.Count - 1; i >= 0; i--)
            {
                var r = definition.assembled[i];
                if (GetState(r.key) != r.value) return "组装未完成：" + DescribeCondition(r, definition.assembled);
            }
            return "请复装全部接头、固定件、托盘和盖板后再测试。";
        }

        string DescribeCondition(PropCondition r, List<PropCondition> scope)
        {
            bool on = r.value == 1;
            if (r.key.StartsWith("out.", StringComparison.Ordinal))
            {
                int remaining = 0;
                foreach (var s in scope)
                    if (s.key.StartsWith("out.", StringComparison.Ordinal) && s.value == r.value && GetState(s.key) != s.value) remaining++;
                var node = definition.FindNode(r.key.Substring(4));
                string name = node != null ? node.displayName : "固定件";
                return remaining > 1 ? "还有 " + remaining + " 颗固定螺钉未" + (on ? "卸下" : "装回") + "（如" + name + "）。"
                    : "请先" + (on ? "卸下" : "装回") + name + "。";
            }
            if (r.key.StartsWith("diagnosed.", StringComparison.Ordinal)) return DescribeMissingDiagnosis(new List<PropCondition> { r });
            if (r.key.StartsWith("pass.", StringComparison.Ordinal))
                return "请先通过“" + definition.FindTest(r.key.Substring(5)).label + "”。";
            switch (r.key)
            {
                case "cover": return on ? "请先取下维修盖板。" : "请先装回维修盖板。";
                case "isolated": return on ? "请先拔下内部供电服务插头；仅关机不等于完成供电隔离。" : "请先接回内部供电。";
                case "released": return on ? "请先释放信号／模块接头。" : "请先插回信号／模块接头。";
                case "tray": return on ? "请先沿导轨抽出输出托盘。" : "请先将输出托盘归位。";
                case "seat": return on ? "模块尚未装到位，请先安装模块。" : "请先取下当前模块。";
                case "module":
                    if (r.value == 2) return "尚未安装替换模块。";
                    if (r.value == 1) return "请先取下模块。";
                    return GetState("module") == 2 ? "替换模块已装上；如需取下，请对替换模块操作。" : "模块已取下。";
                case "old.out": return on ? "请先取下原故障模块。" : "原故障模块不在安装位。";
                case "new.out": return on ? "替换模块已装上。" : "替换模块不在安装位。";
                case "power": return on ? "测试供电未开启：请先“" + Label("power.on") + "”。" : "请先关机。";
                case "fixture": return on ? "外部测试连接未接入：请先“" + Label("fixture.connect") + "”。" : "请先解除外部测试连接。";
                case "strip": return on ? "请先展开顶部护条。" : "请先收起顶部护条。";
                case "repaired": return on ? "还没有做任何处理。" : "已经处理过了。";
                default: return "尚不能进行这项操作，前置步骤未完成。";
            }
        }

        string Label(string commandId)
        {
            var command = definition.FindCommand(commandId);
            return command != null ? command.label : commandId;
        }

        public bool TryExecute(string commandId, string targetObjectName, RepairActionType tool)
        {
            if (executing) return false;
            if (!CanExecute(commandId, targetObjectName, tool, out var reason))
            {
                if (Session != null && Session.CurrentRecord != null) Session.CurrentRecord.InvalidActionCount++;
                Say(reason, FeedbackKind.Warning);
                return false;
            }
            executing = true;
            try
            {
                var c = definition.FindCommand(commandId);
                if (CommandPolicy != null && !CommandPolicy.BeforeExecute(this, c, out var blocked))
                { Say(blocked, FeedbackKind.Warning); return false; }
                if (c.kind == PropCommandKind.Complete) return Complete();
                if (c.invalidatesTests) ClearTestPasses();
                foreach (var e in c.effects) state[e.key] = e.value;
                history.Add(c.id);
                if (c.kind == PropCommandKind.Test) RunTest(definition.FindTest(c.testId));
                else if (c.kind == PropCommandKind.Measure) RunProbe(definition.FindProbe(c.probeId));
                else Say(c.label, FeedbackKind.Info);
                ApplyVisuals();
                Changed?.Invoke();
                return true;
            }
            finally { executing = false; }
        }

        public ScanOutcome TryInspect(string objectName)
        {
            if (!initialized || Completed || Session.Stage != RepairStage.Inspect) return ScanOutcome.Rejected;
            var n = definition.FindNode(objectName);
            if (n == null || !Matches(n.inspectRequires) || !objects[objectName].gameObject.activeInHierarchy)
            {
                Say("请先露出部件，再进行检查。", FeedbackKind.Warning);
                return ScanOutcome.Rejected;
            }
            bool wasScanning = Session.ScanModeActive;
            Session.SetScanMode(true);
            var result = Session.ReportScan(objectName);
            Session.SetScanMode(wasScanning);
            return result;
        }

        /// <summary>What the probe would read now: true when its fault is this order's and is not fixed yet.</summary>
        public bool ProbeReadsAbnormal(PropProbe probe)
        {
            var fault = definition.FindFault(probe.fault);
            return fault != null && fault == ActiveFault && !Matches(fault.fixedWhen);
        }

        void RunProbe(PropProbe probe)
        {
            bool abnormal = ProbeReadsAbnormal(probe);
            state["meter." + probe.id] = abnormal ? 2 : 1;
            Say(abnormal ? probe.abnormalReading : probe.normalReading, abnormal ? FeedbackKind.Warning : FeedbackKind.Info);
        }

        void RunTest(PropTest test)
        {
            PropTestResult result;
            bool retest = RepairAttempted;
            state["last.retest"] = retest ? 1 : 0;
            try
            {
                result = TestProbe != null ? TestProbe.Measure(this, test)
                    : new PropTestResult(!test.functional || FaultFixed, RetainedPartsIntact(), true);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[RepairProps] Bench measurement failed: " + e.Message, this);
                result = new PropTestResult(false, true, true, "测试设备未能取得结果，请重新测试。");
            }
            bool pass = result.functionalPass && result.readOnly && result.retainedIdentityMatches && RetainedPartsIntact();
            state["diagnosed." + test.id] = 1;
            LastTestPassed = pass;
            if (!pass)
            {
                ClearTestPasses();
                state["retest.failed"] = 1;
                if (retest) FailedRetests++;
                string finding = result.message ?? (test.functional && ActiveFault != null ? ActiveFault.symptom : test.faultFinding);
                if (test.functional && ActiveFault != null)
                {
                    // The complaint is still there: the guide goes back to opening up and measuring.
                    if (retest) { state["repaired"] = 0; finding = "症状没有变化，刚才的处理没解决问题。\n" + finding; }
                    else if (!string.IsNullOrEmpty(definition.suspectHint)) finding += "\n" + definition.suspectHint;
                }
                Say(WithStory(finding, test, false), FeedbackKind.Warning);
                return;
            }
            int count = GetState("count." + test.id) + 1;
            state["count." + test.id] = count;
            state["pass." + test.id] = count >= test.consecutivePasses ? 1 : 0;
            state["retest.failed"] = 0;
            Say(WithStory((result.message ?? test.passFinding) + (test.consecutivePasses > 1 ? " (" + count + "/" + test.consecutivePasses + ")" : ""), test, true), FeedbackKind.Success);
        }

        string WithStory(string message, PropTest test, bool pass)
        {
            string note = RepairPropStories.TestNote(definition.kind, test.id, pass);
            return string.IsNullOrEmpty(note) ? message : message + "\n" + note;
        }

        void ClearTestPasses()
        {
            foreach (var t in definition.tests) { state["pass." + t.id] = 0; state["count." + t.id] = 0; }
            LastTestPassed = false;
        }

        bool Complete()
        {
            // RepairSession has no reversible-action guard. A self-locked clue seals the legacy entry point.
            // Only this verified branch briefly opens it, without persisting changes to the shared asset.
            var gate = definition.repairCase.FindStep(definition.completionStepId);
            if (gate == null) { Say("缺少复测确认配置，暂时无法交付。", FeedbackKind.Error); return false; }
            var lockClues = gate.requiredClues;
            ActionOutcome outcome;
            try
            {
                gate.requiredClues = new List<string>();
                outcome = Session.PerformAction(gate.action, gate.targetPointId);
            }
            finally { gate.requiredClues = lockClues; }
            if (outcome != ActionOutcome.Performed && outcome != ActionOutcome.AlreadyDone) return false;
            if (!Session.TryBeginDiagnosis()) return false;
            // The shared clinic settles a customer trade from the record's repair decision, which it reads while the
            // ending is submitted. A verified retest is a correct repair; an ending alone never reaches delivery.
            var record = Session.CurrentRecord;
            record.Decision = RepairDecision.Repair;
            record.Outcome = new DecisionOutcome { decision = RepairDecision.Repair, isCorrect = true,
                resultTitle = "维修验证通过", reason = "复装与全部复测通过，原有核心保留。" };
            if (!Session.SubmitEnding(definition.completionEndingId))
            {
                record.Decision = null; record.Outcome = null;
                return false;
            }
            Completed = true;
            history.Add("complete");
            Say("复装与复测通过，原有部件和数据保留，未读取私人内容。", FeedbackKind.Success);
            Changed?.Invoke();
            return true;
        }

        bool RetainedPartsIntact()
        {
            foreach (var n in definition.nodes)
            {
                if (!n.immutable) continue;
                var t = objects[n.objectName];
                if (t == null || !t.gameObject.activeSelf || t.localPosition != retainedPositions[n.objectName]
                    || Quaternion.Angle(t.localRotation, retainedRotations[n.objectName]) > 0.001f
                    || t.localScale != retainedScales[n.objectName] || t.parent != retainedParents[n.objectName]) return false;
            }
            return true;
        }

        void ApplyVisuals()
        {
            bool powered = GetState("power") == 1 && GetState("fixture") == 1;
            foreach (var test in definition.tests)
                state["display." + test.id] = powered ? (GetState("pass." + test.id) == 1 ? 2 : 1) : 0;
            if (definition.kind == RepairPropKind.Receiver)
                state["curtain"] = powered && GetState("strip") == 1 ? (GetState("pass.signal") == 1 ? 2 : 1) : 0;
            foreach (var n in definition.nodes)
            {
                var t = objects[n.objectName];
                bool visible = string.IsNullOrEmpty(n.visibleKey) || GetState(n.visibleKey) == n.visibleValue;
                if (!string.IsNullOrEmpty(n.hideWhenKey) && GetState(n.hideWhenKey) == n.hideWhenValue) visible = false;
                if (n.inventoryOnly && GetState(n.outKey) == n.outValue) visible = false;
                if (!n.immutable) t.gameObject.SetActive(visible);
                if (!string.IsNullOrEmpty(n.rotationKey))
                    t.localRotation = rotations[n.objectName] * Quaternion.AngleAxis(GetState(n.rotationKey) * n.rotationDegrees, n.rotationAxis);
                if (parts.TryGetValue(n.objectName, out var part))
                {
                    part.SetState(GetState(n.outKey) == n.outValue ? RepairPartState.Removed : RepairPartState.Installed, true);
                }
            }
            if (definition.kind == RepairPropKind.Omamori)
            {
                string moduleName = GetState("last.module") == 2 ? "OM_WakeModule_New" : "OM_WakeModule_Fault";
                var module = objects[moduleName];
                Vector3 delta = GetState("seat") == 0 ? module.position - module.parent.TransformPoint(positions[moduleName]) : Vector3.zero;
                foreach (var n in definition.nodes)
                {
                    if (!n.followsRemovedWakeModule) continue;
                    var t = objects[n.objectName];
                    if (!n.movable) t.localPosition = positions[n.objectName];
                    t.position += delta;
                }
            }
        }

        void Say(string text, FeedbackKind kind) { LastFeedback = text; Feedback?.Invoke(text, kind); }
        void OnDestroy()
        {
            (CommandPolicy as IDisposable)?.Dispose();
            if (ownedShift != null) Destroy(ownedShift);
        }
    }
}
