using System;
using System.Collections.Generic;
using UnityEngine;

namespace BorderRepair.RepairProps
{
    /// <summary>A named part of the surgery patient's cyber arm: what the close-up surgery view selects and the operation
    /// moves. Its pick shapes exist only while that view is open (<see cref="Pickable"/>) and nothing covers the part
    /// (<see cref="Exposed"/>), so the room crosshair and the player never meet them and nothing under the service cover
    /// is picked. They follow the part's own pieces (not its sub-parts'); a small part (a screw) gets one box at least
    /// <see cref="MinPick"/> on a side instead, so it can still be clicked.</summary>
    [AddComponentMenu("")]
    public sealed class SurgeryArmPart : MonoBehaviour
    {
        /// <summary>Set by the close-up surgery view while it is open.</summary>
        public static bool Pickable;
        const float MinPick = .016f, Small = .03f;
        readonly List<Collider> picks = new List<Collider>();

        public string Id { get; private set; }
        /// <summary>False while something covers it (the internals under the service cover, the socket's contacts).</summary>
        public bool Exposed { get; set; } = true;
        public Vector3 RestPosition { get; private set; }
        public Quaternion RestRotation { get; private set; }
        /// <summary>Parent-space direction the part comes out along (screws, bolts, plugs).</summary>
        public Vector3 Travel { get; private set; }

        public void Configure(string id, Vector3 travel)
        {
            Id = id; Travel = travel;
            RestPosition = transform.localPosition; RestRotation = transform.localRotation;
        }

        public void Restore()
        {
            transform.localPosition = RestPosition; transform.localRotation = RestRotation; transform.localScale = Vector3.one;
        }

        public int PickShapes => picks.Count;
        void LateUpdate() => Sync();

        void Sync()
        {
            bool want = Pickable && Exposed;
            if (want == picks.Count > 0) return;
            if (!want)
            {
                foreach (var shape in picks) if (shape != null) Destroy(shape);
                picks.Clear();
                return;
            }
            var own = new List<MeshFilter>();
            foreach (var filter in GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh != null && filter.GetComponentInParent<SurgeryArmPart>(true) == this) own.Add(filter);
            if (own.Count == 0) return;
            bool any = false; var box = new Bounds();
            foreach (var filter in own)
            {
                var b = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = transform.InverseTransformPoint(filter.transform.TransformPoint(corner));
                    if (!any) { box = new Bounds(p, Vector3.zero); any = true; } else box.Encapsulate(p);
                }
            }
            if (Mathf.Max(box.size.x, box.size.y, box.size.z) < Small)
            {
                var single = gameObject.AddComponent<BoxCollider>();
                single.isTrigger = true; single.center = box.center; single.size = Vector3.Max(box.size, Vector3.one * MinPick);
                picks.Add(single);
                return;
            }
            foreach (var filter in own)
            {
                Collider shape;
                if (filter.sharedMesh.isReadable && filter.sharedMesh.name.StartsWith("SurgeryArm_", StringComparison.Ordinal))
                {
                    var hull = filter.gameObject.AddComponent<MeshCollider>();
                    hull.sharedMesh = filter.sharedMesh; hull.convex = true;
                    shape = hull;
                }
                else shape = filter.gameObject.AddComponent<BoxCollider>();   // the unit cube pieces: a box fits them exactly
                shape.isTrigger = true;
                picks.Add(shape);
            }
        }

        public static void SyncAll()
        {
            foreach (var part in FindObjectsByType<SurgeryArmPart>(FindObjectsInactive.Include, FindObjectsSortMode.None)) part.Sync();
        }
    }

    /// <summary>A straight lead kept between two points (the neural plug's cable to the socket ring).</summary>
    [AddComponentMenu("")]
    public sealed class SurgeryCable : MonoBehaviour
    {
        public Transform from, to;
        public Vector3 fromLocal, toLocal;
        public float thickness = .006f;

        void LateUpdate() => Fit();

        public void Fit()
        {
            if (from == null || to == null) return;
            Vector3 a = from.TransformPoint(fromLocal), b = to.TransformPoint(toLocal), d = b - a;
            transform.SetPositionAndRotation((a + b) * .5f, d.sqrMagnitude > 1e-8f ? Quaternion.FromToRotation(Vector3.up, d) : Quaternion.identity);
            transform.localScale = new Vector3(thickness, d.magnitude, thickness);
        }
    }

    /// <summary>The cyber hand's fingers: 0 open .. 1 a fist.</summary>
    [AddComponentMenu("")]
    public sealed class SurgeryHand : MonoBehaviour
    {
        public float Curl;
        public Transform[] proximal = new Transform[0], distal = new Transform[0];
        public Transform thumb, thumbTip;
        public Quaternion thumbRest = Quaternion.identity;

        void LateUpdate()
        {
            float c = Mathf.Clamp01(Curl);
            foreach (var t in proximal) t.localRotation = Quaternion.Euler(c * 78f, 0, 0);
            foreach (var t in distal) t.localRotation = Quaternion.Euler(c * 88f, 0, 0);
            if (thumb != null) thumb.localRotation = thumbRest * Quaternion.Euler(c * 40f, 0, 0);
            if (thumbTip != null) thumbTip.localRotation = Quaternion.Euler(c * 50f, 0, 0);
        }
    }

    // Placeholder geometry for 罗亚's leased right forearm, its replacement, and the body-side socket both mount on,
    // detailed enough for the close-up surgery (taken apart the way the bench props are). Body side, on his forearm
    // bone and staying when the forearm comes off: the brass socket ring with its clamp band and four radial clamp bolts
    // (the third wrapped in his own tape), the contact face, and the neural plug on its lead. Forearm: spigot, tapered
    // shell, a service bay on top (lease lock module with its seal, battery, battery service plug) under a cover held by
    // two screws, the status lamp and neural port by the socket, and a hand with jointed fingers. The leased arm is light
    // grey with the lease company's yellow cover, red lamp (locked) and fingers frozen half-closed; the replacement is
    // graphite with its lamp dark until linked. Only the leased arm's service parts are separate parts.
    // Frame: the patient's RightLowerArm bone. Elbow at the origin, hand toward -Y, +Z up when he lies on the table, +X
    // outward (away from his body). Low-poly flat-shaded meshes, three shared materials, colours by property block. These
    // stand in until the user's own model (Tripo) exists; part names are the contract the operation works against.
    public sealed class SurgeryCyberArm : IDisposable
    {
        public const string Whole = "Arm", Old = "Forearm_Old", New = "Forearm_New";
        public const float SocketDepth = .034f;
        public const int TapedBolt = 3;
        public static readonly string[] Bolts = { "ClampBolt_01", "ClampBolt_02", "ClampBolt_03", "ClampBolt_04" };
        public static readonly string[] CoverScrews = { "CoverScrew_01", "CoverScrew_02" };
        /// <summary>Parts inside the service bay: hidden from picking while the cover is on.</summary>
        public static readonly string[] Internals = { "LeaseLock", "BatteryCell", "BatteryPlug" };
        // Radial bolt directions on the clamp band's upper arc, in degrees from +Z (up) toward +X (outward).
        static readonly float[] BoltAngles = { -35f, 5f, 45f, 85f };
        /// <summary>The neural plug seated in the forearm's port, and pulled, hanging on its lead above the socket ring.</summary>
        public static readonly Vector3 PlugSeated = new Vector3(.011f, -.057f, .0545f), PlugParked = new Vector3(.016f, -.026f, .082f);
        public static readonly Quaternion PlugParkedRotation = Quaternion.Euler(-55f, 0, 0);
        /// <summary>Where the battery service plug is set down once pulled (relative to its seat).</summary>
        public static readonly Vector3 BatteryPlugPulled = new Vector3(.020f, 0, .016f);
        /// <summary>Locked hand: the fingers froze half-closed when the arm was locked.</summary>
        public const float LockedCurl = .35f;

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor"), Emission = Shader.PropertyToID("_EmissionColor");
        static readonly Color LeaseGrey = new Color(.62f, .63f, .64f), LeaseWall = new Color(.52f, .53f, .55f), Graphite = new Color(.30f, .31f, .35f),
            GraphiteWall = new Color(.24f, .25f, .28f), DarkSteel = new Color(.22f, .23f, .25f), Steel = new Color(.58f, .59f, .61f),
            Washer = new Color(.44f, .45f, .47f), Brass = new Color(.70f, .55f, .16f), Rubber = new Color(.10f, .10f, .11f),
            Yellow = new Color(.86f, .66f, .12f), Black = new Color(.05f, .05f, .06f), Paper = new Color(.90f, .89f, .84f),
            LockRed = new Color(.45f, .07f, .06f), Seal = new Color(.93f, .91f, .86f), CellBlue = new Color(.16f, .20f, .28f),
            Control = new Color(.10f, .16f, .14f), PlugBody = new Color(.13f, .13f, .15f), TabRed = new Color(.85f, .12f, .08f),
            Orange = new Color(.95f, .45f, .10f), Lead = new Color(.08f, .08f, .09f), Tape = new Color(.88f, .84f, .66f),
            Ink = new Color(.12f, .14f, .30f), Floor = new Color(.10f, .11f, .12f), Joint = new Color(.17f, .17f, .19f);
        public static readonly Color Grime = new Color(.24f, .17f, .10f), FaceClean = new Color(.08f, .08f, .09f),
            PinDull = new Color(.36f, .29f, .17f), PinBright = new Color(1f, .84f, .38f);
        public static readonly Color LampRed = new Color(.95f, .12f, .08f), LampGreen = new Color(.15f, .95f, .45f),
            LampAmber = new Color(1f, .62f, .16f), LampDark = new Color(.05f, .06f, .06f);

        readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();
        Mesh cube;
        Material metal, matte, glow;

        public static string Label(string id)
        {
            switch (id)
            {
                case Whole: return "罗亚的右臂";
                case Old: return "旧前臂（租赁·锁死）";
                case New: return "替换前臂";
                case "SocketRing": return "接口环（身体侧）";
                case "Socket": return "接口触点";
                case "ClampTape": return "螺栓3上的胶布";
                case "NeuralPlug": return "神经接头";
                case "ServiceCover": return "检修盖板";
                case "LeaseLock": return "租赁锁模块";
                case "BatteryPlug": return "电池服务插头";
                case "BatteryCell": return "前臂电池";
                case "StatusLamp": return "状态灯";
                case "StatusLamp_New": return "状态灯（替换件）";
                case "Hand": return "旧前臂的手";
                case "Hand_New": return "替换件的手";
            }
            if (id != null && id.StartsWith("ClampBolt_", StringComparison.Ordinal)) return "卡箍螺栓" + int.Parse(id.Substring(10));
            if (id != null && id.StartsWith("CoverScrew_", StringComparison.Ordinal)) return "检修盖螺钉" + int.Parse(id.Substring(11));
            return id;
        }

        public static SurgeryArmPart FindPart(Transform root, string id)
        {
            if (root == null) return null;
            foreach (var part in root.GetComponentsInChildren<SurgeryArmPart>(true)) if (part.Id == id) return part;
            return null;
        }

        // ------------------------------------------------------------------------------------------------ body side
        /// <summary>Builds the body-side socket on the forearm bone and returns its group.</summary>
        public Transform BuildSocket(Transform bone)
        {
            Ensure();
            var group = new GameObject("SurgerySocket").transform;
            group.SetParent(bone, false);
            var ring = Node(group, "SocketRing", Vector3.zero);
            Piece(ring.transform, "Ring", Prism(16, .056f, .056f, SocketDepth), new Vector3(0, -SocketDepth * .5f, 0), Quaternion.identity, Vector3.one, Brass);
            Piece(ring.transform, "ClampBand", Prism(16, .059f, .059f, .014f), new Vector3(0, -.019f, 0), Quaternion.identity, Vector3.one, DarkSteel);
            Mark(ring, "SocketRing");
            // Clamp bolts, radial on the band's upper arc, where the gantry (and the close-up camera) can reach them.
            for (int i = 0; i < Bolts.Length; i++)
            {
                float a = BoltAngles[i] * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                var bolt = Node(group, Bolts[i], new Vector3(0, -.019f, 0) + dir * .059f, Quaternion.FromToRotation(Vector3.up, dir));
                Piece(bolt.transform, "Washer", Prism(12, .0085f, .0085f, .0012f), new Vector3(0, .0006f, 0), Quaternion.identity, Vector3.one, Washer);
                Piece(bolt.transform, "Head", Prism(6, .0068f, .0068f, .0055f), new Vector3(0, .004f, 0), Quaternion.identity, Vector3.one, Steel);
                Piece(bolt.transform, "Shank", Prism(8, .0034f, .0034f, .016f), new Vector3(0, -.008f, 0), Quaternion.identity, Vector3.one, Washer);
                Mark(bolt, Bolts[i], dir);
                if (i + 1 != TapedBolt) continue;
                // His tape, wound round the bolt head: his own handwriting on it.
                var tape = Node(group, "ClampTape", bolt.transform.localPosition, bolt.transform.localRotation);
                Piece(tape.transform, "Wrap", Prism(12, .0100f, .0100f, .0088f), new Vector3(0, .0042f, 0), Quaternion.identity, Vector3.one, Tape, matte);
                Piece(tape.transform, "Flap", cube, new Vector3(.009f, .006f, 0), Quaternion.Euler(0, 0, 25f), new Vector3(.007f, .003f, .010f), Tape, matte);
                Piece(tape.transform, "Ink", cube, new Vector3(0, .0042f, .0101f), Quaternion.identity, new Vector3(.010f, .0025f, .0006f), Ink, matte);
                Mark(tape, "ClampTape", dir);
            }
            // Contact face: the socket's mating face and its pins, inside the forearm's spigot until the forearm is off.
            var face = Node(group, "Socket", new Vector3(0, -SocketDepth - .0015f, 0));
            Piece(face.transform, "Face", Prism(16, .044f, .044f, .003f), Vector3.zero, Quaternion.identity, Vector3.one, Grime, matte);
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f * Mathf.Deg2Rad;
                Piece(face.transform, "Pin", Prism(6, .0028f, .0028f, .004f), new Vector3(Mathf.Cos(a) * .030f, -.0025f, Mathf.Sin(a) * .030f), Quaternion.identity, Vector3.one, PinDull);
            }
            Piece(face.transform, "Pin", Prism(8, .006f, .006f, .004f), new Vector3(0, -.0025f, 0), Quaternion.identity, Vector3.one, PinDull);
            Mark(face, "Socket");
            // The neural plug, seated in the forearm's port, on a lead from the top of the ring.
            var plug = Node(group, "NeuralPlug", PlugSeated);
            Piece(plug.transform, "Body", cube, Vector3.zero, Quaternion.identity, new Vector3(.020f, .024f, .012f), PlugBody);
            Piece(plug.transform, "Collar", cube, new Vector3(0, .010f, 0), Quaternion.identity, new Vector3(.022f, .004f, .013f), Orange, matte);
            Mark(plug, "NeuralPlug", Vector3.forward);
            var lead = Piece(group, "NeuralLead", Prism(8, .5f, .5f, 1f), Vector3.zero, Quaternion.identity, Vector3.one, Lead, matte).AddComponent<SurgeryCable>();
            lead.from = group; lead.fromLocal = new Vector3(.012f, -.008f, .057f);
            lead.to = plug.transform; lead.toLocal = new Vector3(0, .012f, .002f);
            lead.Fit();
            face.GetComponent<SurgeryArmPart>().Exposed = false;
            return group;
        }

        /// <summary>Recolours the socket's contact face: 0 as it came in (grime, dull pins) .. 1 cleaned.</summary>
        public static void SetSocketClean(SurgeryArmPart face, float clean)
        {
            if (face == null) return;
            foreach (var r in face.GetComponentsInChildren<Renderer>(true))
                Paint(r, r.name == "Face" ? Color.Lerp(Grime, FaceClean, clean) : Color.Lerp(PinDull, PinBright, clean));
        }

        // ------------------------------------------------------------------------------------------------ forearm
        /// <summary>A forearm + hand in the bone frame (elbow end at the origin), unparented.</summary>
        public Transform BuildForearm(bool replacement)
        {
            Ensure();
            var root = new GameObject(replacement ? "CyberArm_Replacement" : "CyberArm_Leased");
            Mark(root, replacement ? New : Old);
            var t = root.transform;
            Color shell = replacement ? Graphite : LeaseGrey, wall = replacement ? GraphiteWall : LeaseWall;
            Piece(t, "Spigot", Prism(16, .045f, .045f, .026f), new Vector3(0, -.033f, 0), Quaternion.identity, Vector3.one, DarkSteel);
            // Ten flat sides, one of them on top (phase 72) for the service bay to sit on.
            Piece(t, "Shell", Prism(10, .040f, .050f, .180f, 72f), new Vector3(0, -.136f, 0), Quaternion.identity, Vector3.one, shell);
            Piece(t, "Wrist", Prism(12, .030f, .034f, .022f), new Vector3(0, -.237f, 0), Quaternion.identity, Vector3.one, Rubber, matte);
            if (!replacement) Piece(t, "LeaseStripe", Prism(10, .0430f, .0436f, .014f, 72f), new Vector3(0, -.207f, 0), Quaternion.identity, Vector3.one, Yellow, matte);
            // Neural port and status lamp on top by the socket.
            Box(t, "NeuralPort", new Vector3(.010f, -.057f, .0460f), new Vector3(.022f, .026f, .010f), Black, matte);
            Box(t, "PortRim", new Vector3(.010f, -.0445f, .0466f), new Vector3(.024f, .002f, .011f), Orange, matte);
            var lamp = Node(t, replacement ? "StatusLamp_New" : "StatusLamp", new Vector3(-.009f, -.068f, .0484f));
            Box(lamp.transform, "Housing", new Vector3(0, 0, -.0015f), new Vector3(.014f, .014f, .003f), Black, matte);
            Piece(lamp.transform, "Lamp", cube, new Vector3(0, 0, .001f), Quaternion.identity, new Vector3(.010f, .010f, .003f), replacement ? LampDark : LampRed, glow, replacement ? 0 : 2.4f);
            Mark(lamp, replacement ? "StatusLamp_New" : "StatusLamp");
            // Service bay on top: floor, walls; inside it the lock module (the replacement has a plain controller), the
            // battery and its service plug; the cover on top with its two screws.
            Box(t, "BayFloor", new Vector3(0, -.141f, .044f), new Vector3(.048f, .100f, .004f), Floor, matte);
            Box(t, "BayWallL", new Vector3(-.0255f, -.141f, .0450f), new Vector3(.003f, .104f, .030f), wall);
            Box(t, "BayWallR", new Vector3(.0255f, -.141f, .0450f), new Vector3(.003f, .104f, .030f), wall);
            Box(t, "BayWallA", new Vector3(0, -.0905f, .0450f), new Vector3(.054f, .003f, .030f), wall);
            Box(t, "BayWallB", new Vector3(0, -.1915f, .0450f), new Vector3(.054f, .003f, .030f), wall);
            var lockModule = Node(t, replacement ? "Controller" : "LeaseLock", new Vector3(0, -.110f, .051f));
            Box(lockModule.transform, "Case", Vector3.zero, new Vector3(.034f, .032f, .010f), replacement ? Control : LockRed, matte);
            if (!replacement)
            {
                Box(lockModule.transform, "Seal", new Vector3(0, 0, .0057f), new Vector3(.036f, .006f, .0014f), Seal, matte);
                Piece(lockModule.transform, "Led", cube, new Vector3(.011f, .011f, .0058f), Quaternion.identity, new Vector3(.005f, .005f, .002f), LampRed, glow, 2f);
            }
            var cell = Node(t, "BatteryCell", new Vector3(0, -.168f, .051f));
            Box(cell.transform, "Cell", Vector3.zero, new Vector3(.042f, .038f, .010f), CellBlue, matte);
            Box(cell.transform, "Label", new Vector3(0, 0, .0055f), new Vector3(.030f, .012f, .001f), Paper, matte);
            var batteryPlug = Node(t, "BatteryPlug", new Vector3(0, -.1385f, .050f));
            Box(batteryPlug.transform, "Body", Vector3.zero, new Vector3(.016f, .010f, .008f), PlugBody);
            Box(batteryPlug.transform, "Tab", new Vector3(0, 0, .006f), new Vector3(.010f, .004f, .006f), TabRed, matte);
            Box(t, "Lead1", new Vector3(-.008f, -.125f, .047f), new Vector3(.002f, .022f, .002f), Lead, matte);
            Box(t, "Lead2", new Vector3(.008f, -.153f, .047f), new Vector3(.002f, .022f, .002f), Lead, matte);
            var cover = Node(t, "ServiceCover", new Vector3(0, -.141f, .0625f));
            Box(cover.transform, "Plate", Vector3.zero, new Vector3(.058f, .108f, .005f), replacement ? GraphiteWall : Yellow);
            if (!replacement) Box(cover.transform, "Stripe", new Vector3(0, .018f, .0028f), new Vector3(.058f, .010f, .0008f), Black, matte);
            Box(cover.transform, "AssetTag", new Vector3(0, -.026f, .0028f), new Vector3(.022f, .013f, .0008f), Paper, matte);
            for (int i = 0; i < CoverScrews.Length; i++)
            {
                var screw = Node(t, CoverScrews[i], new Vector3(0, i == 0 ? -.093f : -.189f, .0664f), Quaternion.Euler(90f, 0, 0));
                Piece(screw.transform, "Head", Prism(8, .0042f, .0042f, .0028f), Vector3.zero, Quaternion.identity, Vector3.one, Steel);
                Box(screw.transform, "Slot", new Vector3(0, .0015f, 0), new Vector3(.0066f, .0008f, .0011f), Black, matte);
                if (!replacement) Mark(screw, CoverScrews[i], Vector3.forward);
            }
            if (!replacement)
            {
                Mark(lockModule, "LeaseLock"); Mark(cell, "BatteryCell"); Mark(batteryPlug, "BatteryPlug", Vector3.forward); Mark(cover, "ServiceCover", Vector3.forward);
            }
            BuildHand(t, replacement, shell);
            if (!replacement) foreach (var id in Internals) FindPart(t, id).Exposed = false;   // under the cover
            return t;
        }

        void BuildHand(Transform arm, bool replacement, Color shell)
        {
            var hand = Node(arm, replacement ? "Hand_New" : "Hand", new Vector3(0, -.248f, 0));
            Box(hand.transform, "Palm", new Vector3(0, -.041f, -.002f), new Vector3(.070f, .078f, .024f), shell);
            Box(hand.transform, "Knuckles", new Vector3(0, -.082f, 0), new Vector3(.072f, .010f, .020f), Joint, matte);
            // Palm down when he lies on his back: thumb on the body side (-X), little finger outward.
            float[] xs = { -.0255f, -.0085f, .0085f, .0255f }, first = { .034f, .037f, .034f, .028f }, second = { .027f, .030f, .027f, .023f };
            var proximal = new Transform[4]; var distal = new Transform[4];
            for (int i = 0; i < 4; i++)
            {
                var j0 = Node(hand.transform, "Finger" + i, new Vector3(xs[i], -.086f, 0)).transform;
                Box(j0, "Seg0", new Vector3(0, -first[i] * .5f, 0), new Vector3(.0145f, first[i], .015f), shell);
                var j1 = Node(j0, "Tip", new Vector3(0, -first[i] - .001f, 0)).transform;
                Box(j1, "Seg1", new Vector3(0, -second[i] * .5f, 0), new Vector3(.0135f, second[i], .014f), shell);
                proximal[i] = j0; distal[i] = j1;
            }
            var thumb = Node(hand.transform, "Thumb", new Vector3(-.038f, -.020f, -.004f), Quaternion.Euler(0, 0, -35f)).transform;
            Box(thumb, "Seg0", new Vector3(0, -.015f, 0), new Vector3(.016f, .030f, .016f), shell);
            var tip = Node(thumb, "Tip", new Vector3(0, -.031f, 0)).transform;
            Box(tip, "Seg1", new Vector3(0, -.012f, 0), new Vector3(.014f, .024f, .014f), shell);
            var fingers = hand.AddComponent<SurgeryHand>();
            fingers.proximal = proximal; fingers.distal = distal; fingers.thumb = thumb; fingers.thumbTip = tip; fingers.thumbRest = thumb.localRotation;
            fingers.Curl = replacement ? 0 : LockedCurl;
            Mark(hand, replacement ? "Hand_New" : "Hand");
        }

        // ------------------------------------------------------------------------------------------------ state
        /// <summary>Status lamp colour (red locked, dark isolated, amber calibrating, green linked) and brightness.</summary>
        public static void SetLamp(Transform arm, Color color, float brightness)
        {
            if (arm == null) return;
            foreach (var r in arm.GetComponentsInChildren<Renderer>(true))
                if (r.name == "Lamp") Paint(r, color, brightness);
        }

        /// <summary>The lease lock module's own LED (lit while the arm is powered and locked).</summary>
        public static void SetLockLed(Transform arm, bool on)
        {
            if (arm == null) return;
            foreach (var r in arm.GetComponentsInChildren<Renderer>(true))
                if (r.name == "Led") Paint(r, on ? LampRed : LampDark, on ? 2f : 0);
        }

        public static float GetCurl(Transform arm)
        {
            var hand = arm != null ? arm.GetComponentInChildren<SurgeryHand>(true) : null;
            return hand != null ? hand.Curl : 0;
        }

        public static void SetCurl(Transform arm, float curl)
        {
            var hand = arm != null ? arm.GetComponentInChildren<SurgeryHand>(true) : null;
            if (hand != null) hand.Curl = curl;
        }

        public static void Paint(Renderer r, Color color, float brightness = 0)
        {
            var block = new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            block.SetColor(BaseColor, color);
            if (r.sharedMaterial != null && r.sharedMaterial.IsKeywordEnabled("_EMISSION")) block.SetColor(Emission, color * brightness);
            r.SetPropertyBlock(block);
        }

        // ------------------------------------------------------------------------------------------------ building blocks
        void Ensure()
        {
            if (cube == null)
            {
                var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube = probe.GetComponent<MeshFilter>().sharedMesh;
                UnityEngine.Object.DestroyImmediate(probe);
            }
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            // Under the surgical spot anything shinier blows the small bolt heads out.
            if (metal == null) { metal = new Material(lit) { name = "SurgeryArm_Metal" }; metal.SetFloat("_Metallic", .35f); metal.SetFloat("_Smoothness", .32f); }
            if (matte == null) { matte = new Material(lit) { name = "SurgeryArm_Matte" }; matte.SetFloat("_Smoothness", .22f); }
            if (glow == null)
            {
                glow = new Material(lit) { name = "SurgeryArm_Glow" };
                glow.SetFloat("_Smoothness", .6f);
                glow.EnableKeyword("_EMISSION");
                glow.SetColor("_EmissionColor", Color.white);
                glow.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
        }

        static GameObject Node(Transform parent, string name, Vector3 position, Quaternion? rotation = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation ?? Quaternion.identity;
            return go;
        }

        static void Mark(GameObject go, string id, Vector3 travel = default)
        {
            go.AddComponent<SurgeryArmPart>().Configure(id, travel);
        }

        GameObject Piece(Transform parent, string name, Mesh mesh, Vector3 position, Quaternion rotation, Vector3 scale, Color color, Material material = null, float brightness = 0)
        {
            var go = Node(parent, name, position, rotation);
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material != null ? material : metal;
            Paint(r, color, brightness);
            return go;
        }

        GameObject Box(Transform parent, string name, Vector3 position, Vector3 size, Color color, Material material = null) =>
            Piece(parent, name, cube, position, Quaternion.identity, size, color, material);

        // An n-sided prism along Y, centred on the origin: radius r0 at the bottom (-length/2), r1 at the top, capped,
        // flat shaded; phase turns it about Y (degrees from +X toward +Z) so a face rather than an edge can point a given way.
        Mesh Prism(int sides, float r0, float r1, float length, float phase = 0)
        {
            string key = sides + "|" + r0 + "|" + r1 + "|" + length + "|" + phase;
            if (meshes.TryGetValue(key, out var cached)) return cached;
            var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var triangles = new List<int>();
            float y0 = -length * .5f, y1 = length * .5f;
            Vector3 Rim(int i, float r, float y)
            {
                float a = (phase + 360f * i / sides) * Mathf.Deg2Rad;
                return new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
            }
            void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
            {
                var normal = Vector3.Cross(b - a, c - a);
                if (Vector3.Dot(normal, outward) < 0) { var swap = b; b = c; c = swap; normal = -normal; }
                normal.Normalize();
                int k = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c);
                normals.Add(normal); normals.Add(normal); normals.Add(normal);
                triangles.Add(k); triangles.Add(k + 1); triangles.Add(k + 2);
            }
            for (int i = 0; i < sides; i++)
            {
                Vector3 a0 = Rim(i, r0, y0), b0 = Rim(i + 1, r0, y0), a1 = Rim(i, r1, y1), b1 = Rim(i + 1, r1, y1);
                var mid = (a0 + b0 + a1 + b1) * .25f;
                var outward = new Vector3(mid.x, 0, mid.z);
                Face(a0, b0, a1, outward); Face(b0, b1, a1, outward);
                Face(new Vector3(0, y0, 0), a0, b0, Vector3.down);
                Face(new Vector3(0, y1, 0), a1, b1, Vector3.up);
            }
            var mesh = new Mesh { name = "SurgeryArm_Prism" + sides };
            mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            meshes[key] = mesh;
            return mesh;
        }

        public void Dispose()
        {
            foreach (var mesh in meshes.Values) if (mesh != null) UnityEngine.Object.Destroy(mesh);
            meshes.Clear();
            if (metal != null) UnityEngine.Object.Destroy(metal);
            if (matte != null) UnityEngine.Object.Destroy(matte);
            if (glow != null) UnityEngine.Object.Destroy(glow);
        }
    }
}
