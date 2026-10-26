using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BorderRepair.RepairProps
{
    // The surgery patient getting onto the V2 table and off again, for validating the flow before the real character
    // (the user's own Tripo model) exists. The patient is a jointed placeholder whose joints carry Unity Humanoid bone
    // names (Hips, Spine, Chest, Neck, Head, Left/Right Upper/Lower Leg, Foot, Upper/Lower Arm, Hand), so a rigged model
    // can later take the same poses. Flow (罗亚 / Luo, the dock loader whose leased right arm was remotely locked):
    //   walk in from the east entrance round the instrument cart to the west side of the table -> (if the restraints
    //   are still buckled, wait there facing the table until the player opens them all) -> turn his back to it
    //   -> sit on the edge -> swing the legs up (long sit) -> lie back, the upper body following the table's back
    //   section (chair pose or flat, live) -> wait while the player buckles the three restraints -> secured.
    //   Opening every strap again reverses it: sit up, swing out, stand, walk back out and leave (not while the
    //   operation holds him: SurgeryProcedureRuntime). While secured his right forearm carries a clickable surgery site
    //   (it opens the close-up surgery view). His right forearm is a detailed cyber arm on a body-side socket
    //   (SurgeryCyberArm), so the operation can take it apart part by part.
    // Begin() starts it (F7 in the editor / development builds); visual only, no colliders, nothing saved.
    [AddComponentMenu("")]
    public sealed class SurgeryPatientRuntime : MonoBehaviour
    {
        public const string ScenePath = RepairBenchV2Runtime.ScenePath;
        public enum Phase { Idle, WalkIn, WaitClear, TurnAround, SitDown, SwingIn, LieBack, AwaitStraps, Secured, SitUp, SwingOut, StandUp, WalkOut }

        public static SurgeryPatientRuntime Current { get; private set; }

        // Room path (floor points) from the east entrance round the instrument cart's west end to the bedside.
        static readonly Vector3[] InPath = { new Vector3(.30f, 0, -3.05f), new Vector3(.10f, 0, -2.72f), new Vector3(-.78f, 0, -2.20f), new Vector3(-.95f, 0, -1.35f) };
        const float WalkSpeed = .85f;
        // Body (metres; 罗亚 is 1.74 m): joint offsets in the body's own frame (up +Y, facing +Z).
        const float HipHeight = .92f, Thigh = .44f, Shin = .43f, SpineLen = .10f, ChestLen = .18f, NeckLen = .22f, HeadLen = .10f;
        const float UpperArm = .29f, Forearm = .26f, ShoulderX = .19f, ShoulderY = .20f, HipX = .09f;
        // Where the hips sit along the table (table frame, metres from the centre toward the head): the back section
        // hinges at +0.11, so the spine bends there; head on the back/head pads, heels at the leg pad's end.
        const float HipsAlong = 0f, EdgeOut = .20f, StandOut = .62f, SeatLift = .08f, LieLift = .11f;

        public enum Bone { Hips, Spine, Chest, Neck, Head, LeftUpperLeg, LeftLowerLeg, LeftFoot, RightUpperLeg, RightLowerLeg, RightFoot, LeftUpperArm, LeftLowerArm, LeftHand, RightUpperArm, RightLowerArm, RightHand }

        sealed class Pose
        {
            public Vector3 hips;
            public Quaternion facing;
            public readonly Dictionary<Bone, Vector3> euler = new Dictionary<Bone, Vector3>();
            public Pose Set(Bone b, float x, float y = 0, float z = 0) { euler[b] = new Vector3(x, y, z); return this; }
        }

        readonly Dictionary<Bone, Transform> bones = new Dictionary<Bone, Transform>();
        readonly List<Vector3> path = new List<Vector3>();
        GameObject patient;
        Material material;
        SurgeryCyberArm kit;
        Phase phase;
        float phaseTime, walkPhase;
        Pose from;
        Vector3 walkFacing = Vector3.forward;
        bool wasSecured;
        GameObject site;
        GameObject hintCanvas;
        Text hintText;
        RectTransform hintTag;

        public Phase PhaseNow => phase;
        public GameObject Patient => patient;
        public Transform BoneTransform(Bone bone) => bones.TryGetValue(bone, out var t) ? t : null;
        public bool Busy => phase != Phase.Idle;
        /// <summary>What the player should do for the patient now (null when nothing).</summary>
        public string Hint => SurgeryCloseupRuntime.Current != null && SurgeryCloseupRuntime.Current.Active ? null   // the close-up has its own guide
            : phase == Phase.WaitClear ? "罗亚在床边等着：先解开三条束带"
            : phase == Phase.AwaitStraps ? "罗亚已躺好：扣上三条束带 (" + (Table != null ? Table.StrapsClosed : 0) + "/3)"
            : phase == Phase.Secured && SurgeryProcedureRuntime.Current != null ? SurgeryProcedureRuntime.Current.Hint : null;
        /// <summary>True while something (the operation) must keep the patient on the table even with the straps open.</summary>
        public static Func<bool> HoldOnTable;
        bool Held => HoldOnTable != null && HoldOnTable();
        /// <summary>The detachable right forearm + hand (the leased cyber arm, or its replacement once fitted).</summary>
        public Transform CyberArm { get; private set; }
        /// <summary>0 open .. 1 a fist: the fitted cyber hand's fingers (the locked arm's are frozen half-closed).</summary>
        public float RightHandCurl
        {
            get => SurgeryCyberArm.GetCurl(CyberArm);
            set => SurgeryCyberArm.SetCurl(CyberArm, value);
        }
        /// <summary>Builds his arm parts (body-side socket, forearms).</summary>
        public SurgeryCyberArm Kit => kit != null ? kit : kit = new SurgeryCyberArm();
        /// <summary>The clickable surgery site on the right forearm (active only while secured).</summary>
        public GameObject Site => site;
        /// <summary>World seat side the patient uses: the table's left (-Right), away from the hand pendant.</summary>
        public Vector3 SideDir => Table != null ? -Table.Right : Vector3.left;
        public event Action<Phase> PhaseChanged;

        static SurgeryTableV2Runtime Table => SurgeryTableV2Runtime.Current;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetHooks() { SceneManager.sceneLoaded -= OnSceneLoaded; Current = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install() => SceneManager.sceneLoaded += OnSceneLoaded;

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!string.Equals(scene.path, ScenePath, StringComparison.OrdinalIgnoreCase)) return;
            if (UnityEngine.XR.XRSettings.isDeviceActive) return;
            var host = new GameObject("SurgeryPatient (runtime only)");
            SceneManager.MoveGameObjectToScene(host, scene);
            host.AddComponent<SurgeryPatientRuntime>();
        }

        void Awake() => Current = this;

        /// <summary>Brings the patient in. False while one is already on the way, or without the V2 table.</summary>
        public bool Begin()
        {
            if (Busy || Table == null || Table.Table == null) return false;
            // A fresh patient each time: the last one may have left with a replaced arm.
            if (patient != null) { Destroy(patient); bones.Clear(); }
            Build();
            patient.SetActive(true);
            path.Clear();
            path.AddRange(InPath);
            path.Add(StandPoint());
            walkFacing = (InPath[1] - InPath[0]).normalized;
            SetPose(StandingAt(InPath[0], walkFacing));
            Enter(Phase.WalkIn);
            return true;
        }

        /// <summary>Gets the patient up and out (also happens when every strap is opened again after securing).</summary>
        public bool Release()
        {
            if ((phase != Phase.AwaitStraps && phase != Phase.Secured) || Held) return false;
            Enter(Phase.SitUp);
            return true;
        }

        void Enter(Phase next)
        {
            from = Capture();
            phase = next;
            phaseTime = 0;
            if (next == Phase.Secured) wasSecured = true;
            if (next == Phase.Idle) wasSecured = false;
            if (site != null) site.SetActive(next == Phase.Secured);
            PhaseChanged?.Invoke(next);
        }

        void Update()
        {
            if (Debug.isDebugBuild && Keyboard.current != null && Keyboard.current.f7Key.wasPressedThisFrame) Begin();
            ShowHint();
            if (phase == Phase.Idle || patient == null || Table == null) return;
            phaseTime += Time.deltaTime;
            FadeNearCamera();
            switch (phase)
            {
                case Phase.WalkIn: if (Walk()) Enter(Table.StrapsClosed > 0 ? Phase.WaitClear : Phase.TurnAround); break;
                case Phase.WaitClear:
                    Blend(StandingAt(StandPoint(), -SideDir), .7f);
                    if (Table.StrapsClosed == 0) Enter(Phase.TurnAround);
                    break;
                case Phase.TurnAround: if (Blend(StandingAt(StandPoint(), SideDir), .7f)) Enter(Phase.SitDown); break;
                case Phase.SitDown: if (Blend(SitEdge(), 1.2f)) Enter(Phase.SwingIn); break;
                case Phase.SwingIn: if (Blend(LongSit(), 1.6f)) Enter(Phase.LieBack); break;
                case Phase.LieBack: if (Blend(Lying(), 1.5f)) Enter(Phase.AwaitStraps); break;
                case Phase.AwaitStraps:
                    SetPose(Lying());
                    if (Table.StrapCount > 0 && Table.StrapsClosed == Table.StrapCount) Enter(Phase.Secured);
                    break;
                case Phase.Secured:
                    SetPose(Lying());
                    if (wasSecured && Table.StrapsClosed == 0 && !Held) Enter(Phase.SitUp);
                    break;
                case Phase.SitUp: if (Blend(LongSit(), 1.4f)) Enter(Phase.SwingOut); break;
                case Phase.SwingOut: if (Blend(SitEdge(), 1.4f)) Enter(Phase.StandUp); break;
                case Phase.StandUp:
                    if (Blend(StandingAt(StandPoint(), SideDir), 1.0f))
                    {
                        path.Clear();
                        for (int i = InPath.Length - 1; i >= 0; i--) path.Add(InPath[i]);
                        Enter(Phase.WalkOut);
                    }
                    break;
                case Phase.WalkOut:
                    if (Walk()) { patient.SetActive(false); Enter(Phase.Idle); }
                    break;
            }
        }

        // A small top-centre line telling the player what the patient is waiting for.
        void ShowHint()
        {
            string hint = Hint;
            if (hintCanvas == null)
            {
                if (hint == null) return;
                hintCanvas = new GameObject("SurgeryPatientHint", typeof(RectTransform));
                SceneManager.MoveGameObjectToScene(hintCanvas, gameObject.scene);
                var canvas = hintCanvas.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 47;
                var scaler = hintCanvas.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = .5f;
                hintTag = (RectTransform)new GameObject("Tag", typeof(RectTransform)).transform;
                hintTag.SetParent(hintCanvas.transform, false);
                hintTag.anchorMin = hintTag.anchorMax = new Vector2(.5f, 1); hintTag.pivot = new Vector2(.5f, 1); hintTag.anchoredPosition = new Vector2(0, -64);
                var bg = hintTag.gameObject.AddComponent<Image>(); bg.raycastTarget = false;
                RepairPropCyberStyle.Framed(bg, RepairPropCyberStyle.Field, RepairPropCyberStyle.Hot, true);
                var label = (RectTransform)new GameObject("Text", typeof(RectTransform)).transform;
                label.SetParent(hintTag, false);
                label.anchorMin = Vector2.zero; label.anchorMax = Vector2.one; label.offsetMin = new Vector2(12, 2); label.offsetMax = new Vector2(-12, -2);
                hintText = label.gameObject.AddComponent<Text>();
                hintText.font = RepairPropCyberStyle.UiFontOr(BorderRepair.UI.UIFontProvider.GetCjkFont(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")));
                hintText.fontSize = 15; hintText.color = RepairPropCyberStyle.Hot; hintText.alignment = TextAnchor.MiddleCenter; hintText.raycastTarget = false;
                hintText.horizontalOverflow = HorizontalWrapMode.Overflow;
            }
            hintCanvas.SetActive(hint != null);
            if (hint == null || hintText.text == hint) return;
            hintText.text = hint;
            hintTag.sizeDelta = new Vector2(hintText.preferredWidth + 28, Mathf.Max(30, hintText.preferredHeight + 12));
        }

        void FadeNearCamera()
        {
            var view = Camera.main;
            if (view == null) return;
            var d = bones[Bone.Hips].position - view.transform.position; d.y = 0;
            bool near = d.magnitude < .45f;
            foreach (var r in patient.GetComponentsInChildren<Renderer>()) if (r.enabled == near) r.enabled = !near;
        }

        // ------------------------------------------------------------------------------------------------ poses
        Vector3 Along(float along, float outward, float height) => Table.Floor + Table.HeadDir * along + SideDir * outward + Vector3.up * height;
        Vector3 StandPoint() { var p = Along(HipsAlong, StandOut, 0); return new Vector3(p.x, 0, p.z); }

        Pose StandingAt(Vector3 floorPoint, Vector3 facing)
        {
            var p = new Pose { hips = floorPoint + Vector3.up * HipHeight, facing = Quaternion.LookRotation(Flat(facing), Vector3.up) };
            return Arms(p.Set(Bone.Spine, 2), 0, 0, 6);
        }

        // On the table's edge with his back to it: thighs level on the pad, shins hanging, hands on the knees.
        Pose SitEdge()
        {
            var p = new Pose { hips = Along(HipsAlong, EdgeOut, SurgeryTableV2Runtime.PadTop + SeatLift), facing = Quaternion.LookRotation(SideDir, Vector3.up) };
            p.Set(Bone.LeftUpperLeg, -88).Set(Bone.RightUpperLeg, -88).Set(Bone.LeftLowerLeg, 82).Set(Bone.RightLowerLeg, 82).Set(Bone.Spine, 6).Set(Bone.Chest, 4).Set(Bone.Neck, -6);
            return Arms(p, -38, -46, 10);
        }

        // Sitting up on the table, legs straight along it toward the foot end.
        Pose LongSit()
        {
            var p = new Pose { hips = Along(HipsAlong, 0, SurgeryTableV2Runtime.PadTop + SeatLift), facing = Quaternion.LookRotation(-Table.HeadDir, Vector3.up) };
            p.Set(Bone.LeftUpperLeg, -86).Set(Bone.RightUpperLeg, -86).Set(Bone.Spine, -4).Set(Bone.Chest, -4);
            return Arms(p, -18, -10, 14);
        }

        // Lying on the pads: body up along the table's head direction, chest facing the ceiling; the spine bends with
        // the back section and the neck with the head cushion, whatever the table's pose is right now.
        Pose Lying()
        {
            // Lifted by the torso's half depth so the back rests on the pads.
            var p = new Pose { hips = Along(HipsAlong, 0, SurgeryTableV2Runtime.PadTop + LieLift), facing = Quaternion.LookRotation(Vector3.up, Table.HeadDir) };
            float back = Table.BackAngle, head = Table.HeadAngle - Table.BackAngle;
            p.Set(Bone.Spine, back * .45f).Set(Bone.Chest, back * .55f).Set(Bone.Neck, head);
            return Arms(p, 0, -8, 9);
        }

        static Pose Arms(Pose p, float shoulderPitch, float elbow, float outward)
        {
            return p.Set(Bone.LeftUpperArm, shoulderPitch, 0, -outward).Set(Bone.RightUpperArm, shoulderPitch, 0, outward)
                    .Set(Bone.LeftLowerArm, elbow).Set(Bone.RightLowerArm, elbow);
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward; }

        // Eased blend from the pose held when the phase began to the (possibly moving) target.
        bool Blend(Pose target, float duration)
        {
            float t = Mathf.Clamp01(phaseTime / duration);
            float e = t * t * (3 - 2 * t);
            var pose = new Pose { hips = Vector3.Lerp(from.hips, target.hips, e), facing = Quaternion.Slerp(from.facing, target.facing, e) };
            // Getting onto the edge goes up and back, not through the table: a small lift mid-move.
            pose.hips += Vector3.up * (.05f * Mathf.Sin(e * Mathf.PI) * (phase == Phase.SitDown || phase == Phase.StandUp ? 1 : 0));
            foreach (Bone b in Enum.GetValues(typeof(Bone)))
            {
                from.euler.TryGetValue(b, out var a);
                target.euler.TryGetValue(b, out var c);
                pose.euler[b] = Vector3.Lerp(a, c, e);
            }
            SetPose(pose);
            return t >= 1;
        }

        // Walks the path at a stroll with a leg/arm swing; true once the last point is reached.
        bool Walk()
        {
            if (path.Count == 0) return true;
            var hips = bones[Bone.Hips].position;
            var feet = new Vector3(hips.x, 0, hips.z);
            var to = path[0] - feet; to.y = 0;
            float step = WalkSpeed * Time.deltaTime;
            if (to.magnitude <= step) { feet = path[0]; path.RemoveAt(0); }
            else
            {
                feet += to.normalized * step;
                walkFacing = Vector3.RotateTowards(walkFacing, to.normalized, 5f * Time.deltaTime, 0);
            }
            walkPhase += Time.deltaTime * 7.5f;
            float swing = Mathf.Sin(walkPhase);
            var pose = new Pose { hips = feet + Vector3.up * (HipHeight - .015f + .015f * Mathf.Abs(Mathf.Cos(walkPhase))), facing = Quaternion.LookRotation(Flat(walkFacing), Vector3.up) };
            pose.Set(Bone.LeftUpperLeg, -24 * swing).Set(Bone.RightUpperLeg, 24 * swing)
                .Set(Bone.LeftLowerLeg, Mathf.Max(0, 34 * Mathf.Sin(walkPhase - 1.2f))).Set(Bone.RightLowerLeg, Mathf.Max(0, -34 * Mathf.Sin(walkPhase - 1.2f)))
                .Set(Bone.Spine, 3);
            pose.Set(Bone.LeftUpperArm, 18 * swing, 0, -6).Set(Bone.RightUpperArm, -18 * swing, 0, 6).Set(Bone.LeftLowerArm, -12).Set(Bone.RightLowerArm, -12);
            SetPose(pose);
            return path.Count == 0;
        }

        void SetPose(Pose pose)
        {
            var hips = bones[Bone.Hips];
            hips.SetPositionAndRotation(pose.hips, pose.facing);
            foreach (var pair in bones)
                if (pair.Key != Bone.Hips) pair.Value.localRotation = Quaternion.Euler(pose.euler.TryGetValue(pair.Key, out var e) ? e : Vector3.zero);
        }

        Pose Capture()
        {
            var pose = new Pose();
            if (patient == null) return pose;
            var hips = bones[Bone.Hips];
            pose.hips = hips.position; pose.facing = hips.rotation;
            foreach (var pair in bones)
                if (pair.Key != Bone.Hips)
                {
                    var e = pair.Value.localEulerAngles;
                    pose.euler[pair.Key] = new Vector3(Mathf.DeltaAngle(0, e.x), Mathf.DeltaAngle(0, e.y), Mathf.DeltaAngle(0, e.z));
                }
            return pose;
        }

        // ------------------------------------------------------------------------------------------------ placeholder body
        void Build()
        {
            patient = new GameObject("SurgeryPatient_Luo (placeholder)");
            SceneManager.MoveGameObjectToScene(patient, gameObject.scene);
            if (material == null) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "SurgeryPatient_Placeholder" }; material.SetFloat("_Smoothness", .2f); }
            var skin = new Color(.80f, .63f, .50f); var jacket = new Color(.18f, .24f, .36f); var trousers = new Color(.34f, .35f, .28f);
            var boots = new Color(.26f, .19f, .13f); var tee = new Color(.58f, .58f, .55f); var hair = new Color(.16f, .14f, .13f);
            var arm = new Color(.56f, .57f, .60f);

            Transform Joint(Bone b, Transform parent, Vector3 local)
            {
                var t = new GameObject(b.ToString()).transform;
                t.SetParent(parent != null ? parent : patient.transform, false);
                t.localPosition = local;
                bones[b] = t;
                return t;
            }
            void Part(Transform joint, Vector3 centre, Vector3 size, Color color, bool box = false) => MakePart(joint, centre, size, color, box);

            var hips = Joint(Bone.Hips, null, Vector3.zero);
            Part(hips, new Vector3(0, .02f, 0), new Vector3(.30f, .16f, .20f), trousers, true);
            var spine = Joint(Bone.Spine, hips, new Vector3(0, SpineLen, 0));
            Part(spine, new Vector3(0, .09f, 0), new Vector3(.30f, .20f, .19f), tee, true);
            var chest = Joint(Bone.Chest, spine, new Vector3(0, ChestLen, 0));
            Part(chest, new Vector3(0, .10f, 0), new Vector3(.42f, .26f, .23f), jacket, true);
            var neck = Joint(Bone.Neck, chest, new Vector3(0, NeckLen, 0));
            Part(neck, new Vector3(0, .04f, 0), new Vector3(.10f, .10f, .10f), skin);
            var head = Joint(Bone.Head, neck, new Vector3(0, HeadLen, 0));
            Part(head, new Vector3(0, .10f, .01f), new Vector3(.19f, .24f, .22f), skin);
            Part(head, new Vector3(0, .17f, -.01f), new Vector3(.20f, .10f, .22f), hair, true);
            Part(head, new Vector3(0, .09f, .11f), new Vector3(.12f, .03f, .02f), hair, true);   // brow line: which way he faces

            foreach (int s in new[] { -1, 1 })
            {
                bool left = s < 0;
                var upperLeg = Joint(left ? Bone.LeftUpperLeg : Bone.RightUpperLeg, hips, new Vector3(s * HipX, -.04f, 0));
                Part(upperLeg, new Vector3(0, -Thigh * .5f, 0), new Vector3(.15f, Thigh + .06f, .15f), trousers);
                var lowerLeg = Joint(left ? Bone.LeftLowerLeg : Bone.RightLowerLeg, upperLeg, new Vector3(0, -Thigh, 0));
                Part(lowerLeg, new Vector3(0, -Shin * .5f, 0), new Vector3(.12f, Shin + .04f, .12f), trousers);
                var foot = Joint(left ? Bone.LeftFoot : Bone.RightFoot, lowerLeg, new Vector3(0, -Shin, 0));
                Part(foot, new Vector3(0, -.04f, .05f), new Vector3(.11f, .09f, .26f), boots, true);

                var upperArm = Joint(left ? Bone.LeftUpperArm : Bone.RightUpperArm, chest, new Vector3(s * ShoulderX, ShoulderY, 0));
                Part(upperArm, new Vector3(0, -UpperArm * .5f, 0), new Vector3(.11f, UpperArm + .04f, .11f), jacket);
                var lowerArm = Joint(left ? Bone.LeftLowerArm : Bone.RightLowerArm, upperArm, new Vector3(0, -UpperArm, 0));
                var hand = Joint(left ? Bone.LeftHand : Bone.RightHand, lowerArm, new Vector3(0, -Forearm, 0));
                if (left)
                {
                    Part(lowerArm, new Vector3(0, -Forearm * .5f, 0), new Vector3(.09f, Forearm + .04f, .09f), skin);
                    Part(hand, new Vector3(0, -.07f, 0), new Vector3(.08f, .15f, .04f), skin, true);
                    continue;
                }
                // His right forearm and hand are the leased cyber arm, locked (grey, red lock lamp): one detachable piece
                // on the body-side socket (ring, clamp bolts, neural plug), which stays with him.
                CyberArm = BuildCyberArm(false);
                CyberArm.SetParent(lowerArm, false);
                Kit.BuildSocket(lowerArm);
                // The surgery site: what the player aims at and clicks on during the operation.
                site = new GameObject("SurgerySite");
                site.transform.SetParent(lowerArm, false);
                site.transform.localPosition = new Vector3(0, -Forearm * .5f, 0);
                site.AddComponent<BoxCollider>().size = new Vector3(.17f, Forearm + .14f, .17f);
                site.AddComponent<SurgeryTableControl>().Configure(SurgeryControlKind.Patient, "罗亚的右臂");
                site.SetActive(false);
            }
            foreach (var r in patient.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        void MakePart(Transform joint, Vector3 centre, Vector3 size, Color color, bool box = false)
        {
            var go = GameObject.CreatePrimitive(box ? PrimitiveType.Cube : PrimitiveType.Capsule);
            DestroyImmediate(go.GetComponent<Collider>());   // now, not at frame end: nothing may block the crosshair
            go.name = joint.name + "_Mesh";
            go.transform.SetParent(joint, false);
            go.transform.localPosition = centre;
            // Unity's capsule is 2 units tall and 1 wide: scale to the segment.
            go.transform.localScale = box ? size : new Vector3(size.x, size.y * .5f, size.z);
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = material;
            var block = new MaterialPropertyBlock(); block.SetColor("_BaseColor", color); r.SetPropertyBlock(block);
        }

        /// <summary>A cyber forearm + hand assembly in the forearm's frame (elbow at the origin, hand toward -Y): the
        /// locked leased arm or the replacement (SurgeryCyberArm). Unparented.</summary>
        public Transform BuildCyberArm(bool replacement)
        {
            var root = Kit.BuildForearm(replacement);
            if (patient != null) SceneManager.MoveGameObjectToScene(root.gameObject, patient.scene);
            return root;
        }

        /// <summary>Recolours a cyber arm's status lamp (red locked, dark isolated, amber calibrating, green linked).</summary>
        public static void SetArmLamp(Transform arm, Color color) => SurgeryCyberArm.SetLamp(arm, color, color.maxColorComponent > .2f ? 2.4f : 0);

        /// <summary>Puts an assembly onto the right forearm (the fitted replacement).</summary>
        public void FitCyberArm(Transform arm)
        {
            arm.SetParent(bones[Bone.RightLowerArm], false);
            arm.localPosition = Vector3.zero; arm.localRotation = Quaternion.identity;
            CyberArm = arm;
        }

        /// <summary>Takes the current forearm assembly off (the gripper carries it from here).</summary>
        public Transform DetachCyberArm()
        {
            var arm = CyberArm;
            if (arm != null) arm.SetParent(null, true);
            if (arm != null && patient != null) SceneManager.MoveGameObjectToScene(arm.gameObject, patient.scene);
            CyberArm = null;
            return arm;
        }

        void OnDestroy()
        {
            if (patient != null) Destroy(patient);
            if (CyberArm != null && CyberArm.root == CyberArm) Destroy(CyberArm.gameObject);
            if (material != null) Destroy(material);
            kit?.Dispose();
            if (hintCanvas != null) Destroy(hintCanvas);
            if (Current == this) Current = null;
        }
    }
}
