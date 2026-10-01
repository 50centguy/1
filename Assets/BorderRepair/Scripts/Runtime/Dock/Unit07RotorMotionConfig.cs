using BorderRepair.Motion;
using UnityEngine;

namespace BorderRepair.Dock
{
    /// <summary>
    /// 七号转子启停的时长与曲线（可在 Inspector 编辑并保存）。曲线纵轴 = 转速变化的完成度：
    /// 减速曲线 0 = 悬停转速、1 = 停止；加速曲线 0 = 停止、1 = 悬停转速。
    /// 默认值与原代码完全一致：断电 2.5 s 匀减速、通电 1.0 s 匀加速（线性）。齿轮菜单里的 Reset 可以恢复默认。
    /// 悬停转速（360°/s）不在这里：它是从 Idle_Hover 片段实测的，改了会和动画对不上。
    /// </summary>
    [CreateAssetMenu(menuName = "Border Repair/Unit07 动作配置/转子启停", fileName = "UNIT07_RotorMotion")]
    public class Unit07RotorMotionConfig : ScriptableObject
    {
        [Header("断电：悬停转速 → 停止")]
        public MotionTiming spinDown = new MotionTiming(2.5f, MotionTiming.Linear());
        [Header("通电：停止 → 悬停转速")]
        public MotionTiming spinUp = new MotionTiming(1.0f, MotionTiming.Linear());

        void Reset()
        {
            spinDown = new MotionTiming(2.5f, MotionTiming.Linear());
            spinUp = new MotionTiming(1.0f, MotionTiming.Linear());
        }

        public static Unit07RotorMotionConfig CreateDefault()
        {
            var c = CreateInstance<Unit07RotorMotionConfig>();
            c.hideFlags = HideFlags.DontSave;
            c.name = "UNIT07_RotorMotion (默认，未接配置资产)";
            return c;
        }
    }
}
