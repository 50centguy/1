using System;
using System.Collections.Generic;
using UnityEngine;

namespace BorderRepair.Cleaning
{
    /// <summary>Per-instance dirt. Authored assets stay untouched; the visual and dedicated probe share a readable UV1 copy.</summary>
    [DisallowMultipleComponent]
    public sealed class CleanableSurface : MonoBehaviour
    {
        [SerializeField] Renderer target;
        [SerializeField] Mesh probeMesh;
        [SerializeField] Material dirtTemplate;
        [SerializeField] int resolution = 128;
        [SerializeField] byte[] uvCoverage;
        [SerializeField] int[] materialSlots;
        [SerializeField] int[] triangleSlots;
        [SerializeField] Color dirtColor = new Color(.32f, .215f, .105f, 1);
        [SerializeField, Range(0, 1)] float dirtAmount = .95f;
        [SerializeField, Range(0, 1)] float dirtSmoothness = .07f;
        [SerializeField, Range(.5f, 1)] float completionThreshold = .97f;
        readonly Dictionary<Material, Material> replacements = new Dictionary<Material, Material>();
        readonly HashSet<int> selectedSlots = new HashSet<int>();
        DirtMaskData data;
        Texture2D mask;
        MaterialPropertyBlock block;
        GameObject probeObject;
        MeshCollider probe;
        MeshFilter visualMesh;
        Mesh originalMesh;
        bool dirty, completed;
        static readonly int MaskId = Shader.PropertyToID("_CleaningMask");
        public float Progress => data?.Progress ?? 0;
        public bool IsClean => data != null && Progress >= completionThreshold;
        public Renderer Target => target;
        public Mesh CleaningMesh => probeMesh;
        public Texture2D MaskTexture => mask;
        public bool Ready => data != null && target != null && probe != null;
        public event Action<float> ProgressChanged;
        public event Action Cleaned;

        public void SetDirtAppearance(Color color, float opacity, float smoothness)
        {
            dirtColor = color; dirtAmount = Mathf.Clamp01(opacity); dirtSmoothness = Mathf.Clamp01(smoothness);
            foreach (var material in replacements.Keys)
            {
                material.SetColor("_DirtColor", dirtColor); material.SetFloat("_DirtAmount", dirtAmount);
                material.SetFloat("_DirtSmoothness", dirtSmoothness);
            }
        }

        public bool SameIsland(Vector2 a, Vector2 b) => data != null && data.SameIsland(a, b);

        public void Configure(Renderer renderer, Mesh readableProbe, Material template, int size, byte[] coverage, int[] slots, int[] triangleMaterialSlots)
        {
            Release();
            target = renderer; probeMesh = readableProbe; dirtTemplate = template; resolution = size;
            uvCoverage = coverage; materialSlots = slots; triangleSlots = triangleMaterialSlots;
            if (Application.isPlaying && isActiveAndEnabled) Initialize();
        }

        void OnEnable() { if (Application.isPlaying) Initialize(); }
        void OnDisable() { SetProbeEnabled(false); }
        void OnDestroy() => Release();

        void Initialize()
        {
            if (data != null) return;
            if (target == null || probeMesh == null || dirtTemplate == null || !probeMesh.isReadable ||
                uvCoverage == null || uvCoverage.Length != resolution * resolution || materialSlots == null) return;
            data = new DirtMaskData(resolution, uvCoverage);
            mask = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false, true)
                { name = name + "_CleaningMask_Runtime", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
            visualMesh = target.GetComponent<MeshFilter>();
            if (visualMesh == null) { data = null; DestroyOwned(mask); mask = null; return; }
            originalMesh = visualMesh.sharedMesh;
            visualMesh.sharedMesh = probeMesh; // Same geometry/base UV0; a private, non-overlapping UV1 atlas controls cleaning.
            block = new MaterialPropertyBlock();
            var materials = target.sharedMaterials;
            foreach (int slot in materialSlots)
            {
                if (slot < 0 || slot >= materials.Length || materials[slot] == null) continue;
                selectedSlots.Add(slot);
                var original = materials[slot];
                var styled = new Material(dirtTemplate) { name = original.name + "_Cleanable_Runtime", hideFlags = HideFlags.DontSave };
                styled.CopyPropertiesFromMaterial(original);
                styled.shader = dirtTemplate.shader;
                styled.shaderKeywords = Array.Empty<string>();
                styled.renderQueue = 2000;
                styled.SetFloat("_Roughness", original.HasProperty("_Roughness") ? original.GetFloat("_Roughness") :
                    1 - (original.HasProperty("_Smoothness") ? original.GetFloat("_Smoothness") : .25f));
                styled.SetFloat("_UseNormal", original.HasProperty("_BumpMap") && original.GetTexture("_BumpMap") != null ? 1 : 0);
                styled.SetFloat("_UseMetallicMap", original.HasProperty("_MetallicGlossMap") && original.GetTexture("_MetallicGlossMap") != null ? 1 : 0);
                styled.SetColor("_DirtColor", dirtColor); styled.SetFloat("_DirtAmount", dirtAmount); styled.SetFloat("_DirtSmoothness", dirtSmoothness);
                replacements.Add(styled, original); materials[slot] = styled;
            }
            target.sharedMaterials = materials;
            probeObject = new GameObject("CleaningProbe_Runtime") { layer = 2, hideFlags = HideFlags.DontSave };
            probeObject.transform.SetParent(target.transform, false);
            probe = probeObject.AddComponent<MeshCollider>(); probe.sharedMesh = probeMesh; probe.convex = false;
            probe.enabled = false;
            Upload();
        }

        public void SetProbeEnabled(bool value) { if (probe != null) probe.enabled = value && isActiveAndEnabled && target != null && target.enabled; }

        public bool Raycast(Ray ray, float distance, out RaycastHit hit)
        {
            hit = default;
            return Ready && probe.enabled && target.enabled && target.gameObject.activeInHierarchy && probe.Raycast(ray, out hit, distance);
        }

        public bool AcceptsHit(RaycastHit hit) => hit.collider == probe && hit.triangleIndex >= 0 &&
            triangleSlots != null && hit.triangleIndex < triangleSlots.Length && selectedSlots.Contains(triangleSlots[hit.triangleIndex]);

        public bool Wipe(Vector2 uv, float radius, float strength)
        {
            if (data == null || !data.Paint(uv, radius, strength, true)) return false;
            if (data.Progress >= completionThreshold) data.Clear();
            dirty = true;
            Notify();
            return true;
        }

        public void ResetDirt() { if (data == null) return; data.Reset(); completed = false; dirty = true; Notify(); }
        public void CleanAll() { if (data == null) return; data.Clear(); dirty = true; Notify(); }
        public string ExportMask() => data?.Export();
        public bool ImportMask(string encoded) { if (data == null || !data.Import(encoded)) return false; dirty = true; completed = false; Notify(); return true; }

        void Notify()
        {
            ProgressChanged?.Invoke(Progress);
            if (IsClean && !completed) { completed = true; Cleaned?.Invoke(); }
        }

        void LateUpdate() { if (dirty) Upload(); }
        public void Upload()
        {
            if (mask == null || target == null) return;
            mask.SetPixels32(data.Pixels); mask.Apply(false, false);
            target.GetPropertyBlock(block); block.SetTexture(MaskId, mask); target.SetPropertyBlock(block);
            dirty = false;
        }

        void Release()
        {
            if (visualMesh != null && visualMesh.sharedMesh == probeMesh) visualMesh.sharedMesh = originalMesh;
            visualMesh = null; originalMesh = null;
            if (target != null && replacements.Count > 0)
            {
                var materials = target.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                    if (materials[i] != null && replacements.TryGetValue(materials[i], out var original)) materials[i] = original;
                target.sharedMaterials = materials;
                target.GetPropertyBlock(block);
                block.SetTexture(MaskId, Texture2D.blackTexture); target.SetPropertyBlock(block);
            }
            foreach (var material in replacements.Keys) DestroyOwned(material);
            replacements.Clear(); selectedSlots.Clear();
            DestroyOwned(probeObject); DestroyOwned(mask);
            probe = null; probeObject = null; mask = null; data = null; dirty = completed = false;
        }
        static void DestroyOwned(UnityEngine.Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj);
        }
    }
}
