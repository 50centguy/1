using UnityEngine;

namespace BorderRepair.Dock
{
    /// <summary>屏幕上的界面区域（例如工单面板）：落在这里的鼠标点击不再打射线点维修座部件。</summary>
    public interface IDockClickBlocker
    {
        /// <param name="screenPos">Input System 的屏幕坐标（左下角为原点）。</param>
        bool BlocksClick(Vector2 screenPos);
    }
}
