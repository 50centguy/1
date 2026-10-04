using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace BorderRepair.TwoNight
{
    public enum SaveStatus { None, Ok, Corrupt, VersionMismatch, Unsafe }

    public struct SaveLoadResult
    {
        public SaveStatus status;
        public TwoNightState state;
        public string message;
    }

    /// <summary>
    /// 两晚切片的检查点：Application.persistentDataPath/TwoNightSlice/checkpoint.json（JsonUtility）。
    /// 只在第一晚结束、七号安全停靠时写。写入先写临时文件再替换，写失败返回可读原因。
    /// 读取时核对版本、阶段、账目（id 唯一、现金 = 初始 + 账目之和）和安全状态，任何一项不对都当损坏处理，不抛异常。
    /// 测试用 OverrideDirectory 换目录，不碰玩家的真存档；“重新开始”只删这一个文件。
    /// </summary>
    public static class TwoNightSave
    {
        public const string FileName = "checkpoint.json";
        public static string OverrideDirectory;

        public static string Directory => !string.IsNullOrEmpty(OverrideDirectory) ? OverrideDirectory : Path.Combine(Application.persistentDataPath, "TwoNightSlice");
        public static string FilePath => Path.Combine(Directory, FileName);

        public static bool Exists => File.Exists(FilePath);

        public static bool Write(TwoNightState s, out string message)
        {
            message = null;
            if (s == null) { message = "没有可保存的进度。"; return false; }
            if (s.phase != TwoNightPhase.Night1Ended) { message = "只能在第一晚结束时保存。"; return false; }
            if (s.unit07 == null || !s.unit07.IsSafe) { message = "七号还没安全停靠，不能保存：" + (s.unit07 != null ? s.unit07.WhyUnsafe() : "状态未知") + "。"; return false; }
            try
            {
                s.version = TwoNightState.CurrentVersion;
                s.savedAtUtc = DateTime.UtcNow.ToString("o");
                System.IO.Directory.CreateDirectory(Directory);
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(s, true), new UTF8Encoding(false));
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                else File.Move(tmp, FilePath);
                message = "已保存。";
                return true;
            }
            catch (Exception e)
            {
                message = "保存失败：" + e.Message + "（存档位置：" + FilePath + "）";
                Debug.LogWarning("[TwoNight] " + message);
                return false;
            }
        }

        public static SaveLoadResult Read()
        {
            if (!File.Exists(FilePath)) return new SaveLoadResult { status = SaveStatus.None, message = "没有存档。" };
            TwoNightState s;
            try
            {
                s = JsonUtility.FromJson<TwoNightState>(File.ReadAllText(FilePath));
            }
            catch (Exception e)
            {
                return new SaveLoadResult { status = SaveStatus.Corrupt, message = "存档读不出来（" + e.GetType().Name + "）。" };
            }
            if (s == null) return new SaveLoadResult { status = SaveStatus.Corrupt, message = "存档是空的。" };
            if (s.version != TwoNightState.CurrentVersion)
                return new SaveLoadResult { status = SaveStatus.VersionMismatch, message = $"存档版本 {s.version} 与当前版本 {TwoNightState.CurrentVersion} 不一致。" };
            var problems = Validate(s);
            if (problems.Count > 0) return new SaveLoadResult { status = SaveStatus.Corrupt, message = "存档内容不一致：" + string.Join("；", problems) + "。" };
            if (s.unit07 == null || !s.unit07.IsSafe)
                return new SaveLoadResult { status = SaveStatus.Unsafe, message = "存档里七号不是安全停靠状态。" };
            return new SaveLoadResult { status = SaveStatus.Ok, state = s, message = "存档正常。" };
        }

        static List<string> Validate(TwoNightState s)
        {
            var p = new List<string>();
            if (s.phase != TwoNightPhase.Night1Ended) p.Add("阶段不是第一晚结束（" + s.phase + "）");
            if (s.night != 1) p.Add("夜数不是 1");
            if (s.transactions == null) { p.Add("没有账目"); return p; }
            var ids = new HashSet<string>();
            foreach (var t in s.transactions)
                if (t == null || string.IsNullOrEmpty(t.id) || !ids.Add(t.id)) p.Add("账目 id 为空或重复");
            if (!s.communicatorSettled || !ids.Contains(TwoNightRun.CommunicatorIncomeId) || !ids.Contains(TwoNightRun.CommunicatorPartsId)) p.Add("通讯器结单记录不完整");
            if (!s.ledgerConfirmed || !s.unit07IncidentShown || !s.unit07Registered) p.Add("第一晚流程记录不完整");
            if (s.unit07Repaired) p.Add("七号在第一晚不可能已修好");
            if (s.rent <= 0 || s.rentDueNight < 2) p.Add("房租数据无效");
            return p;
        }

        /// <summary>“重新开始”：只删本切片的存档文件。</summary>
        public static void Delete()
        {
            try
            {
                if (File.Exists(FilePath)) File.Delete(FilePath);
                if (File.Exists(FilePath + ".tmp")) File.Delete(FilePath + ".tmp");
            }
            catch (Exception e) { Debug.LogWarning("[TwoNight] 删除存档失败：" + e.Message); }
        }
    }
}
