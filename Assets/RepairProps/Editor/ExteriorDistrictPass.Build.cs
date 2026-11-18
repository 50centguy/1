using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BorderRepair.RepairProps.EditorTools
{
    // The exterior pass over the Berth 04 district. Re-runnable: everything it adds lives under one root that is rebuilt,
    // and the edits to the handed-over geometry (static flags, colliders, far shadows, window lights, snowfall and wharf
    // lamp levels) set absolute values. Written for the walk B1 -> stairs -> court mouth -> berth 04 and back.
    public static partial class ExteriorDistrictPass
    {
        const string AssetDir = "Assets/RepairProps/Scenes/Temp_ExteriorPass";
        const string TexDir = AssetDir + "/Textures", MatDir = AssetDir + "/Materials", MeshDir = AssetDir + "/Meshes";
        const string PassRoot = "08 Exterior pass - wayfinding, life, snow and light";

        // Roots of the handed-over exterior (the clinic interior prefabs and the lights/cameras roots stay as they are).
        static readonly string[] ExteriorRoots =
        {
            "01 Basement shell and open entrance", "04 Winter street and basement stairhead", "05 Street exterior study",
            "06 Winter harbour and stairwell", "07 Courtyard junction and berth 04", "Settled snow - stair shoulders and wheel ruts",
            "Night service enamel plaque", PassRoot,
        };

        // The walking route, B1 stairhead to the shore power terminal (x, z), and the branch to the residents' door.
        static readonly Vector2[] Route =
        {
            new Vector2(-1.5f, -5.0f), new Vector2(-1.9f, -5.9f), new Vector2(-4.5f, -6.1f), new Vector2(-8.5f, -5.8f),
            new Vector2(-11.6f, -4.9f), new Vector2(-13.4f, -3.6f), new Vector2(-15.2f, -3.2f), new Vector2(-18.5f, -3.1f),
            new Vector2(-21.8f, -2.9f), new Vector2(-24.0f, -3.0f),
        };
        static readonly Vector2[] CourtBranch =
        {
            new Vector2(-11.6f, -3.0f), new Vector2(-11.2f, -0.5f), new Vector2(-11.8f, 1.5f), new Vector2(-13.9f, 2.0f), new Vector2(-14.1f, 3.0f),
        };

        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        static int meshCount, groundHits, groundMisses;
        static StringBuilder log;

        [MenuItem("Border Repair/Exterior/Apply exterior pass")]
        public static void Apply()
        {
            log = new StringBuilder();
            mats.Clear(); meshCount = 0; groundHits = 0; groundMisses = 0;
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Count("before");

            var old = scene.GetRootGameObjects().FirstOrDefault(g => g.name == PassRoot);
            if (old != null) Object.DestroyImmediate(old);
            PrepareAssets();
            var lent = new List<Collider>();
            foreach (var exteriorRoot in scene.GetRootGameObjects().Where(r => ExteriorRoots.Contains(r.name)))
                foreach (var mf in exteriorRoot.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh != null && mf.GetComponent<Collider>() == null && mf.GetComponent<MeshRenderer>() != null)
                        lent.Add(mf.gameObject.AddComponent<MeshCollider>());
            Physics.SyncTransforms();

            var root = new GameObject(PassRoot).transform;
            BuildWayfinding(Group(root, "Wayfinding - the way back to B1"));
            BuildLife(Group(root, "Life - residents and night shift"));
            BuildCyber(Group(root, "Cyber - devices people use"));
            BuildSnow(Group(root, "Snow - path, icicles, heaps, steam"));
            BuildLamps(Group(root, "Light - lamp volumes and far lights"));
            BuildCameras(Group(root, "Review cameras"));
            VaryWindows();
            foreach (var c in lent) Object.DestroyImmediate(c);
            log.Append("meshes lent a probe collider while building: ").Append(lent.Count).Append('\n');
            Optimise();
            Count("after");
            log.Append("ground probes: ").Append(groundHits).Append(" hit, ").Append(groundMisses).Append(" missed; meshes saved: ").Append(meshCount).Append('\n');

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory(OutDir);
            File.WriteAllText(Path.Combine(OutDir, "report.txt"), log.ToString());
            Debug.Log("[Exterior] applied\n" + log);
        }

        // ------------------------------------------------------------------------------------------------ wayfinding
        static void BuildWayfinding(Transform g)
        {
            // A lit blade sign standing out from the facade between the first-floor windows, over the pipe run: the shop
            // reads from both ends of the street (the lightbox over the stairs faces the stairs and is edge-on from there).
            var blade = Group(g, "Blade sign - 义体修理 B1");
            var steel = Lit("Dark steel", Hex("#292E30"), .35f, .4f);
            var sign = Lit("Blade sign enamel", Color.white, .25f, 0, Tex("blade_sign"), new Color(.78f, .72f, .6f), Tex("blade_sign"));
            float bx = -3.4f, by = 5.6f, bz = -0.72f;   // between the first-floor windows, above the pipe run
            Box("Blade frame", blade, new Vector3(bx, by, bz), new Vector3(.1f, 1.42f, .62f), steel);
            Decal("Blade face east", blade, new Vector3(bx + .052f, by, bz), Vector3.right, .56f, 1.32f, sign, 0f);
            Decal("Blade face west", blade, new Vector3(bx - .052f, by, bz), Vector3.left, .56f, 1.32f, sign, 0f);
            Box("Blade bracket upper", blade, new Vector3(bx, by + .6f, -.22f), new Vector3(.04f, .04f, .38f), steel);
            Box("Blade bracket lower", blade, new Vector3(bx, by - .6f, -.22f), new Vector3(.04f, .04f, .38f), steel);
            SnowCap(blade, new Vector3(bx, by + .72f, bz), new Vector3(.16f, .045f, .66f));

            // The back of the junction post: what someone walking back from the berth reads.
            var plate = Group(g, "Junction post - back arrow to B1");
            Box("Arrow plate backing", plate, new Vector3(-13.075f, 2.42f, -2.2f), new Vector3(.03f, .31f, .79f), steel);
            Decal("Arrow plate face", plate, new Vector3(-13.09f, 2.42f, -2.2f), Vector3.left, .75f, .28f, Lit("Junction arrow enamel", Color.white, .3f, 0, Tex("junction_back_arrow"), new Color(.42f, .42f, .4f), Tex("junction_back_arrow")), 0f);

            // The shop's own spray on the ground-floor flank: wrench, B1 and an arrow toward the stairs.
            if (WallHit(new Vector3(-4.7f, 2.08f, -2.5f), Vector3.forward, out var spray))
                Decal("Spray - wrench B1 arrow", g, spray.point, spray.normal, 1.9f, .95f, Lit("Spray paint", new Color(1, 1, 1, .85f), .05f, 0, Tex("spray_b1_arrow"), mode: Mode.Transparent));

            // The out-of-service implant maintenance box, with the tape that sends people down to B1.
            if (WallHit(new Vector3(-7.3f, 2.05f, -2.5f), Vector3.forward, out var cab))
            {
                var box = Group(g, "Implant maintenance box - out of service");
                var paint = Lit("Smoked green paint", Hex("#505C53"), .3f, .1f);
                var front = cab.point + cab.normal * .11f;
                Box("Cabinet", box, cab.point + cab.normal * .11f, new Vector3(.55f, .78f, .22f), paint, Face(cab.normal));
                Box("Door seam", box, front + cab.normal * .112f + Vector3.right * .12f, new Vector3(.01f, .7f, .01f), steel, Face(cab.normal));
                Decal("Dead screen", box, front + cab.normal * .111f + Vector3.up * -.08f, cab.normal, .2f, .12f, Lit("Dead screen", Hex("#171C1E"), .8f), 0f);
                Decal("Out of service tape", box, front + cab.normal * .112f + Vector3.up * .22f, cab.normal, .5f, .156f, Lit("Out of service tape", Color.white, .25f, 0, Tex("out_of_service"), mode: Mode.Cutout), 0f);
                Box("Conduit into the pavement", box, new Vector3(front.x, (Ground(front.x, front.z - .05f) + front.y - .39f) * .5f, front.z - .04f), new Vector3(.05f, front.y - .39f - Ground(front.x, front.z) + .02f, .05f), steel);
            }

            Strip("Trampled path - B1 to berth 04", g, Route, .55f, Lit("Trampled slush", new Color(.72f, .7f, .7f, 1f), .1f, 0, Tex("trampled_path"), mode: Mode.Transparent));
            Strip("Trampled path - court branch", g, CourtBranch, .4f, Mat("Trampled slush"));
        }

        // ------------------------------------------------------------------------------------------------ life
        static void BuildLife(Transform g)
        {
            var steel = Mat("Dark steel");
            var snowMat = SnowMaterial();
            // Stairhead: the shovel the shop clears its steps with, the salt bucket, the heap it made.
            {
                float y = Ground(-.2f, -4.85f);
                var bottom = new Vector3(-.16f, y + .02f, -4.98f);
                var top = new Vector3(-.36f, y + 1.12f, -4.66f);
                var handle = Lit("Worn wood handle", Hex("#5A4636"), .25f);
                Rod("Snow shovel handle", g, bottom + (top - bottom) * .12f, top, .018f, handle);
                Box("Snow shovel blade", g, bottom + (top - bottom).normalized * .1f, new Vector3(.32f, .36f, .02f), Lit("Shovel blade - faded red", Hex("#884A3F"), .3f, .3f), Quaternion.LookRotation(Vector3.Cross(top - bottom, Vector3.right).normalized, (top - bottom).normalized));
                float by = Ground(.05f, -5.35f);
                Cyl("Salt bucket", g, new Vector3(.05f, by + .16f, -5.35f), .14f, .32f, Lit("Salt bucket plastic", new Color(.55f, .5f, .3f), .35f));
                Cyl("Salt bucket lid", g, new Vector3(.05f, by + .33f, -5.35f), .15f, .02f, Lit("Salt bucket lid", new Color(.42f, .45f, .4f), .35f));
            }

            // Residents' court: a bicycle nobody has ridden since the snow, frozen washing, the cat's bowl, notices.
            float wallBike = WallZ(-16.9f);
            {
                var bike = Group(g, "Bicycle under snow");
                var frame = Lit("Bicycle frame - rusted blue", new Color(.24f, .3f, .36f), .3f, .4f);
                var tyre = Lit("Old rubber", Hex("#373935"), .2f);
                float y = Ground(-16.9f, wallBike - .3f), z = wallBike - .3f;
                var rear = new Vector3(-17.4f, y + .33f, z); var front = new Vector3(-16.4f, y + .33f, z);
                MeshObj("Rear wheel", bike, Torus(.32f, .022f, 20, 6), tyre, rear, Quaternion.identity);
                MeshObj("Front wheel", bike, Torus(.32f, .022f, 20, 6), tyre, front, Quaternion.identity);
                var seat = new Vector3(-17.15f, y + .82f, z); var head = new Vector3(-16.55f, y + .86f, z); var crank = new Vector3(-16.95f, y + .3f, z);
                Rod("Top tube", bike, seat, head, .016f, frame); Rod("Down tube", bike, head, crank, .018f, frame);
                Rod("Seat tube", bike, crank, seat + Vector3.up * .05f, .016f, frame); Rod("Chain stay", bike, crank, rear, .012f, frame);
                Rod("Seat stay", bike, seat, rear, .011f, frame); Rod("Fork", bike, head, front, .014f, frame);
                Rod("Handlebar stem", bike, head, head + new Vector3(-.03f, .14f, 0), .014f, frame);
                Rod("Handlebar", bike, head + new Vector3(-.03f, .14f, -.24f), head + new Vector3(-.03f, .14f, .24f), .012f, frame);
                Box("Saddle", bike, seat + Vector3.up * .08f, new Vector3(.24f, .05f, .12f), tyre);
                SnowCap(bike, seat + Vector3.up * .12f, new Vector3(.26f, .035f, .13f));
                SnowCap(bike, Vector3.Lerp(seat, head, .5f) + Vector3.up * .03f, new Vector3(.5f, .025f, .05f));
            }
            {
                var line = Group(g, "Washing line - frozen towels");
                float z = wallBike - .45f;
                Rod("Bracket east", line, new Vector3(-15.4f, 3.3f, wallBike), new Vector3(-15.4f, 3.3f, z), .015f, steel);
                Rod("Bracket west", line, new Vector3(-18.6f, 3.3f, wallBike), new Vector3(-18.6f, 3.3f, z), .015f, steel);
                Rod("Line", line, new Vector3(-15.4f, 3.3f, z), new Vector3(-18.6f, 3.3f, z), .005f, Lit("Washing line", new Color(.6f, .6f, .56f), .2f));
                var colours = new[] { new Color(.45f, .22f, .2f), new Color(.38f, .45f, .52f), new Color(.72f, .7f, .64f), new Color(.6f, .5f, .26f) };
                for (int i = 0; i < 4; i++)
                {
                    float x = -15.9f - i * .78f;
                    var towel = Lit("Towel " + i, colours[i], .15f);
                    Box("Frozen towel " + i, line, new Vector3(x, 3.3f - .33f, z), new Vector3(.48f, .66f, .012f), towel, Quaternion.Euler(0, 0, (i % 2 == 0 ? 2.5f : -2f)));
                    SnowCap(line, new Vector3(x, 3.31f, z), new Vector3(.5f, .02f, .03f));
                }
            }
            {
                float wz = WallZ(-13.25f);
                float y = Ground(-13.25f, wz - .35f);
                Cyl("Cat bowl", g, new Vector3(-13.2f, y + .02f, wz - .32f), .07f, .04f, Lit("Bowl steel", new Color(.55f, .58f, .58f), .7f, .8f));
                Box("Flattened cardboard", g, new Vector3(-12.9f, y + .01f, wz - .45f), new Vector3(.5f, .02f, .4f), Lit("Wet cardboard", new Color(.36f, .29f, .21f), .2f), Quaternion.Euler(0, 12, 0));
            }
            if (WallHit(new Vector3(-11.2f, 2.25f, 1.0f), Vector3.forward, out var board))
                Decal("Court notices", g, board.point, board.normal, 1.5f, .75f, Lit("Court notices", Color.white, .2f, 0, Tex("court_notices"), mode: Mode.Cutout));
            {
                float y = Ground(-11.55f, 2.45f);
                Cyl("Thermos on the bench", g, new Vector3(-11.55f, y + .1f, 2.45f), .045f, .2f, Lit("Thermos red", Hex("#884A3F"), .45f, .3f));
                Cyl("Cigarette tin", g, new Vector3(-11.8f, y + .03f, 2.55f), .05f, .06f, Lit("Tin", new Color(.5f, .52f, .5f), .5f, .7f));
            }

            // Berth: the night shift's crates, a tarp, a thermos and gloves left on top, gas cylinders, the painted number.
            {
                var crates = Group(g, "Berth 04 - crates and the shift's things");
                var wood = Lit("Crate wood", new Color(.33f, .27f, .2f), .2f);
                var tarp = Lit("Tarp rubber green", new Color(.2f, .25f, .21f), .35f);
                float y1 = Ground(-20.9f, -6.3f);
                var c1 = Box("Crate", crates, new Vector3(-20.9f, y1 + .3f, -6.3f), new Vector3(.9f, .6f, .7f), wood, Quaternion.Euler(0, 4, 0));
                Box("Crate stacked", crates, new Vector3(-20.85f, y1 + .85f, -6.32f), new Vector3(.7f, .5f, .6f), wood, Quaternion.Euler(0, -7, 0));
                Collide(c1, new Vector3(1, 1.85f, 1), new Vector3(0, .42f, 0));
                SnowCap(crates, new Vector3(-20.85f, y1 + 1.11f, -6.32f), new Vector3(.72f, .04f, .62f));
                float y2 = Ground(-19.8f, -6.7f);
                var t = Box("Tarp over crate", crates, new Vector3(-19.8f, y2 + .38f, -6.7f), new Vector3(1.0f, .76f, .82f), tarp, Quaternion.Euler(0, -10, 0));
                Collide(t, Vector3.one, Vector3.zero);
                SnowCap(crates, new Vector3(-19.8f, y2 + .78f, -6.7f), new Vector3(.95f, .04f, .78f));
                Cyl("Thermos", crates, new Vector3(-21.1f, y1 + .7f, -6.15f), .045f, .2f, Mat("Thermos red"));
                Box("Work gloves", crates, new Vector3(-20.75f, y1 + .62f, -6.1f), new Vector3(.2f, .03f, .12f), Lit("Work gloves", new Color(.55f, .45f, .22f), .1f), Quaternion.Euler(0, 30, 0));
                float y3 = Ground(-19.2f, -7.6f);
                var cyl = Lit("Gas cylinder", new Color(.42f, .45f, .48f), .4f, .5f);
                for (int i = 0; i < 3; i++) Cyl("Gas cylinder " + i, crates, new Vector3(-19.45f + i * .26f, y3 + .55f, -7.6f), .11f, 1.1f, cyl);
                Box("Cylinder cage rail", crates, new Vector3(-19.2f, y3 + .75f, -7.42f), new Vector3(.9f, .04f, .03f), steel);
                Heap(crates, "Ploughed snow by the crates", -19.6f, -5.7f, new Vector3(1.7f, .4f, 1.0f), snowMat);
                var crateFace = Quaternion.Euler(0, 4, 0) * Vector3.forward;
                Decal("Stencil 04 on crate", crates, new Vector3(-20.9f, y1 + .3f, -6.3f) + crateFace * .351f, crateFace, .62f, .31f, Lit("Berth stencil paint", Color.white, .1f, 0, Tex("berth04_stencil"), mode: Mode.Transparent), 0f);
            }
        }

        // ------------------------------------------------------------------------------------------------ cyber
        static void BuildCyber(Transform g)
        {
            var steel = Mat("Dark steel");
            var paint = Mat("Smoked green paint");
            var lease = Lit("Lease sticker", Color.white, .3f, 0, Tex("lease_sticker"), mode: Mode.Cutout);
            var red = Beacon("Status red", Hex("#DB6858"), 2.4f, .5f, .3f);
            var redSteady = Beacon("Status red steady", Hex("#DB6858"), 1.8f, 0, 1);
            var cyan = Beacon("Status cyan", Hex("#66A9A4"), 1.6f, 0, 1);
            var green = Beacon("Status green", Hex("#78CF91"), 1.4f, 0, 1);
            var amber = Beacon("Status amber", Hex("#E5AB59"), 1.5f, .25f, .8f);

            // Implant charging post beside the residents' bench: sit, plug the arm in, pay by the kWh.
            {
                var post = Group(g, "Implant charging post - court bench");
                float x = -10.95f, z = 2.6f, y = Ground(x, z);
                var body = Box("Post", post, new Vector3(x, y + .6f, z), new Vector3(.26f, 1.2f, .2f), paint);
                Collide(body, Vector3.one, Vector3.zero);
                Box("Hood", post, new Vector3(x, y + 1.23f, z - .02f), new Vector3(.32f, .05f, .27f), steel);
                SnowCap(post, new Vector3(x, y + 1.27f, z - .02f), new Vector3(.32f, .04f, .26f));
                Decal("Charge screen", post, new Vector3(x, y + .98f, z - .101f), Vector3.back, .2f, .1f, Screen("Charge screen", "charge_lcd", Hex("#78CF91")), 0f);
                Decal("Lease sticker", post, new Vector3(x, y + .72f, z - .101f), Vector3.back, .18f, .06f, lease, 0f);
                var cable = Lit("Charge cable", Hex("#373935"), .4f);
                var p0 = new Vector3(x + .13f, y + .85f, z - .04f);
                Tube("Charge cable", post, Catenary(p0, new Vector3(x + .17f, y + .58f, z - .06f), .22f, 10), .012f, cable);
                Box("Plug on its hook", post, new Vector3(x + .17f, y + .55f, z - .06f), new Vector3(.05f, .08f, .04f), steel);
            }

            // Residents' door: intercom with names, and the port camera watching the court.
            {
                float wz = WallZ(-13.28f);
                var door = Group(g, "Residents' door - intercom and camera");
                Box("Intercom panel", door, new Vector3(-13.28f, 2.45f, wz - .02f), new Vector3(.2f, .3f, .04f), steel);
                Decal("Intercom names", door, new Vector3(-13.28f, 2.43f, wz - .041f), Vector3.back, .15f, .225f, Lit("Intercom names", Color.white, .3f, 0, Tex("intercom_names")), 0f);
                Sphere("Intercom LED", door, new Vector3(-13.28f, 2.58f, wz - .045f), .014f, cyan);
                float cz = WallZ(-14.85f);
                Rod("Camera bracket", door, new Vector3(-14.85f, 3.65f, cz), new Vector3(-14.85f, 3.65f, cz - .22f), .015f, steel);
                var aim = Quaternion.LookRotation(new Vector3(.45f, -.5f, -1f));
                Box("Port camera", door, new Vector3(-14.85f, 3.62f, cz - .3f), new Vector3(.11f, .09f, .24f), Lit("Camera housing", Hex("#B7AD92"), .35f), aim);
                Sphere("Camera LED", door, new Vector3(-14.85f, 3.68f, cz - .38f), .016f, red);
            }

            // Community parcel locker with a landing pad for the delivery drones.
            {
                float wz = WallZ(-19.3f);
                var locker = Group(g, "Parcel locker - drone landing pad");
                float y = Ground(-19.3f, wz - .3f);
                var body = Box("Locker", locker, new Vector3(-19.3f, y + .85f, wz - .27f), new Vector3(1.3f, 1.7f, .5f), Lit("Locker grey", new Color(.37f, .4f, .4f), .35f, .3f));
                Collide(body, Vector3.one, Vector3.zero);
                var door = Lit("Locker door", new Color(.44f, .47f, .46f), .35f, .3f);
                for (int c = 0; c < 3; c++)
                    for (int r = 0; r < 4; r++)
                        if (!(c == 1 && r == 1))
                            Box("Door " + c + r, locker, new Vector3(-19.72f + c * .42f, y + .3f + r * .38f, wz - .525f), new Vector3(.38f, .34f, .01f), door);
                Decal("Locker screen", locker, new Vector3(-19.3f, y + .68f, wz - .53f), Vector3.back, .3f, .11f, Screen("Locker screen", "locker_lcd", Hex("#E5AB59")), 0f);
                Cyl("Landing pad", locker, new Vector3(-19.3f, y + 1.72f, wz - .27f), .3f, .03f, steel);
                MeshObj("Pad ring", locker, Torus(.27f, .012f, 24, 5), amber, new Vector3(-19.3f, y + 1.745f, wz - .27f), Quaternion.Euler(90, 0, 0));
                Decal("Lease sticker", locker, new Vector3(-18.82f, y + 1.45f, wz - .521f), Vector3.back, .18f, .06f, lease, 0f);
                Heap(locker, "Drift in the court corner", -20.3f, wz - .8f, new Vector3(1.2f, .35f, .9f), SnowMaterial());
            }

            // Port authority camera mast on the junction island: the regulator is present, in red, small.
            {
                var mast = Group(g, "NSP camera mast - junction");
                float y = Ground(-15.6f, -2.2f);
                Cyl("Mast", mast, new Vector3(-15.6f, y + 1.3f, -2.2f), .045f, 2.6f, steel);
                Box("Camera", mast, new Vector3(-15.6f, y + 2.6f, -2.32f), new Vector3(.12f, .1f, .26f), Mat("Camera housing"), Quaternion.LookRotation(new Vector3(.6f, -.35f, -1f)));
                Sphere("Camera LED", mast, new Vector3(-15.55f, y + 2.66f, -2.45f), .016f, red);
                Decal("Monitoring plate", mast, new Vector3(-15.6f, y + 2.05f, -2.25f), Vector3.back, .36f, .15f, Lit("NSP monitor plate", Color.white, .3f, 0, Tex("nsp_monitor")), 0f);
            }

            // Shore power at berth 04: the pedestal the launch is plugged into gets its test readout (the boat's draw and
            // the lease), a weather hood over it and a status lamp; a spare cable reel stands on the quay beside it.
            var ped = Renderers("Shore power pedestal").FirstOrDefault();
            if (ped != null)
            {
                var term = Group(g, "Shore power terminal - berth 04");
                var b = ped.bounds;
                float face = b.min.z;
                if (WallHit(new Vector3(b.center.x, b.center.y + .1f, b.min.z - 1.5f), Vector3.forward, out var pf)) face = pf.point.z;
                var screen = new Vector3(b.center.x, b.center.y + .1f, face);
                Box("Screen bezel", term, screen + Vector3.back * .01f, new Vector3(.52f, .36f, .02f), steel);
                Decal("Terminal screen", term, screen + Vector3.back * .021f, Vector3.back, .46f, .31f, Screen("Shore terminal screen", "shore_terminal", Hex("#78CF91")), 0f);
                Box("Weather hood", term, new Vector3(b.center.x, screen.y + .23f, face - .1f), new Vector3(.62f, .03f, .22f), steel, Quaternion.Euler(-14, 0, 0));
                SnowCap(term, new Vector3(b.center.x, screen.y + .26f, face - .1f), new Vector3(.6f, .03f, .2f));
                SnowCap(term, new Vector3(b.center.x, b.max.y + .02f, b.center.z), new Vector3(b.size.x + .02f, .045f, b.size.z + .02f));
                Sphere("Status lamp", term, new Vector3(b.center.x + .22f, screen.y - .24f, face - .015f), .018f, green);
                Decal("Lease sticker", term, new Vector3(b.max.x, b.center.y - .15f, b.center.z), Vector3.right, .18f, .06f, lease);
                float ry = Ground(-24.95f, -2.85f);
                var reel = new Vector3(-24.95f, ry + .3f, -2.85f);
                var flange = Lit("Reel orange - faded", new Color(.55f, .33f, .16f), .3f, .2f);
                var drum = Cyl("Cable wound on reel", term, reel, .22f, .3f, Mat("Charge cable"), Quaternion.Euler(0, 0, 90));
                Collide(drum, new Vector3(1.4f, 2, 1.4f), Vector3.zero);
                Cyl("Reel flange a", term, reel + Vector3.right * .16f, .3f, .025f, flange, Quaternion.Euler(0, 0, 90));
                Cyl("Reel flange b", term, reel - Vector3.right * .16f, .3f, .025f, flange, Quaternion.Euler(0, 0, 90));
                SnowCap(term, reel + Vector3.up * .23f, new Vector3(.3f, .03f, .16f));
                Tube("Lead pedestal to reel", term, Catenary(new Vector3(b.max.x + .01f, b.min.y + .12f, b.center.z - .1f), reel + new Vector3(0, -.2f, .2f), .04f, 12), .018f, Mat("Charge cable"));
            }

            // The launch: warm wheelhouse, navigation lights, masthead.
            foreach (var r in Renderers("Bridge portlight")) r.sharedMaterial = Lit("Wheelhouse window - warm", new Color(.2f, .16f, .12f), .7f, 0, null, new Color(1f, .66f, .34f) * 1.2f);
            var wheelhouse = Renderers("Wheelhouse").FirstOrDefault();
            if (wheelhouse != null)
            {
                var b = wheelhouse.bounds;
                var boat = Group(g, "Launch - navigation lights");
                Sphere("Port light", boat, new Vector3(b.center.x + .6f, b.max.y - .2f, b.min.z - .03f), .05f, Beacon("Nav red", new Color(.9f, .2f, .15f), 2.2f, 0, 1));
                Sphere("Starboard light", boat, new Vector3(b.center.x + .6f, b.max.y - .2f, b.max.z + .03f), .05f, Beacon("Nav green", new Color(.2f, .85f, .4f), 2.0f, 0, 1));
                Rod("Mast", boat, new Vector3(b.center.x, b.max.y, b.center.z), new Vector3(b.center.x, b.max.y + .9f, b.center.z), .02f, steel);
                Sphere("Masthead light", boat, new Vector3(b.center.x, b.max.y + .93f, b.center.z), .05f, Beacon("Masthead white", new Color(1f, .95f, .85f), 2.0f, 0, 1));
            }

            // External units on the facades (heat exchangers for the flats; green run lamp).
            var unitMat = Lit("External unit", Hex("#B7AD92"), .3f, .1f);
            var grille = Lit("Fan grille", Hex("#292E30"), .2f, .3f);
            foreach (var (x, y, towardWall) in new[] { (4.5f, 6.25f, 1f), (-6.8f, 6.25f, 1f), (-18.3f, 6.7f, -1f), (-13.3f, 6.7f, -1f) })
            {
                var from = new Vector3(x, y, towardWall > 0 ? -3f : -8f);
                if (!WallHit(from, new Vector3(0, 0, towardWall), out var hit)) continue;
                var u = Group(g, "External unit " + x.ToString("0.0"));
                var c = hit.point + hit.normal * .16f;
                Box("Unit", u, c, new Vector3(.62f, .42f, .28f), unitMat, Face(hit.normal));
                Decal("Grille", u, c + hit.normal * .141f, hit.normal, .3f, .3f, grille, 0f);
                Sphere("Run lamp", u, c + hit.normal * .142f + new Vector3(.24f, .14f, 0), .012f, green);
                Rod("Bracket", u, hit.point + Vector3.down * .24f, c + Vector3.down * .22f + hit.normal * .1f, .012f, steel);
                SnowCap(u, c + Vector3.up * .23f, new Vector3(.62f, .04f, .28f));
            }

            // Far lights: crane obstruction lamps, windows in the far flats, the port authority board, two working sheds.
            foreach (var crane in Named("Dock portal crane"))
            {
                var rs = crane.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) continue;
                var top = rs.OrderByDescending(r => r.bounds.max.y).First().bounds;
                Sphere("Crane obstruction lamp", g, new Vector3(top.center.x, top.max.y + .12f, top.center.z), .18f, Beacon("Obstruction red", Hex("#DB6858"), 3.0f, .5f, .35f));
                foreach (Transform t in crane.GetComponentsInChildren<Transform>().Where(t => t.name == "Machinery housing"))
                {
                    var hr = t.GetComponent<Renderer>();
                    if (hr == null) continue;
                    var hb = hr.bounds;
                    Sphere("Housing lamp", g, new Vector3(hb.center.x, hb.max.y + .1f, hb.center.z), .12f, Mat("Obstruction red"));
                }
            }
            var far = Unlit("Distant windows", Color.white, Tex("distant_windows"));
            foreach (var flats in Renderers("Distant flats"))
            {
                var b = flats.bounds;
                Decal("Lit windows", g, new Vector3(b.center.x, b.center.y + .3f, b.min.z), Vector3.back, b.size.x * .86f, b.size.y * .82f, far, .03f);
            }
            var middle = Renderers("Distant flats").OrderBy(r => Mathf.Abs(r.bounds.center.x + 43.5f)).FirstOrDefault();
            if (middle != null)
            {
                var b = middle.bounds;
                Decal("NSP night board", g, new Vector3(b.center.x, b.max.y - 1.6f, b.min.z), Vector3.back, 4.4f, 2.2f, Unlit("NSP board", new Color(.9f, .95f, .95f), Tex("nsp_billboard")), .07f);
            }
            foreach (var roof in Renderers("Distant rooftop"))
            {
                var b = roof.bounds;
                Decal("Lit windows", g, new Vector3(b.center.x, b.center.y + .4f, b.max.z), Vector3.forward, b.size.x * .8f, b.size.y * .7f, far, .03f);
            }
            var shedDoor = Unlit("Shed door - working light", new Color(1f, .7f, .42f) * .9f);
            foreach (var shed in Renderers("Far harbour sheds").Where(r => Mathf.Abs(r.bounds.center.x + 40.5f) < .5f || Mathf.Abs(r.bounds.center.x + 55.2f) < .5f))
            {
                var b = shed.bounds;
                Decal("Open door", g, new Vector3(b.center.x, b.min.y + 1.0f, b.min.z), Vector3.back, 1.2f, 2.0f, shedDoor, .03f);
            }
        }

        // ------------------------------------------------------------------------------------------------ snow
        static void BuildSnow(Transform g)
        {
            var ice = Lit("Icicle ice", new Color(.74f, .82f, .88f, 1), .85f);
            // Under the lightbox, the clinic sills, the lamp hoods, the shutter hood across the street, the wheelhouse roof.
            foreach (var r in Renderers("Weathered repair lightbox"))
                Icicles(g, "Icicles - lightbox", new Vector3(r.bounds.min.x + .1f, r.bounds.min.y, r.bounds.min.z - .02f), new Vector3(r.bounds.max.x - .1f, r.bounds.min.y, r.bounds.min.z - .02f), 9, .18f, ice, 1);
            int sill = 0;
            foreach (var r in Renderers("Snow on projecting sill").OrderBy(r => r.bounds.center.x).ThenBy(r => r.bounds.center.y))
            {
                if (Hash(r.bounds.center) > .5f) continue;
                var b = r.bounds;
                bool facesStreet = b.center.z > -5;   // clinic facade sills overhang toward -z, the far side toward +z
                float z = facesStreet ? b.min.z + .02f : b.max.z - .02f;
                Icicles(g, "Icicles - sill", new Vector3(b.min.x + .12f, b.min.y, z), new Vector3(b.max.x - .12f, b.min.y, z), 5, .22f, ice, 10 + sill++);
            }
            foreach (var r in Renderers("Hood"))
                Icicles(g, "Icicles - lamp hood", new Vector3(r.bounds.min.x + .05f, r.bounds.min.y, r.bounds.center.z), new Vector3(r.bounds.max.x - .05f, r.bounds.min.y, r.bounds.center.z), 4, .12f, ice, 40 + sill++);
            foreach (var r in Renderers("Shutter hood"))
                Icicles(g, "Icicles - shutter hood", new Vector3(r.bounds.min.x + .1f, r.bounds.min.y, r.bounds.max.z - .02f), new Vector3(r.bounds.max.x - .1f, r.bounds.min.y, r.bounds.max.z - .02f), 10, .3f, ice, 60);
            foreach (var r in Renderers("Wheelhouse roof"))
                Icicles(g, "Icicles - wheelhouse roof", new Vector3(r.bounds.min.x + .1f, r.bounds.min.y, r.bounds.min.z + .02f), new Vector3(r.bounds.max.x - .1f, r.bounds.min.y, r.bounds.min.z + .02f), 6, .14f, ice, 70);

            // Steam: the basement's heater breathing out of the service door vent, a riser vent in the court, the launch's
            // idling engine, a manhole over the district heating main.
            var puff = Particles("Steam puff", Tex("steam_puff"));
            Steam("Steam - service door vent", g, new Vector3(2.1f, 2.95f, -.32f), new Vector3(0, .6f, -1f), 1f, puff);
            float ry = Ground(-8.6f, 2.9f);
            Cyl("Riser vent stub", g, new Vector3(-8.6f, 4.55f, 2.9f), .06f, .3f, Mat("Dark steel"));
            Steam("Steam - court riser vent", g, new Vector3(-8.6f, 4.72f, 2.9f), Vector3.up, 1.2f, puff);
            foreach (var r in Renderers("Funnel")) Steam("Exhaust - launch funnel", g, new Vector3(r.bounds.center.x, r.bounds.max.y + .05f, r.bounds.center.z), Vector3.up, 1.1f, puff);
            float my = Ground(-6.5f, -7.4f);
            Cyl("Manhole cover", g, new Vector3(-6.5f, my + .008f, -7.4f), .36f, .016f, Lit("Cast iron", new Color(.16f, .17f, .17f), .5f, .6f));
            Steam("Steam - manhole", g, new Vector3(-6.5f, my + .05f, -7.4f), Vector3.up, 1.3f, puff);

            // Snowfall: absolute levels, a little under the handed-over ones (the quay emitter spawned 600/s over 11k flakes).
            foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
            {
                (int max, float rate) level;
                switch (ps.name)
                {
                    case "Street and open stairwell": level = (4800, 260); break;
                    case "Working quay": level = (7200, 400); break;
                    case "Far basin": level = (4000, 130); break;
                    default: continue;
                }
                var main = ps.main; int was = main.maxParticles; main.maxParticles = level.max;
                var em = ps.emission; em.rateOverTime = level.rate;
                log.Append("snowfall ").Append(ps.name).Append(": max ").Append(was).Append(" -> ").Append(level.max).Append(", rate -> ").Append(level.rate).Append('\n');
            }
        }

        // ------------------------------------------------------------------------------------------------ light
        static void BuildLamps(Transform g)
        {
            foreach (var r in Renderers("Diffuser"))
                LampCone(g, "Lamp volume - street", r.bounds.center + Vector3.down * .06f, .36f, Volume("Lamp volume sodium", new Color(1f, .72f, .42f), .2f));
            foreach (var r in Renderers("Sodium lamp lens"))
                LampCone(g, "Lamp volume - wharf", r.bounds.center + Vector3.down * .05f, .36f, Volume("Lamp volume wharf", new Color(1f, .58f, .26f), .14f));
            foreach (var r in Renderers("Entry lamp under lintel"))
                LampCone(g, "Lamp volume - court door", r.bounds.center + Vector3.down * .04f, .45f, Volume("Lamp volume court", new Color(1f, .72f, .45f), .16f));
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l => l.name == "Wharf amber pool"))
            {
                log.Append("wharf amber pool ").Append(l.intensity).Append(" -> 6\n");
                l.intensity = 6f;   // the berth snow was blowing out to flat orange
            }

            // Rust streaks under a few sills: water runs from the same places every thaw.
            var rust = Lit("Rust streak", Color.white, .05f, 0, Tex("rust_streak"), mode: Mode.Transparent);
            foreach (var (x, y, h) in new[] { (-6.8f, 4.0f, .8f), (4.5f, 4.0f, .8f), (-4.5f, 6.32f, .8f), (6.8f, 6.32f, .8f), (-2.3f, 6.32f, .7f) })
                if (WallHit(new Vector3(x, y, -3f), Vector3.forward, out var hit))
                    Decal("Rust streak", g, hit.point, hit.normal, .42f, h, rust);
        }

        static void BuildCameras(Transform g)
        {
            foreach (var view in ExtraViews)
            {
                var go = new GameObject(view.name);
                go.transform.SetParent(g, false);
                go.transform.SetPositionAndRotation(view.eye, Quaternion.LookRotation(view.look - view.eye));
                var cam = go.AddComponent<Camera>();
                cam.fieldOfView = 55; cam.nearClipPlane = .05f; cam.farClipPlane = 300; cam.enabled = false;   // review only
            }
        }

        // Lit windows: most stay dark; a few warm, a few dimmer, one in twenty-five the blue of a screen, some kitchen green.
        static void VaryWindows()
        {
            var warm = Lit("Window - warm", new Color(.22f, .17f, .12f), .7f, 0, null, new Color(.95f, .62f, .3f) * 1.05f);
            var dim = Lit("Window - warm dim", new Color(.18f, .14f, .1f), .7f, 0, null, new Color(.95f, .62f, .3f) * .5f);
            var tv = Lit("Window - screen blue", new Color(.1f, .12f, .16f), .7f, 0, null, new Color(.38f, .52f, .78f) * .75f);
            var kitchen = Lit("Window - kitchen", new Color(.15f, .17f, .14f), .7f, 0, null, new Color(.72f, .82f, .62f) * .6f);
            int lit = 0, all = 0;
            foreach (var r in Renderers("Clouded glazing").Concat(Renderers("End block glazing")))
            {
                all++;
                float h = Hash(r.bounds.center);
                var m = h < .10f ? warm : h < .16f ? dim : h < .20f ? tv : h < .23f ? kitchen : null;
                if (m == null) continue;
                r.sharedMaterial = m; lit++;
            }
            log.Append("windows lit: ").Append(lit).Append(" of ").Append(all).Append('\n');
        }

        // ------------------------------------------------------------------------------------------------ optimisation
        static void Optimise()
        {
            var scene = EditorSceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects().Where(r => ExteriorRoots.Contains(r.name)).ToArray();
            int staticCount = 0, removed = 0, farShadows = 0;
            var removedBy = new Dictionary<string, int>();
            var play = new Bounds(new Vector3(-9, 1.5f, -4), new Vector3(42, 6, 20));   // the walkable district: x -30..12, z -14..6
            foreach (var root in roots)
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    var go = t.gameObject;
                    var r = go.GetComponent<Renderer>();
                    if (r != null && go.GetComponent<ParticleSystem>() == null && go.GetComponent<TextMesh>() == null && go.GetComponent<Light>() == null)
                    {
                        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
                        staticCount++;
                        // Far scenery (cranes, sheds, distant flats, the far pier) stops casting into the 50 m shadow map.
                        if (r.shadowCastingMode != ShadowCastingMode.Off && DistanceToRoute(r.bounds.center) > 22f) { r.shadowCastingMode = ShadowCastingMode.Off; farShadows++; }
                    }
                    if (root.name == "01 Basement shell and open entrance") continue;   // the interior keeps every collider
                    foreach (var col in go.GetComponents<Collider>())
                    {
                        if (col is MeshCollider || col.isTrigger) continue;
                        var b = col.bounds;
                        string why = null;
                        if (b.min.y > 3.4f) why = "above head height";
                        else if (!b.Intersects(play)) why = "outside the walkable district";
                        else if (Mathf.Max(b.size.x, b.size.y, b.size.z) < .12f) why = "smaller than a hand";
                        else if (FlushDetail(go.name)) why = "window or cable detail on a wall that already collides";
                        if (why == null) continue;
                        Object.DestroyImmediate(col);
                        removed++;
                        removedBy[why] = removedBy.TryGetValue(why, out var n) ? n + 1 : 1;
                    }
                }
            // Review cameras left enabled all render every frame in Play mode; only the eye-height camera should.
            int cams = 0;
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
                if (cam.enabled && !cam.name.StartsWith("Main Camera")) { cam.enabled = false; cams++; }
            log.Append("review cameras switched off (Shots still renders them): ").Append(cams).Append('\n');
            log.Append("static renderers: ").Append(staticCount).Append("; colliders removed: ").Append(removed)
               .Append(" (").Append(string.Join(", ", removedBy.Select(kv => kv.Key + " " + kv.Value))).Append("); far renderers no longer casting shadows: ").Append(farShadows).Append('\n');
        }

        static bool FlushDetail(string name) => name == "Clouded glazing" || name == "Vertical sash" || name == "Transom" || name == "Curtain edge" || name == "Curtain"
            || name == "Sash" || name == "End block sash" || name == "End block mullion" || name == "End block glazing" || name == "Recessed sash frame"
            || name == "Snow on projecting sill" || name == "Utility droop" || name == "Sagging utility cable" || name == "Moored power lead"
            || name == "Pressed side ribs" || name == "Rolled metal slat" || name == "Louvred shutter" || name == "Pipe strap";

        static float DistanceToRoute(Vector3 p)
        {
            float best = float.MaxValue;
            var q = new Vector2(p.x, p.z);
            for (int i = 0; i + 1 < Route.Length; i++)
            {
                var a = Route[i]; var b = Route[i + 1];
                float t = Mathf.Clamp01(Vector2.Dot(q - a, b - a) / (b - a).sqrMagnitude);
                best = Mathf.Min(best, Vector2.Distance(q, a + (b - a) * t));
            }
            return best;
        }

        static void Count(string when)
        {
            var all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int colliders = Object.FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            int lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            int renderers = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            int maxFlakes = Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None).Sum(p => p.main.maxParticles);
            log.Append(when).Append(": objects ").Append(all.Length).Append(", renderers ").Append(renderers).Append(", colliders ").Append(colliders)
               .Append(", realtime lights ").Append(lights).Append(", particle budget ").Append(maxFlakes).Append('\n');
        }

        // ------------------------------------------------------------------------------------------------ assets
        static void PrepareAssets()
        {
            if (AssetDatabase.IsValidFolder(MeshDir)) AssetDatabase.DeleteAsset(MeshDir);
            AssetDatabase.CreateFolder(AssetDir, "Meshes");
            if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder(AssetDir, "Materials");
            AssetDatabase.Refresh();
            foreach (var path in Directory.GetFiles(TexDir, "*.png"))
            {
                var p = path.Replace('\\', '/');
                var imp = (TextureImporter)AssetImporter.GetAtPath(p);
                if (imp == null) continue;
                imp.textureType = TextureImporterType.Default;
                imp.sRGBTexture = true;
                imp.alphaIsTransparency = true;
                imp.mipmapEnabled = true;
                imp.filterMode = FilterMode.Bilinear;
                imp.maxTextureSize = 1024;
                imp.wrapMode = p.Contains("trampled_path") || p.Contains("distant_windows") ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                imp.SaveAndReimport();
            }
        }

        static Texture2D Tex(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/" + name + ".png");

        enum Mode { Opaque, Cutout, Transparent }

        static Material Mat(string name) => mats[name];

        static Material Lit(string name, Color baseColour, float smooth = .3f, float metal = 0, Texture tex = null, Color? emission = null, Texture emissionTex = null, Mode mode = Mode.Opaque)
        {
            if (mats.TryGetValue(name, out var have)) return have;
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", baseColour);
            if (tex != null) m.SetTexture("_BaseMap", tex);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission.Value);
                if (emissionTex != null) m.SetTexture("_EmissionMap", emissionTex);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            Surface(m, mode);
            return Save(name, m);
        }

        static Material Screen(string name, string tex, Color glow) => Lit(name, new Color(.08f, .1f, .09f), .85f, 0, Tex(tex), glow * 1.1f, Tex(tex));

        static Material Unlit(string name, Color colour, Texture tex = null)
        {
            if (mats.TryGetValue(name, out var have)) return have;
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetColor("_BaseColor", colour);
            if (tex != null) m.SetTexture("_BaseMap", tex);
            return Save(name, m);
        }

        static Material Particles(string name, Texture tex)
        {
            if (mats.TryGetValue(name, out var have)) return have;
            var m = new Material(Shader.Find("Universal Render Pipeline/Particles/Simple Lit"));
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", Color.white);
            Surface(m, Mode.Transparent);
            return Save(name, m);
        }

        static Material Beacon(string name, Color colour, float intensity, float rate, float duty)
        {
            if (mats.TryGetValue(name, out var have)) return have;
            var m = new Material(Shader.Find("BorderRepair/Exterior/Beacon"));
            m.SetColor("_Color", colour); m.SetFloat("_Intensity", intensity); m.SetFloat("_Rate", rate); m.SetFloat("_Duty", duty);
            return Save(name, m);
        }

        static Material Volume(string name, Color colour, float intensity)
        {
            if (mats.TryGetValue(name, out var have)) return have;
            var m = new Material(Shader.Find("BorderRepair/Exterior/LampVolume"));
            m.SetColor("_Color", colour); m.SetFloat("_Intensity", intensity);
            return Save(name, m);
        }

        static Material SnowMaterial()
        {
            if (mats.TryGetValue("Snow", out var have)) return have;
            var m = AssetDatabase.LoadAssetAtPath<Material>("Assets/RepairProps/Scenes/Temp_Street_snow.mat") ?? Lit("Snow fallback", new Color(.86f, .89f, .93f), .25f);
            mats["Snow"] = m;
            return m;
        }

        static void Surface(Material m, Mode mode)
        {
            if (mode == Mode.Cutout)
            {
                m.SetFloat("_AlphaClip", 1); m.SetFloat("_Cutoff", .4f); m.EnableKeyword("_ALPHATEST_ON");
                m.renderQueue = (int)RenderQueue.AlphaTest; m.SetOverrideTag("RenderType", "TransparentCutout");
            }
            else if (mode == Mode.Transparent)
            {
                m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0); m.SetFloat("_BlendModePreserveSpecular", 0);
                m.DisableKeyword("_ALPHAPREMULTIPLY_ON"); m.SetFloat("_EnvironmentReflections", 0); m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)RenderQueue.Transparent; m.SetOverrideTag("RenderType", "Transparent");
                m.SetShaderPassEnabled("DepthOnly", false); m.SetShaderPassEnabled("ShadowCaster", false);
            }
        }

        static Material Save(string name, Material m)
        {
            var path = MatDir + "/" + Safe(name) + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) { existing.shader = m.shader; EditorUtility.CopySerialized(m, existing); m = existing; }
            else AssetDatabase.CreateAsset(m, path);
            mats[name] = m;
            return m;
        }

        static Color Hex(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }

        // ------------------------------------------------------------------------------------------------ placement
        static Transform Group(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        static IEnumerable<Renderer> Renderers(string name)
        {
            var pass = PassRootTransform();
            return Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.name == name && !r.transform.IsChildOf(pass)).ToArray();
        }

        static IEnumerable<Transform> Named(string name) =>
            Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Where(t => t.name == name);

        static Transform PassRootTransform() => EditorSceneManager.GetActiveScene().GetRootGameObjects().First(r => r.name == PassRoot).transform;

        // Height of what is underfoot (snow, kerbs, paving), probed from below the overhead pipes and lines.
        static float Ground(float x, float z)
        {
            if (Physics.Raycast(new Vector3(x, 2.9f, z), Vector3.down, out var hit, 6f, ~0, QueryTriggerInteraction.Ignore)) { groundHits++; return hit.point.y; }
            groundMisses++;
            return 1.35f;
        }

        static bool WallHit(Vector3 from, Vector3 dir, out RaycastHit hit)
        {
            bool ok = Physics.Raycast(from, dir.normalized, out hit, 8f, ~0, QueryTriggerInteraction.Ignore);
            if (!ok) log.Append("no wall from ").Append(from.ToString("F1")).Append('\n');
            return ok;
        }

        // The residents' building face along the court (the door is at z 4.3).
        static float WallZ(float x) => WallHit(new Vector3(x, 2.2f, 1.0f), Vector3.forward, out var hit) ? hit.point.z : 4.25f;

        static Quaternion Face(Vector3 normal) => Quaternion.LookRotation(-normal, Vector3.up);

        static float Hash(Vector3 p) => Mathf.Abs(Mathf.Sin(Vector3.Dot(p, new Vector3(12.9898f, 78.233f, 37.719f))) * 43758.5453f) % 1f;

        static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Quaternion rot, Material m, bool shadows = true)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = m;
            if (!shadows) r.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        static GameObject Box(string name, Transform parent, Vector3 centre, Vector3 size, Material m, Quaternion? rot = null) =>
            Prim(PrimitiveType.Cube, name, parent, centre, size, rot ?? Quaternion.identity, m);

        static GameObject Cyl(string name, Transform parent, Vector3 centre, float radius, float height, Material m, Quaternion? rot = null) =>
            Prim(PrimitiveType.Cylinder, name, parent, centre, new Vector3(radius * 2, height * .5f, radius * 2), rot ?? Quaternion.identity, m);

        static GameObject Sphere(string name, Transform parent, Vector3 centre, float radius, Material m) =>
            Prim(PrimitiveType.Sphere, name, parent, centre, Vector3.one * radius * 2, Quaternion.identity, m, false);

        static void Rod(string name, Transform parent, Vector3 a, Vector3 b, float radius, Material m)
        {
            var d = b - a;
            Prim(PrimitiveType.Cylinder, name, parent, (a + b) * .5f, new Vector3(radius * 2, d.magnitude * .5f, radius * 2), Quaternion.FromToRotation(Vector3.up, d.normalized), m);
        }

        // A flat sign or decal a hair off its surface, readable from the side the normal points to.
        static GameObject Decal(string name, Transform parent, Vector3 point, Vector3 normal, float width, float height, Material m, float lift = .012f, Vector3? up = null) =>
            Prim(PrimitiveType.Quad, name, parent, point + normal.normalized * lift, new Vector3(width, height, 1), Quaternion.LookRotation(-normal, up ?? Vector3.up), m, false);

        static void SnowCap(Transform parent, Vector3 centre, Vector3 size) => Box("Snow cap", parent, centre, size, SnowMaterial());

        // A shovelled heap: a low, lumpy cone of rings with jittered radii, sunk a little into what it sits on.
        static void Heap(Transform parent, string name, float x, float z, Vector3 size, Material snow)
        {
            float y = Ground(x, z);
            var rnd = new System.Random(Mathf.RoundToInt(x * 131 + z * 71));
            float[] radius = { 1.05f, .9f, .66f, .36f }, height = { -.12f, .38f, .74f, .95f };
            const int segs = 9;
            var v = new List<Vector3>(); var t = new List<int>();
            for (int k = 0; k < radius.Length; k++)
                for (int j = 0; j < segs; j++)
                {
                    float a = (j + (float)rnd.NextDouble() * .3f) * Mathf.PI * 2 / segs;
                    float r = radius[k] * (.84f + .3f * (float)rnd.NextDouble());
                    v.Add(new Vector3(Mathf.Cos(a) * r, height[k] + ((float)rnd.NextDouble() - .5f) * .1f, Mathf.Sin(a) * r));
                }
            v.Add(new Vector3(((float)rnd.NextDouble() - .5f) * .2f, 1f, ((float)rnd.NextDouble() - .5f) * .2f));
            int apex = v.Count - 1;
            for (int k = 0; k + 1 < radius.Length; k++)
                for (int j = 0; j < segs; j++)
                {
                    int a = k * segs + j, b = k * segs + (j + 1) % segs, c = a + segs, d = b + segs;
                    var o = v[a] + v[d]; o.y = 0;
                    Tri(t, v, a, b, c, o); Tri(t, v, b, d, c, o);
                }
            for (int j = 0; j < segs; j++)
            {
                int a = (radius.Length - 1) * segs + j, b = (radius.Length - 1) * segs + (j + 1) % segs;
                var o = v[a] + v[b]; o.y = .5f;
                Tri(t, v, a, b, apex, o);
            }
            var uv = v.Select(p => new Vector2(p.x * size.x * .25f, p.z * size.z * .25f + p.y * size.y * .5f)).ToList();
            var go = MeshObj(name, parent, SaveMesh("Snow heap", v, t, uv), HeapMaterial(snow), new Vector3(x, y, z), Quaternion.Euler(0, (float)rnd.NextDouble() * 360, 0));
            go.transform.localScale = new Vector3(size.x * .5f, size.y, size.z * .5f);
        }

        // Shovelled snow carries grit off the pavement: the settled-snow material, a shade greyer.
        static Material HeapMaterial(Material snow)
        {
            if (mats.TryGetValue("Shovelled snow", out var have)) return have;
            var m = new Material(snow);
            m.SetColor("_BaseColor", snow.GetColor("_BaseColor") * new Color(.7f, .7f, .72f, 1));
            return Save("Shovelled snow", m);
        }

        static void Collide(GameObject go, Vector3 sizeScale, Vector3 offset)
        {
            var c = go.AddComponent<BoxCollider>();
            c.size = sizeScale; c.center = offset;
        }

        static void Steam(string name, Transform parent, Vector3 pos, Vector3 dir, float scale, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(dir.normalized));
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.prewarm = true; main.playOnAwake = true; main.duration = 5;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.4f * scale, 3.8f * scale);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.2f, .45f);
            main.startSize = new ParticleSystem.MinMaxCurve(.22f * scale, .45f * scale);
            main.startRotation = new ParticleSystem.MinMaxCurve(0, 6.28f);
            main.startColor = new Color(.66f, .7f, .74f, .3f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 40;
            var em = ps.emission; em.rateOverTime = 9;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 14; shape.radius = .06f * scale;
            var colour = ps.colorOverLifetime; colour.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                         new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .15f), new GradientAlphaKey(0, 1) });
            colour.color = grad;
            var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, .6f, 1, 2.4f));
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(.1f); vel.y = new ParticleSystem.MinMaxCurve(.28f); vel.z = new ParticleSystem.MinMaxCurve(0f);
            var noise = ps.noise; noise.enabled = true; noise.strength = .2f; noise.frequency = .5f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat; r.renderMode = ParticleSystemRenderMode.Billboard; r.shadowCastingMode = ShadowCastingMode.Off;
            ps.Play();
        }

        // ------------------------------------------------------------------------------------------------ meshes
        static GameObject MeshObj(string name, Transform parent, Mesh mesh, Material m, Vector3 pos, Quaternion rot, bool shadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            if (!shadows) r.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        static Mesh SaveMesh(string name, List<Vector3> v, List<int> t, List<Vector2> uv = null)
        {
            var mesh = new Mesh { name = name };
            if (v.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(v); mesh.SetTriangles(t, 0);
            if (uv != null) mesh.SetUVs(0, uv);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, MeshDir + "/" + Safe(name) + "_" + (meshCount++) + ".asset");
            return mesh;
        }

        // A triangle whose front face points along `outward` (Unity's front face is the one RecalculateNormals faces).
        static void Tri(List<int> t, List<Vector3> v, int a, int b, int c, Vector3 outward)
        {
            if (Vector3.Dot(Vector3.Cross(v[b] - v[a], v[c] - v[a]), outward) < 0) { int s = b; b = c; c = s; }
            t.Add(a); t.Add(b); t.Add(c);
        }

        static Mesh Torus(float radius, float tube, int segments, int sides)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2 / segments;
                var centre = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * radius;
                for (int j = 0; j <= sides; j++)
                {
                    float b = j * Mathf.PI * 2 / sides;
                    v.Add(centre + (centre.normalized * Mathf.Cos(b) + Vector3.forward * Mathf.Sin(b)) * tube);
                }
            }
            int row = sides + 1;
            for (int i = 0; i < segments; i++)
                for (int j = 0; j < sides; j++)
                {
                    int a = i * row + j, b = a + row;
                    var centre = (v[a] + v[b] + v[a + 1]) / 3f;
                    var ring = new Vector3(centre.x, centre.y, 0).normalized * radius;
                    var outward = centre - ring;
                    Tri(t, v, a, b, a + 1, outward); Tri(t, v, a + 1, b, b + 1, outward);
                }
            return SaveMesh("Torus", v, t);
        }

        static List<Vector3> Catenary(Vector3 a, Vector3 b, float sag, int n)
        {
            var points = new List<Vector3>();
            for (int i = 0; i <= n; i++)
            {
                float s = i / (float)n;
                points.Add(Vector3.Lerp(a, b, s) + Vector3.down * sag * 4 * s * (1 - s));
            }
            return points;
        }

        static void Tube(string name, Transform parent, List<Vector3> points, float radius, Material m, int sides = 6)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i < points.Count; i++)
            {
                var dir = (points[Mathf.Min(i + 1, points.Count - 1)] - points[Mathf.Max(i - 1, 0)]).normalized;
                var side = Vector3.Cross(dir, Mathf.Abs(dir.y) > .9f ? Vector3.right : Vector3.up).normalized;
                var up = Vector3.Cross(side, dir);
                for (int j = 0; j < sides; j++)
                {
                    float a = j * Mathf.PI * 2 / sides;
                    v.Add(points[i] + (side * Mathf.Cos(a) + up * Mathf.Sin(a)) * radius);
                }
            }
            for (int i = 0; i + 1 < points.Count; i++)
                for (int j = 0; j < sides; j++)
                {
                    int a = i * sides + j, b = i * sides + (j + 1) % sides, c = a + sides, d = b + sides;
                    var outward = (v[a] + v[d]) * .5f - (points[i] + points[i + 1]) * .5f;
                    Tri(t, v, a, c, b, outward); Tri(t, v, b, c, d, outward);
                }
            MeshObj(name, parent, SaveMesh(name, v, t), m, Vector3.zero, Quaternion.identity);
        }

        // Icicles along an edge: one mesh, short ones common, a few long; each a five-sided cone hanging from the edge.
        static void Icicles(Transform parent, string name, Vector3 from, Vector3 to, int count, float maxLength, Material m, int seed)
        {
            var rnd = new System.Random(seed * 7919 + 17);
            var v = new List<Vector3>(); var t = new List<int>();
            for (int k = 0; k < count; k++)
            {
                float s = (k + .5f + (float)(rnd.NextDouble() - .5) * .7f) / count;
                var top = Vector3.Lerp(from, to, Mathf.Clamp01(s)) + Vector3.up * .005f;
                float length = maxLength * (.25f + .75f * Mathf.Pow((float)rnd.NextDouble(), 2.2f));
                float radius = .008f + length * .07f;
                int baseIndex = v.Count;
                for (int j = 0; j < 5; j++)
                {
                    float a = j * Mathf.PI * 2 / 5 + (float)rnd.NextDouble() * .3f;
                    v.Add(top + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius);
                }
                v.Add(top + Vector3.down * length + new Vector3((float)(rnd.NextDouble() - .5) * .01f, 0, 0));
                int tip = v.Count - 1;
                for (int j = 0; j < 5; j++)
                {
                    int a = baseIndex + j, b = baseIndex + (j + 1) % 5;
                    var outward = ((v[a] + v[b]) * .5f - top); outward.y = 0;
                    Tri(t, v, a, b, tip, outward);
                }
            }
            MeshObj(name, parent, SaveMesh(name, v, t), m, Vector3.zero, Quaternion.identity, false);
        }

        // A light cone hanging from a lamp to the ground under it.
        static void LampCone(Transform parent, string name, Vector3 apex, float spread, Material m)
        {
            float ground = Ground(apex.x, apex.z);
            float h = apex.y - ground - .05f;
            if (h < .5f) return;
            float r0 = .06f, r1 = h * spread;
            const int sides = 24;
            var v = new List<Vector3>(); var t = new List<int>(); var uv = new List<Vector2>();
            for (int j = 0; j <= sides; j++)
            {
                float a = j * Mathf.PI * 2 / sides;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                v.Add(dir * r0); uv.Add(new Vector2(j / (float)sides, 0));
                v.Add(dir * r1 + Vector3.down * h); uv.Add(new Vector2(j / (float)sides, 1));
            }
            for (int j = 0; j < sides; j++)
            {
                int a = j * 2, b = a + 1, c = a + 2, d = a + 3;
                var outward = new Vector3(v[b].x + v[d].x, 0, v[b].z + v[d].z);
                Tri(t, v, a, b, c, outward); Tri(t, v, c, b, d, outward);
            }
            MeshObj(name, parent, SaveMesh(name, v, t, uv), m, apex, Quaternion.identity, false);
        }

        // A path strip laid on whatever is underfoot, each edge vertex probed, a few centimetres above it.
        static void Strip(string name, Transform parent, Vector2[] path, float half, Material m)
        {
            var samples = new List<Vector2>();
            for (int i = 0; i + 1 < path.Length; i++)
            {
                int n = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(path[i], path[i + 1]) / .5f));
                for (int k = 0; k < n; k++) samples.Add(Vector2.Lerp(path[i], path[i + 1], k / (float)n));
            }
            samples.Add(path[path.Length - 1]);
            var v = new List<Vector3>(); var t = new List<int>(); var uv = new List<Vector2>();
            float along = 0;
            for (int i = 0; i < samples.Count; i++)
            {
                var dir = (samples[Mathf.Min(i + 1, samples.Count - 1)] - samples[Mathf.Max(i - 1, 0)]).normalized;
                var side = new Vector2(-dir.y, dir.x) * half;
                if (i > 0) along += Vector2.Distance(samples[i], samples[i - 1]);
                foreach (var s in new[] { -1f, 1f })
                {
                    var p = samples[i] + side * s;
                    v.Add(new Vector3(p.x, Ground(p.x, p.y) + .02f, p.y));
                    uv.Add(new Vector2(s < 0 ? 0 : 1, along / 1.6f));
                }
            }
            for (int i = 0; i + 1 < samples.Count; i++)
            {
                int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
                Tri(t, v, a, b, c, Vector3.up); Tri(t, v, b, d, c, Vector3.up);
            }
            MeshObj(name, parent, SaveMesh(name, v, t, uv), m, Vector3.zero, Quaternion.identity, false);
        }
    }
}
