using System.IO;
using System.Text;
using UnityEngine;

namespace WorkbenchArea
{
    /// <summary>
    /// 鼠标操作记录（给真人鼠标检查用）：记下每次悬停目标变化与每次左键点击，包括点空、点到被挡住的对象。
    /// 退出 Play / 关闭程序时写文件：编辑器里写到 &lt;项目&gt;/Logs/wb/mouse_session_*.txt，独立程序写到 persistentDataPath。
    /// 只记录，不改变任何行为。
    /// </summary>
    public class WbInteractionLog : MonoBehaviour
    {
        [SerializeField] WbCameraRig rig;
        readonly StringBuilder sb = new StringBuilder();
        WbInspectable lastHover;
        float hoverSince;
        int clicks, emptyClicks, blockedClicks;

        public void Configure(WbCameraRig r) => rig = r;

        void OnEnable()
        {
            WbCameraRig.Clicked += OnClicked;
            sb.AppendLine($"# 工作台区域 · 鼠标操作记录 {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}，屏幕 {Screen.width}×{Screen.height}");
            sb.AppendLine("# 时间(s)\t镜头\t事件\t对象\t角色\t说明");
        }

        void OnDisable()
        {
            WbCameraRig.Clicked -= OnClicked;
            Flush();
        }

        string View => rig != null ? rig.Current.ToString() : "?";

        void Update()
        {
            if (rig == null) return;
            if (rig.Hovered != lastHover)
            {
                if (lastHover != null) Line("hover_end", lastHover, $"停留 {Time.unscaledTime - hoverSince:F2} s");
                lastHover = rig.Hovered;
                hoverSince = Time.unscaledTime;
                if (lastHover != null) Line("hover", lastHover, lastHover.IsBlocked ? "被挡住" : "");
            }
        }

        void OnClicked(WbCameraRig r, WbInspectable w)
        {
            clicks++;
            if (w == null) emptyClicks++;
            else if (w.IsBlocked) blockedClicks++;
            Line("click", w, w == null ? "点空" : w.IsBlocked ? "被挡住" : "");
        }

        void Line(string evt, WbInspectable w, string note) =>
            sb.AppendLine($"{Time.unscaledTime:F2}\t{View}\t{evt}\t{(w != null ? w.displayName : "-")}\t{(w != null ? w.role : "-")}\t{note}");

        void Flush()
        {
            if (clicks == 0 && lastHover == null) return;
            sb.AppendLine($"# 合计：点击 {clicks}，点空 {emptyClicks}，点到被挡住的对象 {blockedClicks}");
#if UNITY_EDITOR
            var dir = Path.Combine(Application.dataPath, "..", "Logs", "wb");
#else
            var dir = Application.persistentDataPath;
#endif
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, $"mouse_session_{System.DateTime.Now:yyyyMMdd_HHmmss}.txt"), sb.ToString(), new UTF8Encoding(false));
        }
    }
}
