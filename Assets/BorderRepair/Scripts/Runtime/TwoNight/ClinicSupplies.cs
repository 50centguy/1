using System;
using System.Collections.Generic;

namespace BorderRepair.TwoNight
{
    public sealed class ClinicSupplyProduct
    {
        public readonly string Id, Name, Category, Compatibility;
        public readonly int Price;
        public ClinicSupplyProduct(string id, string name, string category, string compatibility, int price)
        { Id = id; Name = name; Category = category; Compatibility = compatibility; Price = price; }
    }

    [Serializable]
    public sealed class ClinicSupplyReceipt
    {
        public string id, sku;
        public int quantity, unitPrice, night;
        public bool refund;
    }

    [Serializable]
    public sealed class ClinicSupplyReservation
    {
        public string caseId, sku;
        public bool consumed;
    }

    [Serializable]
    public sealed class ClinicSupplyState
    {
        public List<ClinicSupplyReceipt> receipts = new List<ClinicSupplyReceipt>();
        public List<ClinicSupplyReservation> reservations = new List<ClinicSupplyReservation>();
    }

    // Stock is derived from receipts and allocations, never from a second mutable counter.
    public static class ClinicSupplies
    {
        public const int MaxPerPurchase = 20, MaxStock = 99;
        public const string ReceiptPrefix = "SUPPLY-";
        static readonly ClinicSupplyProduct[] products = {
            new ClinicSupplyProduct("rx-rf", "射频接口小板", "信号接收", "信号接收器 · 原主板保留", 28),
            new ClinicSupplyProduct("mc-interface", "卡匣接口板", "存储接口", "记忆卡匣 · 数据核心保留", 24),
            new ClinicSupplyProduct("ps-output", "稳压输出模块", "供电模块", "维修电源 · 密封电池保留", 55),
            new ClinicSupplyProduct("om-wake", "唤醒驱动模块", "神经周边", "赛博御守 · 记忆核心保留", 22)
        };
        public static IReadOnlyList<ClinicSupplyProduct> Products => Array.AsReadOnly(products);
        public static ClinicSupplyProduct Find(string sku) => Array.Find(products, p => p.Id == sku);
        public static string RequiredSku(string caseId)
        {
            switch (caseId)
            {
                case "repair-prop-receiver": return "rx-rf";
                case "repair-prop-cartridge": return "mc-interface";
                case "repair-prop-power": return "ps-output";
                case "repair-prop-omamori": return "om-wake";
                default: return null;
            }
        }
        public static bool IsReceipt(string id) => id != null && id.StartsWith(ReceiptPrefix, StringComparison.Ordinal);
        public static int Available(TwoNightState s, string sku)
        {
            if (s?.supplies == null) return 0;
            int count = 0;
            foreach (var r in s.supplies.receipts) if (r.sku == sku) count += r.refund ? -r.quantity : r.quantity;
            foreach (var r in s.supplies.reservations) if (r.sku == sku) count--;
            return count;
        }
        public static int Allocated(TwoNightState s, string sku)
            => s?.supplies == null ? 0 : s.supplies.reservations.FindAll(r => r.sku == sku && !r.consumed).Count;
        public static int Consumed(TwoNightState s, string sku)
            => s?.supplies == null ? 0 : s.supplies.reservations.FindAll(r => r.sku == sku && r.consumed).Count;
        public static bool HasAllocation(TwoNightState s, string caseId, string sku = null)
            => s?.supplies != null && RequiredSku(caseId) != null && s.supplies.reservations.Exists(r => r.caseId == caseId &&
                !r.consumed && r.sku == RequiredSku(caseId) && (sku == null || r.sku == sku));

        public static bool Quote(TwoNightState s, IReadOnlyDictionary<string, int> cart, out int total, out string reason)
        {
            total = 0; reason = null;
            if (!CanTrade(s, out reason)) return false;
            if (cart == null || cart.Count == 0) { reason = "购物车为空。"; return false; }
            long sum = 0;
            foreach (var entry in cart)
            {
                var p = Find(entry.Key);
                if (p == null) { reason = "配件型号不存在。"; return false; }
                if (entry.Value < 1 || entry.Value > MaxPerPurchase) { reason = "单次每种配件限购 1 至 20 件。"; return false; }
                if ((long)Available(s, p.Id) + Allocated(s, p.Id) + entry.Value > MaxStock)
                { reason = p.Name + "库存上限为 99 件。"; return false; }
                sum += (long)p.Price * entry.Value;
            }
            if (sum > s.Cash) { reason = "现金不足，还差 " + (sum - s.Cash) + " 信用点。"; return false; }
            total = (int)sum;
            return true;
        }

        public static bool Purchase(TwoNightState s, IReadOnlyDictionary<string, int> cart, string requestId, out string reason)
        {
            if (!ValidRequest(requestId) || s == null) { reason = "采购单号无效。"; return false; }
            requestId = Guid.ParseExact(requestId, "N").ToString("N");
            string prefix = ReceiptPrefix + requestId + "-";
            if (s.transactions.Exists(t => t.id.StartsWith(prefix, StringComparison.Ordinal)))
            { reason = "这张采购单已经入账。"; return false; }
            if (!Quote(s, cart, out _, out reason)) return false;
            // Validate the entire basket before posting any debit or stock receipt.
            foreach (var p in products)
                if (cart.TryGetValue(p.Id, out int count)) AddReceipt(s, prefix + p.Id, p, count, false);
            reason = "采购完成，配件已入库。";
            return true;
        }

        public static bool Refund(TwoNightState s, string sku, int count, string requestId, out string reason)
        {
            if (!CanTrade(s, out reason)) return false;
            var p = Find(sku);
            if (p == null || count < 1 || count > MaxPerPurchase || !ValidRequest(requestId))
            { reason = "退货型号、数量或单号无效。"; return false; }
            requestId = Guid.ParseExact(requestId, "N").ToString("N");
            string id = ReceiptPrefix + requestId + "-" + sku;
            if (s.transactions.Exists(t => t.id.StartsWith(ReceiptPrefix + requestId + "-", StringComparison.Ordinal)))
            { reason = "这张退货单已经入账。"; return false; }
            if (Available(s, sku) < count) { reason = "可退库存不足；已安装或已交件的配件不可退货。"; return false; }
            if ((long)s.Cash + (long)p.Price * count > int.MaxValue) { reason = "余额超过账本上限。"; return false; }
            AddReceipt(s, id, p, count, true);
            reason = "闲置配件已退回，款项已入账。";
            return true;
        }

        public static bool Reserve(TwoNightState s, string caseId, string sku, out string reason)
        {
            reason = null;
            var trade = s?.ActiveTrade;
            if (s?.supplies == null || s.phase != TwoNightPhase.Night1Counter || Find(sku) == null || RequiredSku(caseId) != sku || trade == null || trade.caseId != caseId ||
                trade.state != ClinicTradeState.InRepair || s.supplies.reservations.Exists(r => r.caseId == caseId))
            { reason = "当前工单不能领用这件配件。"; return false; }
            if (Available(s, sku) < 1) { reason = "缺少" + Find(sku).Name + "，请先到配件采购入库。"; return false; }
            s.supplies.reservations.Add(new ClinicSupplyReservation { caseId = caseId, sku = sku });
            return true;
        }
        public static bool Release(TwoNightState s, string caseId)
        {
            if (s?.supplies == null) return false;
            return s.supplies.reservations.RemoveAll(r => r.caseId == caseId && !r.consumed) == 1;
        }
        public static void Deliver(TwoNightState s, string caseId)
        {
            var r = s?.supplies?.reservations.Find(a => a.caseId == caseId);
            if (r != null) r.consumed = true;
        }

        static bool ValidRequest(string id) => Guid.TryParseExact(id, "N", out _);
        static bool CanTrade(TwoNightState s, out string reason)
        {
            reason = null;
            if (s?.supplies == null || !s.unifiedClinic || s.phase != TwoNightPhase.Night1Counter)
            { reason = "当前不在配件采购营业时段。"; return false; }
            long cash = s.startingCash;
            foreach (var t in s.transactions) cash += t.amount;
            if (cash < 0 || cash > int.MaxValue) { reason = "账本余额无效，请先核对收支。"; return false; }
            return true;
        }
        static void AddReceipt(TwoNightState s, string id, ClinicSupplyProduct p, int count, bool refund)
        {
            int amount = p.Price * count * (refund ? 1 : -1);
            TwoNightRun.Post(s, id, amount, (refund ? "配件退货 · " : "配件采购 · ") + p.Name + " × " + count, s.night);
            s.supplies.receipts.Add(new ClinicSupplyReceipt { id = id, sku = p.Id, quantity = count,
                unitPrice = p.Price, night = s.night, refund = refund });
        }

        // Checkpoint audit connects every stock movement to exactly one cash transaction and customer allocation.
        public static void Validate(TwoNightState s, List<string> errors)
        {
            bool trackedProps = s.customerTrades != null && s.customerTrades.Exists(t => t != null && RequiredSku(t.caseId) != null);
            if ((s.supplyRulesVersion != 0 && s.supplyRulesVersion != 1) ||
                (s.supplyRulesVersion == 1) != (s.supplies != null) ||
                (trackedProps && s.supplyRulesVersion == 0 && !s.legacyFreeSupplies))
                errors.Add("Supply rules or history missing");
            if (s.supplies == null)
            {
                if (s.transactions.Exists(t => t != null && IsReceipt(t.id))) errors.Add("Supply ledger without stock history");
                return;
            }
            if (!s.unifiedClinic || s.customerTrades == null || s.supplies.receipts == null || s.supplies.reservations == null)
            { errors.Add("Invalid supply history"); return; }
            var ids = new HashSet<string>();
            var units = new Dictionary<string, long>();
            long cash = s.startingCash;
            foreach (var t in s.transactions)
            {
                if (t == null) continue;
                cash += t.amount;
                if (cash < 0 || cash > int.MaxValue) errors.Add("Supply cash chronology invalid");
            }
            foreach (var p in products) units[p.Id] = 0;
            foreach (var r in s.supplies.receipts)
            {
                var p = r != null ? Find(r.sku) : null;
                if (p == null || !IsReceipt(r.id) || r.id.Length < ReceiptPrefix.Length + 33 ||
                    !ValidRequest(r.id.Substring(ReceiptPrefix.Length, 32)) ||
                    r.id != ReceiptPrefix + Guid.ParseExact(r.id.Substring(ReceiptPrefix.Length, 32), "N").ToString("N") + "-" + r.sku ||
                    !ids.Add(r.id) || r.quantity < 1 ||
                    r.quantity > MaxPerPurchase || r.unitPrice != p.Price || r.night != 1)
                { errors.Add("Invalid supply receipt"); continue; }
                var t = s.transactions.Find(a => a != null && a.id == r.id);
                if (t == null || t.amount != (r.refund ? 1 : -1) * r.unitPrice * r.quantity || t.night != r.night)
                    errors.Add("Supply cash receipt mismatch");
                units[p.Id] += r.refund ? -r.quantity : r.quantity;
                if (units[p.Id] < 0) errors.Add("Supply refund exceeds purchases");
            }
            if (s.transactions.FindAll(t => t != null && IsReceipt(t.id)).Count != ids.Count)
                errors.Add("Unmatched supply cash entry");
            var cases = new HashSet<string>();
            foreach (var r in s.supplies.reservations)
            {
                var trade = r != null ? s.customerTrades?.Find(t => t != null && t.caseId == r.caseId) : null;
                if (r == null || Find(r.sku) == null || r.sku != RequiredSku(r.caseId) || !cases.Add(r.caseId) || trade == null || trade.returnedUnpaid ||
                    r.consumed != (trade.state == ClinicTradeState.Delivered))
                { errors.Add("Invalid supply allocation"); continue; }
                units[r.sku]--;
            }
            foreach (var count in units.Values) if (count < 0 || count > MaxStock) errors.Add("Invalid supply stock total");
            foreach (var trade in s.customerTrades)
                if (trade != null && !trade.returnedUnpaid && !trade.noPartUsed && trade.state == ClinicTradeState.Delivered && !cases.Contains(trade.caseId))
                    errors.Add("Delivered order missing consumed part");
        }
    }
}
