using UnityEngine;

namespace BorderRepair.FirstOrder
{
    /// <summary>
    /// 工作台上的零件落点（触发盒）：只接受指定的部件。落点位置由场景构建脚本在工作台实际对象上算出
    /// （操作垫表面、旧件托盘里的空位），不改工作台资源。landing = 部件包围盒底面中心落到的位置。
    /// </summary>
    public class FirstOrderDropZone : MonoBehaviour
    {
        public string zoneId;
        public string displayName;
        [Tooltip("落点所在的工作台真实对象路径")]
        public string benchObjectPath;
        public string acceptsPartId;
        public Transform landing;
        public Renderer marker;     // 可见的落点提示（等待放下时才显示）

        public void ShowMarker(bool on) { if (marker != null) marker.enabled = on; }
    }
}
