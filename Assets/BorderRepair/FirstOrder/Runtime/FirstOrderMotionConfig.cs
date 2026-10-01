using BorderRepair.Motion;
using UnityEngine;

namespace BorderRepair.FirstOrder
{
    /// <summary>
    /// 七号首单原型里占位移动的时长与运动曲线（可在 Inspector 编辑并保存）。默认值与原代码完全一致：
    /// 锁扣、上盖、轴承的每一段直线移动 0.45 s 缓入缓出（SmoothStep）；七号离座上浮 1.2 s 缓入缓出；离座时 Animator 0.2 s 过渡回 Idle_Hover。
    /// 只改“走多快、怎么加减速”，不改路径：抬起高度、搬运高度、锁扣移出距离仍由场景构建时的几何决定，曲线超出 0–1 的部分会被截断。
    /// 齿轮菜单里的 Reset 可以恢复默认。
    /// </summary>
    [CreateAssetMenu(menuName = "Border Repair/Unit07 动作配置/首单占位移动", fileName = "UNIT07_FirstOrderMotion")]
    public class FirstOrderMotionConfig : ScriptableObject
    {
        [Header("锁扣扳开 / 扣回、上盖和轴承的每一段直线移动")]
        public MotionTiming partMove = new MotionTiming(0.45f, MotionTiming.SmoothStep());
        [Header("七号离座上浮（接触垫 → 悬停高度）")]
        public MotionTiming liftOff = new MotionTiming(1.2f, MotionTiming.SmoothStep());
        [Header("离座时七号 Animator 过渡回 Idle_Hover 的时间（秒）")]
        [Min(0f)] public float liftBlendSeconds = 0.2f;

        void Reset()
        {
            partMove = new MotionTiming(0.45f, MotionTiming.SmoothStep());
            liftOff = new MotionTiming(1.2f, MotionTiming.SmoothStep());
            liftBlendSeconds = 0.2f;
        }

        public static FirstOrderMotionConfig CreateDefault()
        {
            var c = CreateInstance<FirstOrderMotionConfig>();
            c.hideFlags = HideFlags.DontSave;
            c.name = "UNIT07_FirstOrderMotion (默认，未接配置资产)";
            return c;
        }
    }
}
