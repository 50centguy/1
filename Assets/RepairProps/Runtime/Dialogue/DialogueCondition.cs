using System;
using System.Collections.Generic;
using System.Globalization;

namespace BorderRepair.RepairProps.Dialogue
{
    /// <summary>What a condition can ask about the game. The call runtime answers from the live night; the editor's preview from toggles.</summary>
    public interface IDialogueWorld
    {
        /// <summary>A "kind:arg" fact, e.g. flag:kept-line, played:rent-n1, delivered:receiver, phase:Night1Counter.</summary>
        bool Has(string kind, string arg);
        /// <summary>A number by name: time (seconds since the scene opened), cash, delivered (orders handed back).</summary>
        bool TryNumber(string name, out float value);
    }

    /// <summary>
    /// A condition written by hand in the dialogue files. Grammar: expr = and ('|' and)*; and = not ('&amp;' not)*;
    /// not = '!' not | '(' expr ')' | atom. Atoms: kind:arg (flag, played, delivered, phase), name op number
    /// (time, cash, delivered with &gt; &gt;= &lt; &lt;= == !=), true, false. Empty text is always true.
    /// </summary>
    public sealed class DialogueCondition
    {
        public static readonly string[] Kinds = { "flag", "played", "delivered", "phase" };
        public static readonly string[] Numbers = { "time", "cash", "delivered" };

        abstract class Node { public abstract bool Eval(IDialogueWorld w); }
        sealed class Const : Node { public bool v; public override bool Eval(IDialogueWorld w) => v; }
        sealed class Not : Node { public Node a; public override bool Eval(IDialogueWorld w) => !a.Eval(w); }
        sealed class And : Node { public Node a, b; public override bool Eval(IDialogueWorld w) => a.Eval(w) && b.Eval(w); }
        sealed class Or : Node { public Node a, b; public override bool Eval(IDialogueWorld w) => a.Eval(w) || b.Eval(w); }
        sealed class Fact : Node { public string kind, arg; public override bool Eval(IDialogueWorld w) => w != null && w.Has(kind, arg); }
        sealed class Compare : Node
        {
            public string name, op; public float value;
            public override bool Eval(IDialogueWorld w)
            {
                if (w == null || !w.TryNumber(name, out float x)) return false;
                switch (op)
                {
                    case ">": return x > value;
                    case ">=": return x >= value;
                    case "<": return x < value;
                    case "<=": return x <= value;
                    case "==": return Math.Abs(x - value) < 1e-4f;
                    default: return Math.Abs(x - value) >= 1e-4f;   // !=
                }
            }
        }

        readonly Node root;
        public string Source { get; }
        public string Error { get; }
        public bool Valid => Error == null;
        /// <summary>Every kind:arg fact the condition mentions, for the editor's preview toggles.</summary>
        public IReadOnlyList<(string kind, string arg)> Facts { get; }

        DialogueCondition(string source, Node root, string error, List<(string, string)> facts) { Source = source; this.root = root; Error = error; Facts = facts; }

        static readonly Dictionary<string, DialogueCondition> cache = new Dictionary<string, DialogueCondition>();

        /// <summary>Parses (cached). Never throws: a bad condition has an Error and evaluates false.</summary>
        public static DialogueCondition Parse(string text)
        {
            text = text ?? "";
            lock (cache)
            {
                if (cache.TryGetValue(text, out var hit)) return hit;
                var facts = new List<(string, string)>();
                Node node = null; string error = null;
                if (string.IsNullOrWhiteSpace(text)) node = new Const { v = true };
                else
                {
                    try
                    {
                        var p = new Parser(text, facts);
                        node = p.Expr();
                        p.SkipSpace();
                        if (!p.AtEnd) throw new FormatException("多余的内容：" + text.Substring(p.Pos));
                    }
                    catch (FormatException e) { error = e.Message; node = new Const { v = false }; }
                }
                var made = new DialogueCondition(text, node, error, facts);
                if (cache.Count > 512) cache.Clear();
                cache[text] = made;
                return made;
            }
        }

        public static bool Evaluate(string text, IDialogueWorld world) => Parse(text).Evaluate(world);
        public bool Evaluate(IDialogueWorld world) => root.Eval(world);

        sealed class Parser
        {
            readonly string s; readonly List<(string, string)> facts;
            public int Pos;
            public Parser(string s, List<(string, string)> facts) { this.s = s; this.facts = facts; }
            public bool AtEnd => Pos >= s.Length;
            public void SkipSpace() { while (Pos < s.Length && char.IsWhiteSpace(s[Pos])) Pos++; }
            bool Eat(char c) { SkipSpace(); if (Pos < s.Length && s[Pos] == c) { Pos++; return true; } return false; }

            public Node Expr()
            {
                var a = AndExpr();
                while (Eat('|')) a = new Or { a = a, b = AndExpr() };
                return a;
            }

            Node AndExpr()
            {
                var a = NotExpr();
                while (Eat('&')) a = new And { a = a, b = NotExpr() };
                return a;
            }

            Node NotExpr()
            {
                if (Eat('!')) return new Not { a = NotExpr() };
                if (Eat('('))
                {
                    var e = Expr();
                    if (!Eat(')')) throw new FormatException("缺少右括号 )");
                    return e;
                }
                return Atom();
            }

            static bool WordChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.';

            Node Atom()
            {
                SkipSpace();
                int start = Pos;
                while (Pos < s.Length && WordChar(s[Pos])) Pos++;
                string word = s.Substring(start, Pos - start);
                if (word.Length == 0) throw new FormatException(AtEnd ? "条件写到一半就结束了" : "看不懂的符号：" + s[Pos]);
                if (Pos < s.Length && s[Pos] == ':')
                {
                    Pos++;
                    int a = Pos;
                    while (Pos < s.Length && WordChar(s[Pos])) Pos++;
                    string arg = s.Substring(a, Pos - a);
                    if (arg.Length == 0) throw new FormatException(word + ": 后面缺少内容");
                    if (Array.IndexOf(Kinds, word) < 0) throw new FormatException("不认识的条件类型：" + word + "（可用 " + string.Join("、", Kinds) + "）");
                    facts.Add((word, arg));
                    return new Fact { kind = word, arg = arg };
                }
                SkipSpace();
                string op = null;
                foreach (var o in new[] { ">=", "<=", "==", "!=", ">", "<" })
                    if (string.CompareOrdinal(s, Pos, o, 0, o.Length) == 0) { op = o; break; }
                if (op == null)
                {
                    if (word == "true") return new Const { v = true };
                    if (word == "false") return new Const { v = false };
                    throw new FormatException("不认识的条件：" + word + "（标记要写成 flag:" + word + "）");
                }
                if (Array.IndexOf(Numbers, word) < 0) throw new FormatException("不认识的数值：" + word + "（可用 " + string.Join("、", Numbers) + "）");
                Pos += op.Length;
                SkipSpace();
                int n = Pos;
                while (Pos < s.Length && (char.IsDigit(s[Pos]) || s[Pos] == '.' || (Pos == n && s[Pos] == '-'))) Pos++;
                if (!float.TryParse(s.Substring(n, Pos - n), NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                    throw new FormatException(word + " " + op + " 后面要跟数字");
                return new Compare { name = word, op = op, value = value };
            }
        }

        /// <summary>"a, b !c" → sets a and b, clears c. Separators: comma, space, 、 or ，.</summary>
        public static IEnumerable<(string flag, bool on)> ParseSet(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) yield break;
            foreach (var raw in text.Split(new[] { ',', ' ', '、', '，', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = raw.Trim();
                bool on = !t.StartsWith("!");
                if (!on) t = t.Substring(1);
                if (t.StartsWith("flag:")) t = t.Substring(5);
                if (t.Length > 0) yield return (t, on);
            }
        }
    }
}
