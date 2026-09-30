using UnityEngine;

namespace BorderRepair.Dock
{
    /// <summary>
    /// 可取下的独立物品（零件盘、磁性零件盒）。本阶段只做“取下 / 放回”两种状态：
    /// 取下时移到手持位置（holdPoint，通常在镜头前方），放回时回到原来的位置。不用物理。
    /// </summary>
    public class DockPickable : MonoBehaviour
    {
        [SerializeField] Transform holdPoint;
        Vector3 homeLocalPosition;
        Quaternion homeLocalRotation;
        Transform homeParent;
        bool captured;

        public bool Taken { get; private set; }
        public Transform HoldPoint { get => holdPoint; set => holdPoint = value; }

        void Awake() => CaptureHome();

        void CaptureHome()
        {
            if (captured) return;
            homeParent = transform.parent;
            homeLocalPosition = transform.localPosition;
            homeLocalRotation = transform.localRotation;
            captured = true;
        }

        public void Take()
        {
            CaptureHome();
            if (Taken || holdPoint == null) return;
            transform.SetParent(holdPoint, true);
            transform.localPosition = Vector3.zero;
            Taken = true;
        }

        public void PutBack()
        {
            if (!Taken) return;
            transform.SetParent(homeParent, true);
            transform.localPosition = homeLocalPosition;
            transform.localRotation = homeLocalRotation;
            Taken = false;
        }

        public void Toggle()
        {
            if (Taken) PutBack();
            else Take();
        }
    }
}
