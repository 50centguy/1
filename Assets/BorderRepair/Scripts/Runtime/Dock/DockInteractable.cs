using UnityEngine;

namespace BorderRepair.Dock
{
    public enum DockAction
    {
        Clamps = 0,        // 左右限位夹具（两侧同时开 / 合）
        PowerSwitch = 1,   // 断电开关
        PartsTray = 2,     // 零件盘（取下 / 放回）
        MagneticBox = 3,   // 磁性零件盒（取下 / 放回）
        EngineLeft = 4,    // 左引擎检查入口（本阶段只判断是否允许，不做拆卸）
        ContactPad = 5,    // 机身接触垫（只作接触 / 查询，不响应点击）
        LiftOff = 6,       // 第二阶段：七号升起离座（没有对应的维修座部件，由按键 / 按钮或程序触发）
    }

    /// <summary>挂在点击 / 碰撞代理上，告诉 Unit07DockController 这个代理代表哪个部件。</summary>
    public class DockInteractable : MonoBehaviour
    {
        public DockAction action;
        [Tooltip("可取下物品（零件盘、磁性盒）的本体；其它代理为空。")]
        public DockPickable pickable;
    }
}
