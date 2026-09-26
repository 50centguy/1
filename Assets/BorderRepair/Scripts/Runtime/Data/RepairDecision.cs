namespace BorderRepair.Data
{
    /// <summary>玩家对一件送修物的最终处理决定。</summary>
    public enum RepairDecision
    {
        Repair = 0,
        RecommendReplacement = 1,
        Refuse = 2
    }

    public static class RepairDecisionText
    {
        public static string Label(RepairDecision decision)
        {
            switch (decision)
            {
                case RepairDecision.Repair: return "维修";
                case RepairDecision.RecommendReplacement: return "建议更换";
                case RepairDecision.Refuse: return "拒绝处理";
                default: return decision.ToString();
            }
        }
    }
}
