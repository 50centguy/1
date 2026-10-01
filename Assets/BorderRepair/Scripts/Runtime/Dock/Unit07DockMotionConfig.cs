using BorderRepair.Motion;
using UnityEngine;

namespace BorderRepair.Dock
{
    /// <summary>
    /// 维修座脚本动作的时长与运动曲线（可在 Inspector 编辑并保存）。默认值与原代码完全一致：
    /// 夹具 0.6 s 线性、七号落座 1.2 s 缓入缓出（SmoothStep）、断电开关手柄 0.3 s 线性、落座后 Animator 过渡 0.15 s。
    /// 齿轮菜单里的 Reset 可以恢复这些默认值。几何（夹具张开角度、手柄角度、悬停高度）不在这里：角度来自维修座 FBX 的属性。
    /// </summary>
    [CreateAssetMenu(menuName = "Border Repair/Unit07 动作配置/维修座动作", fileName = "UNIT07_DockMotion")]
    public class Unit07DockMotionConfig : ScriptableObject
    {
        [Header("夹具张开 / 合拢（合拢时沿同一条曲线倒着走）")]
        public MotionTiming clamps = new MotionTiming(0.6f, MotionTiming.Linear());
        [Header("七号落座（悬停高度 → 接触垫）")]
        public MotionTiming descend = new MotionTiming(1.2f, MotionTiming.SmoothStep());
        [Header("断电开关手柄（ON ↔ OFF，反向时倒着走）")]
        public MotionTiming lever = new MotionTiming(0.3f, MotionTiming.Linear());
        [Header("落座后七号 Animator 过渡到落座姿态的时间（秒）")]
        [Min(0f)] public float seatedBlendSeconds = 0.15f;

        void Reset()
        {
            clamps = new MotionTiming(0.6f, MotionTiming.Linear());
            descend = new MotionTiming(1.2f, MotionTiming.SmoothStep());
            lever = new MotionTiming(0.3f, MotionTiming.Linear());
            seatedBlendSeconds = 0.15f;
        }

        public static Unit07DockMotionConfig CreateDefault()
        {
            var c = CreateInstance<Unit07DockMotionConfig>();
            c.hideFlags = HideFlags.DontSave;
            c.name = "UNIT07_DockMotion (默认，未接配置资产)";
            return c;
        }
    }
}
