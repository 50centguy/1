using System;
using System.Collections.Generic;
using System.Linq;
using BorderRepair.TwoNight;
using static BorderRepair.RepairProps.RepairPropStoryText;

namespace BorderRepair.RepairProps.Dialogue
{
    /// <summary>Checks the dialogue files for mistakes the game would only show by going quiet: bad jumps, bad conditions, missing keys.</summary>
    public static class DialogueValidator
    {
        public readonly struct Issue
        {
            public readonly bool Error;
            public readonly string Where, Message;
            public Issue(bool error, string where, string message) { Error = error; Where = where; Message = message; }
            public override string ToString() => (Error ? "错误" : "提醒") + "  " + Where + "：" + Message;
        }

        /// <summary>The moments UNIT 07 speaks; the code looks these keys up (after-call groups are per call id).</summary>
        public static readonly (string key, string what)[] AssistantGroups =
        {
            ("greet", "第一次上线（开店后几秒，还没交付任何单子）"),
            ("first-item", "第一件物品放上维修台"),
            ("purchase-received", "收到采购单，去搬货"),
            ("parts-delivered", "配件放到维修台"),
            ("retest-failed", "复测没通过"),
            ("all-done", "今晚的单子都交付了"),
        };

        public static readonly (string key, string what)[] AssistantHints =
        {
            ("hint-busy", "正在搬货时被点"),
            ("hint-carrying", "玩家手上拿着物品"),
            ("hint-pending-case", "柜台有新单子"),
            ("hint-in-repair", "维修台上的物品还没修完"),
            ("hint-ready-for-delivery", "修好了，等交付"),
        };

        public const string AfterCallPrefix = "after-call:";
        public const int MaxChoices = 4;
        static readonly string[] PropKinds = Enum.GetNames(typeof(RepairPropKind));
        static readonly string[] PhaseNames = Enum.GetNames(typeof(TwoNightPhase));
        static readonly string[] DeliverKinds = PropKinds.Select(k => k.ToLowerInvariant()).ToArray();

        public static List<Issue> Check(CallFile calls, AssistantFile assistant, PropFile props, IEnumerable<string> callers = null)
        {
            var issues = new List<Issue>();
            var callList = calls?.calls ?? Array.Empty<Call>();
            var callIds = new HashSet<string>(callList.Where(c => c != null && !string.IsNullOrEmpty(c.id)).Select(c => c.id));
            var callerSet = callers != null ? new HashSet<string>(callers) : null;
            var setFlags = new HashSet<string>();
            var readFlags = new List<(string where, string flag)>();

            void Cond(string where, string text, bool required = false)
            {
                if (string.IsNullOrWhiteSpace(text)) { if (required) issues.Add(new Issue(false, where, "没有触发条件，这通来电不会自己打进来")); return; }
                var c = DialogueCondition.Parse(text);
                if (!c.Valid) { issues.Add(new Issue(true, where, "条件写错了：" + c.Error)); return; }
                foreach (var (kind, arg) in c.Facts)
                {
                    if (kind == "flag") readFlags.Add((where, arg));
                    else if (kind == "played" && !callIds.Contains(arg)) issues.Add(new Issue(false, where, "played:" + arg + " 指向不存在的来电"));
                    else if (kind == "phase" && Array.IndexOf(PhaseNames, arg) < 0) issues.Add(new Issue(true, where, "phase:" + arg + " 不是有效阶段（可用 " + string.Join("、", PhaseNames.Skip(1)) + "）"));
                    else if (kind == "delivered" && Array.IndexOf(DeliverKinds, arg) < 0) issues.Add(new Issue(false, where, "delivered:" + arg + " 不是今晚的物品（可用 " + string.Join("、", DeliverKinds) + "）"));
                }
            }
            void Set(string text) { foreach (var (flag, _) in DialogueCondition.ParseSet(text)) setFlags.Add(flag); }

            var seenCalls = new HashSet<string>();
            for (int ci = 0; ci < callList.Length; ci++)
            {
                var call = callList[ci];
                if (call == null) continue;
                string w = "来电 " + (string.IsNullOrEmpty(call.id) ? "#" + (ci + 1) : call.id);
                if (string.IsNullOrWhiteSpace(call.id)) issues.Add(new Issue(true, w, "缺少 id"));
                else if (!seenCalls.Add(call.id)) issues.Add(new Issue(true, w, "id 重复"));
                if (string.IsNullOrWhiteSpace(call.name)) issues.Add(new Issue(false, w, "没有显示名"));
                if (callerSet != null && !callerSet.Contains(call.caller ?? "")) issues.Add(new Issue(false, w, "头像 \"" + call.caller + "\" 不存在，通话时不显示画面"));
                Cond(w + " 触发条件", call.trigger, required: true);
                var lines = call.lines ?? Array.Empty<CallLine>();
                if (lines.Length == 0) issues.Add(new Issue(true, w, "一句台词都没有"));
                var labels = new HashSet<string>();
                foreach (var l in lines)
                    if (l != null && !string.IsNullOrWhiteSpace(l.id) && !labels.Add(l.id.Trim()))
                        issues.Add(new Issue(true, w, "标签 \"" + l.id + "\" 重复"));
                void Jump(string where, string next)
                {
                    if (string.IsNullOrWhiteSpace(next)) return;
                    string t = next.Trim();
                    if (!string.Equals(t, DialogueRunner.End, StringComparison.OrdinalIgnoreCase) && !labels.Contains(t))
                        issues.Add(new Issue(true, where, "跳转目标 \"" + t + "\" 不存在"));
                }
                for (int li = 0; li < lines.Length; li++)
                {
                    var l = lines[li];
                    if (l == null) continue;
                    string lw = w + " 第 " + (li + 1) + " 句";
                    var choices = (l.choices ?? Array.Empty<Choice>()).Where(c => c != null).ToArray();
                    if (string.IsNullOrWhiteSpace(l.text) && choices.Length == 0) issues.Add(new Issue(true, lw, "台词是空的"));
                    Cond(lw + " 条件", l.when);
                    Set(l.set);
                    Jump(lw, l.next);
                    if (choices.Length > MaxChoices) issues.Add(new Issue(true, lw, "选项超过 " + MaxChoices + " 个，多出来的不会显示"));
                    for (int k = 0; k < choices.Length; k++)
                    {
                        var c = choices[k];
                        string cw = lw + " 选项 " + (k + 1);
                        if (string.IsNullOrWhiteSpace(c.text)) issues.Add(new Issue(true, cw, "选项没有文字"));
                        Cond(cw + " 条件", c.when);
                        Set(c.set);
                        Jump(cw, c.next);
                    }
                    if (choices.Length > 0 && choices.All(c => !string.IsNullOrWhiteSpace(c.when)))
                        issues.Add(new Issue(false, lw, "每个选项都有条件；都不满足时这句会当作普通台词直接往下走"));
                }
            }
            foreach (var (where, flag) in readFlags)
                if (!setFlags.Contains(flag)) issues.Add(new Issue(false, where, "flag:" + flag + " 从来没有被设置过"));

            // UNIT 07
            var groups = assistant?.groups ?? Array.Empty<Group>();
            var faces = Enum.GetNames(typeof(ClinicAssistantRuntime.Face));
            var groupKeys = new HashSet<string>();
            foreach (var g in groups)
            {
                if (g == null) continue;
                string gw = "七号 " + g.key;
                if (!groupKeys.Add(g.key ?? "")) issues.Add(new Issue(true, gw, "键重复"));
                bool known = AssistantGroups.Any(k => k.key == g.key) || (g.key != null && g.key.StartsWith(AfterCallPrefix));
                if (!known) issues.Add(new Issue(false, gw, "游戏里不会用到这个键"));
                if (g.key != null && g.key.StartsWith(AfterCallPrefix) && !callIds.Contains(g.key.Substring(AfterCallPrefix.Length)))
                    issues.Add(new Issue(false, gw, "对应的来电不存在"));
                foreach (var l in g.lines ?? Array.Empty<Line>())
                {
                    if (l == null) continue;
                    if (Array.IndexOf(faces, l.face) < 0) issues.Add(new Issue(true, gw, "表情 \"" + l.face + "\" 不存在"));
                    if (string.IsNullOrWhiteSpace(l.text)) issues.Add(new Issue(true, gw, "有一句是空的"));
                }
            }
            foreach (var (key, what) in AssistantGroups)
                if (!groupKeys.Contains(key)) issues.Add(new Issue(false, "七号 " + key, "缺少这组台词（" + what + "时不说话）"));
            var hints = assistant?.hints ?? Array.Empty<Hint>();
            foreach (var (key, what) in AssistantHints)
                if (!hints.Any(h => h != null && h.key == key && !string.IsNullOrWhiteSpace(h.text))) issues.Add(new Issue(true, "七号提示 " + key, "缺少提示（" + what + "）"));
            foreach (var b in assistant?.banterAfterCall ?? Array.Empty<CallBanter>())
                if (b != null && !callIds.Contains(b.afterCall ?? "")) issues.Add(new Issue(false, "七号闲聊", "afterCall \"" + b.afterCall + "\" 指向不存在的来电"));

            // Props
            var propList = props?.props ?? Array.Empty<Prop>();
            foreach (var kind in PropKinds)
            {
                var p = propList.FirstOrDefault(x => x != null && x.kind == kind);
                if (p == null) { issues.Add(new Issue(true, "物品 " + kind, "缺少这件物品的文本")); continue; }
                if (string.IsNullOrWhiteSpace(p.backstory)) issues.Add(new Issue(false, "物品 " + kind, "没有背景故事"));
                foreach (var f in p.findings ?? Array.Empty<Finding>())
                    if (f != null && (string.IsNullOrWhiteSpace(f.node) || string.IsNullOrWhiteSpace(f.text))) issues.Add(new Issue(true, "物品 " + kind, "有一条部件痕迹缺少部件名或文字"));
            }
            return issues;
        }
    }
}
