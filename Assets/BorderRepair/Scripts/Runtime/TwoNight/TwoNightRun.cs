namespace BorderRepair.TwoNight
{
    /// <summary>
    /// 两晚切片的进度规则（纯 C#，不挂在场景上，没有事件订阅）。所有转换都检查当前阶段，越序调用返回 false 且不改任何状态。
    /// 账目用稳定 id 只记一次：重复结单、重复确认、读档、进出场景都不会再入账。
    /// </summary>
    public static class TwoNightRun
    {
        public const string CommunicatorIncomeId = "N1-COMM-INCOME";
        public const string CommunicatorPartsId = "N1-COMM-PARTS";
        public const string Unit07WorkOrderId = "WO-U07-N1";

        /// <summary>当前进程里的进度（场景之间传递用；新进程从存档恢复）。</summary>
        public static TwoNightState Current { get; private set; }

        public static void Set(TwoNightState state) => Current = state;
        public static void Clear() => Current = null;

        public static TwoNightState NewGame(TwoNightEconomy eco)
        {
            eco = eco != null ? eco : TwoNightEconomy.Defaults();
            Current = new TwoNightState
            {
                night = 1,
                phase = TwoNightPhase.Night1Counter,
                startingCash = eco.startingCash,
                rent = eco.rent,
                rentDueNight = eco.rentDueNight,
            };
            return Current;
        }

        /// <summary>
        /// 通讯器结单。只有判定正确的维修决定（correct = true）才记收入和耗材；错误 / 拒绝结果不入账、不推进阶段。
        /// 已经结过的单再调用返回 false，不重复记账。
        /// </summary>
        public static bool SettleCommunicator(TwoNightState s, bool correct, string caseId, TwoNightEconomy eco)
        {
            if (s == null || s.phase != TwoNightPhase.Night1Counter || s.communicatorSettled || !correct) return false;
            eco = eco != null ? eco : TwoNightEconomy.Defaults();
            Post(s, CommunicatorIncomeId, eco.communicatorIncome, "收藏家 · 通讯器维修收入", 1);
            Post(s, CommunicatorPartsId, -eco.communicatorPartsCost, "通讯器 · 耗材与配件", 1);
            s.communicatorSettled = true;
            s.communicatorCaseId = caseId;
            s.phase = TwoNightPhase.Night1Ledger;
            return true;
        }

        /// <summary>记一笔账；同 id 已存在则不记（返回 false）。</summary>
        public static bool Post(TwoNightState s, string id, int amount, string label, int night)
        {
            if (s == null || string.IsNullOrEmpty(id) || s.HasTransaction(id)) return false;
            s.transactions.Add(new TwoNightTransaction { id = id, amount = amount, label = label, night = night });
            return true;
        }

        public static bool ConfirmLedger(TwoNightState s)
        {
            if (s == null || s.phase != TwoNightPhase.Night1Ledger || !s.communicatorSettled) return false;
            s.ledgerConfirmed = true;
            s.phase = TwoNightPhase.Night1Incident;
            return true;
        }

        /// <summary>端盘失衡演出播完（只播一次）。</summary>
        public static bool FinishIncident(TwoNightState s)
        {
            if (s == null || s.phase != TwoNightPhase.Night1Incident || !s.ledgerConfirmed) return false;
            s.unit07IncidentShown = true;
            s.phase = TwoNightPhase.Night1Docking;
            return true;
        }

        /// <summary>登记七号内部维修单：只在安全停靠（落座、夹紧、断电、停稳、托盘归位、机身回正）时允许。</summary>
        public static bool RegisterUnit07(TwoNightState s, Unit07SafeState safe, out string why)
        {
            why = null;
            if (s == null || s.phase != TwoNightPhase.Night1Docking) { why = "现在不能登记。"; return false; }
            if (safe == null || !safe.IsSafe) { why = "还不安全：" + (safe != null ? safe.WhyUnsafe() : "状态未知") + "。"; return false; }
            s.unit07 = safe;
            s.unit07Registered = true;
            s.unit07WorkOrderId = Unit07WorkOrderId;
            s.unit07Repaired = false;
            s.phase = TwoNightPhase.Night1Ended;
            return true;
        }

        /// <summary>从第一晚结束进入第二晚开场（只改内存里的阶段，不入账）。</summary>
        public static bool BeginNight2(TwoNightState s)
        {
            if (s == null || s.phase != TwoNightPhase.Night1Ended) return false;
            s.night = 2;
            s.phase = TwoNightPhase.Night2Open;
            return true;
        }
    }
}
