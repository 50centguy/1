using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace BorderRepair.RepairProps.EditorTools
{
    // The B1 clinic interior of the Berth 04 district: an inventory, a re-runnable interior pass and review shots.
    // The pass keeps the counter where the street can see it and sorts the room into a customer side (gate, receiving
    // tray, waiting chairs) and a workshop behind the counter (bench, UNIT 07's dock, shelving), with the surgery bay
    // behind a partition and a curtain at the back right. Everything it adds lives under one root that is rebuilt; the
    // handed-over objects it moves or re-materials get absolute values, and the project prefabs themselves are untouched
    // (only this scene's instances are moved). Assets go to Temp_InteriorPass.
    // Batch: -executeMethod BorderRepair.RepairProps.EditorTools.ExteriorDistrictPass.{InspectInterior|ApplyInterior|InteriorShots}
    public static partial class ExteriorDistrictPass
    {
        const string InteriorDir = "Assets/RepairProps/Scenes/Temp_InteriorPass";
        const string InteriorRoot = "09 Interior pass - B1 clinic";

        // The basement room: x -3.6..3.6, floor y 0, ceiling underside 2.545, z from the gate line to the back wall.
        // Inner wall faces: x -3.51 / 3.51, z 0.15 (the threshold walls) and 6.31.
        static readonly Bounds Room = new Bounds(new Vector3(0, 1.3f, 3.05f), new Vector3(7.4f, 2.8f, 6.9f));
        const float WallL = -3.51f, WallR = 3.51f, WallFront = .15f, WallBack = 6.31f, CeilingY = 2.545f;
        // The room-side face of the front wall after the lining (the street's ground-floor flank reaches z 0.27 inside).
        const float FrontFace = .29f;

        // Interior colours: warm against the street's cold (ART_STYLE_GUIDE v1: dirty grey concrete, smoked-green old
        // paint, dark charcoal; amber only at the counter and the bench). Albedos, not on-screen colours.
        static readonly Color InConcrete = new Color(.47f, .46f, .43f), InPaint = new Color(.3f, .4f, .35f), InFloor = new Color(.37f, .35f, .32f),
            InCeiling = new Color(.5f, .5f, .48f), InTile = new Color(.74f, .75f, .72f);

        // Eye height 1.62 m over the basement floor.
        static readonly (string name, Vector3 eye, Vector3 look, float fov)[] InteriorViews =
        {
            ("Int 1 - gate looking in", new Vector3(-1.5f, 1.62f, .25f), new Vector3(.2f, 1.05f, 6.2f), 62),
            ("Int 2 - behind the counter", new Vector3(-2.7f, 1.62f, 2.4f), new Vector3(2.2f, 1.1f, 1.2f), 62),
            ("Int 3 - bench corner looking out", new Vector3(-3.1f, 1.62f, 6.0f), new Vector3(1.5f, 1.0f, .6f), 62),
            ("Int 4 - surgery bay looking back", new Vector3(3.2f, 1.62f, .7f), new Vector3(-2.2f, 1.0f, 5.6f), 62),
            ("Int 5 - dock front", new Vector3(-.4f, 1.62f, 3.2f), new Vector3(-1.7f, .9f, 6.3f), 62),
            ("Int 6 - surgery bay from the curtain", new Vector3(2.15f, 1.62f, 2.45f), new Vector3(2.45f, .8f, 5.6f), 62),
        };

        // ------------------------------------------------------------------------------------------------ apply
        [MenuItem("Border Repair/Interior/Apply interior pass")]
        public static void ApplyInterior()
        {
            log = new StringBuilder();
            mats.Clear(); meshCount = 0; bevelled.Clear();
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var old = scene.GetRootGameObjects().FirstOrDefault(g => g.name == InteriorRoot);
            if (old != null) Object.DestroyImmediate(old);
            PrepareInteriorAssets();
            texDir = InteriorDir + "/Textures"; matDir = InteriorDir + "/Materials"; meshDir = InteriorDir + "/Meshes";
            try
            {
                var root = new GameObject(InteriorRoot).transform;
                InteriorLayout(Group(root, "Layout - shelving, dock corner, staff gate"));
                InteriorSurfaces(Group(root, "Surfaces - paint, concrete, tile, floor"));
                InteriorLight(Group(root, "Light - fixtures"));
                WaitingCorner(Group(root, "Customer side - entrance and waiting chairs"));
                SurgeryBay(Group(root, "Surgery bay - curtain, tile, cart"));
                WorkshopLife(Group(root, "Workshop - counter back, dock, life"));
                InteriorServices(Group(root, "Services - retrofitted heating pipe and its leak"));
                DepthTestedText();
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                    GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
                BakeRoomProbe(Group(root, "Reflection - room probe"));
            }
            finally { texDir = TexDir; matDir = MatDir; meshDir = MeshDir; }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory(OutDir);
            File.WriteAllText(Path.Combine(OutDir, "interior_report.txt"), log.ToString());
            Debug.Log("[Interior] applied\n" + log);
        }

        static void PrepareInteriorAssets()
        {
            if (AssetDatabase.IsValidFolder(InteriorDir + "/Meshes")) AssetDatabase.DeleteAsset(InteriorDir + "/Meshes");
            AssetDatabase.CreateFolder(InteriorDir, "Meshes");
            if (!AssetDatabase.IsValidFolder(InteriorDir + "/Materials")) AssetDatabase.CreateFolder(InteriorDir, "Materials");
            AssetDatabase.Refresh();
            foreach (var path in Directory.GetFiles(InteriorDir + "/Textures").Where(f => f.EndsWith(".png") || f.EndsWith(".jpg")))
            {
                var p = path.Replace('\\', '/');
                var imp = (TextureImporter)AssetImporter.GetAtPath(p);
                if (imp == null) continue;
                bool normalMap = Path.GetFileNameWithoutExtension(p).EndsWith("_n");
                imp.textureType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
                imp.sRGBTexture = !normalMap;
                imp.mipmapEnabled = true;
                imp.filterMode = FilterMode.Bilinear;
                imp.maxTextureSize = 1024;
                imp.wrapMode = p.Contains("/srf_") || p.Contains("/int_wear") ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                imp.alphaSource = TextureImporterAlphaSource.FromInput;
                imp.alphaIsTransparency = false;
                imp.SaveAndReimport();
            }
        }

        static Transform Shell(string name) => EditorSceneManager.GetActiveScene().GetRootGameObjects()
            .First(r => r.name == "01 Basement shell and open entrance").transform.Find(name);

        static Transform ClinicAsset(string name) => EditorSceneManager.GetActiveScene().GetRootGameObjects()
            .First(r => r.name == "02 Clinic assets from the project").transform.Cast<Transform>().First(t => t.name == name);

        static Transform SourceOf(Transform t) => PrefabUtility.GetCorrespondingObjectFromSource(t);

        // Named() skips inactive objects; the dock corner kit starts switched off.
        static Transform AnyNamed(string name) =>
            Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).First(t => t.name == name);

        // ------------------------------------------------------------------------------------------------ layout
        static void InteriorLayout(Transform g)
        {
            // The partition was 1.6 m long, the shelving stood across the back wall and ran 0.3 m through it, and the
            // surgery ring touched it. Longer and 20 cm further left, it now carries the shelving on the workshop side,
            // closes the bay to the curtain line, and clears the ring (2.2 m across, 1.30..3.50).
            var partition = Shell("Surgery privacy partition");
            partition.SetPositionAndRotation(new Vector3(1.12f, 1.3f, 4.93f), Quaternion.identity);
            partition.localScale = new Vector3(.18f, 2.6f, 2.76f);
            foreach (var name in new[] { "SurgeryTableV2", "SurgeryGantryV2" })
            {
                var t = ClinicAsset(name);
                t.position = new Vector3(2.40f, t.position.y, 5.04f);
            }
            log.Append("partition x 1.12, z 3.55..6.31; surgery table and ring x 2.40\n");

            // The harbour water of the handoff is one box under the whole district (top y 0.05); in the basement it
            // stood 5 cm over the floor slab (top y 0) and hid it. Its top goes to y -0.03, under every floor.
            var water = EditorSceneManager.GetActiveScene().GetRootGameObjects().First(r => r.name == "06 Winter harbour and stairwell").transform.Find("Water basin");
            water.position = new Vector3(-43f, -.2f, 15f);
            log.Append("harbour water box top y -0.03 (was 0.05, over the basement floor)\n");

            // Shelving (rack and its goods, children of the counter kit): back to the handoff's placement, then turned
            // 90 degrees so its back sits against the partition and its shelves face the bench and the dock.
            var rack = AnyNamed("ZC_Racking");
            var goods = AnyNamed("ZC_ShelfGoods");
            foreach (var t in new[] { rack, goods })
            {
                t.localPosition = new Vector3(-5.8315897f, -2.4085639f, 1.0000005f);
                t.localRotation = SourceOf(t).localRotation;
            }
            var rb = rack.GetComponent<Renderer>().bounds;
            var pivot = new Vector3(rb.center.x, 0, rb.center.z);
            var turn = Quaternion.Euler(0, 90, 0);
            var to = new Vector3(1.03f - .03f - rb.extents.z, 0, 4.93f);
            foreach (var t in new[] { rack, goods }) t.SetPositionAndRotation(to + turn * (t.position - pivot), turn * t.rotation);
            log.Append("shelving: back against the partition, centre ").Append(rack.GetComponent<Renderer>().bounds.center.ToString("F2")).Append('\n');

            // The goods mesh carries three storage boxes a metre in front of the rack with their bottoms 33 cm off the
            // floor: they floated in the handoff too (the water over the floor hid how much). They get a plank across
            // concrete blocks under them, which is how a damp basement keeps stock off the floor.
            var gm = goods.GetComponent<MeshFilter>().sharedMesh;
            float front = rack.GetComponent<Renderer>().bounds.min.x - .01f;
            var low = gm.vertices.Select(v => goods.TransformPoint(v)).Where(v => v.x < front && v.y < .6f).ToList();
            if (low.Count > 0)
            {
                var sb = new Bounds(low[0], Vector3.zero);
                foreach (var v in low) sb.Encapsulate(v);
                log.Append("floor stock ").Append(sb.min.ToString("F2")).Append("..").Append(sb.max.ToString("F2")).Append('\n');
                if (sb.min.y > .05f) PlankOnBlocks(g, sb);
            }

            // UNIT 07's service corner (charge panel, run/charge/error lamps, meters, spares, umbilical, mat, drip pan). The
            // handoff had it switched off in the gate corner, half inside the threshold wall; on, and hung on the back wall
            // around the dock. In its own placement its wall plane is x -3.28 with the kit centred on z 0.
            var kit = AnyNamed("DockCornerV2");
            kit.localPosition = SourceOf(kit).localPosition;
            kit.localRotation = SourceOf(kit).localRotation;
            kit.gameObject.SetActive(true);
            var kitTurn = Quaternion.Euler(0, 90, 0);
            var from = new Vector3(-3.28f, 0, 0);
            var onto = new Vector3(-1.75f, 0, WallBack);
            kit.SetPositionAndRotation(onto + kitTurn * (kit.position - from), kitTurn * kit.rotation);
            log.Append("dock corner kit on the back wall around UNIT 07\n");

            // Staff gate: closes the gap between the end of the counter's angled wing and the partition, so the waiting
            // side and the workshop are separate. Waist high, painted steel and bars, a plate on the customer side.
            var counter = AnyNamed("ZC_Counter").GetComponent<MeshFilter>();
            var cv = counter.sharedMesh.vertices.Select(v => counter.transform.TransformPoint(v)).Where(v => v.y > .5f).ToList();
            float far = cv.Max(v => v.x + v.z);
            var end = cv.Where(v => v.x + v.z > far - .03f).OrderByDescending(v => v.x).First();
            var a = new Vector3(end.x + .03f, 0, end.z);
            var b = new Vector3(1.03f - .02f, 0, 3.58f);
            BuildStaffGate(g, a, b);
            log.Append("staff gate from ").Append(a.ToString("F2")).Append(" to ").Append(b.ToString("F2")).Append('\n');
        }

        // A scaffold plank across concrete blocks, its top at the underside of what stands on it.
        static void PlankOnBlocks(Transform g, Bounds b)
        {
            var wood = Worn("Int - plank, old pine", Hex("#6F6250"), .2f);
            var block = Worn("Int - concrete block", Hex("#77756F"), .1f);
            var bench = Group(g, "Plank on blocks under the floor stock");
            float top = b.min.y, t = .035f, w = b.size.x + .07f, len = b.size.z + .12f;
            var c = new Vector3(b.center.x, 0, b.center.z);
            BBox("Plank", bench, new Vector3(c.x, top - t * .5f, c.z), new Vector3(w, t, len), wood);
            foreach (float k in new[] { -.5f, 0f, .5f })
            {
                var at = new Vector3(c.x, (top - t) * .5f, c.z + k * (len - .3f));
                BBox("Block", bench, at, new Vector3(.2f, top - t, .39f), block);
                BBox("Block core", bench, at + Vector3.left * .101f, new Vector3(.002f, (top - t) * .55f, .13f), Worn("Int - block core shadow", Hex("#2A2927"), .1f));
            }
        }

        static void BuildStaffGate(Transform g, Vector3 a, Vector3 b)
        {
            var gate = Group(g, "Staff gate");
            var paint = Worn("Int - gate steel, old green paint", Hex("#3D4A43"), .35f, .3f);
            var along = (b - a).normalized;
            float len = (b - a).magnitude;
            var rot = Quaternion.LookRotation(Vector3.Cross(along, Vector3.up), Vector3.up);   // local x along the gate
            BBox("Post at the counter", gate, a + Vector3.up * .55f, new Vector3(.05f, 1.1f, .05f), paint);
            BBox("Post at the partition", gate, b + Vector3.up * .55f, new Vector3(.05f, 1.1f, .05f), paint);
            var c = (a + b) * .5f;
            float w = len - .1f;
            BBox("Top rail", gate, c + Vector3.up * 1.02f, new Vector3(w, .04f, .04f), paint, rot);
            BBox("Bottom rail", gate, c + Vector3.up * .12f, new Vector3(w, .035f, .035f), paint, rot);
            BBox("Mid rail", gate, c + Vector3.up * .6f, new Vector3(w, .025f, .025f), paint, rot);
            for (float s = .08f; s < w - .04f; s += .1f)
                BBox("Bar", gate, a + along * (.05f + s) + Vector3.up * .57f, new Vector3(.014f, .9f, .014f), paint);
            BBox("Latch", gate, b - along * .1f + Vector3.up * .95f, new Vector3(.06f, .05f, .08f), Worn("Int - worn steel", Hex("#6E7471"), .5f, .7f), rot);
            var customerSide = Vector3.Cross(Vector3.up, along).normalized;
            if (customerSide.z > 0) customerSide = -customerSide;
            Decal("Plate - staff only", gate, c + Vector3.up * .8f + customerSide * .025f, customerSide, .34f, .13f,
                  Lit("Int - staff plate", Color.white, .3f, 0, Tex("int_staff_plate")), 0f);
        }

        // ------------------------------------------------------------------------------------------------ surfaces
        static void InteriorSurfaces(Transform g)
        {
            // Walls: the dirty concrete scan above a smoked-green painted dado with a dark line on top; grime along the
            // foot. The riveted container panels the shell wore are gone from this room.
            var wall = SurfaceMat("Int - wall, concrete over green paint", "srf_int_wall", 2.6f, 5, InConcrete, 0, .2f, .06f,
                                  ground: "srf_int_paint", groundColour: InPaint, groundScale: 1.8f);
            wall.SetFloat("_Dado", 1.15f); wall.SetFloat("_FootGrime", .45f); wall.SetFloat("_WetSmoothness", .5f);
            wall.SetFloat("_GlossVar", .7f); wall.SetFloat("_Peel", .4f); wall.SetFloat("_NormalScale", 1.3f);
            // Floor: worn concrete with a polished sheen where it is walked, darker and glossy where it is damp (the
            // entrance, and patches); it picks up the lamps and the room probe.
            var floor = SurfaceMat("Int - floor, worn concrete", "srf_int_floor", 2.6f, 5, InFloor, 0, .24f, .14f);
            floor.SetFloat("_Dado", 0); floor.SetFloat("_FootGrime", 0); floor.SetFloat("_GlossVar", .7f);
            floor.SetFloat("_Puddle", .35f); floor.SetVector("_WetZone", new Vector4(-1.5f, .95f, 1.25f, .85f));
            var ceiling = SurfaceMat("Int - ceiling, old paint", "srf_int_ceiling", 1.8f, 5, InCeiling, 0, .1f, .03f);
            ceiling.SetFloat("_NormalScale", .55f); ceiling.SetFloat("_Dado", 0); ceiling.SetFloat("_FootGrime", 0); ceiling.SetFloat("_GlossVar", .3f);
            foreach (var n in new[] { "Left wall", "Right wall", "Back wall", "Surgery privacy partition" }) Shell(n).GetComponent<Renderer>().sharedMaterial = wall;
            Shell("Ceiling").GetComponent<Renderer>().sharedMaterial = ceiling;
            Shell("Basement concrete slab").GetComponent<Renderer>().sharedMaterial = floor;

            // The threshold walls are shared with the street (they wear the exterior concrete), and the street's
            // ground-floor flank block pokes 12 cm through them into the room above 1.35 m. The room side gets a lining
            // of the room's wall out to z 0.29, left and right of the gate and over it.
            var lining = Group(g, "Front wall lining - room side");
            float z = (WallFront + FrontFace) * .5f, d = FrontFace - WallFront;
            Box("Lining left of the gate", lining, new Vector3((WallL - 2.27f) * .5f, CeilingY * .5f, z), new Vector3(-2.27f - WallL, CeilingY, d), wall);
            Box("Lining right of the gate", lining, new Vector3((-.78f + WallR) * .5f, CeilingY * .5f, z), new Vector3(WallR + .78f, CeilingY, d), wall);
            Box("Lining over the gate", lining, new Vector3(-1.525f, (2.26f + CeilingY) * .5f, z), new Vector3(1.49f, CeilingY - 2.26f, d), wall);
            // A painted steel skirting along the room side where chairs and boots hit it.
            log.Append("walls, ceiling, floor re-materialled; front lining added\n");
        }

        // ------------------------------------------------------------------------------------------------ light
        static void InteriorLight(Transform g)
        {
            var housing = Worn("Int - fixture housing, old enamel", Hex("#A8A396"), .35f);
            var warmTube = Lit("Int - fluorescent tube, warm white", Hex("#E9E2D2"), .5f, 0, null, new Color(1f, .87f, .7f) * 1.4f);
            var neutralTube = Lit("Int - fluorescent tube, cool white", Hex("#E2EAE7"), .5f, 0, null, new Color(.86f, .97f, .95f) * 1.4f);
            // Two ceiling battens: the workshop (warm white, the main light of the room) and the waiting side (neutral).
            Batten(g, "Batten - workshop", new Vector3(-.85f, 0, 4.25f), true, housing, warmTube, new Color(1f, .86f, .68f), 5.5f, 5f, true, 100);
            Batten(g, "Batten - waiting side", new Vector3(2.05f, 0, 1.75f), false, housing, neutralTube, new Color(.86f, .95f, .93f), 3f, 4.4f, true, 110);

            // Snow-light from the stairwell through the open gate: cool, with the frame's shadow on the floor.
            var spillPos = new Vector3(-1.52f, 2.1f, -.95f);
            var spill = AddLight(g, "Street spill through the gate", spillPos, LightType.Spot, new Color(.6f, .72f, .9f), 2.4f, 5.5f, true);
            spill.transform.rotation = Quaternion.LookRotation(new Vector3(-1.45f, 0, 1.7f) - spillPos);
            spill.spotAngle = 62; spill.innerSpotAngle = 25;

            // Warm bounce: the light the lamps throw back off the bench, the counter and the floor. Without it the
            // night-sky ambient the whole district shares turns every shadow in here cold.
            AddLight(g, "Warm bounce - workshop", new Vector3(-1.7f, 1.0f, 4.5f), LightType.Point, new Color(1f, .78f, .56f), .65f, 4.5f, false);
            AddLight(g, "Warm bounce - counter", new Vector3(-1.5f, .75f, 2.7f), LightType.Point, new Color(1f, .76f, .52f), .5f, 3.4f, false);

            // A bare bulb in a wire cage just inside the gate: lights the mat and whoever stands at the tray.
            var bulbGroup = Group(g, "Caged bulb inside the gate");
            var black = Worn("Int - black bakelite", Hex("#1C1D1C"), .45f);
            var cage = Worn("Int - wire cage", Hex("#3A3C3A"), .4f, .6f);
            var bulbPos = new Vector3(-1.52f, 2.12f, .5f);
            Rod("Cord", bulbGroup, new Vector3(bulbPos.x, CeilingY, bulbPos.z), bulbPos + Vector3.up * .1f, .006f, black);
            Cyl("Socket", bulbGroup, bulbPos + Vector3.up * .07f, .022f, .06f, black);
            Sphere("Bulb", bulbGroup, bulbPos, .035f, Lit("Int - bulb, tungsten", Hex("#F4E2C0"), .6f, 0, null, new Color(1f, .78f, .5f) * 2.2f));
            for (int k = 0; k < 4; k++)
            {
                var d = Quaternion.Euler(0, k * 45, 0) * Vector3.right * .055f;
                Rod("Cage wire", bulbGroup, bulbPos + d + Vector3.up * .05f, bulbPos - d + Vector3.up * .05f, .003f, cage);
                Rod("Cage rib", bulbGroup, bulbPos + d + Vector3.up * .05f, bulbPos + d * .3f + Vector3.down * .07f, .003f, cage);
            }
            AddLight(bulbGroup, "Caged bulb", bulbPos + Vector3.down * .05f, LightType.Point, new Color(1f, .79f, .55f), 1.6f, 3.4f, false);

            // The shell's own fixtures: the bench tube was a blown-out white box; the alcove one is the surgery's neutral.
            Shell("Bench task fixture").GetComponent<Renderer>().sharedMaterial = warmTube;
            Shell("Alcove fixture").GetComponent<Renderer>().sharedMaterial = neutralTube;

            // The handed-over lights: the cold room bounce made a blue hot spot on the ceiling; it becomes a low warm
            // fill under the ceiling. The surgery fill goes neutral white (v1: the surgery position is neutral white).
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                switch (l.name)
                {
                    case "Cool room bounce":
                        // Low and cool, so the shadows read blue-grey against the warm pools instead of going brown.
                        l.color = new Color(.56f, .66f, .8f); l.intensity = .3f; l.range = 7f;
                        l.transform.position = new Vector3(.4f, 1.3f, 2.6f); break;
                    case "Dim alcove fill":
                        // The bay: the brightest, cleanest light in the room, an island on the table.
                        l.color = new Color(.94f, .98f, 1f); l.intensity = 4.2f;
                        if (l.type == LightType.Spot) { l.spotAngle = 85; l.innerSpotAngle = 40; }
                        break;
                    case "Low dock fill": l.intensity = 1.8f; break;
                    default: continue;
                }
                log.Append("light ").Append(l.name).Append(" -> #").Append(ColorUtility.ToHtmlStringRGB(l.color)).Append(" i ").Append(l.intensity).Append('\n');
            }
        }

        static void Batten(Transform g, string name, Vector3 at, bool alongX, Material housing, Material tube, Color colour, float intensity, float range, bool shadows, float angle)
        {
            var f = Group(g, name);
            var size = alongX ? new Vector3(1.25f, .07f, .14f) : new Vector3(.14f, .07f, 1.25f);
            BBox("Housing", f, new Vector3(at.x, CeilingY - .035f, at.z), size, housing);
            var tubeSize = alongX ? new Vector3(1.18f, .03f, .035f) : new Vector3(.035f, .03f, 1.18f);
            var side = alongX ? new Vector3(0, 0, .035f) : new Vector3(.035f, 0, 0);
            BBox("Tube", f, new Vector3(at.x, CeilingY - .085f, at.z) + side, tubeSize, tube).GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            BBox("Tube", f, new Vector3(at.x, CeilingY - .085f, at.z) - side, tubeSize, tube).GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            var l = AddLight(f, name, new Vector3(at.x, CeilingY - .14f, at.z), LightType.Spot, colour, intensity, range, shadows);
            l.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            l.spotAngle = angle; l.innerSpotAngle = angle * .4f;
            // A real batten also lights the ceiling round it.
            AddLight(f, name + " ceiling halo", new Vector3(at.x, CeilingY - .2f, at.z), LightType.Point, colour, intensity * .12f, 1.6f, false);
        }

        static void BakeRoomProbe(Transform g)
        {
            var go = new GameObject("Room reflection probe");
            go.transform.SetParent(g, false);
            go.transform.position = new Vector3(0, 1.3f, 3.3f);
            var probe = go.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Custom;
            probe.boxProjection = true;
            probe.size = new Vector3(WallR - WallL, CeilingY, WallBack - FrontFace);
            probe.center = new Vector3(0, CeilingY * .5f - 1.3f, (WallBack + FrontFace) * .5f - 3.3f);
            probe.resolution = 128; probe.hdr = true; probe.blendDistance = .3f; probe.importance = 2;
            var room = new Bounds();
            room.SetMinMax(new Vector3(WallL - .3f, -.3f, -.6f), new Vector3(WallR + .3f, CeilingY + .3f, WallBack + .3f));
            var flagged = new List<(GameObject go, StaticEditorFlags flags)>();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || !r.bounds.Intersects(room)) continue;
                var f = GameObjectUtility.GetStaticEditorFlags(r.gameObject);
                if ((f & StaticEditorFlags.ReflectionProbeStatic) != 0) continue;
                flagged.Add((r.gameObject, f));
                GameObjectUtility.SetStaticEditorFlags(r.gameObject, f | StaticEditorFlags.ReflectionProbeStatic);
            }
            var path = InteriorDir + "/Room reflection.exr";
            try { Lightmapping.BakeReflectionProbe(probe, path); }
            finally { foreach (var (o, f) in flagged) GameObjectUtility.SetStaticEditorFlags(o, f); }
            AssetDatabase.ImportAsset(path);
            probe.customBakedTexture = AssetDatabase.LoadAssetAtPath<Texture>(path);
            log.Append("room reflection probe baked from ").Append(flagged.Count).Append(" renderers: ").Append(probe.customBakedTexture != null).Append('\n');
        }

        static Light AddLight(Transform parent, string name, Vector3 pos, LightType type, Color colour, float intensity, float range, bool shadows)
        {
            var l = new GameObject(name + " light").AddComponent<Light>();
            l.transform.SetParent(parent, false);
            l.transform.position = pos;
            l.type = type; l.color = colour; l.intensity = intensity; l.range = range;
            l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            return l;
        }

        // ------------------------------------------------------------------------------------------------ customer side
        static void WaitingCorner(Transform g)
        {
            var steel = Worn("Int - chair frame, dark steel", Hex("#363B3A"), .45f, .6f);
            var vinyl = Worn("Int - vinyl seat, faded teal", Hex("#3C504D"), .42f);
            var tape = Lit("Int - repair tape, grey", Hex("#86887F"), .3f);
            var enamel = Worn("Int - appliance enamel, old white", Hex("#C3BEAE"), .45f);
            var rubber = Lit("Int - rubber mat, wet", Hex("#1E201F"), .22f);

            // Three linked waiting chairs along the right wall, facing the counter and the gate.
            var chairs = Group(g, "Waiting chairs - three linked");
            float x = 3.06f;
            BBox("Beam", chairs, new Vector3(x, .36f, 1.45f), new Vector3(.06f, .05f, 1.72f), steel);
            foreach (float lz in new[] { .7f, 2.2f })
            {
                BBox("Leg", chairs, new Vector3(x, .18f, lz), new Vector3(.05f, .36f, .05f), steel);
                BBox("Foot", chairs, new Vector3(x, .015f, lz), new Vector3(.5f, .03f, .05f), steel);
            }
            float[] seats = { .88f, 1.45f, 2.02f };
            for (int k = 0; k < 3; k++)
            {
                float sz = seats[k];
                BBox("Seat", chairs, new Vector3(x - .02f, .43f, sz), new Vector3(.46f, .05f, .5f), vinyl);
                BBox("Back", chairs, new Vector3(x + .24f, .72f, sz), new Vector3(.045f, .44f, .48f), vinyl, Quaternion.Euler(0, 0, 8));
                BBox("Back bracket", chairs, new Vector3(x + .22f, .5f, sz), new Vector3(.03f, .14f, .04f), steel);
                if (k == 1)
                {
                    BBox("Tape across the split seat", chairs, new Vector3(x - .02f, .457f, sz), new Vector3(.36f, .004f, .06f), tape, Quaternion.Euler(0, 35, 0));
                    BBox("Tape across the split seat", chairs, new Vector3(x - .02f, .458f, sz + .03f), new Vector3(.3f, .004f, .055f), tape, Quaternion.Euler(0, -40, 0));
                }
            }
            // Someone's paper left on the end seat.
            BBox("Folded newspaper", chairs, new Vector3(x - .05f, .465f, 2.05f), new Vector3(.26f, .018f, .34f), Lit("Int - newsprint", Hex("#B9B5A6"), .2f), Quaternion.Euler(0, 12, 0));

            // An oil-filled radiator next to the chairs, plugged into the wall; amber pilot lamp.
            var heater = Group(g, "Electric radiator");
            float hz = 2.9f, hx = 3.2f;
            for (int k = 0; k < 7; k++)
                BBox("Fin", heater, new Vector3(hx, .38f, hz - .17f + k * .057f), new Vector3(.14f, .52f, .03f), enamel);
            BBox("Spine", heater, new Vector3(hx, .38f, hz), new Vector3(.06f, .5f, .4f), enamel);
            BBox("Control box", heater, new Vector3(hx, .4f, hz + .23f), new Vector3(.1f, .14f, .06f), enamel);
            BBox("Pilot lamp", heater, new Vector3(hx - .051f, .44f, hz + .23f), new Vector3(.006f, .018f, .018f), Lit("Int - pilot lamp, amber", Hex("#D9A968"), .6f, 0, null, new Color(1f, .62f, .25f) * 2f));
            foreach (float wz in new[] { hz - .15f, hz + .15f }) Cyl("Castor", heater, new Vector3(hx, .03f, wz), .03f, .03f, Worn("Int - black plastic", Hex("#202221"), .4f), Quaternion.Euler(0, 0, 90));
            Tube("Heater cable", heater, Catenary(new Vector3(hx, .35f, hz + .26f), new Vector3(WallR - .02f, .32f, hz + .45f), .25f, 8), .005f, Lit("Int - cable, black", Hex("#151615"), .3f));
            BBox("Wall socket", heater, new Vector3(WallR - .015f, .32f, hz + .45f), new Vector3(.02f, .08f, .08f), enamel);

            // Water dispenser in the corner by the front wall, with a stack of paper cups.
            var water = Group(g, "Water dispenser");
            BBox("Body", water, new Vector3(3.17f, .48f, .5f), new Vector3(.32f, .96f, .32f), enamel);
            Cyl("Bottle", water, new Vector3(3.17f, 1.17f, .5f), .13f, .42f, Worn("Int - water bottle, blue plastic", new Color(.55f, .66f, .7f), .8f));
            BBox("Tap hot", water, new Vector3(3.0f, .82f, .45f), new Vector3(.03f, .04f, .03f), Worn("Int - tap red", Hex("#8A3A32"), .5f));
            BBox("Tap cold", water, new Vector3(3.0f, .82f, .55f), new Vector3(.03f, .04f, .03f), Worn("Int - tap blue", Hex("#3A5866"), .5f));
            BBox("Drip tray", water, new Vector3(3.0f, .62f, .5f), new Vector3(.08f, .02f, .2f), black: true);
            Cyl("Paper cups", water, new Vector3(3.2f, .76f, .71f), .035f, .2f, Lit("Int - paper cup", Hex("#D7D2C2"), .2f));

            // Notice board and a clock on the front wall (the clerk faces them from the counter), the care poster over the
            // chairs.
            var front = Group(g, "Front wall - notices and clock");
            float fz = FrontFace;
            BBox("Board frame", front, new Vector3(1.75f, 1.55f, fz + .012f), new Vector3(1.02f, .66f, .024f), Worn("Int - board frame, dark wood", Hex("#3B342B"), .35f));
            Decal("Notice board", front, new Vector3(1.75f, 1.55f, fz + .025f), Vector3.forward, .96f, .6f, Lit("Int - notice board", Color.white, .2f, 0, Tex("int_notice_board")), 0f);
            Cyl("Clock body", front, new Vector3(.3f, 2.05f, fz + .025f), .16f, .05f, Worn("Int - clock body", Hex("#2E302E"), .5f), Quaternion.Euler(90, 0, 0));
            Decal("Clock face", front, new Vector3(.3f, 2.05f, fz + .051f), Vector3.forward, .3f, .3f, Lit("Int - clock face", Color.white, .5f, 0, Tex("int_clock")), 0f);
            Decal("Care poster", front, new Vector3(WallR - .005f, 1.45f, 1.45f), Vector3.left, .42f, .63f, Lit("Int - care poster", Color.white, .15f, 0, Tex("int_care_poster")), 0f);

            // Inside the gate: a rubber mat with melt water on it, and a bucket of wet umbrellas.
            var entry = Group(g, "Entrance mat and umbrellas");
            BBox("Rubber mat", entry, new Vector3(-1.52f, .006f, .66f), new Vector3(1.3f, .012f, .9f), rubber);
            Decal("Melt water", entry, new Vector3(-1.45f, .0f, 1.25f), Vector3.up, 1.1f, .7f, Lit("Int - melt water", new Color(.06f, .07f, .07f, .45f), .66f, 0, null, mode: Mode.Transparent), .014f);
            Cyl("Umbrella bucket", entry, new Vector3(-.5f, .21f, .4f), .11f, .42f, Worn("Int - bucket, galvanised", Hex("#6E7472"), .45f, .6f));
            Rod("Umbrella", entry, new Vector3(-.52f, .1f, .4f), new Vector3(-.46f, .98f, .37f), .009f, black: true);
            Cyl("Umbrella canopy, furled", entry, new Vector3(-.48f, .62f, .38f), .045f, .55f, Lit("Int - umbrella cloth", Hex("#23282B"), .55f), Quaternion.Euler(0, 0, -4));
        }

        // Overloads for the common black parts, so the props above read as a list.
        static GameObject BBox(string name, Transform parent, Vector3 centre, Vector3 size, bool black) =>
            BBox(name, parent, centre, size, Worn("Int - black plastic", Hex("#202221"), .4f));

        static void Rod(string name, Transform parent, Vector3 a, Vector3 b, float radius, bool black) =>
            Rod(name, parent, a, b, radius, Worn("Int - black plastic", Hex("#202221"), .4f));

        // Props: the colour over a tiling wear texture (grime blotches, speckle, light scratches) whose alpha drives the
        // smoothness, so handled and scratched spots shine and grimy ones go dull.
        static Material Worn(string name, Color c, float smooth, float metal = 0)
        {
            if (mats.TryGetValue(name, out var have)) return have;
            var m = Lit(name, new Color(c.r * 1.17f, c.g * 1.17f, c.b * 1.17f, 1), Mathf.Min(1, smooth * 1.3f), metal, Tex("int_wear"));
            m.SetFloat("_SmoothnessTextureChannel", 1);
            m.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            EditorUtility.SetDirty(m);
            return m;
        }

        // A box with chamfered edges (edges catch the light; plain cubes never do) and UVs in metres, so the wear tiles at
        // the same density on every prop. Very thin parts stay plain cubes.
        static readonly Dictionary<string, Mesh> bevelled = new Dictionary<string, Mesh>();

        static GameObject BBox(string name, Transform parent, Vector3 centre, Vector3 size, Material m, Quaternion? rot = null)
        {
            float min = Mathf.Min(size.x, Mathf.Min(size.y, size.z));
            if (min < .01f) return Box(name, parent, centre, size, m, rot);
            float b = Mathf.Min(.012f, min * .28f);
            var key = size.x.ToString("F3") + "_" + size.y.ToString("F3") + "_" + size.z.ToString("F3") + "_" + b.ToString("F4");
            if (!bevelled.TryGetValue(key, out var mesh) || mesh == null) bevelled[key] = mesh = BevelMesh(size, b);
            return MeshObj(name, parent, mesh, m, centre, rot ?? Quaternion.identity);
        }

        static Mesh BevelMesh(Vector3 size, float b)
        {
            var h = size * .5f;
            var v = new List<Vector3>(); var t = new List<int>(); var uv = new List<Vector2>();
            Vector3 F(int a, float sa, int a1, float s1, int a2, float s2)
            {
                var q = Vector3.zero;
                q[a] = sa * h[a]; q[a1] = s1 * (h[a1] - b); q[a2] = s2 * (h[a2] - b);
                return q;
            }
            void Poly(params Vector3[] ps)
            {
                var c = Vector3.zero;
                foreach (var q in ps) c += q;
                if (Vector3.Dot(Vector3.Cross(ps[1] - ps[0], ps[2] - ps[0]), c) < 0) System.Array.Reverse(ps);
                var n = Vector3.Cross(ps[1] - ps[0], ps[2] - ps[0]);
                int ax = Mathf.Abs(n.x) > Mathf.Abs(n.y) ? (Mathf.Abs(n.x) > Mathf.Abs(n.z) ? 0 : 2) : (Mathf.Abs(n.y) > Mathf.Abs(n.z) ? 1 : 2);
                int i0 = v.Count;
                foreach (var q in ps) { v.Add(q); uv.Add(new Vector2(q[(ax + 1) % 3], q[(ax + 2) % 3]) * 2); }
                for (int k = 1; k < ps.Length - 1; k++) { t.Add(i0); t.Add(i0 + k); t.Add(i0 + k + 1); }
            }
            float[] sg = { -1, 1 };
            for (int a = 0; a < 3; a++)
                foreach (var sa in sg)
                {
                    int a1 = (a + 1) % 3, a2 = (a + 2) % 3;
                    Poly(F(a, sa, a1, -1, a2, -1), F(a, sa, a1, 1, a2, -1), F(a, sa, a1, 1, a2, 1), F(a, sa, a1, -1, a2, 1));
                }
            for (int a = 0; a < 3; a++)
                for (int c = a + 1; c < 3; c++)
                {
                    int e = 3 - a - c;
                    foreach (var sa in sg) foreach (var sc in sg)
                        Poly(F(a, sa, c, sc, e, -1), F(a, sa, c, sc, e, 1), F(c, sc, a, sa, e, 1), F(c, sc, a, sa, e, -1));
                }
            foreach (var sx in sg) foreach (var sy in sg) foreach (var sz in sg)
                Poly(F(0, sx, 1, sy, 2, sz), F(1, sy, 0, sx, 2, sz), F(2, sz, 0, sx, 1, sy));
            return SaveMesh("Bevel box", v, t, uv);
        }

        // ------------------------------------------------------------------------------------------------ surgery bay
        static void SurgeryBay(Transform g)
        {
            // White tile, applied over the room's wall to 1.8 m on the bay's three sides.
            var tile = SurfaceMat("Int - wall tile, surgery bay", "srf_int_tile", 1.2f, 5, InTile, 0, .7f, .3f);
            tile.SetFloat("_Dado", 0); tile.SetFloat("_FootGrime", .25f); tile.SetFloat("_GlossVar", .35f); tile.SetFloat("_NormalScale", .8f);
            var tiles = Group(g, "Wall tile to 1.8 m");
            float h = 1.8f;
            Box("Tile - right wall", tiles, new Vector3(WallR - .01f, h * .5f, (3.62f + WallBack) * .5f), new Vector3(.02f, h, WallBack - 3.62f), tile);
            Box("Tile - back wall", tiles, new Vector3((1.21f + WallR) * .5f, h * .5f, WallBack - .01f), new Vector3(WallR - 1.21f, h, .02f), tile);
            Box("Tile - partition", tiles, new Vector3(1.22f, h * .5f, (3.55f + WallBack) * .5f), new Vector3(.02f, h, WallBack - 3.55f), tile);
            var cap = Worn("Int - tile edge, white", Hex("#CFCFC8"), .5f);
            BBox("Tile edge - right", tiles, new Vector3(WallR - .015f, h + .01f, (3.62f + WallBack) * .5f), new Vector3(.03f, .02f, WallBack - 3.62f), cap);
            BBox("Tile edge - back", tiles, new Vector3((1.21f + WallR) * .5f, h + .01f, WallBack - .015f), new Vector3(WallR - 1.21f, .02f, .03f), cap);
            BBox("Tile edge - partition", tiles, new Vector3(1.225f, h + .01f, (3.55f + WallBack) * .5f), new Vector3(.03f, .02f, WallBack - 3.55f), cap);

            // Curtain on a ceiling rail across the bay's front, drawn back to the right wall.
            var curtain = Group(g, "Curtain - drawn back");
            var steel = Worn("Int - rail, brushed steel", Hex("#8C918F"), .6f, .8f);
            float railZ = 3.68f, railY = CeilingY - .07f;
            BBox("Rail", curtain, new Vector3((1.21f + WallR) * .5f, railY, railZ), new Vector3(WallR - 1.21f, .025f, .03f), steel);
            foreach (float rx in new[] { 1.5f, 2.4f, 3.3f }) BBox("Rail hanger", curtain, new Vector3(rx, (railY + CeilingY) * .5f, railZ), new Vector3(.015f, CeilingY - railY, .015f), steel);
            var cloth = Lit("Int - curtain, pale green cotton", Hex("#7C8B84"), .2f);
            cloth.SetFloat("_Cull", 0); cloth.doubleSidedGI = true;
            MeshObj("Curtain folds", curtain, Pleats(2.98f, WallR - .03f, railZ, .2f, railY - .03f, 9, .09f), cloth, Vector3.zero, Quaternion.identity);
            Decal("Plate - surgery bay", curtain, new Vector3(1.75f, railY - .17f, railZ - .02f), Vector3.back, .36f, .135f, Lit("Int - bay plate", Color.white, .3f, 0, Tex("int_bay_plate")), 0f);
            Rod("Plate wire", curtain, new Vector3(1.62f, railY, railZ - .02f), new Vector3(1.62f, railY - .1f, railZ - .02f), .002f, steel);
            Rod("Plate wire", curtain, new Vector3(1.88f, railY, railZ - .02f), new Vector3(1.88f, railY - .1f, railZ - .02f), .002f, steel);

            // Instrument cart between the partition and the table's foot; a clinical waste bin in the back corner.
            var cart = Group(g, "Instrument cart");
            var cx = 1.58f; var cz = 4.25f;
            foreach (var sh in new[] { .32f, .82f }) BBox("Shelf", cart, new Vector3(cx, sh, cz), new Vector3(.42f, .02f, .56f), steel);
            foreach (var dx in new[] { -.19f, .19f }) foreach (var dz in new[] { -.26f, .26f })
                {
                    BBox("Post", cart, new Vector3(cx + dx, .44f, cz + dz), new Vector3(.02f, .82f, .02f), steel);
                    Cyl("Castor", cart, new Vector3(cx + dx, .025f, cz + dz), .025f, .025f, Worn("Int - black plastic", Hex("#202221"), .4f), Quaternion.Euler(0, 0, 90));
                }
            BBox("Tray", cart, new Vector3(cx, .84f, cz - .06f), new Vector3(.3f, .02f, .22f), steel);
            for (int k = 0; k < 4; k++) BBox("Instrument", cart, new Vector3(cx - .1f + k * .065f, .853f, cz - .06f), new Vector3(.012f, .008f, .17f), steel);
            BBox("Glove box", cart, new Vector3(cx, .385f, cz + .14f), new Vector3(.24f, .11f, .13f), Worn("Int - glove box", Hex("#7E8F96"), .3f));
            Cyl("Tape roll", cart, new Vector3(cx + .1f, .86f, cz + .18f), .035f, .03f, Lit("Int - tape roll", Hex("#D8D3C2"), .3f));
            Cyl("Waste bin", g, new Vector3(3.22f, .25f, 6.05f), .16f, .5f, Worn("Int - waste bin, grey", Hex("#5B605D"), .4f));
            Cyl("Waste bin lid", g, new Vector3(3.22f, .51f, 6.05f), .165f, .025f, Worn("Int - waste bin lid, mustard", Hex("#9C8441"), .4f));
        }

        // A drawn-back curtain: a zigzag of pleats between x0 and x1 at z, from y0 to y1, depth `depth`.
        static Mesh Pleats(float x0, float x1, float z, float y0, float y1, int folds, float depth)
        {
            var v = new List<Vector3>(); var t = new List<int>(); var uv = new List<Vector2>();
            int n = folds * 2;
            for (int i = 0; i <= n; i++)
            {
                float s = i / (float)n;
                float x = Mathf.Lerp(x0, x1, s);
                float dz = (i % 2 == 0 ? -1 : 1) * depth * .5f;
                float hem = .04f * Mathf.Sin(i * 2.3f);                                     // the hem is not quite level
                v.Add(new Vector3(x, y0 + hem, z + dz * 1.25f)); v.Add(new Vector3(x, y1, z + dz * .6f));
                uv.Add(new Vector2(s, 0)); uv.Add(new Vector2(s, 1));
            }
            for (int i = 0; i < n; i++)
            {
                int a = i * 2;
                t.Add(a); t.Add(a + 1); t.Add(a + 2);
                t.Add(a + 2); t.Add(a + 1); t.Add(a + 3);
            }
            return SaveMesh("Curtain pleats", v, t, uv);
        }

        // ------------------------------------------------------------------------------------------------ workshop
        static void WorkshopLife(Transform g)
        {
            var rubber = Lit("Int - rubber mat, worn", Hex("#262827"), .3f);
            // Anti-fatigue mat where the clerk stands behind the counter, and one at the bench.
            BBox("Mat - behind the counter", g, new Vector3(-1.85f, .006f, 3.5f), new Vector3(1.7f, .012f, .75f), rubber);
            BBox("Mat - at the bench", g, new Vector3(-2.15f, .006f, 4.25f), new Vector3(.7f, .012f, 1.5f), rubber);

            // The clerk's things on the left wall by the counter: coat on a hook, last month's calendar.
            var wallL = Group(g, "Left wall - coat and calendar");
            var steel = Worn("Int - worn steel", Hex("#6E7471"), .5f, .7f);
            BBox("Hook rail", wallL, new Vector3(WallL + .015f, 1.72f, 2.35f), new Vector3(.03f, .05f, .5f), Worn("Int - board frame, dark wood", Hex("#3B342B"), .35f));
            foreach (float hz in new[] { 2.2f, 2.5f }) Rod("Hook", wallL, new Vector3(WallL + .03f, 1.72f, hz), new Vector3(WallL + .09f, 1.76f, hz), .006f, steel);
            var parka = Lit("Int - parka, dark olive", Hex("#3A3F33"), .3f);
            BBox("Parka body", wallL, new Vector3(WallL + .1f, 1.32f, 2.2f), new Vector3(.13f, .74f, .42f), parka, Quaternion.Euler(0, 0, -3));
            BBox("Parka hood", wallL, new Vector3(WallL + .1f, 1.72f, 2.2f), new Vector3(.14f, .16f, .26f), Lit("Int - parka fur trim", Hex("#6B6457"), .1f));
            BBox("Parka sleeve", wallL, new Vector3(WallL + .13f, 1.2f, 2.0f), new Vector3(.1f, .52f, .12f), parka, Quaternion.Euler(4, 0, -2));
            Decal("Calendar - still on October", wallL, new Vector3(WallL, 1.58f, 2.95f), Vector3.right, .3f, .45f, Lit("Int - calendar", Color.white, .2f, 0, Tex("int_calendar")), .006f);

            // A string of old festive bulbs along the top of the left wall over the counter: the one bit of winter warmth.
            var bulbs = Group(g, "Old festive bulbs over the counter");
            var path = Catenary(new Vector3(WallL + .04f, 2.42f, 1.0f), new Vector3(WallL + .04f, 2.4f, 3.15f), .14f, 18);
            Tube("Bulb string wire", bulbs, path, .003f, Lit("Int - cable, black", Hex("#151615"), .3f), 4);
            Color[] glow = { new Color(1f, .62f, .25f), new Color(.85f, .3f, .22f), new Color(.45f, .7f, .4f), new Color(1f, .82f, .5f), new Color(.5f, .62f, .75f) };
            for (int k = 1; k < path.Count - 1; k += 2)
            {
                var c = glow[(k / 2) % glow.Length];
                Sphere("Bulb", bulbs, path[k] + Vector3.down * .025f, .014f, Lit("Int - festive bulb " + (k / 2) % glow.Length, c * .8f, .6f, 0, null, c * 1.4f));
            }

            // A thermos by the cash box; a rag drying on the dock kit's cable tray.
            var counterTop = AnyNamed("ZC_CounterTop").GetComponent<Renderer>().bounds.max.y;
            Cyl("Thermos", g, new Vector3(-2.88f, counterTop + .13f, 2.6f), .038f, .26f, Worn("Int - thermos, dull red", Hex("#6E3A33"), .45f, .2f));
            Cyl("Thermos cup", g, new Vector3(-2.88f, counterTop + .28f, 2.6f), .04f, .05f, Worn("Int - thermos cup, grey", Hex("#7A7D79"), .5f, .6f));
            BBox("Rag drying", g, new Vector3(-.82f, 2.05f, WallBack - .12f), new Vector3(.24f, .32f, .012f), Lit("Int - rag, faded orange", Hex("#8A5A3E"), .15f), Quaternion.Euler(0, 0, 4));
        }

        // ------------------------------------------------------------------------------------------------ services
        static void InteriorServices(Transform g)
        {
            // A heating pipe added later: comes through the front wall high on the right, runs along the waiting side under
            // the ceiling and goes out through the right wall before the bay. Its taped joint drips into a bucket, and the
            // wall below it carries the damp streak (wall leaks trace back to a pipe).
            var foil = Worn("Int - pipe lagging, foil", Hex("#9A9F9E"), .55f, .5f);
            var tape = Lit("Int - repair tape, grey", Hex("#86887F"), .3f);
            var bracket = Worn("Int - worn steel", Hex("#6E7471"), .5f, .7f);
            float px = WallR - .14f, py = CeilingY - .17f;
            Rod("Heating pipe", g, new Vector3(px, py, WallFront), new Vector3(px, py, 3.35f), .05f, foil);
            Rod("Heating pipe into the wall", g, new Vector3(px, py, 3.35f), new Vector3(WallR, py, 3.35f), .05f, foil);
            for (float bz = .6f; bz < 3.3f; bz += 1.1f)
                BBox("Pipe bracket", g, new Vector3(px + .07f, py + .09f, bz), new Vector3(.16f, .02f, .03f), bracket);
            Cyl("Taped joint", g, new Vector3(px, py, 2.42f), .066f, .14f, tape, Quaternion.Euler(90, 0, 0));
            Cyl("Drip bucket", g, new Vector3(px - .02f, .13f, 2.42f), .13f, .26f, Worn("Int - bucket, blue plastic", Hex("#4A5E6A"), .4f));
            Cyl("Water in the bucket", g, new Vector3(px - .02f, .2f, 2.42f), .118f, .005f, Lit("Int - dark water", Hex("#141A1C"), .9f));
            Decal("Damp streak under the joint", g, new Vector3(WallR, 1.65f, 2.45f), Vector3.left, .32f, 1.25f,
                  Lit("Int - damp streak", new Color(.16f, .15f, .12f, .65f), .5f, 0, Tex("rust_streak"), mode: Mode.Transparent), .004f);
        }

        // ------------------------------------------------------------------------------------------------ text
        // Every 3D label in the scene used Unity's font material, which draws over walls: the stairhead "B1 REPAIR" showed
        // mirrored through the basement wall and the counter's "收件" through the counter. One depth-tested material per
        // font; a label seen from behind is culled.
        static void DepthTestedText()
        {
            var shader = Shader.Find("BorderRepair/Interior/DepthText");
            int n = 0;
            foreach (var tm in Object.FindObjectsByType<TextMesh>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var r = tm.GetComponent<MeshRenderer>();
                if (r == null || tm.font == null) continue;
                var name = "Text - depth tested - " + tm.font.name;
                if (!mats.TryGetValue(name, out var m))
                {
                    m = new Material(shader);
                    m.SetTexture("_MainTex", tm.font.material.mainTexture);
                    m = Save(name, m);
                }
                r.sharedMaterial = m;
                n++;
            }
            log.Append("text meshes on the depth-tested material: ").Append(n).Append('\n');
        }

        // ------------------------------------------------------------------------------------------------ review
        [MenuItem("Border Repair/Interior/Inspect B1 clinic")]
        public static void InspectInterior()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var sb = new StringBuilder();
            foreach (var root in scene.GetRootGameObjects())
            {
                var inside = root.GetComponentsInChildren<Renderer>(true).Where(r => Room.Contains(r.bounds.center)).ToList();
                var lights = root.GetComponentsInChildren<Light>(true).Where(l => Room.Contains(l.transform.position)).ToList();
                if (inside.Count + lights.Count == 0) continue;
                sb.Append("## ").Append(root.name).Append("  renderers inside: ").Append(inside.Count).Append('\n');
                foreach (var grp in inside.GroupBy(r => GroupKey(r.transform, root.transform)))
                {
                    var b = grp.First().bounds;
                    foreach (var r in grp) b.Encapsulate(r.bounds);
                    sb.Append("  ").Append(PathOf(grp.Key, root.transform)).Append("  n=").Append(grp.Count())
                      .Append(" c=").Append(b.center.ToString("F2")).Append(" s=").Append(b.size.ToString("F2"))
                      .Append(grp.Key.gameObject.activeInHierarchy ? "" : " (inactive)").Append('\n');
                }
                foreach (var l in lights)
                    sb.Append("  LIGHT ").Append(PathOf(l.transform, root.transform)).Append(' ').Append(l.type).Append(" p=").Append(l.transform.position.ToString("F2"))
                      .Append(" #").Append(ColorUtility.ToHtmlStringRGB(l.color)).Append(" i=").Append(l.intensity.ToString("F2")).Append(" r=").Append(l.range.ToString("F2")).Append('\n');
            }
            // Optional probe: INTERIOR_PROBE="x0 y0 z0 x1 y1 z1" lists every renderer whose bounds touch that box.
            var probe = System.Environment.GetEnvironmentVariable("INTERIOR_PROBE");
            if (!string.IsNullOrEmpty(probe))
            foreach (var one in probe.Split(';'))
            {
                var f = one.Trim().Split(' ').Select(float.Parse).ToArray();
                var box = new Bounds(); box.SetMinMax(new Vector3(f[0], f[1], f[2]), new Vector3(f[3], f[4], f[5]));
                sb.Append("## probe ").Append(one).Append('\n');
                foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.bounds.Intersects(box)))
                    sb.Append("  ").Append(PathOf(r.transform, null)).Append(" c=").Append(r.bounds.center.ToString("F2")).Append(" s=").Append(r.bounds.size.ToString("F2"))
                      .Append(" mat=").Append(r.sharedMaterial ? r.sharedMaterial.name : "none").Append('\n');
            }
            Directory.CreateDirectory(OutDir);
            File.WriteAllText(Path.Combine(OutDir, "interior.txt"), sb.ToString());
            Debug.Log("[Interior] inspected");
        }

        static Transform GroupKey(Transform t, Transform root)
        {
            var chain = new List<Transform>();
            for (var p = t; p != null && p != root; p = p.parent) chain.Add(p);
            chain.Reverse();
            for (int i = 0; i < chain.Count; i++)
                if (PrefabUtility.IsAnyPrefabInstanceRoot(chain[i].gameObject) || i == 1) return chain[i];
            return chain.Count > 0 ? chain[chain.Count - 1] : t;
        }

        static string PathOf(Transform t, Transform stop)
        {
            var parts = new List<string>();
            for (var p = t; p != null && p != stop; p = p.parent) parts.Add(p.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        // Interior views, the counter eye and stair landing cameras, and a plan from above with the ceiling and anything
        // over 2.45 m switched off. Does not save the scene.
        [MenuItem("Border Repair/Interior/Render interior shots")]
        public static void InteriorShots()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            ShaderUtil.allowAsyncCompilation = false;
            var dir = Path.Combine(OutDir, "shots");
            Directory.CreateDirectory(dir);
            foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
                if (ps.transform.parent == null || ps.transform.parent.GetComponent<ParticleSystem>() == null) ps.Simulate(8f, true, true);
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            var go = new GameObject("Interior review camera");
            var cam = go.AddComponent<Camera>();
            GameObject post = null;
            if (System.Environment.GetEnvironmentVariable("INTERIOR_LOOK") != "raw")
            {
                var data = cam.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = true;
                data.SetRenderer(1);
                data.volumeLayerMask = 1 | (1 << 30);
                var look = System.Type.GetType("BorderRepair.Art.UnifiedClinic.RetroClinicCamera, BorderRepair.Art.UnifiedClinic.Runtime");
                if (look != null) go.AddComponent(look);
                post = new GameObject("Review post - clinic room profile");
                var volume = post.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = 10;
                volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/WorkbenchArea/Art/WB_PostProfile.asset");
            }
            cam.nearClipPlane = .03f; cam.farClipPlane = 200;
            foreach (var cs in new[] { "Counter eye - facing street", "Main Camera - stair landing eye height" })
            {
                var c = GameObject.Find(cs);
                if (c == null) continue;
                go.transform.SetPositionAndRotation(c.transform.position, c.transform.rotation);
                cam.fieldOfView = c.GetComponent<Camera>().fieldOfView;
                GrabShot(cam, rt, tex, Path.Combine(dir, Safe(cs) + ".png"));
            }
            foreach (var v in InteriorViews)
            {
                go.transform.SetPositionAndRotation(v.eye, Quaternion.LookRotation(v.look - v.eye));
                cam.fieldOfView = v.fov;
                GrabShot(cam, rt, tex, Path.Combine(dir, Safe(v.name) + ".png"));
            }
            // Optional extra views: INTERIOR_VIEWS="name|ex ey ez|lx ly lz;..."
            var extra = System.Environment.GetEnvironmentVariable("INTERIOR_VIEWS");
            if (!string.IsNullOrEmpty(extra))
                foreach (var spec in extra.Split(';'))
                {
                    var parts = spec.Split('|');
                    var e = parts[1].Trim().Split(' ').Select(float.Parse).ToArray();
                    var l = parts[2].Trim().Split(' ').Select(float.Parse).ToArray();
                    var eye = new Vector3(e[0], e[1], e[2]);
                    go.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(new Vector3(l[0], l[1], l[2]) - eye));
                    cam.fieldOfView = 55;
                    GrabShot(cam, rt, tex, Path.Combine(dir, Safe(parts[0].Trim()) + ".png"));
                }
            var hidden = new List<Renderer>();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if (r.enabled && (r.bounds.min.y > 2.3f || r.name == "Ceiling" || (r.bounds.center.y > 2.2f && r.bounds.size.y < .6f)))
                { r.enabled = false; hidden.Add(r); }
            var plan = new GameObject("Plan").AddComponent<Camera>();
            plan.orthographic = true; plan.orthographicSize = 4.4f;
            plan.transform.SetPositionAndRotation(new Vector3(0, 30, 3.1f), Quaternion.Euler(90, 0, 0));
            plan.nearClipPlane = 1; plan.farClipPlane = 40;
            GrabShot(plan, rt, tex, Path.Combine(dir, "Int_0_-_plan.png"));
            foreach (var r in hidden) r.enabled = true;
            Object.DestroyImmediate(plan.gameObject);
            Object.DestroyImmediate(go);
            if (post != null) Object.DestroyImmediate(post);
            rt.Release();
            Debug.Log("[Interior] rendered shots into " + dir);
        }

        static void GrabShot(Camera cam, RenderTexture rt, Texture2D tex, string file)
        {
            var was = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = was;
            File.WriteAllBytes(file, tex.EncodeToPNG());
        }
    }
}
