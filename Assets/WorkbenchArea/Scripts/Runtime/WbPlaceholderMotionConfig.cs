using BorderRepair.Motion;
using UnityEngine;

namespace WorkbenchArea
{
    /// <summary>
    /// 工作台占位演示（托盘取放、义肢占位件拆装、工具箱上层移开）每一段直线移动的时长与运动曲线。
    /// 默认值与原代码完全一致：每段 0.35 s 缓入缓出（SmoothStep）。只改走多快、怎么加减速，不改路径；曲线超出 0–1 的部分会被截断。
    /// 齿轮菜单里的 Reset 可以恢复默认。
    /// </summary>
    [CreateAssetMenu(menuName = "Border Repair/Unit07 动作配置/工作台占位移动", fileName = "WB_PlaceholderMotion")]
    public class WbPlaceholderMotionConfig : ScriptableObject
    {
        [Header("每一段直线移动（抬起 / 平移 / 放下）")]
        public MotionTiming step = new MotionTiming(0.35f, MotionTiming.SmoothStep());

        void Reset() => step = new MotionTiming(0.35f, MotionTiming.SmoothStep());

        public static WbPlaceholderMotionConfig CreateDefault()
        {
            var c = CreateInstance<WbPlaceholderMotionConfig>();
            c.hideFlags = HideFlags.DontSave;
            c.name = "WB_PlaceholderMotion (默认，未接配置资产)";
            return c;
        }
    }
}
