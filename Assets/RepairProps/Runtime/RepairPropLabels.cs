using System;
using System.Collections.Generic;
using System.Linq;
using BorderRepair.Cleaning;
using BorderRepair.Controls;
using BorderRepair.Core;
using BorderRepair.Narrative;
using UnityEngine;

namespace BorderRepair.RepairProps
{
    public enum PropLabelKind { Keepsake, Junk }
    public enum PropLabelState { On, Peeling, Kept, Returning, Gone }

    /// <summary>
    /// Stickers, tags and tape on the item on the bench, as objects (not part of the dirt texture). A click by hand pulls
    /// one off. Junk (an old price tag, tape) is thrown away and leaves glue to wipe. What matters to the customer (a
    /// child's drawing, a name label) is kept beside the view, its place on the item stays marked, and it has to go back
    /// before the item is handed over. The labels live outside the item's hierarchy and follow the shell they sit on, so
    /// the item gains no objects, colliders or names.
    /// </summary>
    [DefaultExecutionOrder(1150)]   // after the bench presentation and the dirt overlay have posed the parts
    public sealed class RepairPropLabels : MonoBehaviour
    {
        public sealed class Label
        {
            public PropLabelKind Kind;
            public PropLabelState State;
            public string Name;
            internal CleanableSurface surface;
            internal Vector3 localPos; internal Quaternion localRot;
            internal Vector2 size, uv;
            internal Transform body, ghost;
            internal float t;
            internal int slot;
            public bool Off => State != PropLabelState.On && State != PropLabelState.Gone;
        }

        /// <summary>Where kept stickers wait, in viewport units of the bench camera (first one; the rest step right).</summary>
        public static Vector2 KeepViewport = new Vector2(.36f, .17f);
        const float PeelSeconds = .5f, ReturnSeconds = .4f, Lift = .0008f;
        public const string HoldReason = "顾客的贴纸还没贴回去：点物品上的虚线框，把它贴回原位。";

        RepairPropStationBridge bridge;
        readonly List<Label> labels = new List<Label>();
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        Transform root;
        Mesh quad;
        AudioSource audioSource;
        AudioClip tear, stick;
        bool built;
        int nextTry;

        public IReadOnlyList<Label> Labels => labels;
        public bool Built => built;
        /// <summary>The label (or the marked place of a kept one) under the pointer, when a click would act on it.</summary>
        public Label Hovered { get; private set; }
        /// <summary>Kept stickers that still have to go back on the item.</summary>
        public IEnumerable<Label> Pending => labels.Where(l => l.Kind == PropLabelKind.Keepsake && l.Off);

        public void Initialize(RepairPropStationBridge owner)
        {
            bridge = owner;
            if (bridge.Flow != null) bridge.Flow.HandoverHold = () => Pending.Any() ? HoldReason : null;
        }

        /// <summary>What the pointer tag says about a label: its name and what a left click does.</summary>
        public string Hint(Label label)
        {
            if (label == null) return null;
            if (label.State == PropLabelState.Kept) return label.Name + "\n左键 ▶ 贴回原位";
            return label.Kind == PropLabelKind.Keepsake ? "顾客的" + label.Name + "\n左键 ▶ 撕下保留" : label.Name + "\n左键 ▶ 撕掉";
        }

        /// <summary>Where the label belongs on the item, in world space; false while that shell is not shown.</summary>
        public bool Home(Label label, out Vector3 position, out Quaternion rotation)
        {
            position = default; rotation = Quaternion.identity;
            var shown = label.surface != null ? label.surface.Shown : null;
            if (shown == null || !shown.enabled || !shown.gameObject.activeInHierarchy) return false;
            position = shown.transform.TransformPoint(label.localPos);
            rotation = shown.transform.rotation * label.localRot;
            return true;
        }

        // ------------------------------------------------------------------ input
        /// <summary>A left click at <paramref name="screen"/>: true when it landed on a label and was used up.</summary>
        public bool Click(Vector2 screen)
        {
            var label = Pick(screen);
            return label != null && Activate(label);
        }

        /// <summary>Pull the label off, or put a kept one back.</summary>
        public bool Activate(Label label)
        {
            if (label == null || !labels.Contains(label)) return false;
            var view = bridge != null && bridge.Station != null ? bridge.Station.View : null;
            if (label.State == PropLabelState.On)
            {
                label.State = PropLabelState.Peeling; label.t = 0;
                Play(ref tear, RepairSoundSynth.Cue.SealTear, .55f, label.Kind == PropLabelKind.Junk ? 1.15f : 1.3f);
                if (label.Kind == PropLabelKind.Junk)
                {
                    bool glue = label.surface != null && label.surface.AddResidue(label.uv, .035f, new Color32(150, 132, 92, 0));
                    if (view != null) view.ShowFeedback("撕掉了" + label.Name + (glue ? "，留下一块胶痕，用清洁工具擦掉。" : "。"), FeedbackKind.Info);
                }
                else
                {
                    label.slot = Enumerable.Range(0, labels.Count).First(i => !labels.Any(l => l != label && l.Kind == PropLabelKind.Keepsake && l.Off && l.slot == i));
                    if (view != null) view.ShowFeedback("撕下了顾客的" + label.Name + "，先收着。交付前贴回原位。", FeedbackKind.Info);
                }
                return true;
            }
            if (label.State == PropLabelState.Kept)
            {
                if (!Home(label, out _, out _)) { if (view != null) view.ShowFeedback("先把这块外壳装回去，再贴" + label.Name + "。", FeedbackKind.Warning); return true; }
                label.State = PropLabelState.Returning; label.t = 0;
                Play(ref stick, RepairSoundSynth.Cue.Replace, .45f, 1.25f);
                if (view != null) view.ShowFeedback(label.Name + "贴回原位了。", FeedbackKind.Info);
                return true;
            }
            return false;
        }

        Label Pick(Vector2 screen)
        {
            var station = bridge != null ? bridge.Station : null;
            var camera = station != null && station.Inspector != null ? station.Inspector.ViewCamera : null;
            if (camera == null || !built || !camera.pixelRect.Contains(screen)) return null;
            var ray = camera.ScreenPointToRay(screen);
            Label best = null; float bestAt = float.MaxValue; bool bestOnItem = false;
            foreach (var label in labels)
            {
                if (label.State == PropLabelState.On || label.State == PropLabelState.Kept)
                {
                    // on the item: the label itself, or the marked place of a kept one
                    if (Home(label, out var p, out var r) && HitQuad(ray, p, r, label.size * 1.15f, true, out float at) && at < bestAt)
                    { best = label; bestAt = at; bestOnItem = true; }
                }
                if (label.State == PropLabelState.Kept && label.body != null && label.body.gameObject.activeInHierarchy &&
                    HitQuad(ray, label.body.position, label.body.rotation, (Vector2)label.body.localScale * 1.2f, false, out float held) && held < bestAt)
                { best = label; bestAt = held; bestOnItem = false; }
            }
            if (best == null || !bestOnItem) return best;
            // Something nearer than the surface the label lies on (a tool, another part) takes the click instead.
            Physics.SyncTransforms();
            return Physics.Raycast(ray, out var hit, bestAt, ~0, QueryTriggerInteraction.Ignore) && hit.distance < bestAt - .004f ? null : best;
        }

        static bool HitQuad(Ray ray, Vector3 centre, Quaternion rotation, Vector2 size, bool frontOnly, out float distance)
        {
            distance = 0;
            var normal = rotation * Vector3.forward;
            float facing = Vector3.Dot(ray.direction, normal);
            if (frontOnly ? facing > -.05f : Mathf.Abs(facing) < .05f) return false;
            distance = Vector3.Dot(centre - ray.origin, normal) / facing;
            if (distance <= 0) return false;
            var local = Quaternion.Inverse(rotation) * (ray.GetPoint(distance) - centre);
            return Mathf.Abs(local.x) <= size.x * .5f && Mathf.Abs(local.y) <= size.y * .5f;
        }

        // ------------------------------------------------------------------ frame
        void LateUpdate()
        {
            if (bridge == null) return;
            if (!built) { if (Time.frameCount >= nextTry) { nextTry = Time.frameCount + 12; TryBuild(); } return; }
            var station = bridge.Station;
            var camera = station != null && station.Inspector != null ? station.Inspector.ViewCamera : null;
            bool atBench = bridge.WorkspaceVisible && camera != null;
            Hovered = bridge.CanInteract && !RepairInput.PointerOverUI ? Pick(RepairInput.PointerPosition) : null;
            float dt = Time.unscaledDeltaTime;
            foreach (var label in labels)
            {
                bool home = Home(label, out var p, out var r);
                var normal = r * Vector3.forward;
                Vector3 keepP = p; Quaternion keepR = r; var keepS = new Vector3(label.size.x, label.size.y, 1);
                if (camera != null) KeepPose(camera, label, out keepP, out keepR, out keepS);
                var full = new Vector3(label.size.x, label.size.y, 1);
                bool show = false; Vector3 pos = p, scale = full; Quaternion rot = r;
                switch (label.State)
                {
                    case PropLabelState.On:
                        show = home;
                        if (label == Hovered) { pos += normal * (label.size.y * .05f); scale *= 1.05f; }
                        break;
                    case PropLabelState.Peeling:
                    {
                        label.t += dt / PeelSeconds;
                        // first half: one edge comes up off the shell; second half: it leaves (kept, or dropped)
                        float a = Smooth(label.t / .5f), b = Smooth((label.t - .5f) / .5f);
                        var lifted = p + normal * (label.size.y * .45f * a) + r * Vector3.right * (label.size.x * .12f * a);
                        var curled = r * Quaternion.AngleAxis(-38f * a, Vector3.up);
                        if (label.Kind == PropLabelKind.Keepsake && atBench)
                        { pos = Vector3.Lerp(lifted, keepP, b); rot = Quaternion.Slerp(curled, keepR, b); scale = Vector3.Lerp(full, keepS, b); }
                        else
                        { pos = lifted + (normal * label.size.x * .8f + Vector3.down * label.size.x * 1.6f) * (b * b); rot = curled * Quaternion.AngleAxis(140f * b, Vector3.forward); scale = full * (1 - b); }
                        show = home || label.t > .5f;
                        if (label.t >= 1) label.State = label.Kind == PropLabelKind.Keepsake ? PropLabelState.Kept : PropLabelState.Gone;
                        break;
                    }
                    case PropLabelState.Kept:
                        show = atBench; pos = keepP; rot = keepR; scale = label == Hovered ? keepS * 1.08f : keepS;
                        break;
                    case PropLabelState.Returning:
                    {
                        label.t += dt / ReturnSeconds;
                        float a = Smooth(label.t);
                        pos = Vector3.Lerp(keepP, p + normal * (label.size.y * .3f * (1 - a)), a); rot = Quaternion.Slerp(keepR, r, a); scale = Vector3.Lerp(keepS, full, a);
                        show = home;
                        if (label.t >= 1 || !home) label.State = home ? PropLabelState.On : PropLabelState.Kept;
                        break;
                    }
                }
                if (label.body != null)
                {
                    if (label.body.gameObject.activeSelf != show) label.body.gameObject.SetActive(show);
                    if (show) { label.body.SetPositionAndRotation(pos, rot); label.body.localScale = scale; }
                }
                if (label.ghost != null)
                {
                    bool mark = home && label.Kind == PropLabelKind.Keepsake && label.Off;
                    if (label.ghost.gameObject.activeSelf != mark) label.ghost.gameObject.SetActive(mark);
                    if (mark) { label.ghost.SetPositionAndRotation(p, r); label.ghost.localScale = full * (label == Hovered ? 1.12f : 1.04f); }
                }
            }
        }

        static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3 - 2 * t); }

        // Kept stickers wait on the mat in front of the item, a row at a fixed depth in front of the bench camera.
        void KeepPose(Camera camera, Label label, out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            float depth = Mathf.Max(camera.nearClipPlane * 2.5f, .28f);
            float viewHeight = 2 * depth * Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad);
            float tall = viewHeight * .085f, k = tall / Mathf.Max(label.size.x, label.size.y);
            scale = new Vector3(label.size.x * k, label.size.y * k, 1);
            position = camera.ViewportToWorldPoint(new Vector3(KeepViewport.x + label.slot * .062f, KeepViewport.y, depth));
            rotation = Quaternion.LookRotation(-camera.transform.forward, camera.transform.up) * Quaternion.AngleAxis(label.slot % 2 == 0 ? 7 : -6, Vector3.forward);
        }

        // ------------------------------------------------------------------ build
        sealed class MeshData { public Vector3[] verts; public int[] tris; public Vector2[] uv; }

        void TryBuild()
        {
            var station = bridge.Station;
            var item = station != null && station.Inspector != null ? station.Inspector.CurrentItem : null;
            if (item == null || !CleanableSurface.StainsEnabled) return;
            var surfaces = item.GetComponentsInChildren<CleanableSurface>(true)
                .Where(s => s.Ready && s.Dirt != null && s.CleaningMesh != null && s.CleaningMesh.isReadable && s.Shown == s.Target && s.Target.enabled && s.Target.gameObject.activeInHierarchy).ToArray();
            if (surfaces.Length == 0) return;
            built = true;
            var rng = new System.Random(Seed(item.name));
            var bounds = surfaces[0].Target.bounds;
            foreach (var s in surfaces) bounds.Encapsulate(s.Target.bounds);
            float extent = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            var data = new Dictionary<Mesh, MeshData>();
            MeshData Data(Mesh mesh)
            {
                if (!data.TryGetValue(mesh, out var d))
                    data[mesh] = d = new MeshData { verts = mesh.vertices, tris = mesh.triangles, uv = mesh.uv2 };
                return d;
            }
            root = new GameObject("RepairPropLabels_Runtime").transform;
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root.gameObject, gameObject.scene);
            audioSource = root.gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false; audioSource.spatialBlend = 0;
            quad = Quad(); owned.Add(quad);
            Physics.SyncTransforms();

            // One thing of the customer's, and one or two leftovers.
            var wanted = new List<(PropLabelKind kind, int design)> { (PropLabelKind.Keepsake, rng.Next(KeepsakeNames.Length)) };
            int junk = rng.Next(1, 3), first = rng.Next(JunkNames.Length);
            for (int i = 0; i < junk; i++) wanted.Add((PropLabelKind.Junk, (first + i) % JunkNames.Length));

            foreach (var (kind, design) in wanted)
            {
                bool strip = kind == PropLabelKind.Junk && design >= 1 && design <= 2;   // tape
                float aspect = strip ? .26f : kind == PropLabelKind.Junk ? .62f : .75f;
                float width = Mathf.Clamp(extent * (strip ? .26f : .17f) * (float)(.85 + rng.NextDouble() * .3), .012f, .07f);
                for (int tries = 0; tries < 260; tries++)
                {
                    var surface = surfaces[rng.Next(surfaces.Length)];
                    var mesh = Data(surface.CleaningMesh);
                    if (mesh.uv == null || mesh.uv.Length != mesh.verts.Length || mesh.tris.Length < 3) continue;
                    float w = width * (tries < 90 ? 1 : tries < 180 ? .75f : .55f);
                    if (!Place(rng, item.transform, surface, mesh, new Vector2(w, w * aspect), out var label)) continue;
                    label.Kind = kind;
                    label.Name = kind == PropLabelKind.Keepsake ? KeepsakeNames[design] : JunkNames[design];
                    Spawn(label, kind == PropLabelKind.Keepsake ? Keepsake(design) : Junk(design));
                    labels.Add(label);
                    break;
                }
            }
        }

        bool Place(System.Random rng, Transform item, CleanableSurface surface, MeshData mesh, Vector2 size, out Label label)
        {
            label = null;
            int t = rng.Next(mesh.tris.Length / 3) * 3;
            int ia = mesh.tris[t], ib = mesh.tris[t + 1], ic = mesh.tris[t + 2];
            var frame = surface.Target.transform;
            Vector3 a = frame.TransformPoint(mesh.verts[ia]), b = frame.TransformPoint(mesh.verts[ib]), c = frame.TransformPoint(mesh.verts[ic]);
            var normal = Vector3.Cross(b - a, c - a);
            if (normal.sqrMagnitude < 1e-12f) return false;
            normal.Normalize();
            if (normal.y < -.3f) return false;                       // the face the item lies on
            var uv = (mesh.uv[ia] + mesh.uv[ib] + mesh.uv[ic]) / 3f;
            if (surface.RemainingAt(uv) < .03f) return false;        // not the outer shell (a screen, a seam, the inside)
            var point = (a + b + c) / 3f;
            // Open air in front of the spot, and the shell flat under the whole label.
            if (Physics.Raycast(point + normal * .06f, -normal, .06f - .004f, ~0, QueryTriggerInteraction.Ignore)) return false;
            var up = Vector3.ProjectOnPlane(Vector3.up, normal);
            if (up.sqrMagnitude < .04f) up = Vector3.ProjectOnPlane(frame.forward, normal);
            if (up.sqrMagnitude < .04f) up = Vector3.ProjectOnPlane(frame.right, normal);
            up = Quaternion.AngleAxis((float)(rng.NextDouble() * 24 - 12), normal) * up.normalized;
            var right = Vector3.Cross(up, normal);
            for (int i = 0; i < 5; i++)
            {
                var corner = i == 4 ? point : point + right * (size.x * .5f * (i % 2 == 0 ? -1 : 1)) + up * (size.y * .5f * (i < 2 ? -1 : 1));
                if (!Physics.Raycast(corner + normal * .006f, -normal, out var hit, .012f, ~0, QueryTriggerInteraction.Ignore)) return false;
                if (Vector3.Dot(hit.normal, normal) < .94f || !hit.transform.IsChildOf(item)) return false;
            }
            foreach (var other in labels)
                if (other.surface == surface && Home(other, out var op, out _) && (op - point).magnitude < (other.size.x + size.x) * .6f) return false;
            label = new Label
            {
                surface = surface, size = size, uv = uv,
                localPos = frame.InverseTransformPoint(point + normal * Lift),
                localRot = Quaternion.Inverse(frame.rotation) * Quaternion.LookRotation(normal, up),
            };
            return true;
        }

        void Spawn(Label label, Texture2D face)
        {
            owned.Add(face);
            int layer = label.surface.Target.gameObject.layer;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            var material = new Material(shader) { name = "PropLabel_Runtime", hideFlags = HideFlags.DontSave, mainTexture = face };
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", face);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .12f);
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", 0);
            owned.Add(material);
            label.body = Sheet("Label " + label.Name, material, layer);

            if (label.Kind != PropLabelKind.Keepsake) return;
            var ghostShader = Shader.Find("Sprites/Default");
            if (ghostShader == null) return;
            var outline = Outline(); owned.Add(outline);
            var ghostMaterial = new Material(ghostShader) { name = "PropLabelGhost_Runtime", hideFlags = HideFlags.DontSave, mainTexture = outline };
            owned.Add(ghostMaterial);
            label.ghost = Sheet("Label place " + label.Name, ghostMaterial, layer);
        }

        Transform Sheet(string name, Material material, int layer)
        {
            var sheet = new GameObject(name) { layer = layer };
            sheet.transform.SetParent(root, false);
            sheet.AddComponent<MeshFilter>().sharedMesh = quad;
            var renderer = sheet.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sheet.SetActive(false);
            return sheet.transform;
        }

        // Faces +Z; seen from there the picture reads the right way round.
        static Mesh Quad()
        {
            var mesh = new Mesh { name = "PropLabelQuad_Runtime", hideFlags = HideFlags.DontSave };
            mesh.vertices = new[] { new Vector3(-.5f, -.5f), new Vector3(.5f, -.5f), new Vector3(.5f, .5f), new Vector3(-.5f, .5f) };
            mesh.uv = new[] { new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1) };
            mesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            return mesh;
        }

        static int Seed(string text)
        {
            unchecked { uint h = 2166136261; foreach (char ch in text ?? "") h = (h ^ ch) * 16777619; return (int)(h & 0x7fffffff); }
        }

        void Play(ref AudioClip clip, RepairSoundSynth.Cue cue, float volume, float pitch)
        {
            if (audioSource == null) return;
            if (clip == null) { clip = RepairSoundSynth.Create(cue); owned.Add(clip); }
            audioSource.pitch = pitch;
            audioSource.PlayOneShot(clip, volume);
        }

        void OnDisable() { if (root != null) root.gameObject.SetActive(false); Hovered = null; }
        void OnEnable() { if (root != null) root.gameObject.SetActive(true); }

        void OnDestroy()
        {
            if (bridge != null && bridge.Flow != null) bridge.Flow.HandoverHold = null;
            if (root != null) Destroy(root.gameObject);
            foreach (var o in owned) if (o != null) Destroy(o);
        }

        // ------------------------------------------------------------------ pictures (small on purpose: the game's own texel size)
        static readonly string[] KeepsakeNames = { "手绘贴纸", "爱心贴纸", "名字贴", "护身符贴纸" };
        static readonly string[] JunkNames = { "旧价签", "布基胶带", "警示胶条", "过期质检签" };

        sealed class Art
        {
            public readonly int W, H; public readonly Color32[] Px;
            public Art(int w, int h, Color32 fill) { W = w; H = h; Px = new Color32[w * h]; for (int i = 0; i < Px.Length; i++) Px[i] = fill; }
            public void Set(int x, int y, Color32 c) { if (x >= 0 && y >= 0 && x < W && y < H) Px[y * W + x] = c; }
            public void Rect(int x0, int y0, int x1, int y1, Color32 c) { for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) Set(x, y, c); }
            public void Frame(Color32 c) { for (int x = 0; x < W; x++) { Set(x, 0, c); Set(x, H - 1, c); } for (int y = 0; y < H; y++) { Set(0, y, c); Set(W - 1, y, c); } }
            public void Disc(float cx, float cy, float r, Color32 c) { for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r) Set(x, y, c); }
            public void Line(int x0, int y0, int x1, int y1, Color32 c)
            {
                int steps = Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0), 1);
                for (int i = 0; i <= steps; i++) Set(Mathf.RoundToInt(Mathf.Lerp(x0, x1, i / (float)steps)), Mathf.RoundToInt(Mathf.Lerp(y0, y1, i / (float)steps)), c);
            }
            public void Heart(float cx, float cy, float s, Color32 c)
            {
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        float u = (x - cx) / s, v = (y - cy) / s + .15f, k = u * u + v * v - 1;
                        if (k * k * k - u * u * v * v * v <= 0) Set(x, y, c);
                    }
            }
            public Texture2D Bake(string name)
            {
                var texture = new Texture2D(W, H, TextureFormat.RGBA32, false) { name = name, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
                texture.SetPixels32(Px); texture.Apply(false, true);
                return texture;
            }
        }

        static Color32 C(int r, int g, int b, int a = 255) => new Color32((byte)r, (byte)g, (byte)b, (byte)a);

        static Texture2D Keepsake(int design)
        {
            Art c;
            switch (design)
            {
                case 0:   // a child's drawing: sun, a figure, a heart, grass
                    c = new Art(32, 24, C(236, 226, 196));
                    c.Disc(6, 18, 3.2f, C(240, 170, 30));
                    for (int i = 0; i < 6; i++) { float a = i * Mathf.PI / 3; c.Set(Mathf.RoundToInt(6 + Mathf.Cos(a) * 5.4f), Mathf.RoundToInt(18 + Mathf.Sin(a) * 5.4f), C(240, 150, 30)); }
                    c.Disc(17, 15, 2.4f, C(60, 90, 190)); c.Line(17, 12, 17, 6, C(60, 90, 190)); c.Line(17, 10, 13, 12, C(60, 90, 190)); c.Line(17, 10, 21, 12, C(60, 90, 190));
                    c.Line(17, 6, 14, 3, C(60, 90, 190)); c.Line(17, 6, 20, 3, C(60, 90, 190));
                    c.Heart(26, 14, 3.4f, C(214, 50, 60));
                    for (int x = 2; x < 30; x++) c.Set(x, 2 + (x % 3 == 0 ? 1 : 0), C(70, 150, 70));
                    c.Frame(C(206, 194, 160));
                    break;
                case 1:   // a heart on pink
                    c = new Art(32, 24, C(244, 150, 180));
                    c.Frame(C(252, 236, 240)); c.Rect(1, 1, 30, 1, C(252, 236, 240)); c.Rect(1, 22, 30, 22, C(252, 236, 240));
                    c.Heart(16, 12, 7.2f, C(206, 30, 60)); c.Heart(16, 12.5f, 4.6f, C(246, 96, 120)); c.Rect(12, 15, 13, 16, C(255, 226, 232));
                    break;
                case 2:   // a name label: band, two lines of handwriting, a star
                    c = new Art(32, 24, C(240, 238, 228));
                    c.Rect(0, 18, 31, 23, C(50, 96, 170));
                    for (int x = 4; x < 22; x++) { c.Set(x, 12 + (int)Mathf.Round(Mathf.Sin(x * 1.3f) * 1.4f), C(40, 44, 70)); if (x < 17) c.Set(x, 6 + (int)Mathf.Round(Mathf.Sin(x * 1.7f + 1) * 1.2f), C(40, 44, 70)); }
                    c.Line(26, 13, 26, 5, C(230, 170, 30)); c.Line(22, 9, 30, 9, C(230, 170, 30)); c.Line(23, 6, 29, 12, C(230, 170, 30)); c.Line(23, 12, 29, 6, C(230, 170, 30));
                    c.Frame(C(196, 194, 184));
                    break;
                default:  // a charm sticker: red, gold edge, a knot
                    c = new Art(32, 24, C(176, 30, 34));
                    c.Frame(C(226, 184, 70)); c.Rect(2, 2, 29, 2, C(226, 184, 70)); c.Rect(2, 21, 29, 21, C(226, 184, 70));
                    for (int i = 0; i <= 6; i++) { c.Set(16 - i, 6 + i, C(238, 200, 90)); c.Set(16 + i, 6 + i, C(238, 200, 90)); c.Set(16 - i, 18 - i, C(238, 200, 90)); c.Set(16 + i, 18 - i, C(238, 200, 90)); }
                    c.Disc(16, 12, 2.2f, C(238, 200, 90)); c.Disc(16, 12, 1f, C(176, 30, 34));
                    break;
            }
            return c.Bake("PropLabel_Keepsake_Runtime");
        }

        static Texture2D Junk(int design)
        {
            Art c;
            switch (design)
            {
                case 0:   // an old price tag: bars, a struck price
                    c = new Art(32, 20, C(232, 220, 150));
                    for (int x = 3; x < 20; x++) if ((x * 7 % 5) < 3) c.Rect(x, 4, x, 15, C(40, 38, 36));
                    c.Rect(22, 12, 29, 15, C(190, 40, 36)); c.Rect(22, 5, 29, 6, C(60, 58, 54)); c.Rect(22, 8, 27, 9, C(60, 58, 54));
                    c.Line(2, 3, 29, 16, C(190, 40, 36));
                    c.Frame(C(190, 178, 116));
                    break;
                case 1:   // duct tape
                    c = new Art(48, 12, C(112, 116, 122));
                    for (int y = 0; y < 12; y++) for (int x = 0; x < 48; x++) if ((x + y * 3) % 4 == 0) c.Set(x, y, C(96, 100, 106));
                    c.Rect(0, 0, 47, 0, C(78, 80, 86)); c.Rect(0, 11, 47, 11, C(140, 144, 150));
                    for (int y = 0; y < 12; y++) { if (y % 3 != 0) c.Set(0, y, C(78, 80, 86)); if (y % 4 == 1) c.Set(47, y, C(78, 80, 86)); }
                    break;
                case 2:   // hazard strip
                    c = new Art(48, 12, C(232, 180, 20));
                    for (int y = 0; y < 12; y++) for (int x = 0; x < 48; x++) if (((x + y) / 5) % 2 == 0) c.Set(x, y, C(34, 32, 30));
                    c.Rect(0, 0, 47, 0, C(150, 116, 14)); c.Rect(0, 11, 47, 11, C(150, 116, 14));
                    break;
                default:  // an expired inspection seal: ring, tick, date lines
                    c = new Art(32, 20, C(226, 228, 222));
                    c.Disc(9, 10, 6.5f, C(50, 140, 80)); c.Disc(9, 10, 4.6f, C(226, 228, 222));
                    c.Line(6, 10, 8, 8, C(50, 140, 80)); c.Line(8, 8, 12, 13, C(50, 140, 80));
                    c.Rect(18, 13, 29, 14, C(70, 70, 70)); c.Rect(18, 9, 27, 10, C(70, 70, 70)); c.Rect(18, 5, 24, 6, C(180, 50, 44));
                    c.Frame(C(170, 172, 166));
                    break;
            }
            return c.Bake("PropLabel_Junk_Runtime");
        }

        // The marked place of a kept sticker: a dashed amber outline, a faint fill.
        static Texture2D Outline()
        {
            var c = new Art(32, 24, C(255, 170, 60, 26));
            for (int x = 0; x < 32; x++) if (x % 4 < 2) { c.Set(x, 0, C(255, 176, 64, 235)); c.Set(x, 23, C(255, 176, 64, 235)); }
            for (int y = 0; y < 24; y++) if (y % 4 < 2) { c.Set(0, y, C(255, 176, 64, 235)); c.Set(31, y, C(255, 176, 64, 235)); }
            return c.Bake("PropLabel_Place_Runtime");
        }
    }
}
