using UnityEngine;

namespace BorderRepair.FirstOrder
{
    /// <summary>
    /// 工作台上的零件落点（触发盒）：只接受指定的部件。落点位置由场景构建脚本在工作台实际对象上算出
    /// （操作垫表面、旧件托盘里的空位），不改工作台资源。landing = 部件包围盒底面中心落到的位置。
    /// orientPart：放下时部件转到 landingRotation（世界朝向），主对象原点落在 landing + landingOffset——
    /// 两者由构建脚本按部件网格顶点算好（上盖总成翻过来内侧朝上、旧轴承平放），最低点贴着落点表面，不穿进去。
    /// useApproach：落点正上方被挡住（上盖落点在工作台台灯下面）时，从 approachPoint 低空水平进出。
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
        public bool orientPart;
        public Quaternion landingRotation = Quaternion.identity;
        public Vector3 landingOffset;
        [Tooltip("工作台一侧的进场点（零件原点的世界位置，摆放朝向下）：落点正上方被挡住时（例如台灯），先落到这里再水平钻进落点上方。构建时按精确网格规划")]
        public bool useApproach;
        public Vector3 approachPoint;

        public void ShowMarker(bool on) { if (marker != null) marker.enabled = on; }
    }
}
