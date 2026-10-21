using System.Collections.Generic;
using UnityEngine;

namespace BorderRepair.RepairProps
{
    // One low-poly clinic NPC (ArtSource/ClinicNpcs): a static body and a head on its own pivot, no skeleton. Life comes
    // from cheap tricks after the reference library's tips: the face sheet holds 2x2 frames (eyes open/closed x mouth
    // closed/open) picked per renderer through _BaseMap_ST, the head turns toward a look target within neck limits,
    // the body breathes and sways, and a walk is a glide with a bob along waypoints. Visual only: no colliders.
    [AddComponentMenu("")]
    public sealed class ClinicNpc : MonoBehaviour
    {
        static readonly int BaseMapSt = Shader.PropertyToID("_BaseMap_ST");
        [SerializeField] string npcName;
        [SerializeField] Transform head, eye, front;
        Renderer headRenderer;
        MaterialPropertyBlock block;
        Quaternion headRest;
        Vector3 bodyScale;
        float blinkUntil, nextBlink, nextMouth, seed;
        bool mouthOpen, eyesClosed;
        readonly Queue<Vector3> path = new Queue<Vector3>();
        Vector3 standFacing;
        float walkPhase;

        public string NpcName => npcName;
        public Transform Head => head;
        public Transform Eye => eye;
        /// <summary>World point the head turns toward (null: look ahead).</summary>
        public Vector3? LookTarget { get; set; }
        /// <summary>While true the mouth flaps.</summary>
        public bool Talking { get; set; }
        public bool Walking => path.Count > 0;
        public bool EyesClosed => eyesClosed;
        public bool MouthOpen => mouthOpen;

        public void Configure(string name, Transform headTransform, Transform eyeMarker, Transform frontMarker)
        {
            npcName = name; head = headTransform; eye = eyeMarker; front = frontMarker;
        }

        void Awake()
        {
            headRenderer = head != null ? head.GetComponent<Renderer>() : null;
            block = new MaterialPropertyBlock();
            headRest = head != null ? head.localRotation : Quaternion.identity;
            bodyScale = transform.localScale;
            seed = Random.value * 10f;
            nextBlink = Time.time + Random.Range(1f, 4f);
            standFacing = transform.forward;
        }

        /// <summary>Local forward of the model (the FBX's Ref_Front marker), so callers can face it anywhere.</summary>
        public Vector3 ModelForward
        {
            get
            {
                if (front == null) return transform.forward;
                var f = front.position - transform.position; f.y = 0;
                return f.sqrMagnitude > 1e-6f ? f.normalized : transform.forward;
            }
        }

        /// <summary>Turns the whole NPC so the model faces the given world direction.</summary>
        public void Face(Vector3 direction)
        {
            direction.y = 0;
            if (direction.sqrMagnitude < 1e-6f) return;
            float delta = Vector3.SignedAngle(ModelForward, direction.normalized, Vector3.up);
            transform.Rotate(0, delta, 0, Space.World);
        }

        public void WalkTo(IEnumerable<Vector3> points, Vector3 finalFacing)
        {
            path.Clear();
            foreach (var p in points) path.Enqueue(p);
            standFacing = finalFacing;
        }

        void Update()
        {
            float t = Time.time;
            // Walking: glide toward the next waypoint at a stroll, turning into the direction of travel.
            if (path.Count > 0)
            {
                var target = path.Peek();
                var to = target - transform.position; to.y = 0;
                float step = 1.0f * Time.deltaTime;
                if (to.magnitude <= step) { transform.position = new Vector3(target.x, transform.position.y, target.z); path.Dequeue(); if (path.Count == 0) Face(standFacing); }
                else
                {
                    transform.position += to.normalized * step;
                    var forward = Vector3.RotateTowards(ModelForward, to.normalized, 6f * Time.deltaTime, 0);
                    Face(forward);
                }
                walkPhase += Time.deltaTime * 9f;
            }
            else walkPhase = Mathf.MoveTowards(walkPhase, Mathf.Round(walkPhase / Mathf.PI) * Mathf.PI, Time.deltaTime * 4f);
            // Breathing (and a step bob while walking) as a scale on the body; heads ride along.
            float breathe = 1 + .006f * Mathf.Sin(t * 1.7f + seed) + .012f * Mathf.Abs(Mathf.Sin(walkPhase));
            transform.localScale = new Vector3(bodyScale.x, bodyScale.y * breathe, bodyScale.z);

            if (head != null)
            {
                // Composed in world space: the FBX hierarchy carries an axis conversion, so local axes are not the room's.
                var parent = head.parent;
                var forward = ModelForward;
                var side = Vector3.Cross(Vector3.up, forward).normalized;
                var world = Quaternion.AngleAxis(3f * Mathf.Sin(t * .37f + seed), Vector3.up) * Quaternion.AngleAxis(1.5f * Mathf.Sin(t * .7f + seed), side)
                    * (parent.rotation * headRest);
                if (LookTarget.HasValue)
                {
                    var dir = LookTarget.Value - head.position;
                    var flat = new Vector3(dir.x, 0, dir.z);
                    float yaw = Mathf.Clamp(Vector3.SignedAngle(forward, flat, Vector3.up), -55, 55);
                    float pitch = Mathf.Clamp(Mathf.Atan2(dir.y, flat.magnitude) * Mathf.Rad2Deg, -22, 18);
                    world = Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.AngleAxis(-pitch, side) * world;
                }
                if (Talking) world = Quaternion.AngleAxis(2.2f * Mathf.Sin(t * 5.3f), side) * world;
                head.localRotation = Quaternion.Slerp(head.localRotation, Quaternion.Inverse(parent.rotation) * world, 1 - Mathf.Exp(-6f * Time.deltaTime));
            }

            // Face frames: blink every few seconds; flap the mouth while talking.
            if (t >= nextBlink) { blinkUntil = t + .12f; nextBlink = t + Random.Range(2.2f, 5.5f) * (Random.value < .15f ? .2f : 1f); }
            bool closed = t < blinkUntil;
            bool open = mouthOpen;
            if (Talking) { if (t >= nextMouth) { open = !mouthOpen || Random.value < .3f; nextMouth = t + Random.Range(.07f, .15f); } }
            else open = false;
            if (closed != eyesClosed || open != mouthOpen || !applied) Apply(closed, open);
        }

        bool applied;
        void Apply(bool closed, bool open)
        {
            eyesClosed = closed; mouthOpen = open; applied = true;
            if (headRenderer == null || headRenderer.sharedMaterials.Length < 2) return;
            headRenderer.GetPropertyBlock(block, 1);
            block.SetVector(BaseMapSt, new Vector4(1, 1, closed ? .5f : 0, open ? -.5f : 0));
            headRenderer.SetPropertyBlock(block, 1);
        }
    }
}
