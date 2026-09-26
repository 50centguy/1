using System;
using BorderRepair.Controls;
using UnityEngine;

namespace BorderRepair.Inspection
{
    /// <summary>
    /// 固定机位检查台：把物品放在 itemAnchor 上，鼠标拖动旋转、滚轮缩放。
    /// 镜头只沿初始视线方向前后移动；距离根据物品包围球计算上下限，保证物品不穿过近裁剪面也不会缩到看不见。
    /// </summary>
    public class ItemInspector : MonoBehaviour
    {
        [SerializeField] Camera viewCamera;
        [SerializeField] Transform itemAnchor;

        [Header("物品尺寸")]
        [Tooltip("把任意 prefab 缩放到统一的包围球半径，替换模型时无需调整镜头。")]
        [SerializeField] bool normalizeItemSize = true;
        [SerializeField] float targetItemRadius = 0.22f;

        [Header("旋转")]
        [SerializeField] float degreesPerPixel = 0.3f;
        [SerializeField] float pitchLimit = 75f;
        [SerializeField] float dragThresholdPixels = 6f;

        [Header("缩放（相对“刚好装下物品”的距离倍数）")]
        [SerializeField] float minFitMultiplier = 0.6f;
        [SerializeField] float defaultFitMultiplier = 1.25f;
        [SerializeField] float maxFitMultiplier = 2.0f;
        [SerializeField] float zoomStepFraction = 0.12f;
        [SerializeField] float nearClipMargin = 0.03f;

        [SerializeField] float smoothing = 14f;

        /// <summary>未拖动的一次左键单击（屏幕坐标）。</summary>
        public event Action<Vector2> Clicked;

        public bool InteractionEnabled { get; set; }
        public GameObject CurrentItem { get; private set; }
        public float ItemRadius { get; private set; } = 0.2f;
        public float MinDistance { get; private set; }
        public float MaxDistance { get; private set; }
        public float TargetDistance => targetDistance;
        public float Pitch => pitch;
        public Camera ViewCamera => viewCamera;

        Vector3 viewDirection;
        float yaw, pitch, targetYaw, targetPitch;
        float distance, targetDistance, defaultDistance;
        Vector2 pressPosition;
        bool pressActive, dragging;

        // 可选的零件焦点（叙事场景的分阶段取景使用）。没有调用 Focus 时焦点恒为物品中心，行为与原来完全相同。
        bool hasFocus;
        Vector3 focusLocal, focusLocalCurrent;      // itemAnchor 局部坐标：随物品一起旋转
        float focusYaw, focusPitch, focusDistance, focusRadius;
        Renderer[] itemRenderers = new Renderer[0];

        public bool HasFocus => hasFocus;
        /// <summary>镜头当前对准的世界坐标（无焦点时就是物品中心）。</summary>
        public Vector3 LookPoint => itemAnchor.TransformPoint(focusLocalCurrent);
        /// <summary>当前生效的最近距离：有焦点时按零件大小和物品挡在镜头前的厚度计算。</summary>
        public float LowerDistanceLimit => hasFocus || focusLocalCurrent != Vector3.zero ? FocusMinDistance() : MinDistance;

        void Awake()
        {
            viewDirection = viewCamera.transform.position - itemAnchor.position;
            viewDirection = viewDirection.sqrMagnitude > 1e-6f ? viewDirection.normalized : -itemAnchor.forward;
            distance = targetDistance = defaultDistance = Vector3.Distance(viewCamera.transform.position, itemAnchor.position);
            MinDistance = MaxDistance = distance;
        }

        public GameObject Mount(GameObject prefab)
        {
            Clear();
            yaw = pitch = targetYaw = targetPitch = 0f;
            hasFocus = false;
            focusLocal = focusLocalCurrent = Vector3.zero;
            itemRenderers = new Renderer[0];
            itemAnchor.rotation = Quaternion.identity;
            if (prefab == null) return null;

            var item = Instantiate(prefab, itemAnchor);
            item.name = prefab.name;
            item.transform.localPosition = Vector3.zero;
            item.transform.localRotation = Quaternion.identity;

            Bounds bounds = CalculateBounds(item);
            if (normalizeItemSize && bounds.extents.magnitude > 1e-4f)
            {
                item.transform.localScale *= targetItemRadius / bounds.extents.magnitude;
                bounds = CalculateBounds(item);
            }
            item.transform.position += itemAnchor.position - bounds.center;

            CurrentItem = item;
            itemRenderers = item.GetComponentsInChildren<Renderer>(true);
            ItemRadius = Mathf.Max(0.01f, bounds.extents.magnitude);
            ComputeDistanceLimits();
            ResetView(true);
            return item;
        }

        public void Clear()
        {
            if (CurrentItem != null) Destroy(CurrentItem);
            CurrentItem = null;
        }

        /// <summary>复位视角；有焦点时回到当前焦点的默认角度和距离。</summary>
        public void ResetView(bool immediate = false)
        {
            targetYaw = hasFocus ? focusYaw : 0f;
            targetPitch = hasFocus ? focusPitch : 0f;
            targetDistance = hasFocus ? focusDistance : defaultDistance;
            if (immediate) SnapToTarget();
        }

        /// <summary>
        /// 把镜头对准物品上的一组零件：worldBounds 为零件的世界包围盒，yaw / pitch 为物品转到的角度，
        /// fill 为零件包围球直径占画面短边的比例（细长物品可以大于 1），minRadius 防止很小的零件被放得过大。平滑过渡，immediate 时立即到位。
        /// </summary>
        public void Focus(Bounds worldBounds, float yawDegrees, float pitchDegrees, float fill, float minRadius = 0f, bool immediate = false)
        {
            if (CurrentItem == null) return;
            hasFocus = true;
            focusLocal = itemAnchor.InverseTransformPoint(worldBounds.center);
            focusRadius = Mathf.Max(minRadius, worldBounds.extents.magnitude, 0.005f);
            focusDistance = focusRadius / Mathf.Tan(Mathf.Clamp(fill, 0.05f, 1.6f) * HalfFovMin());
            focusYaw = yawDegrees;
            focusPitch = Mathf.Clamp(pitchDegrees, -pitchLimit, pitchLimit);
            targetYaw = focusYaw;
            targetPitch = focusPitch;
            targetDistance = Mathf.Min(focusDistance, MaxDistance);
            if (immediate) SnapToTarget();
        }

        /// <summary>取消零件焦点，回到整件物品的默认视角。</summary>
        public void ClearFocus(bool immediate = false)
        {
            if (!hasFocus && focusLocalCurrent == Vector3.zero) return;
            hasFocus = false;
            focusLocal = Vector3.zero;
            ResetView(immediate);
            if (immediate) focusLocalCurrent = Vector3.zero;
        }

        float HalfFovMin() =>
            Mathf.Min(viewCamera.fieldOfView, Camera.VerticalToHorizontalFieldOfView(viewCamera.fieldOfView, Mathf.Max(0.1f, viewCamera.aspect))) * 0.5f * Mathf.Deg2Rad;

        /// <summary>有焦点时的最近距离：不小于零件本身取景距离的一半，并且镜头不能进到物品里（按渲染器包围盒在视线方向上的厚度估算）。</summary>
        float FocusMinDistance()
        {
            var look = LookPoint;
            float ahead = 0f;
            foreach (var r in itemRenderers)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                var b = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    ahead = Mathf.Max(ahead, Vector3.Dot(c - look, viewDirection));
                }
            }
            float byPart = hasFocus ? focusDistance * 0.5f : MinDistance;
            return Mathf.Max(byPart, ahead + viewCamera.nearClipPlane + nearClipMargin);
        }

        public void Rotate(Vector2 deltaDegrees)
        {
            targetYaw -= deltaDegrees.x;
            targetPitch = Mathf.Clamp(targetPitch + deltaDegrees.y, -pitchLimit, pitchLimit);
        }

        /// <summary>正数拉近，负数拉远，单位为滚轮格。</summary>
        public void Zoom(float steps)
        {
            targetDistance = Mathf.Clamp(targetDistance * (1f - steps * zoomStepFraction), hasFocus ? LowerDistanceLimit : MinDistance, MaxDistance);
        }

        public void SnapToTarget()
        {
            yaw = targetYaw;
            pitch = targetPitch;
            if (hasFocus || focusLocalCurrent != Vector3.zero)
            {
                focusLocalCurrent = focusLocal;
                ApplyTransforms();                           // 先转到目标角度，再按该角度计算焦点的最近距离
                distance = Mathf.Clamp(targetDistance, LowerDistanceLimit, Mathf.Max(LowerDistanceLimit, MaxDistance));
            }
            else distance = targetDistance;
            ApplyTransforms();
        }

        void ComputeDistanceLimits()
        {
            float halfFov = Mathf.Min(viewCamera.fieldOfView, Camera.VerticalToHorizontalFieldOfView(viewCamera.fieldOfView, Mathf.Max(0.1f, viewCamera.aspect))) * 0.5f * Mathf.Deg2Rad;
            float fitDistance = ItemRadius / Mathf.Sin(halfFov);
            MinDistance = Mathf.Max(ItemRadius + viewCamera.nearClipPlane + nearClipMargin, fitDistance * minFitMultiplier);
            MaxDistance = Mathf.Max(MinDistance + 0.01f, fitDistance * maxFitMultiplier);
            defaultDistance = Mathf.Clamp(fitDistance * defaultFitMultiplier, MinDistance, MaxDistance);
        }

        void Update()
        {
            if (!InteractionEnabled || CurrentItem == null)
            {
                pressActive = dragging = false;
                return;
            }

            bool startPress = (RepairInput.LeftPressed || RepairInput.RightPressed) && !RepairInput.PointerOverUI;
            if (startPress)
            {
                pressActive = true;
                dragging = false;
                pressPosition = RepairInput.PointerPosition;
            }

            if (pressActive)
            {
                bool held = RepairInput.LeftHeld || RepairInput.RightHeld;
                if (held)
                {
                    if (!dragging && (RepairInput.PointerPosition - pressPosition).magnitude > dragThresholdPixels)
                        dragging = true;
                    if (dragging)
                        Rotate(RepairInput.PointerDelta * degreesPerPixel);
                }
                else
                {
                    if (!dragging && RepairInput.LeftReleased) Clicked?.Invoke(RepairInput.PointerPosition);
                    pressActive = dragging = false;
                }
            }

            float scroll = RepairInput.ScrollSteps;
            if (scroll != 0f && !RepairInput.PointerOverUI) Zoom(scroll);
        }

        void LateUpdate()
        {
            float t = 1f - Mathf.Exp(-smoothing * Time.unscaledDeltaTime);
            yaw = Mathf.Lerp(yaw, targetYaw, t);
            pitch = Mathf.Lerp(pitch, targetPitch, t);
            if (hasFocus || focusLocalCurrent != Vector3.zero)
            {
                focusLocalCurrent = Vector3.Lerp(focusLocalCurrent, focusLocal, t);
                if (!hasFocus && focusLocalCurrent.sqrMagnitude < 1e-10f) focusLocalCurrent = Vector3.zero;
                float lower = LowerDistanceLimit;
                distance = Mathf.Clamp(Mathf.Lerp(distance, targetDistance, t), lower, Mathf.Max(lower, MaxDistance));
            }
            else distance = Mathf.Clamp(Mathf.Lerp(distance, targetDistance, t), MinDistance, MaxDistance);
            ApplyTransforms();
        }

        void ApplyTransforms()
        {
            Vector3 right = Vector3.Cross(Vector3.up, -viewDirection);
            if (right.sqrMagnitude < 1e-6f) right = Vector3.right;
            itemAnchor.rotation = Quaternion.AngleAxis(pitch, right.normalized) * Quaternion.AngleAxis(yaw, Vector3.up);
            // 无焦点时 focusLocalCurrent 为零，TransformPoint 就是锚点位置（与原来相同）
            viewCamera.transform.position = itemAnchor.TransformPoint(focusLocalCurrent) + viewDirection * distance;
            viewCamera.transform.rotation = Quaternion.LookRotation(-viewDirection, Vector3.up);
        }

        /// <summary>
        /// 世界空间包围盒，包含当前隐藏的外观（如换件后才显示的新天线），
        /// 这样换件前后都使用同一套缩放上下限，新部件不会超出画面。
        /// </summary>
        static Bounds CalculateBounds(GameObject go)
        {
            bool has = false;
            var bounds = new Bounds(go.transform.position, Vector3.zero);
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || mf.GetComponent<Renderer>() == null) continue;
                var m = mf.transform.localToWorldMatrix;
                var b = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(corner);
                    if (!has) { bounds = new Bounds(p, Vector3.zero); has = true; }
                    else bounds.Encapsulate(p);
                }
            }
            return has ? bounds : new Bounds(go.transform.position, Vector3.one * 0.2f);
        }
    }
}
