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
        /// <summary>清洁等连续工具占用左键时，只允许右键旋转；不会在松开左键时触发扫描。</summary>
        public bool LeftPointerReserved { get; set; }
        public GameObject CurrentItem { get; private set; }
        public float ItemRadius { get; private set; } = 0.2f;
        public float MinDistance { get; private set; }
        public float MaxDistance { get; private set; }
        public float TargetDistance => targetDistance;
        public float Pitch => pitch;
        public Camera ViewCamera => viewCamera;
        /// <summary>物品挂载点（统一诊所把检视台整体挪到房间维修台上时用）。</summary>
        public Transform ItemAnchor => itemAnchor;

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
        bool benchMounted;
        Vector3[] supportLocalCorners = new Vector3[0];
        Vector3 anchorRestLocal;
        float lift;
        const float HoverClearance = 0.02f;

        public bool BenchMounted => benchMounted;
        /// <summary>台面模式下物品被拿起的高度（0 = 放在台面上）。</summary>
        public float Lift => lift;

        // Opt-in for the physical bench: turning the item lifts it off the bench so it can spin freely in the air,
        // while the camera holds still. Support is measured from the assembled meshes, so parts flying off do not
        // change how it rests.
        public void SetBenchMounted(bool enabled)
        {
            if (enabled && !benchMounted)
            {
                distance = targetDistance = defaultDistance = Mathf.Clamp(defaultDistance * 1.15f, MinDistance, MaxDistance);
                anchorRestLocal = itemAnchor.localPosition;
            }
            if (!enabled && benchMounted) itemAnchor.localPosition = anchorRestLocal;
            benchMounted = enabled;
            lift = 0f;
            supportLocalCorners = new Vector3[0];
            if (!enabled || CurrentItem == null) return;
            var corners = new System.Collections.Generic.List<Vector3>();
            foreach (var filter in CurrentItem.GetComponentsInChildren<MeshFilter>())
            {
                var renderer = filter.GetComponent<Renderer>();
                if (renderer == null || !renderer.enabled || filter.sharedMesh == null) continue;
                var bounds = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    corners.Add(CurrentItem.transform.InverseTransformPoint(filter.transform.TransformPoint(corner)));
                }
            }
            supportLocalCorners = corners.ToArray();
        }

        /// <summary>
        /// 物品放下（未旋转、未拿起）时最低点的世界高度。拿起和转动都不改变它，所以台面贴合不会跟着旋转上下移动镜头。
        /// </summary>
        public bool TryGetStableSupportBottom(out float bottom)
        {
            bottom = float.PositiveInfinity;
            if (!TryGetRestExtent(out float restBottom, out _, out _)) return false;
            bottom = RestPivot().y + restBottom;
            return true;
        }

        // Measured in the anchor's unrotated frame around the assembled meshes' centre (the turning pivot):
        // the pivot itself, the resting bottom below it, and the farthest assembled corner from it (the
        // clearance any held orientation needs). Hidden alternate-state meshes are left out, so the item
        // turns about the middle of what the player actually sees.
        bool TryGetRestExtent(out float restBottom, out float radius, out Vector3 pivotLocal)
        {
            restBottom = 0f; radius = 0f; pivotLocal = Vector3.zero;
            if (!benchMounted || supportLocalCorners.Length == 0 || CurrentItem == null || CurrentItem.transform.parent != itemAnchor) return false;
            var item = CurrentItem.transform;
            var box = new Bounds(item.localPosition + item.localRotation * Vector3.Scale(item.localScale, supportLocalCorners[0]), Vector3.zero);
            foreach (var p in supportLocalCorners)
                box.Encapsulate(item.localPosition + item.localRotation * Vector3.Scale(item.localScale, p));
            pivotLocal = box.center;
            float scale = itemAnchor.lossyScale.y;
            restBottom = (box.min.y - pivotLocal.y) * scale;
            foreach (var p in supportLocalCorners)
            {
                var local = item.localPosition + item.localRotation * Vector3.Scale(item.localScale, p);
                radius = Mathf.Max(radius, (local - pivotLocal).magnitude * scale);
            }
            return true;
        }

        Vector3 RestPivot()
        {
            var parent = itemAnchor.parent;
            return parent != null ? parent.TransformPoint(anchorRestLocal) : itemAnchor.position;
        }

        float TargetLift()
        {
            bool turned = dragging || Mathf.Abs(Mathf.DeltaAngle(0f, targetYaw)) > .5f || Mathf.Abs(targetPitch) > .5f
                || Mathf.Abs(Mathf.DeltaAngle(0f, yaw)) > .5f || Mathf.Abs(pitch) > .5f;
            if (!turned || !TryGetRestExtent(out float restBottom, out float radius, out _)) return 0f;
            return Mathf.Max(0f, radius + restBottom) + HoverClearance;
        }

        public void FrameBenchOverview()
        {
            if (!benchMounted) return;
            targetDistance = defaultDistance = MaxDistance;
            SnapToTarget();
        }

        public bool HasFocus => hasFocus;
        /// <summary>镜头当前对准的世界坐标（无焦点时就是物品中心）。</summary>
        public Vector3 LookPoint => itemAnchor.TransformPoint(focusLocalCurrent != Vector3.zero || !TryGetRestExtent(out _, out _, out var pivot)
            ? focusLocalCurrent : pivot);
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
            if (benchMounted) itemAnchor.localPosition = anchorRestLocal;
            benchMounted = false;
            lift = 0f;
            supportLocalCorners = new Vector3[0];
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
            if (benchMounted) lift = TargetLift();
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

            if (LeftPointerReserved && !RepairInput.RightHeld) pressActive = dragging = false;
            bool startPress = ((!LeftPointerReserved && RepairInput.LeftPressed) || RepairInput.RightPressed) && !RepairInput.PointerOverUI;
            if (startPress)
            {
                pressActive = true;
                dragging = false;
                pressPosition = RepairInput.PointerPosition;
            }

            if (pressActive)
            {
                bool held = (!LeftPointerReserved && RepairInput.LeftHeld) || RepairInput.RightHeld;
                if (held)
                {
                    if (!dragging && (RepairInput.PointerPosition - pressPosition).magnitude > dragThresholdPixels)
                        dragging = true;
                    if (dragging)
                        Rotate(RepairInput.PointerDelta * degreesPerPixel);
                }
                else
                {
                    if (!LeftPointerReserved && !dragging && RepairInput.LeftReleased) Clicked?.Invoke(RepairInput.PointerPosition);
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
            if (benchMounted) lift = Mathf.Lerp(lift, TargetLift(), t);
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
            // 台面模式：绕可见模型的中心转，物品被拿起离开台面；镜头仍按放下时的位置取景，不跟着上下移动
            if (benchMounted && TryGetRestExtent(out _, out _, out var pivot))
            {
                var rest = RestPivot();
                itemAnchor.position += rest + Vector3.up * lift - itemAnchor.TransformPoint(pivot);
                var look = focusLocalCurrent != Vector3.zero ? itemAnchor.TransformPoint(focusLocalCurrent) - Vector3.up * lift : rest;
                viewCamera.transform.position = look + viewDirection * distance;
                viewCamera.transform.rotation = Quaternion.LookRotation(-viewDirection, Vector3.up);
                return;
            }
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
