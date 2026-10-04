using System;
using System.Collections.Generic;
using UnityEngine;

namespace BorderRepair.TwoNight
{
    /// <summary>两晚切片的阶段。只管夜、案件、结账和转换；七号的维修步骤仍由 FirstOrderFlow 管，供电 / 夹具 / 转子仍由维修座管。</summary>
    public enum TwoNightPhase
    {
        None = 0,
        Night1Counter = 1,     // 第一晚：收藏家的通讯器
        Night1Ledger = 2,      // 通讯器有效结单，账本待确认
        Night1Incident = 3,    // 账本已确认：七号端盘失衡演出
        Night1Docking = 4,     // 演出结束：玩家停靠、夹紧、断电、等停稳、登记
        Night1Ended = 5,       // 已登记并存档：第一晚结束（唯一的存档点）
        Night2Open = 6,        // 第二晚开场：可以开始检查七号
    }

    /// <summary>一笔账。id 稳定且唯一：同一笔账只能记一次。金额是整数信用点，正 = 收入，负 = 支出。</summary>
    [Serializable]
    public class TwoNightTransaction
    {
        public string id;
        public int amount;
        public string label;
        public int night;
    }

    /// <summary>七号停在维修座上的安全状态（存档只存这些结论，不存协程、点击对象或搬运中的零件）。</summary>
    [Serializable]
    public class Unit07SafeState
    {
        public bool seated;
        public bool clamped;
        public bool powerOff;
        public bool rotorsStopped;
        public bool trayStowed;
        public bool robotUpright;

        public bool IsSafe => seated && clamped && powerOff && rotorsStopped && trayStowed && robotUpright;

        public string WhyUnsafe()
        {
            var l = new List<string>();
            if (!seated) l.Add("七号还没落座");
            if (!clamped) l.Add("夹具没夹紧");
            if (!powerOff) l.Add("还没断电");
            if (!rotorsStopped) l.Add("叶轮还没停稳");
            if (!trayStowed) l.Add("零件盘没放回托盘架");
            if (!robotUpright) l.Add("七号机身没回正");
            return string.Join("、", l);
        }
    }

    /// <summary>两晚切片的全部进度（也是存档内容，JsonUtility 序列化）。</summary>
    [Serializable]
    public class TwoNightState
    {
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        public int night;
        public TwoNightPhase phase;
        public int startingCash;
        public int rent;
        public int rentDueNight;
        public List<TwoNightTransaction> transactions = new List<TwoNightTransaction>();

        public bool communicatorSettled;
        public string communicatorCaseId;
        public bool ledgerConfirmed;

        public bool unit07IncidentShown;
        public bool unit07Registered;
        public string unit07WorkOrderId;
        public bool unit07Repaired;
        public Unit07SafeState unit07 = new Unit07SafeState();

        public string savedAtUtc;

        public int Cash
        {
            get
            {
                int c = startingCash;
                foreach (var t in transactions) c += t.amount;
                return c;
            }
        }

        public int RentShortfall => Mathf.Max(0, rent - Cash);
        public bool HasTransaction(string id) => transactions.Exists(t => t.id == id);
    }
}
