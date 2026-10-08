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

        public static TwoNightState NewGame(TwoNightEconomy eco, bool unifiedClinic = false)
        {
            eco = eco != null ? eco : TwoNightEconomy.Defaults();
            Current = new TwoNightState
            {
                night = 1,
                phase = TwoNightPhase.Night1Counter,
                unifiedClinic = unifiedClinic,
                communicatorIncome = eco.communicatorIncome,
                communicatorPartsCost = eco.communicatorPartsCost,
                communicatorAmountsRecorded = true,
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
            if (s == null || s.unifiedClinic) return false;
            return Settle(s, correct, caseId, eco);
        }

        public static bool ReceiveCommunicator(TwoNightState s, string caseId)
        {
            return s != null && ReceiveCustomer(s, caseId, 0, s.communicatorIncome, s.communicatorPartsCost);
        }

        public static bool CompleteCommunicatorRepair(TwoNightState s, bool correct, string caseId)
        {
            return s != null && s.ActiveTrade != null && s.ActiveTrade.queueIndex == 0 && CompleteCustomerRepair(s, correct, caseId);
        }

        public static bool DeliverCommunicator(TwoNightState s, string caseId, TwoNightEconomy eco)
        {
            return s != null && s.ActiveTrade != null && s.ActiveTrade.queueIndex == 0 && DeliverCustomer(s, caseId);
        }

        public static bool ReceiveCustomer(TwoNightState s, string caseId, int index, int income, int partsCost)
        {
            if (s == null || !s.unifiedClinic || s.phase != TwoNightPhase.Night1Counter ||
                string.IsNullOrEmpty(caseId) || income < 0 || partsCost < 0 || s.customerTrades == null ||
                index != s.customerTrades.Count || index >= s.customerQueueCount ||
                s.customerTrades.Exists(t => t.caseId == caseId) ||
                (s.ActiveTrade != null && s.ActiveTrade.state != ClinicTradeState.Delivered)) return false;
            s.customerTrades.Add(new ClinicCustomerTrade { caseId = caseId, queueIndex = index,
                income = income, partsCost = partsCost, state = ClinicTradeState.InRepair });
            if (index == 0) { s.communicatorCaseId = caseId; s.communicatorTrade = ClinicTradeState.InRepair; }
            return true;
        }

        public static bool CompleteCustomerRepair(TwoNightState s, bool correct, string caseId)
            => CompleteCustomerDecision(s, correct, caseId, BorderRepair.Data.RepairDecision.Repair);

        public static bool CompleteCustomerDecision(TwoNightState s, bool correct, string caseId, BorderRepair.Data.RepairDecision decision)
        {
            if (s == null || !s.unifiedClinic || s.phase != TwoNightPhase.Night1Counter || !correct ||
                s.ActiveTrade == null || s.ActiveTrade.caseId != caseId || s.ActiveTrade.state != ClinicTradeState.InRepair ||
                (decision != BorderRepair.Data.RepairDecision.Repair && decision != BorderRepair.Data.RepairDecision.Refuse &&
                 decision != BorderRepair.Data.RepairDecision.RecommendReplacement)) return false;
            var trade = s.ActiveTrade;
            trade.decision = decision;
            trade.returnedUnpaid = decision != BorderRepair.Data.RepairDecision.Repair;
            if (trade.returnedUnpaid) { trade.income = 0; trade.partsCost = 0; }
            trade.state = trade.returnedUnpaid ? ClinicTradeState.ReadyForReturn : ClinicTradeState.ReadyForDelivery;
            if (trade.queueIndex == 0) s.communicatorTrade = trade.state;
            return true;
        }

        public static bool DeliverCustomer(TwoNightState s, string caseId)
        {
            if (s == null || !s.unifiedClinic || s.phase != TwoNightPhase.Night1Counter || s.ActiveTrade == null ||
                s.ActiveTrade.caseId != caseId ||
                (s.ActiveTrade.state != ClinicTradeState.ReadyForDelivery && s.ActiveTrade.state != ClinicTradeState.ReadyForReturn)) return false;
            var trade = s.ActiveTrade;
            if (s.HasTransaction(trade.IncomeId) || s.HasTransaction(trade.PartsId)) return false;
            if (!trade.returnedUnpaid)
            {
                Post(s, trade.IncomeId, trade.income, caseId + " · 维修收入", 1);
                Post(s, trade.PartsId, -trade.partsCost, caseId + " · 耗材与配件", 1);
            }
            trade.state = ClinicTradeState.Delivered;
            if (trade.queueIndex == 0)
            {
                s.communicatorSettled = true;
                s.communicatorTrade = ClinicTradeState.Delivered;
                s.communicatorIncome = trade.income;
                s.communicatorPartsCost = trade.partsCost;
                s.communicatorAmountsRecorded = true;
            }
            if (s.customerTrades.Count == s.customerQueueCount) s.phase = TwoNightPhase.Night1Ledger;
            return true;
        }

        static bool Settle(TwoNightState s, bool correct, string caseId, TwoNightEconomy eco)
        {
            if (s == null || s.phase != TwoNightPhase.Night1Counter || s.communicatorSettled || !correct) return false;
            if (s.HasTransaction(CommunicatorIncomeId) || s.HasTransaction(CommunicatorPartsId)) return false;
            eco = eco != null ? eco : TwoNightEconomy.Defaults();
            s.communicatorIncome = eco.communicatorIncome;
            s.communicatorPartsCost = eco.communicatorPartsCost;
            s.communicatorAmountsRecorded = true;
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
            if (s == null || s.phase != TwoNightPhase.Night1Ended || !s.unit07Registered ||
                s.unit07 == null || !s.unit07.IsSafe) return false;
            s.night = 2;
            s.phase = TwoNightPhase.Night2Open;
            return true;
        }
    }
}
