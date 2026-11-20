using System.Collections.Generic;
using BorderRepair.FirstOrder;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace BorderRepair.RepairProps
{
    // Joins the played clinic and the district copy (see Berth04Playable), all in the clinic's frame, which does not move:
    //   - opens the clinic door (its leaves, the closed-door blockers and the dead-end corridor stub off), so the player
    //     can walk out to the stairs;
    //   - blends the lighting by where the eye is: the clinic's ambient and room fill in the room, the district's ambient,
    //     overcast sun (with its shadows) and fog outside, crossfaded across the doorway;
    //   - culls through the door: from the room, the district and its lamps are drawn only while the door is in view;
    //     from outside, only the clinic renderers seen through the door opening are drawn, and its shadowed lamps drop
    //     their shadows while the door is out of view. What the player carries is never culled;
    //   - steps higher outdoors (kerbs), and puts a player who falls off the map back at the top of the B1 stairs.
    public sealed class Berth04Join : MonoBehaviour
    {
        // The door in the clinic's south wall and the room inside the walls (clinic frame).
        public const float DoorZ = -3.5f, DoorHalfWidth = .85f, DoorHeight = 2.45f;
        static readonly Bounds ClinicRoom = new Bounds(new Vector3(0, 1.56f, .05f), new Vector3(7.3f, 3.2f, 7.1f));
        // With the stations moved into the district's B1 (ClinicLayout), the room is the B1's.
        static Bounds Room => ClinicLayout.Active != null ? ClinicLayout.Active.room : ClinicRoom;
        static readonly Vector3 DoorCentre = new Vector3(0, DoorHeight * .5f, DoorZ);
        static readonly Vector3[] DoorCorners =
        {
            new Vector3(-DoorHalfWidth, -.05f, DoorZ), new Vector3(DoorHalfWidth, -.05f, DoorZ),
            new Vector3(DoorHalfWidth, DoorHeight, DoorZ), new Vector3(-DoorHalfWidth, DoorHeight, DoorZ),
        };

        Scene clinic, district;
        Camera cam;
        FirstPersonWalker walker;
        CharacterController body;
        float roomStep;
        DistrictAtmosphere outdoor;
        Light roomFill;
        float roomFillIntensity, sunIntensity;
        Color indoorSky, indoorEquator, indoorGround;

        Renderer[] districtRenderers;
        readonly HashSet<Renderer> districtHidden = new HashSet<Renderer>();
        readonly List<Renderer> leakers = new List<Renderer>();
        readonly HashSet<Renderer> leakHidden = new HashSet<Renderer>();
        public int LeakingParticleCount => leakers.Count;
        Light[] districtLights;
        bool[] districtLightOn;

        readonly List<Renderer> clinicRenderers = new List<Renderer>();
        readonly List<GameObject> scanRoots = new List<GameObject>();
        readonly List<Renderer> scanRenderers = new List<Renderer>();
        readonly List<bool> clinicKeep = new List<bool>();
        readonly HashSet<Renderer> clinicHidden = new HashSet<Renderer>();
        readonly List<(Light light, LightShadows shadows)> clinicShadowLamps = new List<(Light, LightShadows)>();
        bool clinicShadowsOff;
        float rescanAt;

        readonly Plane[] cameraPlanes = new Plane[6];
        readonly Plane[] portal = new Plane[5];
        float lastBlend = -1;

        /// <summary>Off draws both sides everywhere (for measuring what the culling saves).</summary>
        public bool Culling = true;

        /// <summary>0 in the room, 1 outside; crossfades in the doorway.</summary>
        public float OutdoorBlend { get; private set; }
        public bool EyeInRoom { get; private set; }
        public bool DoorInView { get; private set; }
        public int DistrictHiddenCount => districtHidden.Count;
        public int DistrictRendererCount => districtRenderers?.Length ?? 0;
        public int ClinicHiddenCount => clinicHidden.Count;
        public int ClinicRendererCount => clinicRenderers.Count;
        public FirstPersonWalker Walker => walker;
        public Camera Eye => cam;
        public DistrictAtmosphere Outdoor => outdoor;

        public void Bind(Scene clinicScene, Scene districtScene)
        {
            clinic = clinicScene; district = districtScene;
            OpenDoor();

            var rig = FindFirstObjectByType<FirstOrderCameraRig>();
            walker = rig != null && rig.Walker != null ? rig.Walker : FindFirstObjectByType<FirstPersonWalker>();
            cam = rig != null ? rig.Cam : Camera.main;
            body = walker != null ? walker.GetComponent<CharacterController>() : null;
            if (body != null) roomStep = body.stepOffset;
            if (cam != null) cam.farClipPlane = Mathf.Max(cam.farClipPlane, 420f);

            foreach (var root in district.GetRootGameObjects())
                if (outdoor == null) outdoor = root.GetComponentInChildren<DistrictAtmosphere>(true);
            roomFill = RenderSettings.sun;
            roomFillIntensity = roomFill != null ? roomFill.intensity : 0;
            sunIntensity = outdoor != null && outdoor.sun != null ? outdoor.sun.intensity : 0;
            if (RenderSettings.ambientMode == AmbientMode.Flat)
                indoorSky = indoorEquator = indoorGround = RenderSettings.ambientLight;
            else
            {
                indoorSky = RenderSettings.ambientSkyColor; indoorEquator = RenderSettings.ambientEquatorColor; indoorGround = RenderSettings.ambientGroundColor;
            }
            if (outdoor != null)
            {
                RenderSettings.skybox = outdoor.skybox;
                RenderSettings.fog = outdoor.fog;
                RenderSettings.fogMode = outdoor.fogMode;
                RenderSettings.fogColor = outdoor.fogColour;
                RenderSettings.fogStartDistance = outdoor.fogStart;
                RenderSettings.fogEndDistance = outdoor.fogEnd;
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;

            // When the room itself is the district's B1, its walls, dressing and lamps are the room: never culled.
            bool b1 = ClinicLayout.Active != null;
            var inside = Room;
            inside.Expand(.3f);
            var rs = new List<Renderer>();
            var near = inside; near.Expand(3f);
            leakers.Clear();
            foreach (var root in district.GetRootGameObjects())
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.forceRenderingOff) continue;
                    if (b1 && r is ParticleSystemRenderer && near.Contains(r.transform.position) && !inside.Contains(r.transform.position)
                        && !UnderSnowfall(r.transform)) { leakers.Add(r); continue; }
                    if (!b1 || !inside.Contains(r.bounds.center)) rs.Add(r);
                }
            districtRenderers = rs.ToArray();
            var ls = new List<Light>();
            foreach (var root in district.GetRootGameObjects())
                foreach (var l in root.GetComponentsInChildren<Light>(true))
                    if (l.type != LightType.Directional && (!b1 || !inside.Contains(l.transform.position))) ls.Add(l);
            districtLights = ls.ToArray();
            districtLightOn = new bool[districtLights.Length];
            for (int i = 0; i < districtLights.Length; i++) districtLightOn[i] = districtLights[i].enabled;
            foreach (var root in clinic.GetRootGameObjects())
                foreach (var l in root.GetComponentsInChildren<Light>(true))
                    if (l.type != LightType.Directional && l.shadows != LightShadows.None) clinicShadowLamps.Add((l, l.shadows));
            RescanClinic();

            RenderPipelineManager.beginCameraRendering += BeforeCamera;
            Apply(0, true);
        }

        void OnDestroy()
        {
            RenderPipelineManager.beginCameraRendering -= BeforeCamera;
        }

        // The door's leaves, their collision proxies, the closed-door blocker and the dead-end corridor stub behind the
        // leaves go; a single box over the whole south wall (if that is what the wall's proxy is) is split round the opening.
        const float Opening = .75f, OpeningHeight = 2.2f;

        void OpenDoor()
        {
            foreach (var root in clinic.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    switch (t.name)
                    {
                        case "ClinicClosedDoorCollision":
                            t.gameObject.SetActive(false);
                            break;
                        case "Door_Leaf_L": case "Door_Leaf_R": case "Corridor_Stub":
                        case "COL_DoorLeaf_L": case "COL_DoorLeaf_R":
                            foreach (var r in t.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
                            foreach (var c in t.GetComponentsInChildren<Collider>(true)) c.enabled = false;
                            break;
                        case "COL_Wall_S":
                            if (t.TryGetComponent<BoxCollider>(out var box) && box.enabled && box.bounds.min.x < -Opening && box.bounds.max.x > Opening)
                                SplitWall(box);
                            break;
                    }
        }

        void SplitWall(BoxCollider wall)
        {
            var b = wall.bounds;
            wall.enabled = false;
            var holder = new GameObject("Open door - south wall collision beside and over the door");
            SceneManager.MoveGameObjectToScene(holder, clinic);
            void Piece(float x0, float x1, float y0, float y1)
            {
                var c = holder.AddComponent<BoxCollider>();
                c.center = new Vector3((x0 + x1) * .5f, (y0 + y1) * .5f, b.center.z);
                c.size = new Vector3(x1 - x0, y1 - y0, b.size.z);
            }
            Piece(b.min.x, -Opening, b.min.y, b.max.y);
            Piece(Opening, b.max.x, b.min.y, b.max.y);
            Piece(-Opening, Opening, OpeningHeight, b.max.y);
        }

        // Outside, the kerbs are 18-20 cm (the room's controller steps 15): step higher there.
        public const float OutdoorStep = .3f;

        void Update()
        {
            if (walker == null) return;
            var feet = walker.transform.position;
            if (outdoor != null && feet.y < -4f) walker.PlaceAt(outdoor.respawn, outdoor.respawnYaw);
            if (body != null)
            {
                float step = Room.Contains(feet + Vector3.up * .5f) ? roomStep : Mathf.Max(roomStep, OutdoorStep);
                if (!Mathf.Approximately(body.stepOffset, step)) body.stepOffset = step;
            }
        }

        int standInFrame = -1;

        void BeforeCamera(ScriptableRenderContext context, Camera c)
        {
            if (cam == null || c == null) return;
            if (c == cam) { Evaluate(cam); return; }
            // A close-up camera standing in for the eye (on screen, no render texture, the eye off): once a frame.
            if (cam.isActiveAndEnabled || c.cameraType != CameraType.Game || c.targetTexture != null || standInFrame == Time.frameCount) return;
            standInFrame = Time.frameCount;
            Evaluate(c);
        }

        /// <summary>Recomputes lighting and culling for a camera (the player's eye before each of its frames; tests pass
        /// a measuring camera before rendering it by hand).</summary>
        public void Evaluate(Camera view)
        {
            var eye = view.transform.position;
            EyeInRoom = Room.Contains(eye) && eye.z > DoorZ;
            Apply(Blend(eye), false);
            // (every frame: the harbour's thicker fog follows the eye, not the blend)
            if (outdoor != null)
            {
                float k = outdoor.FogScaleAt(eye);
                RenderSettings.fogDensity = outdoor.fogDensity * Mathf.Lerp(.35f, 1f, OutdoorBlend) * k;
                // (linear fog: thicker means nearer)
                if (outdoor.fogMode == FogMode.Linear) { RenderSettings.fogStartDistance = outdoor.fogStart / k; RenderSettings.fogEndDistance = outdoor.fogEnd / k; }
            }

            GeometryUtility.CalculateFrustumPlanes(view, cameraPlanes);
            var door = new Bounds(DoorCentre, new Vector3(DoorHalfWidth * 2, DoorHeight, .2f));
            bool atDoor = Mathf.Abs(eye.z - DoorZ) < .3f && Mathf.Abs(eye.x) < DoorHalfWidth + .3f;
            DoorInView = atDoor || (GeometryUtility.TestPlanesAABB(cameraPlanes, door) && Vector3.Distance(eye, DoorCentre) < 60f);
            if (DoorInView && !atDoor) BuildPortal(eye);
            // Seen through the B1's display window from the street, the room is drawn whole (it is small).
            bool windowSeen = ClinicLayout.Active != null && !EyeInRoom && GeometryUtility.TestPlanesAABB(cameraPlanes, ClinicLayout.Active.window)
                && Vector3.Distance(eye, ClinicLayout.Active.window.center) < 40f;
            // Street particles that drift through the B1's front wall (the service door's vent steam beside the window)
            // are not drawn from inside.
            foreach (var r in leakers) if (r != null) Set(r, EyeInRoom, leakHidden);
            if (!Culling)
            {
                ShowAll(clinicHidden); ShowAll(districtHidden); SetClinicShadows(true);
                for (int i = 0; i < districtLights.Length; i++)
                    if (districtLights[i] != null && districtLights[i].enabled != districtLightOn[i]) districtLights[i].enabled = districtLightOn[i];
                return;
            }

            if (EyeInRoom)
            {
                ShowAll(clinicHidden);
                SetClinicShadows(true);
                // The room is small and its door, once in view, a wide opening: testing each district renderer against it
                // costs more (and breaks up the static batches) than drawing the district whole. So all or nothing.
                // The B1 also looks out through its display window.
                bool open = DoorInView || (ClinicLayout.Active != null && GeometryUtility.TestPlanesAABB(cameraPlanes, ClinicLayout.Active.window));
                if (open) ShowAll(districtHidden);
                else if (districtHidden.Count != districtRenderers.Length)
                    foreach (var r in districtRenderers) if (r != null) Set(r, true, districtHidden);
                for (int i = 0; i < districtLights.Length; i++)
                {
                    var l = districtLights[i];
                    if (l == null) continue;
                    bool on = districtLightOn[i] && open;
                    if (l.enabled != on) l.enabled = on;
                }
            }
            else
            {
                ShowAll(districtHidden);
                for (int i = 0; i < districtLights.Length; i++)
                    if (districtLights[i] != null && districtLights[i].enabled != districtLightOn[i]) districtLights[i].enabled = districtLightOn[i];
                if (Time.unscaledTime >= rescanAt) RescanClinic();
                for (int i = 0; i < clinicRenderers.Count; i++)
                {
                    var r = clinicRenderers[i];
                    if (r == null) continue;
                    bool show = clinicKeep[i] || atDoor || windowSeen || (DoorInView && GeometryUtility.TestPlanesAABB(portal, r.bounds));
                    Set(r, !show, clinicHidden);
                }
                SetClinicShadows(DoorInView || windowSeen);
            }
        }

        // 0 in the room, 1 outside, a smooth crossfade through the doorway (the only way between them).
        static float Blend(Vector3 eye)
        {
            if (Mathf.Abs(eye.x) < 1.6f && eye.z > -4.8f && eye.z < -2.6f && eye.y < 3.2f)
                return Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-2.8f, -4.6f, eye.z));
            return Room.Contains(eye) ? 0 : 1;
        }

        void Apply(float t, bool force)
        {
            OutdoorBlend = t;
            if (!force && Mathf.Abs(t - lastBlend) < .004f) return;
            lastBlend = t;
            if (outdoor != null)
            {
                RenderSettings.ambientSkyColor = Color.Lerp(indoorSky, outdoor.ambientSky, t);
                RenderSettings.ambientEquatorColor = Color.Lerp(indoorEquator, outdoor.ambientEquator, t);
                RenderSettings.ambientGroundColor = Color.Lerp(indoorGround, outdoor.ambientGround, t);
                // Indoors the clinic had no fog; keep a trace so the stairwell seen through the door still reads hazy.
                RenderSettings.fogDensity = outdoor.fogDensity * Mathf.Lerp(.35f, 1f, t);
                if (outdoor.sun != null)
                {
                    outdoor.sun.intensity = sunIntensity * t;
                    outdoor.sun.enabled = t > .01f;
                }
            }
            if (roomFill != null)
            {
                roomFill.intensity = roomFillIntensity * (1 - t);
                roomFill.enabled = t < .99f;
            }
            RenderSettings.sun = t >= .5f && outdoor != null && outdoor.sun != null ? outdoor.sun : roomFill;
        }

        // The four planes through the eye and the door's edges, and the door plane itself facing away from the eye.
        void BuildPortal(Vector3 eye)
        {
            var beyond = DoorCentre + (DoorCentre - eye).normalized;
            for (int i = 0; i < 4; i++)
            {
                var p = new Plane(eye, DoorCorners[i], DoorCorners[(i + 1) % 4]);
                if (!p.GetSide(beyond)) p.Flip();
                portal[i] = p;
            }
            var d = new Plane(new Vector3(0, 0, eye.z > DoorZ ? -1 : 1), DoorCentre);
            portal[4] = d;
        }

        // forceRenderingOff is shared with the clinic runtimes, which retire the old bench, bed, counter and shelving with
        // it: the join only ever releases what it switched off itself, and leaves alone what someone else switched off.
        // The live snowfall covers street and stairwell; its own exclusion volume keeps it out of the room.
        static bool UnderSnowfall(Transform t) { for (; t != null; t = t.parent) if (t.name.IndexOf("snowfall", System.StringComparison.OrdinalIgnoreCase) >= 0) return true; return false; }

        static void Set(Renderer r, bool hide, HashSet<Renderer> hidden)
        {
            if (hide)
            {
                if (r.forceRenderingOff) return;
                r.forceRenderingOff = true;
                hidden.Add(r);
            }
            else if (hidden.Remove(r)) r.forceRenderingOff = false;
        }

        static void ShowAll(HashSet<Renderer> hidden)
        {
            if (hidden.Count == 0) return;
            foreach (var r in hidden) if (r != null) r.forceRenderingOff = false;
            hidden.Clear();
        }

        void SetClinicShadows(bool on)
        {
            if (clinicShadowsOff == !on) return;
            clinicShadowsOff = !on;
            foreach (var (l, s) in clinicShadowLamps) if (l != null) l.shadows = on ? s : LightShadows.None;
        }

        // The clinic's runtimes keep adding things (the bench, the cyber kit, customers, the item in hand).
        void RescanClinic()
        {
            rescanAt = Time.unscaledTime + 2f;
            clinicRenderers.Clear(); clinicKeep.Clear();
            var player = walker != null ? walker.transform : null;
            var eye = cam != null ? cam.transform : null;
            clinic.GetRootGameObjects(scanRoots);
            foreach (var root in scanRoots)
            {
                root.GetComponentsInChildren(true, scanRenderers);
                foreach (var r in scanRenderers)
                {
                    if (r.forceRenderingOff && !clinicHidden.Contains(r)) continue;   // retired by a runtime
                    clinicRenderers.Add(r);
                    clinicKeep.Add((player != null && r.transform.IsChildOf(player)) || (eye != null && r.transform.IsChildOf(eye)));
                }
            }
            scanRoots.Clear();
            scanRenderers.Clear();
        }
    }
}
