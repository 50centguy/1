using BorderRepair.TwoNight;

namespace BorderRepair.RepairProps
{
    public interface IRepairPropCommandPolicy
    {
        bool CanExecute(RepairPropFlow flow, PropCommand command, out string reason);
        bool BeforeExecute(RepairPropFlow flow, PropCommand command, out string reason);
        bool ReplacementAvailable { get; }
    }

    public sealed class RepairPropSupplyPolicy : IRepairPropCommandPolicy, System.IDisposable
    {
        readonly TwoNightState state;
        readonly string caseId;
        public string Sku { get; }
        public bool ReplacementAvailable => ClinicSupplies.Available(state, Sku) > 0 || ClinicSupplies.HasAllocation(state, caseId, Sku);
        public RepairPropSupplyPolicy(TwoNightState state, string caseId, RepairPropKind kind)
        { this.state = state; this.caseId = caseId; Sku = SkuFor(kind); }
        public static string SkuFor(RepairPropKind kind)
        {
            switch (kind)
            {
                case RepairPropKind.Receiver: return "rx-rf";
                case RepairPropKind.Cartridge: return "mc-interface";
                case RepairPropKind.Power: return "ps-output";
                case RepairPropKind.Omamori: return "om-wake";
                default: return null;
            }
        }
        public bool CanExecute(RepairPropFlow flow, PropCommand command, out string reason)
        {
            reason = null;
            if (command.id == "module.install.new" && ClinicSupplies.Available(state, Sku) < 1)
                reason = "缺少" + ClinicSupplies.Find(Sku).Name + "，请打开配件采购。";
            // A fault fixed without a part (cleaned or re-crimped) delivers with no allocation.
            bool partInside = flow.GetState("module") == 2;
            if ((command.id == "complete" && partInside || command.id == "module.remove.new") && !ClinicSupplies.HasAllocation(state, caseId, Sku))
                reason = "当前替换件没有领用记录，请核对库存。";
            return reason == null;
        }
        public bool BeforeExecute(RepairPropFlow flow, PropCommand command, out string reason)
        {
            if (!CanExecute(flow, command, out reason)) return false;
            if (command.id == "module.install.new") return ClinicSupplies.Reserve(state, caseId, Sku, out reason);
            if (command.id == "complete" && flow.GetState("module") != 2 && state.ActiveTrade != null && state.ActiveTrade.caseId == caseId)
                state.ActiveTrade.noPartUsed = true;
            if (command.id == "module.remove.new" && !ClinicSupplies.Release(state, caseId))
            { reason = "配件回库失败，未改变安装状态。"; return false; }
            return true;
        }
        public void Dispose() => ClinicSupplies.Release(state, caseId);
    }
}
