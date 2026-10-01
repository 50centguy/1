namespace BorderRepair.Dock
{
    /// <summary>
    /// “允许结束维修”接口：维修座在断电维修之后要恢复供电时，先问它。
    /// 以后由工单 / 维修流程实现（例如：本单所有零件已装回、复查已通过），维修座本身不判断维修内容。
    /// 本分支只提供测试场景用的手动占位实现 <see cref="ManualServiceCompletionGate"/>，没有接入任何工单代码。
    /// </summary>
    public interface IDockServiceCompletionGate
    {
        /// <summary>现在能否结束维修、恢复供电。不能时 reason 给出原因（会显示给玩家）。</summary>
        bool CanFinishService(out string reason);
    }
}
