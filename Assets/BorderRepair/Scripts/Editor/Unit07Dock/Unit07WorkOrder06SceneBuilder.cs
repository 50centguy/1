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
    }
}
