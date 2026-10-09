using System.Collections;
using System.Linq;
using BorderRepair.Dock;
using UnityEngine;

namespace BorderRepair.FirstOrder
{
    /// <summary>整引擎维修时左引擎在哪：七号上 → 拔线松键 → 抱在手上 → 维修台上 → 抱回 → 装回七号 → 接回线缆。</summary>
    public enum EngineMountState { OnRobot, Unplugged, InHands, OnBench, InHandsBack, Mounted, Reconnected }

    /// <summary>
    /// 整引擎维修（统一诊所第二晚开启；其他场景不开，流程和原来一样）：
    /// 检查后先拔下左引擎内侧的辅助线缆、松开连接键，把整个左引擎卸下来抱在手上（跟着视角，碰撞关闭不挡准星），
    /// 抱到维修台放下，在台上做原来的拆修步骤（锁扣、上盖、轴承、进气口都随引擎在台上，零件只在台面上挪动），
    /// 修好后抱回七号装上、接回线缆，才能通电复测。
    /// 占位表现：没有手部动作；拔线松键用连接键和插头沿外法线移出 8 mm 表示。
    /// </summary>
    public partial class FirstOrderFlow
    {
        /// <summary>抱着时引擎中心在眼睛坐标里的位置（右、上、前，米）。</summary>
        public static readonly Vector3 EngineHoldLocal = new Vector3(0f, -0.30f, 0.62f);
        const float EngineHandleSeconds = 0.45f, ConnectorTravel = 0.008f;

        FirstOrderPart engineConnector;
        FirstOrderDropZone engineBenchZone, engineMountZone;
        Vector3 engineFacePoint;
        Transform engineHomeParent;
        Vector3 engineHomeLocalPos;
        Quaternion engineHomeLocalRot;
        Vector3 latchesDirLocal;
        Vector3 engineHomeCenter;
        Collider[] engineColliders = new Collider[0];
        bool[] engineColliderEnabled = new bool[0];
        bool robotAnimatorWasEnabled = true;
        (Transform t, Vector3 home)[] connectorPieces = new (Transform, Vector3)[0];
        Vector3 connectorOutLocal;

        public bool WholeEngineMode => engineConnector != null;
        public EngineMountState EngineState { get; private set; } = EngineMountState.OnRobot;
        public FirstOrderPart EngineConnector => engineConnector;
        public FirstOrderDropZone EngineBenchZone => engineBenchZone;
        public FirstOrderDropZone EngineMountZone => engineMountZone;
        public Vector3 EngineFacePoint => engineFacePoint;
        /// <summary>开启整引擎模式时（引擎在七号上）引擎包围盒中心。</summary>
        public Vector3 EngineHomeCenter => engineHomeCenter;
        /// <summary>引擎装回七号后的原位（世界坐标）。</summary>
        public Pose EngineHomeWorldPose => engineHomeParent == null ? new Pose(engineLHinge.position, engineLHinge.rotation)
            : new Pose(engineHomeParent.TransformPoint(engineHomeLocalPos), engineHomeParent.rotation * engineHomeLocalRot);

        /// <summary>开启整引擎维修。connector：左引擎内侧连接键（成员：螺栓、辅助线缆插头）；benchSpot：维修台上放引擎的落点；mount：七号左侧安装位；facePoint：玩家站在维修台前的位置（引擎外侧锁扣朝它）。</summary>
        public void EnableWholeEngineMode(FirstOrderPart connector, FirstOrderDropZone benchSpot, FirstOrderDropZone mount, Vector3 facePoint)
        {
            engineConnector = connector; engineBenchZone = benchSpot; engineMountZone = mount; engineFacePoint = facePoint;
            engineHomeParent = engineLHinge.parent; engineHomeLocalPos = engineLHinge.localPosition; engineHomeLocalRot = engineLHinge.localRotation;
            var c = EngineBounds().center;
            engineHomeCenter = c;
            // 两个锁扣的中间方向（放到维修台上时朝向玩家：外侧、后侧锁扣都能从台前看到）
            var dirs = Vector3.ProjectOnPlane(latchOuter.WorldBounds().center - c, Vector3.up).normalized + Vector3.ProjectOnPlane(latchRear.WorldBounds().center - c, Vector3.up).normalized;
            latchesDirLocal = engineLHinge.InverseTransformDirection(dirs.normalized);
            connectorPieces = new[] { connector.transform }.Concat(connector.members).Where(t => t != null).Select(t => (t, t.localPosition)).ToArray();
            var outWorld = connector.WorldBounds().center - c; outWorld.y = 0f;
            connectorOutLocal = connector.transform.parent.InverseTransformDirection(outWorld.normalized * ConnectorTravel);
            EngineState = EngineMountState.OnRobot;
            SetZone(benchSpot, false); SetZone(mount, false);
        }

        static void SetZone(FirstOrderDropZone z, bool on)
        {
            if (z == null) return;
            foreach (var c in z.GetComponents<Collider>()) c.enabled = on;
            z.ShowMarker(on);
        }

        /// <summary>左引擎（含挂在它下面的零件和故障美术件）当前的世界包围盒。</summary>
        public Bounds EngineBounds()
        {
            var rs = engineLHinge.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            if (rs.Length == 0) return new Bounds(engineLHinge.position, Vector3.zero);
            var b = rs[0].bounds; foreach (var r in rs.Skip(1)) b.Encapsulate(r.bounds); return b;
        }

        bool IsEngineTarget(Component hit) =>
            hit is FirstOrderPart p && p != newBearing && p != rightEngine &&
                (p == engineConnector || p == cover || p == latchOuter || p == latchRear || p == bearing || p == clog || p.transform.IsChildOf(engineLHinge)) ||
            hit is DockInteractable d && d.action == DockAction.EngineLeft;

        /// <summary>整引擎维修的点击处理；返回 false 表示交给原来的流程。</summary>
        bool TryWholeEngine(Component hit, out bool result)
        {
            result = false;
            string n = hit is FirstOrderPart fp ? fp.realPath : hit.name;
            bool engineHit = IsEngineTarget(hit);
            bool zoneHit = hit is FirstOrderDropZone;
            switch (EngineState)
            {
                case EngineMountState.OnRobot:
                    if (Step <= FoStep.InspectLeftEngine || InspectionLocked) return false;
                    if (hit == engineConnector || engineHit)   // 连接键夹在引擎和机身之间、不好点：点左引擎任何地方都算“拔线松键”
                    {
                        if (!EngineSafe) { result = Refuse(n, "叶轮还没停稳，不能拆引擎。"); return true; }
                        StartCoroutine(MoveConnector(true));
                        EngineState = EngineMountState.Unplugged;
                        result = Ok(n, "拔下左引擎内侧的辅助线缆、松开连接键。再点左引擎，把整个引擎卸下来抱在手上。");
                        return true;
                    }
                    if (zoneHit) { result = Refuse(n, "左引擎还在七号上：先卸下来拿到维修台。"); return true; }
                    return false;
                case EngineMountState.Unplugged:
                    if (engineHit)
                    {
                        StartCoroutine(PickUpEngine(EngineMountState.InHands));
                        result = Ok(n, "把左引擎整个卸下来，抱在手上。走到维修台，准星对准台面左键放下。");
                        return true;
                    }
                    if (zoneHit) { result = Refuse(n, "先把左引擎卸下来（点左引擎）。"); return true; }
                    return false;
                case EngineMountState.InHands:
                case EngineMountState.InHandsBack:
                    bool back = EngineState == EngineMountState.InHandsBack;
                    if (!back && hit == engineBenchZone)
                    {
                        StartCoroutine(PlaceEngineOnBench());
                        result = Ok(n, "左引擎放到维修台上。扳开外侧、后侧两个锁扣，取下上盖检查。");
                        return true;
                    }
                    if (back && hit == engineMountZone)
                    {
                        StartCoroutine(MountEngine());
                        result = Ok(n, "左引擎装回七号左侧。点连接键：插回辅助线缆、拧紧连接键。");
                        return true;
                    }
                    result = Refuse(n, back ? "手上抱着修好的左引擎：对准七号左侧的安装位装回。" : "手上抱着左引擎：先放到维修台上（准星对准台面）。");
                    return true;
                case EngineMountState.OnBench:
                    if (hit == engineConnector) { result = Refuse(n, "引擎在维修台上：连接键要等装回七号以后再接。"); return true; }
                    if (Step == FoStep.PowerOn && engineHit && (hit != clog || ClogCleared))
                    {
                        StartCoroutine(PickUpEngine(EngineMountState.InHandsBack));
                        result = Ok(n, "修好的左引擎抱起来了。走回七号，准星对准左侧安装位装回。");
                        return true;
                    }
                    return false;
                case EngineMountState.Mounted:
                    if (hit == engineConnector || engineHit && hit != clog)   // 装回后点引擎任何地方都算“接回线缆、拧紧连接键”
                    {
                        StartCoroutine(MoveConnector(false));
                        EngineState = EngineMountState.Reconnected;
                        result = Ok(n, "插回辅助线缆、拧紧连接键。" + (ClogCleared ? "点断电开关：通电。" : "进气口还堵着：先清理，再通电。"));
                        return true;
                    }
                    return false;
                default:
                    if (hit == engineConnector) { result = Refuse(n, "连接键已经接好了。"); return true; }
                    return false;
            }
        }

        /// <summary>通电前的整引擎检查：引擎必须装回七号并接好线缆。返回拒绝原因，没有问题时为 null。</summary>
        string WholeEnginePowerBlock()
        {
            if (!WholeEngineMode || EngineState == EngineMountState.Reconnected) return null;
            switch (EngineState)
            {
                case EngineMountState.OnBench: return "左引擎还在维修台上：先抱回七号装上。";
                case EngineMountState.InHands: case EngineMountState.InHandsBack: return "左引擎还在手上：先装回七号。";
                case EngineMountState.Mounted: return "左引擎装回了，但辅助线缆和连接键还没接：先点左引擎接上。";
                default: return "左引擎还没装好。";
            }
        }

        string WholeEngineHint()
        {
            if (!WholeEngineMode || Step <= FoStep.InspectLeftEngine) return null;
            switch (EngineState)
            {
                case EngineMountState.OnRobot: return Step < FoStep.PowerOn ? "点左引擎：拔下辅助线缆、松开连接键" : null;
                case EngineMountState.Unplugged: return "点左引擎：整个卸下来抱在手上";
                case EngineMountState.InHands: return "抱到维修台，准星对准台面左键放下";
                case EngineMountState.OnBench:
                    if (Step == FoStep.ReleaseLatches) return "扳开外侧、后侧两个锁扣（引擎在维修台上）";
                    if (Step == FoStep.CloseLatches) return "扣回外侧、后侧两个锁扣";
                    if (Step == FoStep.PowerOn) return ClogCleared ? "点左引擎：抱起来装回七号" : "先点进气口清理堵塞，再把引擎装回七号";
                    return null;
                case EngineMountState.InHandsBack: return "走回七号，准星对准左侧安装位装回";
                case EngineMountState.Mounted: return "点左引擎：插回辅助线缆、拧紧连接键";
                default: return null;
            }
        }

        Transform HoldAnchor => rig != null && rig.Walker != null ? rig.Walker.Eye : null;

        void SetEngineColliders(bool on)
        {
            if (!on)
            {
                engineColliders = engineLHinge.GetComponentsInChildren<Collider>(true);
                engineColliderEnabled = engineColliders.Select(c => c.enabled).ToArray();
                foreach (var c in engineColliders) c.enabled = false;        // 抱着时不挡准星的射线
            }
            else for (int i = 0; i < engineColliders.Length; i++) if (engineColliders[i] != null) engineColliders[i].enabled = engineColliderEnabled[i];
        }

        IEnumerator MoveConnector(bool open)
        {
            Busy = true;
            var from = connectorPieces.Select(p => p.t.localPosition).ToArray();
            for (float t = 0f; t < 0.3f; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / 0.3f);
                for (int i = 0; i < connectorPieces.Length; i++)
                    connectorPieces[i].t.localPosition = Vector3.Lerp(from[i], connectorPieces[i].home + (open ? connectorOutLocal : Vector3.zero), k);
                yield return null;
            }
            for (int i = 0; i < connectorPieces.Length; i++) connectorPieces[i].t.localPosition = connectorPieces[i].home + (open ? connectorOutLocal : Vector3.zero);
            engineConnector.Location = open ? PartLocation.Released : PartLocation.Installed;
            engineConnector.LocationDetail = open ? "已拔下辅助线缆、松开连接键" : "已插回辅助线缆、拧紧连接键";
            Busy = false;
        }

        IEnumerator PickUpEngine(EngineMountState next)
        {
            Busy = true;
            var anchor = HoldAnchor;
            if (next == EngineMountState.InHands && dock.RobotAnimator != null)
            {
                // 引擎拿下来以后不让七号的动画再驱动这个节点（装回时恢复）
                robotAnimatorWasEnabled = dock.RobotAnimator.enabled;
                dock.RobotAnimator.enabled = false;
            }
            SetEngineColliders(false);
            EngineState = next;
            SetZone(next == EngineMountState.InHands ? engineBenchZone : engineMountZone, true);
            engineLHinge.SetParent(anchor != null ? anchor : partsCarrier, true);
            if (anchor != null)
            {
                var from = engineLHinge.localPosition;
                var to = from + (EngineHoldLocal - anchor.InverseTransformPoint(EngineBounds().center));
                for (float t = 0f; t < EngineHandleSeconds; t += Time.deltaTime)
                {
                    engineLHinge.localPosition = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / EngineHandleSeconds));
                    yield return null;
                }
                engineLHinge.localPosition = to;
            }
            Busy = false;
        }

        /// <summary>放在维修台上的位姿：和装在七号上一样直立，绕竖直轴转到两个锁扣的中间方向朝向玩家；包围盒中心对准落点、最低点贴着台面。</summary>
        public Quaternion EngineBenchRotation(Vector3 spot)
        {
            var home = EngineHomeWorldPose;
            var want = Vector3.ProjectOnPlane(engineFacePoint - spot, Vector3.up);
            var latch = Vector3.ProjectOnPlane(home.rotation * latchesDirLocal, Vector3.up);
            return (want.sqrMagnitude > 1e-6f && latch.sqrMagnitude > 1e-6f ? Quaternion.FromToRotation(latch.normalized, want.normalized) : Quaternion.identity) * home.rotation;
        }

        /// <summary>按放到维修台上的朝向，引擎包围盒的尺寸（找空位用）。</summary>
        public Vector3 EngineBenchSize(Vector3 spot)
        {
            var savedPos = engineLHinge.position; var savedRot = engineLHinge.rotation;
            engineLHinge.rotation = EngineBenchRotation(spot);
            var size = EngineBounds().size;
            engineLHinge.SetPositionAndRotation(savedPos, savedRot);
            return size;
        }

        public Pose EngineBenchPose()
        {
            var spot = engineBenchZone.landing.position;
            var rot = EngineBenchRotation(spot);
            var savedPos = engineLHinge.position; var savedRot = engineLHinge.rotation;
            engineLHinge.rotation = rot;
            var b = EngineBounds();
            var pos = engineLHinge.position + new Vector3(spot.x - b.center.x, spot.y + 0.001f - b.min.y, spot.z - b.center.z);
            engineLHinge.SetPositionAndRotation(savedPos, savedRot);
            return new Pose(pos, rot);
        }

        IEnumerator PlaceEngineOnBench()
        {
            Busy = true;
            SetZone(engineBenchZone, false);
            var target = EngineBenchPose();
            engineLHinge.SetParent(partsCarrier, true);
            yield return MoveEngine(target.position, target.rotation);
            SetEngineColliders(true);
            EngineState = EngineMountState.OnBench;
            Busy = false;
        }

        IEnumerator MountEngine()
        {
            Busy = true;
            SetZone(engineMountZone, false);
            var home = EngineHomeWorldPose;
            engineLHinge.SetParent(partsCarrier, true);
            yield return MoveEngine(home.position, home.rotation);
            engineLHinge.SetParent(engineHomeParent, false);
            engineLHinge.localPosition = engineHomeLocalPos;
            engineLHinge.localRotation = engineHomeLocalRot;
            SetEngineColliders(true);
            if (dock.RobotAnimator != null) dock.RobotAnimator.enabled = robotAnimatorWasEnabled;
            EngineState = EngineMountState.Mounted;
            Busy = false;
        }

        IEnumerator MoveEngine(Vector3 pos, Quaternion rot)
        {
            Vector3 p0 = engineLHinge.position; Quaternion r0 = engineLHinge.rotation;
            for (float t = 0f; t < EngineHandleSeconds; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / EngineHandleSeconds);
                engineLHinge.SetPositionAndRotation(Vector3.Lerp(p0, pos, k), Quaternion.Slerp(r0, rot, k));
                yield return null;
            }
            engineLHinge.SetPositionAndRotation(pos, rot);
        }
    }
}
