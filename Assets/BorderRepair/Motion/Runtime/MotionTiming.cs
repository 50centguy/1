using System;
using UnityEngine;

namespace BorderRepair.Motion
{
    /// <summary>
    /// 一段脚本动作的“时长 + 运动曲线”，可以在 Inspector 里编辑并保存在配置资产里。
    /// 曲线横轴 = 时间进度（0 = 开始，1 = 结束），纵轴 = 动作完成度（0 = 起点姿态，1 = 终点姿态）。
    /// 纵轴超出 0–1 的部分会被截断：动作只在原来的起点和终点之间走，不会越过路径端点（碰撞路径不变）。
    /// </summary>
    [Serializable]
    public class MotionTiming
    {
        [Tooltip("动作时长（秒）")]
        [Min(0.01f)] public float seconds = 1f;
        [Tooltip("横轴：时间进度 0→1；纵轴：动作完成度 0→1。超出 0–1 的部分会被截断。")]
        public AnimationCurve curve = Linear();

        public MotionTiming() { }

        public MotionTiming(float seconds, AnimationCurve curve)
        {
            this.seconds = seconds;
            this.curve = curve;
        }

        public float Seconds => Mathf.Max(seconds, 0.01f);

        /// <summary>按时间进度（0–1）求动作完成度（0–1）。曲线为空时按线性。</summary>
        public float Evaluate(float progress)
        {
            progress = Mathf.Clamp01(progress);
            if (curve == null || curve.length == 0) return progress;
            if (progress >= 1f) return Mathf.Clamp01(curve.Evaluate(1f));
            return Mathf.Clamp01(curve.Evaluate(progress));
        }

        /// <summary>
        /// 反查：完成度 value 对应的时间进度（曲线单调递增时精确；不单调时取第一个达到该值的点附近）。
        /// 用于动作中途反向（例如转子减速一半又通电）时，接着当前完成度继续，不跳变。
        /// </summary>
        public float InverseEvaluate(float value)
        {
            value = Mathf.Clamp01(value);
            if (value <= Evaluate(0f)) return 0f;
            if (value >= Evaluate(1f)) return 1f;
            float lo = 0f, hi = 1f;
            for (int i = 0; i < 40; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (Evaluate(mid) < value) lo = mid; else hi = mid;
            }
            return (lo + hi) * 0.5f;
        }

        public MotionTiming Clone() => new MotionTiming(seconds, curve != null ? new AnimationCurve(curve.keys) : Linear());

        /// <summary>线性：完成度 = 时间进度（等同于 Mathf.MoveTowards / Lerp 匀速）。</summary>
        public static AnimationCurve Linear() => new AnimationCurve(new Keyframe(0f, 0f, 1f, 1f), new Keyframe(1f, 1f, 1f, 1f));

        /// <summary>缓入缓出：两端切线为 0 的三次曲线，与 Mathf.SmoothStep(0, 1, t) 完全相同（3t² − 2t³）。</summary>
        public static AnimationCurve SmoothStep() => new AnimationCurve(new Keyframe(0f, 0f, 0f, 0f), new Keyframe(1f, 1f, 0f, 0f));
    }
}
