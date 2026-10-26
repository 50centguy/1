using System.Collections.Generic;
using UnityEngine;

namespace BorderRepair.RepairProps
{
    // The omamori's red hanging cord as a soft rope: a Verlet loop between the two steel lugs on the crown plus a tail
    // with a tassel, so the cord swings and drapes when the charm is carried, turned or laid down. The bow knot on the
    // face is part of the V2 shell mesh. Visual only: no colliders, nothing for the flow.
    [DefaultExecutionOrder(900)]
    public sealed class OmamoriCord : MonoBehaviour
    {
        // Model space (metres, prefab root): the two lugs on the crown and the shell box the cord rests against.
        static readonly Vector3 LeftLug = new Vector3(-.016f, .0975f, 0f), RightLug = new Vector3(.016f, .0975f, 0f);
        static readonly Bounds Shell = new Bounds(new Vector3(0f, .0465f, .001f), new Vector3(.0600f, .0930f, .0215f));
        const float LoopLength = .052f, TailLength = .042f, Radius = .0013f, TasselRadius = .0028f, TasselLength = .011f;
        const int LoopPoints = 15, TailPoints = 11, Sides = 6, Iterations = 14;
        const float Step = 1f / 120f, Damping = .985f;

        sealed class Strand
        {
            public Vector3[] pos, prev;
            public float segment;
            public bool pinnedEnd;
        }

        Strand loop, tail;
        Transform root;
        Mesh mesh;
        GameObject visual;
        Material material;
        float accumulator;
        Vector3[] vertices;

        public const string VisualName = "OmamoriCord_Runtime";
        public bool Ready => visual != null;
        public Vector3 TailEnd => tail != null ? tail.pos[tail.pos.Length - 1] : transform.position;
        public Vector3 LoopMiddle => loop != null ? loop.pos[loop.pos.Length / 2] : transform.position;

        void Start()
        {
            root = transform;
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.name = "OmamoriCord_Runtime";
            var red = new Color(.62f, .045f, .12f);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", red);
            if (material.HasProperty("_Color")) material.SetColor("_Color", red);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .25f);

            visual = new GameObject(VisualName);
            visual.transform.SetParent(root, false);
            mesh = new Mesh { name = "OmamoriCord" };
            mesh.MarkDynamic();
            visual.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = visual.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // Starts arched over the crown with the tail down the left shoulder, then settles under gravity.
            loop = Build(LoopPoints, LoopLength, i =>
            {
                float t = i / (float)(LoopPoints - 1);
                return Vector3.Lerp(LeftLug, RightLug, t) + new Vector3(0f, Mathf.Sin(t * Mathf.PI) * .018f, .002f);
            }, true);
            tail = Build(TailPoints, TailLength, i =>
            {
                float t = i / (float)(TailPoints - 1);
                return LeftLug + new Vector3(-.006f - t * .02f, -t * .035f, .002f);
            }, false);
            Rebuild();
        }

        Strand Build(int count, float length, System.Func<int, Vector3> layout, bool pinnedEnd)
        {
            var s = new Strand { pos = new Vector3[count], prev = new Vector3[count], pinnedEnd = pinnedEnd };
            for (int i = 0; i < count; i++) s.pos[i] = s.prev[i] = root.TransformPoint(layout(i));
            s.segment = length / (count - 1);
            return s;
        }

        void LateUpdate()
        {
            if (visual == null) return;
            accumulator = Mathf.Min(accumulator + Time.deltaTime, Step * 6);
            while (accumulator >= Step)
            {
                accumulator -= Step;
                Simulate(loop, Step);
                Simulate(tail, Step);
            }
            Rebuild();
        }

        void Simulate(Strand s, float dt)
        {
            float scale = root.lossyScale.x;
            var gravity = Physics.gravity * dt * dt;
            int last = s.pos.Length - 1;
            for (int i = 0; i < s.pos.Length; i++)
            {
                var p = s.pos[i];
                s.pos[i] += (p - s.prev[i]) * Damping + gravity;
                s.prev[i] = p;
            }
            var a = root.TransformPoint(LeftLug);
            var b = root.TransformPoint(RightLug);
            float rest = s.segment * scale;
            for (int k = 0; k < Iterations; k++)
            {
                s.pos[0] = a;
                if (s.pinnedEnd) s.pos[last] = b;
                for (int i = 0; i < last; i++)
                {
                    var d = s.pos[i + 1] - s.pos[i];
                    float len = d.magnitude;
                    if (len < 1e-7f) continue;
                    var fix = d * ((len - rest) / len * .5f);
                    bool fixedA = i == 0, fixedB = s.pinnedEnd && i + 1 == last;
                    if (fixedA && fixedB) continue;
                    if (fixedA) s.pos[i + 1] -= fix * 2;
                    else if (fixedB) s.pos[i] += fix * 2;
                    else { s.pos[i] += fix; s.pos[i + 1] -= fix; }
                }
                // Braided cord resists folding: neighbours two apart keep most of their straight spacing.
                for (int i = 1; i < last; i++)
                {
                    var d = s.pos[i + 1] - s.pos[i - 1];
                    float len = d.magnitude, min = rest * 1.7f;
                    if (len >= min || len < 1e-7f) continue;
                    var push = d * ((min - len) / len * .25f);
                    if (i - 1 > 0) s.pos[i - 1] -= push;
                    if (!(s.pinnedEnd && i + 1 == last)) s.pos[i + 1] += push;
                }
                for (int i = 1; i < s.pos.Length; i++) s.pos[i] = OutsideShell(s.pos[i], Radius * 1.2f);
            }
            s.pos[0] = a;
            if (s.pinnedEnd) s.pos[last] = b;
        }

        // Pushes a point that sank into the charm's body back out through the nearest face.
        Vector3 OutsideShell(Vector3 world, float pad)
        {
            var local = root.InverseTransformPoint(world);
            var min = Shell.min - Vector3.one * pad;
            var max = Shell.max + Vector3.one * pad;
            if (local.x <= min.x || local.x >= max.x || local.y <= min.y || local.y >= max.y || local.z <= min.z || local.z >= max.z) return world;
            float[] gaps = { local.x - min.x, max.x - local.x, local.y - min.y, max.y - local.y, local.z - min.z, max.z - local.z };
            int best = 0;
            for (int i = 1; i < 6; i++) if (gaps[i] < gaps[best]) best = i;
            switch (best)
            {
                case 0: local.x = min.x; break;
                case 1: local.x = max.x; break;
                case 2: local.y = min.y; break;
                case 3: local.y = max.y; break;
                case 4: local.z = min.z; break;
                default: local.z = max.z; break;
            }
            return root.TransformPoint(local);
        }

        void Rebuild()
        {
            int loopVerts = LoopPoints * Sides, tailVerts = (TailPoints + 2) * Sides;
            if (vertices == null)
            {
                vertices = new Vector3[loopVerts + tailVerts + 1];
                mesh.vertices = vertices;
                var tris = new List<int>();
                Tube(tris, 0, LoopPoints);
                Tube(tris, loopVerts, TailPoints + 2);
                int cap = loopVerts + tailVerts, ring = loopVerts + (TailPoints + 1) * Sides;
                for (int j = 0; j < Sides; j++) { tris.Add(ring + j); tris.Add(ring + (j + 1) % Sides); tris.Add(cap); }
                mesh.SetTriangles(tris, 0);
            }
            var toLocal = visual.transform.worldToLocalMatrix;
            Ring(loop.pos, toLocal, 0, i => Radius);
            // The tail carries the tassel: a knot bulge where it leaves the lug, then a flared tuft past the last point.
            var tailWithTuft = new Vector3[TailPoints + 2];
            System.Array.Copy(tail.pos, tailWithTuft, TailPoints);
            var dir = tail.pos[TailPoints - 1] - tail.pos[TailPoints - 2];
            dir = dir.sqrMagnitude > 1e-10f ? dir.normalized : Vector3.down;
            float s = root.lossyScale.x;
            tailWithTuft[TailPoints] = tail.pos[TailPoints - 1] + dir * TasselLength * .35f * s;
            tailWithTuft[TailPoints + 1] = tail.pos[TailPoints - 1] + dir * TasselLength * s;
            Ring(tailWithTuft, toLocal, loopVerts, i => i <= 1 ? Radius * 1.9f : i < TailPoints - 1 ? Radius : i == TailPoints - 1 ? Radius * 1.6f : TasselRadius);
            vertices[loopVerts + tailVerts] = toLocal.MultiplyPoint3x4(tailWithTuft[TailPoints + 1] + dir * .0015f * s);
            mesh.vertices = vertices;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        void Ring(Vector3[] points, Matrix4x4 toLocal, int start, System.Func<int, float> radius)
        {
            float s = root.lossyScale.x;
            var up = root.forward;
            for (int i = 0; i < points.Length; i++)
            {
                var along = (points[Mathf.Min(i + 1, points.Length - 1)] - points[Mathf.Max(i - 1, 0)]);
                along = along.sqrMagnitude > 1e-12f ? along.normalized : Vector3.down;
                var side = Vector3.Cross(along, Mathf.Abs(Vector3.Dot(along, up)) > .9f ? root.right : up).normalized;
                var other = Vector3.Cross(along, side);
                float r = radius(i) * s;
                for (int j = 0; j < Sides; j++)
                {
                    float angle = j * Mathf.PI * 2 / Sides;
                    vertices[start + i * Sides + j] = toLocal.MultiplyPoint3x4(points[i] + (side * Mathf.Cos(angle) + other * Mathf.Sin(angle)) * r);
                }
            }
        }

        static void Tube(List<int> tris, int start, int rings)
        {
            for (int i = 0; i < rings - 1; i++)
                for (int j = 0; j < Sides; j++)
                {
                    int a = start + i * Sides + j, b = start + i * Sides + (j + 1) % Sides;
                    int c = a + Sides, d = b + Sides;
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(b); tris.Add(c); tris.Add(d);
                }
        }

        void OnDestroy()
        {
            if (visual != null) Destroy(visual);
            if (mesh != null) Destroy(mesh);
            if (material != null) Destroy(material);
        }
    }
}
