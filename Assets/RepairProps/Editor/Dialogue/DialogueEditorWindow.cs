using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BorderRepair.RepairProps.Dialogue;
using UnityEditor;
using UnityEngine;
using static BorderRepair.RepairProps.RepairPropStoryText;

namespace BorderRepair.RepairProps.Editor
{
    /// <summary>
    /// Border Repair > Repair Props > 对话编辑器. Edits the three story files (calls, UNIT 07, props) with undo, checks
    /// them, previews a call's branches, and writes them back with DialogueJson. Saving in play mode takes effect on
    /// the next call or line.
    /// </summary>
    public sealed class DialogueEditorWindow : EditorWindow
    {
        public const string Folder = "Assets/RepairProps/Story/Resources/" + ResourceFolder;
        public static string CallsPath => Folder + "/calls.json";
        public static string AssistantPath => Folder + "/assistant.json";
        public static string PropsPath => Folder + "/props.json";

        static readonly string[] Tabs = { "来电", "七号", "物品", "试播", "检查" };

        DialogueEditorData data;
        string savedCalls, savedAssistant, savedProps;
        int tab, selectedCall, selectedProp;
        Vector2 listScroll, mainScroll;
        readonly HashSet<string> expanded = new HashSet<string>();
        List<DialogueValidator.Issue> issues = new List<DialogueValidator.Issue>();
        double issuesAt;
        GUIStyle area, small, errorStyle, warnStyle, header;

        // Preview
        int previewCall = -1;
        DialogueRunner previewRunner;
        PreviewWorld previewWorld = new PreviewWorld();
        readonly List<(bool player, string who, string text)> transcript = new List<(bool, string, string)>();
        Vector2 previewScroll;

        [MenuItem("Border Repair/Repair Props/对话编辑器 (Dialogue Editor)", priority = 20)]
        public static void Open()
        {
            var w = GetWindow<DialogueEditorWindow>();
            w.titleContent = new GUIContent("对话编辑器");
            w.minSize = new Vector2(760, 420);
            w.Show();
        }

        void OnEnable()
        {
            saveChangesMessage = "对话文件有未保存的修改，要保存吗？";
            Undo.undoRedoPerformed += OnUndoRedo;
            if (data == null) Load();
        }

        void OnDisable() => Undo.undoRedoPerformed -= OnUndoRedo;

        void OnDestroy() { if (data != null) DestroyImmediate(data); }

        void OnUndoRedo() { issuesAt = 0; previewRunner = null; Repaint(); }

        // ---- files ---------------------------------------------------------------------------------------------

        static T Read<T>(string path) where T : new()
        {
            if (!File.Exists(path)) return new T();
            return JsonUtility.FromJson<T>(File.ReadAllText(path)) ?? new T();
        }

        void Load()
        {
            if (data == null) { data = CreateInstance<DialogueEditorData>(); data.hideFlags = HideFlags.DontSave; }
            data.calls = Read<CallFile>(CallsPath);
            data.assistant = Read<AssistantFile>(AssistantPath);
            data.props = Read<PropFile>(PropsPath);
            Normalize();
            savedCalls = DialogueJson.Write(data.calls);
            savedAssistant = DialogueJson.Write(data.assistant);
            savedProps = DialogueJson.Write(data.props);
            Undo.ClearUndo(data);
            hasUnsavedChanges = false;
            issuesAt = 0; previewRunner = null;
        }

        // Unity serialization turns null arrays into empty ones and back; keep them non-null so the GUI can index them.
        void Normalize()
        {
            data.calls.calls ??= new Call[0];
            foreach (var c in data.calls.calls)
            {
                c.lines ??= new CallLine[0];
                foreach (var l in c.lines) l.choices ??= new Choice[0];
            }
            data.assistant.groups ??= new Group[0];
            foreach (var g in data.assistant.groups) g.lines ??= new Line[0];
            data.assistant.hints ??= new Hint[0];
            data.assistant.banter ??= new string[0];
            data.assistant.banterAfterCall ??= new CallBanter[0];
            data.props.props ??= new Prop[0];
            foreach (var p in data.props.props) { p.findings ??= new Finding[0]; p.testNotes ??= new TestNote[0]; }
        }

        bool Dirty(out string calls, out string assistant, out string props)
        {
            calls = DialogueJson.Write(data.calls);
            assistant = DialogueJson.Write(data.assistant);
            props = DialogueJson.Write(data.props);
            return calls != savedCalls || assistant != savedAssistant || props != savedProps;
        }

        public override void SaveChanges() { Save(); base.SaveChanges(); }

        void Save()
        {
            Refresh(force: true);
            int errors = issues.Count(i => i.Error);
            if (errors > 0 && !EditorUtility.DisplayDialog("对话编辑器", $"还有 {errors} 个错误（见「检查」页）。仍然保存吗？", "保存", "取消")) return;
            Dirty(out var calls, out var assistant, out var props);
            Directory.CreateDirectory(Folder);
            var utf8 = new System.Text.UTF8Encoding(false);
            if (calls != savedCalls) File.WriteAllText(CallsPath, calls, utf8);
            if (assistant != savedAssistant) File.WriteAllText(AssistantPath, assistant, utf8);
            if (props != savedProps) File.WriteAllText(PropsPath, props, utf8);
            savedCalls = calls; savedAssistant = assistant; savedProps = props;
            AssetDatabase.ImportAsset(CallsPath); AssetDatabase.ImportAsset(AssistantPath); AssetDatabase.ImportAsset(PropsPath);
            RepairPropStoryText.Reload();
            hasUnsavedChanges = false;
            ShowNotification(new GUIContent("已保存"));
        }

        // ---- GUI -----------------------------------------------------------------------------------------------

        void Styles()
        {
            if (area != null) return;
            area = new GUIStyle(EditorStyles.textArea) { wordWrap = true };
            small = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
            errorStyle = new GUIStyle(EditorStyles.label) { wordWrap = true, normal = { textColor = new Color(.95f, .35f, .3f) } };
            warnStyle = new GUIStyle(EditorStyles.label) { wordWrap = true, normal = { textColor = new Color(.95f, .7f, .2f) } };
            header = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
        }

        void OnGUI()
        {
            if (data == null) Load();
            Styles();
            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.S && (e.control || e.command)) { Save(); e.Use(); }
            Undo.RecordObject(data, "编辑对话");

            Toolbar();
            Refresh();
            switch (tab)
            {
                case 0: CallsTab(); break;
                case 1: AssistantTab(); break;
                case 2: PropsTab(); break;
                case 3: PreviewTab(); break;
                default: IssuesTab(); break;
            }
            if (e.type == EventType.Repaint || e.type == EventType.Layout) hasUnsavedChanges = Dirty(out _, out _, out _);
        }

        void Toolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                int errors = issues.Count(i => i.Error), warns = issues.Count - errors;
                var labels = Tabs.Select((t, i) => i == 4 && issues.Count > 0 ? $"{t} ({errors}/{warns})" : t).ToArray();
                tab = GUILayout.Toolbar(tab, labels, EditorStyles.toolbarButton, GUILayout.Width(420));
                GUILayout.FlexibleSpace();
                if (hasUnsavedChanges) GUILayout.Label("● 未保存", EditorStyles.miniLabel);
                if (GUILayout.Button("保存 (Ctrl+S)", EditorStyles.toolbarButton)) Save();
                if (GUILayout.Button("重新载入", EditorStyles.toolbarButton)
                    && (!hasUnsavedChanges || EditorUtility.DisplayDialog("对话编辑器", "放弃未保存的修改，重新读取文件？", "重新载入", "取消"))) Load();
                if (GUILayout.Button("打开文件夹", EditorStyles.toolbarButton)) EditorUtility.RevealInFinder(CallsPath);
            }
        }

        void Refresh(bool force = false)
        {
            if (!force && EditorApplication.timeSinceStartup - issuesAt < .5) return;
            issuesAt = EditorApplication.timeSinceStartup;
            issues = DialogueValidator.Check(data.calls, data.assistant, data.props, ClinicNpcRuntime.Names);
        }

        // ---- calls ---------------------------------------------------------------------------------------------

        void CallsTab()
        {
            var calls = data.calls.calls;
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(230)))
                {
                    listScroll = EditorGUILayout.BeginScrollView(listScroll, GUILayout.ExpandHeight(true));
                    for (int i = 0; i < calls.Length; i++)
                    {
                        var c = calls[i];
                        bool bad = issues.Any(x => x.Error && x.Where.StartsWith("来电 " + c.id));
                        string label = (bad ? "⚠ " : "") + c.id + "  ·  " + c.name;
                        if (GUILayout.Toggle(selectedCall == i, label, "Button")) selectedCall = i;
                    }
                    EditorGUILayout.EndScrollView();
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("＋ 新来电")) { data.calls.calls = Insert(calls, calls.Length, NewCall(calls)); selectedCall = calls.Length; }
                        GUI.enabled = selectedCall >= 0 && selectedCall < calls.Length;
                        if (GUILayout.Button("复制"))
                        {
                            var copy = JsonUtility.FromJson<Call>(JsonUtility.ToJson(calls[selectedCall]));
                            copy.id = Unique(calls, copy.id + "-copy");
                            data.calls.calls = Insert(calls, selectedCall + 1, copy); selectedCall++;
                        }
                        if (GUILayout.Button("删除") && EditorUtility.DisplayDialog("删除来电", $"删除来电 {calls[selectedCall].id}？可以撤销。", "删除", "取消"))
                        { data.calls.calls = RemoveAt(calls, selectedCall); selectedCall = Mathf.Min(selectedCall, data.calls.calls.Length - 1); }
                        GUI.enabled = true;
                    }
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUI.enabled = selectedCall > 0;
                        if (GUILayout.Button("上移")) { data.calls.calls = Swap(calls, selectedCall, selectedCall - 1); selectedCall--; }
                        GUI.enabled = selectedCall >= 0 && selectedCall < calls.Length - 1;
                        if (GUILayout.Button("下移")) { data.calls.calls = Swap(calls, selectedCall, selectedCall + 1); selectedCall++; }
                        GUI.enabled = true;
                    }
                    EditorGUILayout.LabelField("游戏按这个顺序检查触发条件，同一时刻只响第一通满足条件的。", small);
                }
                using (new EditorGUILayout.VerticalScope())
                {
                    if (selectedCall < 0 || selectedCall >= data.calls.calls.Length) { EditorGUILayout.HelpBox("左边选一通来电，或者新建一通。", MessageType.Info); return; }
                    mainScroll = EditorGUILayout.BeginScrollView(mainScroll);
                    CallEditor(data.calls.calls[selectedCall]);
                    EditorGUILayout.EndScrollView();
                }
            }
        }

        static Call NewCall(Call[] calls) => new Call
        {
            id = Unique(calls, "new-call"), caller = ClinicNpcRuntime.Names[0], name = "", title = "", trigger = "",
            lines = new[] { new CallLine { text = "", choices = new Choice[0] } },
        };

        static string Unique(Call[] calls, string id)
        {
            string candidate = id; int n = 2;
            while (calls.Any(c => c.id == candidate)) candidate = id + "-" + n++;
            return candidate;
        }

        void CallEditor(Call call)
        {
            EditorGUILayout.LabelField("来电", header);
            call.id = EditorGUILayout.DelayedTextField(new GUIContent("id", "唯一名字，条件里用 played:id 引用，七号的来电后台词用 after-call:id"), call.id);
            using (new EditorGUILayout.HorizontalScope())
            {
                var names = ClinicNpcRuntime.Names;
                int at = Array.IndexOf(names, call.caller);
                var options = names.Concat(new[] { at < 0 ? "其他：" + call.caller : "其他…" }).ToArray();
                int pick = EditorGUILayout.Popup(new GUIContent("头像", "通话卡片上显示哪个人物的实时画面"), at < 0 ? names.Length : at, options);
                if (pick < names.Length) call.caller = names[pick];
                else call.caller = EditorGUILayout.TextField(call.caller, GUILayout.Width(120));
            }
            call.name = EditorGUILayout.TextField(new GUIContent("显示名", "字幕里说话人的名字"), call.name);
            call.title = EditorGUILayout.TextField(new GUIContent("头衔"), call.title);
            call.trigger = ConditionField(new GUIContent("触发条件", "满足时打进来（每局一次）。空着就不会自己打进来。"), call.trigger);
            ConditionHelp();

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("台词", header);
            var labels = call.lines.Where(l => !string.IsNullOrWhiteSpace(l.id)).Select(l => l.id.Trim()).Distinct().ToArray();
            for (int i = 0; i < call.lines.Length; i++)
            {
                if (LineEditor(call, i, labels)) break;   // the array changed; draw it fresh next frame
            }
            if (GUILayout.Button("＋ 添加一句")) call.lines = Insert(call.lines, call.lines.Length, new CallLine { text = "", choices = new Choice[0] });

            var mine = issues.Where(x => x.Where.StartsWith("来电 " + call.id)).ToList();
            if (mine.Count > 0)
            {
                EditorGUILayout.Space(6);
                foreach (var x in mine) EditorGUILayout.LabelField(x.ToString(), x.Error ? errorStyle : warnStyle);
            }
            EditorGUILayout.Space(6);
            if (GUILayout.Button("试播这通来电")) { tab = 3; previewCall = selectedCall; previewRunner = null; }
        }

        /// <summary>Draws one line; true when it changed the line array (insert, delete, move).</summary>
        bool LineEditor(Call call, int i, string[] labels)
        {
            var line = call.lines[i];
            line.choices ??= new Choice[0];
            string key = call.id + "#" + i;
            bool advanced = expanded.Contains(key) || !string.IsNullOrWhiteSpace(line.id) || !string.IsNullOrWhiteSpace(line.when)
                || !string.IsNullOrWhiteSpace(line.set) || !string.IsNullOrWhiteSpace(line.next) || line.choices.Length > 0;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    string tag = string.IsNullOrWhiteSpace(line.id) ? "#" + (i + 1) : "#" + (i + 1) + " [" + line.id + "]";
                    GUILayout.Label(tag, EditorStyles.boldLabel, GUILayout.Width(110));
                    line.player = EditorGUILayout.Popup(line.player ? 1 : 0, new[] { call.name + "（对方）", "你（玩家）" }, GUILayout.Width(120)) == 1;
                    GUILayout.FlexibleSpace();
                    if (!advanced && GUILayout.Button("分支 / 条件", EditorStyles.miniButton)) expanded.Add(key);
                    if (GUILayout.Button("▲", EditorStyles.miniButtonLeft, GUILayout.Width(24)) && i > 0) { call.lines = Swap(call.lines, i, i - 1); return true; }
                    if (GUILayout.Button("▼", EditorStyles.miniButtonMid, GUILayout.Width(24)) && i < call.lines.Length - 1) { call.lines = Swap(call.lines, i, i + 1); return true; }
                    if (GUILayout.Button("插入", EditorStyles.miniButtonMid, GUILayout.Width(36))) { call.lines = Insert(call.lines, i + 1, new CallLine { text = "", player = line.player, choices = new Choice[0] }); return true; }
                    if (GUILayout.Button("✕", EditorStyles.miniButtonRight, GUILayout.Width(24))) { call.lines = RemoveAt(call.lines, i); return true; }
                }
                line.text = EditorGUILayout.TextArea(line.text ?? "", area, GUILayout.MinHeight(34));
                if (!advanced) return false;

                EditorGUI.indentLevel++;
                line.id = EditorGUILayout.TextField(new GUIContent("标签", "给这句起名，别的句子或选项可以跳到这里"), line.id);
                line.when = ConditionField(new GUIContent("条件", "不满足就跳过这句"), line.when);
                line.set = EditorGUILayout.TextField(new GUIContent("设置标记", "说到这句时设置标记，例如：kept-line, !angry（! 表示清除）"), line.set);
                line.next = JumpField(new GUIContent("说完跳到", "说完这句后跳到哪里"), line.next, labels);
                EditorGUILayout.LabelField(line.choices.Length == 0 ? "选项：没有（说完自动往下）" : "选项：玩家必须选一个才继续（按数字键或点击）", small);
                for (int k = 0; k < line.choices.Length; k++)
                {
                    var c = line.choices[k];
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Label((k + 1).ToString(), GUILayout.Width(28));
                            c.text = EditorGUILayout.TextField(c.text);
                            if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(24))) { line.choices = RemoveAt(line.choices, k); return true; }
                        }
                        c.next = JumpField(new GUIContent("选了跳到"), c.next, labels);
                        c.when = ConditionField(new GUIContent("出现条件", "不满足时这个选项不出现"), c.when);
                        c.set = EditorGUILayout.TextField(new GUIContent("设置标记"), c.set);
                    }
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(EditorGUI.indentLevel * 15);
                    GUI.enabled = line.choices.Length < DialogueValidator.MaxChoices;
                    if (GUILayout.Button("＋ 选项", EditorStyles.miniButton, GUILayout.Width(70))) line.choices = Insert(line.choices, line.choices.Length, new Choice { text = "" });
                    GUI.enabled = true;
                    if (GUILayout.Button("收起", EditorStyles.miniButton, GUILayout.Width(50))) expanded.Remove(key);
                }
                EditorGUI.indentLevel--;
            }
            return false;
        }

        string ConditionField(GUIContent label, string value)
        {
            value = EditorGUILayout.TextField(label, value);
            if (!string.IsNullOrWhiteSpace(value))
            {
                var c = DialogueCondition.Parse(value);
                if (!c.Valid) EditorGUILayout.LabelField("    ↳ " + c.Error, errorStyle);
            }
            return value;
        }

        string JumpField(GUIContent label, string value, string[] labels)
        {
            var options = new List<string> { "（顺序往下）", "end（挂断）" };
            options.AddRange(labels);
            string v = value?.Trim() ?? "";
            int at = string.IsNullOrEmpty(v) ? 0 : string.Equals(v, DialogueRunner.End, StringComparison.OrdinalIgnoreCase) ? 1 : options.IndexOf(v);
            if (at < 0) { options.Add("⚠ " + v + "（不存在）"); at = options.Count - 1; }
            int pick = EditorGUILayout.Popup(label, at, options.ToArray());
            if (pick == at) return value;
            return pick == 0 ? "" : pick == 1 ? DialogueRunner.End : options[pick].StartsWith("⚠ ") ? value : options[pick];
        }

        bool showHelp;
        void ConditionHelp()
        {
            showHelp = EditorGUILayout.Foldout(showHelp, "条件怎么写", true);
            if (!showHelp) return;
            EditorGUILayout.HelpBox(
                "flag:名字 — 标记已设置（由台词或选项的「设置标记」设置）\n" +
                "played:来电id — 这通来电本局已经打过\n" +
                "delivered:receiver / cartridge / power / omamori — 这件物品已交付\n" +
                "phase:Night1Counter / Night2Open … — 当前阶段\n" +
                "time > 25 — 开场（关掉开始页）后过了多少秒（只对触发条件有意义）\n" +
                "cash >= 1200 — 现金；delivered >= 2 — 已交付几单\n" +
                "组合：& 并且，| 或者，! 不是，括号分组。例：phase:Night1Counter & (delivered>=1 | time>150)", MessageType.None);
        }

        // ---- UNIT 07 -------------------------------------------------------------------------------------------

        void AssistantTab()
        {
            var a = data.assistant;
            mainScroll = EditorGUILayout.BeginScrollView(mainScroll);
            EditorGUILayout.LabelField("固定时刻的台词", header);
            foreach (var (key, what) in DialogueValidator.AssistantGroups) GroupEditor(a, key, what);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("每通来电挂断后", header);
            foreach (var call in data.calls.calls) GroupEditor(a, DialogueValidator.AfterCallPrefix + call.id, "「" + call.name + "」的来电（" + call.id + "）挂断后");
            foreach (var g in a.groups.Where(g => g.key != null && g.key.StartsWith(DialogueValidator.AfterCallPrefix)
                         && !data.calls.calls.Any(c => DialogueValidator.AfterCallPrefix + c.id == g.key)).ToArray())
                GroupEditor(a, g.key, "⚠ 对应的来电不存在");

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("被点击时的提示（按当前状态选一条）", header);
            foreach (var (key, what) in DialogueValidator.AssistantHints)
            {
                var h = a.hints.FirstOrDefault(x => x.key == key);
                EditorGUILayout.LabelField(what, small);
                string text = EditorGUILayout.TextArea(h?.text ?? "", area);
                if (h == null && text.Length > 0) a.hints = Insert(a.hints, a.hints.Length, new Hint { key = key, text = text });
                else if (h != null) h.text = text;
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("闲聊（被点击时轮流说）", header);
            for (int i = 0; i < a.banter.Length; i++)
                using (new EditorGUILayout.HorizontalScope())
                {
                    a.banter[i] = EditorGUILayout.TextField(a.banter[i]);
                    if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(24))) { a.banter = RemoveAt(a.banter, i); break; }
                }
            if (GUILayout.Button("＋ 闲聊", GUILayout.Width(90))) a.banter = Insert(a.banter, a.banter.Length, "");
            EditorGUILayout.LabelField("某通来电打过之后才加入的闲聊", small);
            var ids = data.calls.calls.Select(c => c.id).ToArray();
            for (int i = 0; i < a.banterAfterCall.Length; i++)
                using (new EditorGUILayout.HorizontalScope())
                {
                    var b = a.banterAfterCall[i];
                    int at = Array.IndexOf(ids, b.afterCall);
                    int pick = EditorGUILayout.Popup(at, ids.Concat(at < 0 ? new[] { "⚠ " + b.afterCall } : new string[0]).ToArray(), GUILayout.Width(150));
                    if (pick >= 0 && pick < ids.Length) b.afterCall = ids[pick];
                    b.text = EditorGUILayout.TextField(b.text);
                    if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(24))) { a.banterAfterCall = RemoveAt(a.banterAfterCall, i); break; }
                }
            if (GUILayout.Button("＋ 来电后闲聊", GUILayout.Width(120)) && ids.Length > 0)
                a.banterAfterCall = Insert(a.banterAfterCall, a.banterAfterCall.Length, new CallBanter { afterCall = ids[0], text = "" });
            EditorGUILayout.EndScrollView();
        }

        static readonly string[] Faces = Enum.GetNames(typeof(ClinicAssistantRuntime.Face));

        void GroupEditor(AssistantFile a, string key, string what)
        {
            var g = a.groups.FirstOrDefault(x => x.key == key);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(what + "　　" + key, EditorStyles.miniBoldLabel);
                if (g == null)
                {
                    if (GUILayout.Button("＋ 添加这组台词", EditorStyles.miniButton, GUILayout.Width(110)))
                        a.groups = Insert(a.groups, a.groups.Length, new Group { key = key, lines = new[] { new Line { face = "Smile", text = "" } } });
                    return;
                }
                g.lines ??= new Line[0];
                for (int i = 0; i < g.lines.Length; i++)
                {
                    var l = g.lines[i];
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        int f = Array.IndexOf(Faces, l.face);
                        int pick = EditorGUILayout.Popup(f < 0 ? 0 : f, Faces, GUILayout.Width(70));
                        l.face = Faces[pick];
                        l.text = EditorGUILayout.TextField(l.text);
                        if (GUILayout.Button("▲", EditorStyles.miniButtonLeft, GUILayout.Width(22)) && i > 0) { g.lines = Swap(g.lines, i, i - 1); break; }
                        if (GUILayout.Button("✕", EditorStyles.miniButtonRight, GUILayout.Width(22))) { g.lines = RemoveAt(g.lines, i); break; }
                    }
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("＋ 一句", EditorStyles.miniButton, GUILayout.Width(60))) g.lines = Insert(g.lines, g.lines.Length, new Line { face = "Smile", text = "" });
                    if (g.lines.Length == 0 && GUILayout.Button("删除这组", EditorStyles.miniButton, GUILayout.Width(70))) a.groups = a.groups.Where(x => x != g).ToArray();
                }
            }
        }

        // ---- props ---------------------------------------------------------------------------------------------

        void PropsTab()
        {
            var kinds = Enum.GetNames(typeof(RepairPropKind));
            selectedProp = GUILayout.Toolbar(selectedProp, new[] { "信号接收器", "记忆卡匣", "维修电源", "赛博御守" }.Take(kinds.Length).ToArray());
            string kind = kinds[Mathf.Clamp(selectedProp, 0, kinds.Length - 1)];
            var p = data.props.props.FirstOrDefault(x => x.kind == kind);
            if (p == null)
            {
                if (GUILayout.Button("＋ 为 " + kind + " 添加文本"))
                    data.props.props = Insert(data.props.props, data.props.props.Length, new Prop { kind = kind, backstory = "", findings = new Finding[0], testNotes = new TestNote[0] });
                return;
            }
            p.findings ??= new Finding[0]; p.testNotes ??= new TestNote[0];
            mainScroll = EditorGUILayout.BeginScrollView(mainScroll);
            EditorGUILayout.LabelField("背景故事", header);
            p.backstory = EditorGUILayout.TextArea(p.backstory ?? "", area, GUILayout.MinHeight(48));

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("部件痕迹（检查这个部件时显示；_Connected、_Released 这类变体共用同一条）", header);
            for (int i = 0; i < p.findings.Length; i++)
            {
                var f = p.findings[i];
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        f.node = EditorGUILayout.TextField(new GUIContent("部件节点名", "模型里的物体名，例如 MC_DataCore"), f.node);
                        if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(24))) { p.findings = RemoveAt(p.findings, i); break; }
                    }
                    f.text = EditorGUILayout.TextArea(f.text ?? "", area);
                }
            }
            if (GUILayout.Button("＋ 部件痕迹", GUILayout.Width(100))) p.findings = Insert(p.findings, p.findings.Length, new Finding { node = "", text = "" });

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("测试附注（测试结果后面加的一句）", header);
            for (int i = 0; i < p.testNotes.Length; i++)
            {
                var t = p.testNotes[i];
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        t.test = EditorGUILayout.TextField(new GUIContent("测试 id", "例如 signal、read、load、identity"), t.test);
                        if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(24))) { p.testNotes = RemoveAt(p.testNotes, i); break; }
                    }
                    EditorGUILayout.LabelField("通过时", small);
                    t.pass = EditorGUILayout.TextArea(t.pass ?? "", area);
                    EditorGUILayout.LabelField("没通过时（可以空着）", small);
                    t.fail = EditorGUILayout.TextArea(t.fail ?? "", area);
                }
            }
            if (GUILayout.Button("＋ 测试附注", GUILayout.Width(100))) p.testNotes = Insert(p.testNotes, p.testNotes.Length, new TestNote { test = "", pass = "", fail = "" });
            EditorGUILayout.EndScrollView();
        }

        // ---- preview -------------------------------------------------------------------------------------------

        sealed class PreviewWorld : IDialogueWorld
        {
            public readonly HashSet<string> facts = new HashSet<string>();
            public float cash = 500, delivered = 0, time = 9999;
            public bool Has(string kind, string arg) => facts.Contains(kind + ":" + arg);
            public bool TryNumber(string name, out float value)
            {
                value = name == "cash" ? cash : name == "delivered" ? delivered : name == "time" ? time : 0;
                return name == "cash" || name == "delivered" || name == "time";
            }
        }

        void PreviewTab()
        {
            var calls = data.calls.calls;
            if (calls.Length == 0) { EditorGUILayout.HelpBox("还没有来电。", MessageType.Info); return; }
            if (previewCall < 0 || previewCall >= calls.Length) previewCall = Mathf.Clamp(selectedCall, 0, calls.Length - 1);
            using (new EditorGUILayout.HorizontalScope())
            {
                int pick = EditorGUILayout.Popup("来电", previewCall, calls.Select(c => c.id + "  ·  " + c.name).ToArray());
                if (pick != previewCall) { previewCall = pick; previewRunner = null; }
                if (GUILayout.Button("从头开始", GUILayout.Width(80))) previewRunner = null;
            }
            var call = calls[previewCall];

            // The facts this call's conditions ask about, as switches.
            var facts = new SortedSet<string>();
            void Collect(string cond) { foreach (var (k, a) in DialogueCondition.Parse(cond).Facts) facts.Add(k + ":" + a); }
            foreach (var l in call.lines)
            {
                Collect(l.when);
                foreach (var c in l.choices ?? new Choice[0]) Collect(c.when);
            }
            EditorGUILayout.LabelField("假设的游戏状态（勾选 = 成立；对话里设置的标记会自动勾上）", small);
            using (new EditorGUILayout.HorizontalScope())
            {
                previewWorld.cash = EditorGUILayout.FloatField("现金", previewWorld.cash);
                previewWorld.delivered = EditorGUILayout.FloatField("已交付", previewWorld.delivered);
            }
            foreach (var f in facts.Union(previewWorld.facts.Where(x => x.StartsWith("flag:"))).Distinct().ToArray())
            {
                bool on = previewWorld.facts.Contains(f);
                bool now = EditorGUILayout.ToggleLeft(f, on);
                if (now != on) { if (now) previewWorld.facts.Add(f); else previewWorld.facts.Remove(f); }
            }

            if (previewRunner == null || previewRunner.Call != call)
            {
                transcript.Clear();
                previewRunner = new DialogueRunner(call, previewWorld, (flag, on) => { if (on) previewWorld.facts.Add("flag:" + flag); else previewWorld.facts.Remove("flag:" + flag); });
                PreviewNext();
            }

            EditorGUILayout.Space(6);
            previewScroll = EditorGUILayout.BeginScrollView(previewScroll, EditorStyles.helpBox, GUILayout.ExpandHeight(true));
            foreach (var (player, who, text) in transcript)
                EditorGUILayout.LabelField(who + "：" + text, player ? warnStyle : EditorStyles.wordWrappedLabel);
            if (previewRunner.Ended) EditorGUILayout.LabelField("—— 挂断 ——", EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.EndScrollView();

            if (previewRunner.AwaitingChoice)
            {
                for (int i = 0; i < previewRunner.Choices.Count; i++)
                    if (GUILayout.Button((i + 1) + "  " + previewRunner.Choices[i].text))
                    {
                        var c = previewRunner.Choose(i);
                        if (!string.IsNullOrEmpty(c.text)) transcript.Add((true, "你", c.text));
                        PreviewNext();
                        GUIUtility.ExitGUI();
                    }
            }
            else if (!previewRunner.Ended && GUILayout.Button("下一句")) PreviewNext();
        }

        void PreviewNext()
        {
            if (!previewRunner.Advance()) return;
            var l = previewRunner.Line;
            if (!string.IsNullOrEmpty(l.text)) transcript.Add((l.player, l.player ? "你" : previewRunner.Call.name, l.text));
            previewScroll.y = float.MaxValue;
        }

        // ---- issues --------------------------------------------------------------------------------------------

        void IssuesTab()
        {
            mainScroll = EditorGUILayout.BeginScrollView(mainScroll);
            if (issues.Count == 0) EditorGUILayout.HelpBox("没有发现问题。", MessageType.Info);
            foreach (var x in issues.OrderBy(x => x.Error ? 0 : 1)) EditorGUILayout.LabelField(x.ToString(), x.Error ? errorStyle : warnStyle);
            EditorGUILayout.EndScrollView();
        }

        // ---- array helpers -------------------------------------------------------------------------------------

        static T[] Insert<T>(T[] a, int i, T v) { var l = new List<T>(a ?? new T[0]); l.Insert(Mathf.Clamp(i, 0, l.Count), v); return l.ToArray(); }
        static T[] RemoveAt<T>(T[] a, int i) { var l = new List<T>(a); l.RemoveAt(i); return l.ToArray(); }
        static T[] Swap<T>(T[] a, int i, int j) { var c = (T[])a.Clone(); (c[i], c[j]) = (c[j], c[i]); return c; }
    }
}
