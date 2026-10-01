using System.Linq;
using BorderRepair.Dock;
using BorderRepair.Unit07WorkOrder;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BorderRepair.EditorTools
{
    /// <summary>
    /// 第三阶段集成场景：UNIT 07 维修座 + 工单 06（七号左引擎失衡）。
    /// 与维修座测试场景共用灯光、维修座、七号、镜头（<see cref="Unit07DockBuilder.BuildSceneBase"/>），
    /// “允许结束维修”接工单（<see cref="Unit07WorkOrder06Bridge"/>），不放占位的 ManualServiceCompletionGate（没有 F 确认）。
    /// 不重新生成预制体、材质，不动 RobotV4 FBX。
    /// 命令行：-executeMethod BorderRepair.EditorTools.Unit07WorkOrder06SceneBuilder.Build
    /// </summary>
    public static class Unit07WorkOrder06SceneBuilder
    {
        public const string ScenePath = "Assets/BorderRepair/Scenes/Unit07_WorkOrder06_Test.unity";

        [MenuItem("Border Repair/Unit07 Dock/Build Work Order 06 Scene")]
        public static void Build()
        {
            var p = Unit07DockBuilder.BuildSceneBase();
            p.Flow.name = "Unit07DockFlow + WorkOrder06";
            var bridge = p.Flow.AddComponent<Unit07WorkOrder06Bridge>();
            bridge.Configure(p.Controller, p.Robot.transform, p.Dock.GetComponentsInChildren<DockPickable>(true).ToArray());
            p.Controller.SetServiceCompletionGate(bridge);   // 唯一的“允许结束维修”：工单
            p.Flow.AddComponent<Unit07DockInput>().Configure(p.Camera, p.Controller, bridge);
            EditorUtility.SetDirty(p.Controller);
            EditorUtility.SetDirty(bridge);
            EditorSceneManager.SaveScene(p.Scene, ScenePath);

            var missing = bridge.MissingModelAnchors();
            Debug.Log($"[Unit07WorkOrder06] 集成场景：{ScenePath}；零件盘 / 磁性盒 {p.Dock.GetComponentsInChildren<DockPickable>(true).Length} 个；" +
                      $"模型缺少的工单节点：{(missing.Count == 0 ? "无" : string.Join(", ", missing))}");
            if (Application.isBatchMode) EditorApplication.Exit(missing.Count == 0 ? 0 : 1);
        }

        // ------------------------------------------------------------------ 验收用：模拟接线错误

        const string SimulateMenu = "Border Repair/Unit07 Dock/验收：模拟接线错误并通电（安全故障）";

        sealed class BypassGate : IDockServiceCompletionGate
        {
            public bool CanFinishService(out string reason) { reason = string.Empty; return true; }
        }

        /// <summary>
        /// 只在编辑器 Play 模式、集成场景里用：把维修座临时接到总是放行的接口，拨断电开关通电，再接回工单。
        /// 用来人工验收安全故障锁定与复位（MouseAcceptance.md 第 F 节）。不进入构建，运行时代码里没有这个入口。
        /// </summary>
        [MenuItem(SimulateMenu)]
        public static void SimulateMiswiredPowerOn()
        {
            var dock = Object.FindFirstObjectByType<Unit07DockController>();
            var bridge = Object.FindFirstObjectByType<Unit07WorkOrder06Bridge>();
            dock.SetServiceCompletionGate(new BypassGate());
            bool ok = dock.SetPower(true);
            dock.SetServiceCompletionGate(bridge);
            Debug.Log($"[Unit07WorkOrder06] 验收模拟接线错误：通电{(ok ? "成功" : "未执行（" + dock.LastMessage + "）")}；已接回工单接口。");
        }

        [MenuItem(SimulateMenu, true)]
        static bool CanSimulate() => Application.isPlaying &&
                                     Object.FindFirstObjectByType<Unit07WorkOrder06Bridge>() != null &&
                                     Object.FindFirstObjectByType<Unit07DockController>() is Unit07DockController d && !d.PowerOn;
    }
}
