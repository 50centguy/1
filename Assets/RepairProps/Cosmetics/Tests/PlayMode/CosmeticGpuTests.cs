using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BorderRepair.RepairProps.Cosmetics.Tests
{
    public sealed class CosmeticGpuTests
    {
        SyntheticCosmeticItem rig;

        [SetUp]
        public void SetUp()
        {
            CosmeticPixels.RequireShaders();
            rig = new SyntheticCosmeticItem();
        }

        [TearDown]
        public void TearDown() { rig?.Dispose(); rig = null; }

        [Test]
        public void Brush_DrawsRealGpuAlphaAtHitUv_WithoutChangingSourceTexturesOrOtherSlots()
        {
            var uv = new Vector2(.30f, .72f);
            var hit = rig.Hit(uv);
            Assert.That(hit.textureCoord.x, Is.EqualTo(uv.x).Within(.0001f));
            Assert.That(hit.textureCoord.y, Is.EqualTo(uv.y).Within(.0001f));
            Assert.That(hit.normal.z, Is.GreaterThan(.99f));
            Assert.That(rig.Flow.Session, Is.Null, "Synthetic surfaces need Configure, not a repair session.");
            Assert.That(rig.Item.Surfaces.Count, Is.EqualTo(1));
            Assert.That(rig.Surface.Layer, Is.Null, "An unedited slot must allocate no overlay.");

            rig.Paint(hit);

            var surface = rig.Surface;
            Assert.That(surface.Edited, Is.True);
            Assert.That(surface.RuntimeMaterial, Is.Not.SameAs(rig.Original));
            Assert.That(rig.Renderer.sharedMaterials[0], Is.SameAs(surface.RuntimeMaterial));
            Assert.That(surface.Layer.IsCreated(), Is.True);
            Assert.That(surface.Layer.width, Is.EqualTo(512));
            Assert.That(surface.Layer.height, Is.EqualTo(512));
            Assert.That(surface.Layer.format, Is.EqualTo(RenderTextureFormat.ARGB32));
            Assert.That(surface.RuntimeMaterial.GetTexture("_CosmeticLayer"), Is.SameAs(surface.Layer));
            Assert.That(surface.RuntimeMaterial.GetTexture("_BaseMap"), Is.SameAs(rig.OriginalBase));
            Assert.That(surface.RuntimeMaterial.GetTexture("_BumpMap"), Is.SameAs(rig.OriginalNormal));
            Assert.That(rig.OriginalBase.width, Is.EqualTo(1024), "The source must not be downsampled to the overlay size.");
            var pixels = CosmeticPixels.Read(surface.Layer);
            CosmeticPixels.AssertBrush(pixels, uv, .015f / SyntheticCosmeticItem.QuadSize);
            rig.AssertOriginalsIntact();
            CosmeticPixels.Capture(surface.Layer, "synthetic-brush");
        }

        [Test]
        public void DecalSamplesImageAlphaAndRotation_OnGpu_NotASolidSpray()
        {
            var image=rig.Texture(32,32,Color.clear);
            var pixels=new Color32[32*32];
            for(int y=0;y<32;y++) for(int x=0;x<32;x++)
                pixels[y*32+x]=x<16 ? new Color32(0,255,0,255) : new Color32(0,0,0,0);
            image.SetPixels32(pixels);image.Apply();
            var hit=rig.Hit(new Vector2(.5f,.5f));
            Assert.IsTrue(rig.Item.BeginStroke());
            Assert.IsTrue(rig.Item.Stamp(hit,Color.red,.02f,1,0,image));rig.Item.EndStroke();
            var layer=CosmeticPixels.Read(rig.Surface.Layer);
            Assert.Greater(layer[256*512+235].a,200);
            Assert.Less(layer[256*512+278].a,5);
            Assert.Greater(layer[256*512+235].g,200);
            rig.Item.Clear();
            Assert.IsTrue(rig.Item.BeginStroke());
            Assert.IsTrue(rig.Item.Stamp(hit,Color.red,.02f,1,180,image));rig.Item.EndStroke();
            var rotated=CosmeticPixels.Read(rig.Surface.Layer);
            Assert.Less(rotated[256*512+235].a,5);
            Assert.Greater(rotated[256*512+278].a,200);
            rig.AssertOriginalsIntact();
        }

        [Test]
        public void UndoRedo_ClearUndo_AndReplacement_RebindExactOriginalAndRetainGpuLayer()
        {
            rig.Paint(rig.Hit(new Vector2(.3f, .72f)));
            var surface = rig.Surface;
            var clone = surface.RuntimeMaterial;
            var layer = surface.Layer;
            var painted = CosmeticPixels.Read(layer);
            Assert.That(rig.Item.ActionCount, Is.EqualTo(1));
            rig.Item.Undo();
            AssertOriginalBound();
            CosmeticPixels.AssertClear(CosmeticPixels.Read(layer));
            Assert.That(surface.Layer, Is.SameAs(layer));
            Assert.That(surface.RuntimeMaterial, Is.SameAs(clone));
            Assert.That(rig.Item.CanRedo, Is.True);
            rig.Item.Redo();
            Assert.That(surface.Edited, Is.True);
            Assert.That(rig.Renderer.sharedMaterials[0], Is.SameAs(clone));
            CollectionAssert.AreEqual(painted, CosmeticPixels.Read(layer));

            var replacement = rig.Texture(64, 64, new Color32(15, 130, 220, 255));
            Assert.That(rig.Item.Replace(surface, replacement, false), Is.True);
            Assert.That(clone.GetTexture("_BaseMap"), Is.SameAs(replacement));
            CollectionAssert.AreEqual(painted, CosmeticPixels.Read(layer));
            rig.Item.Undo();
            Assert.That(clone.GetTexture("_BaseMap"), Is.SameAs(rig.OriginalBase));
            Assert.That(surface.Edited, Is.True, "Undoing replacement must replay the earlier paint.");
            rig.Item.Redo();
            Assert.That(clone.GetTexture("_BaseMap"), Is.SameAs(replacement));
            rig.Item.Clear();
            AssertOriginalBound();
            CosmeticPixels.AssertClear(CosmeticPixels.Read(layer));
            rig.Item.Undo();
            Assert.That(rig.Renderer.sharedMaterials[0], Is.SameAs(clone));
            Assert.That(clone.GetTexture("_BaseMap"), Is.SameAs(replacement));
            CollectionAssert.AreEqual(painted, CosmeticPixels.Read(layer));
            rig.Item.Redo();
            AssertOriginalBound();
            rig.Item.Undo();
            rig.Item.Undo();
            rig.Item.Undo();
            Assert.That(rig.Item.ActionCount, Is.Zero);
            AssertOriginalBound();
            rig.AssertOriginalsIntact();
        }

        [Test]
        public void InternalMaterialSlot_AndIneligibleScreenButtonCoreHits_RejectWithoutAllocation()
        {
            var internalHit = rig.Hit(new Vector2(.5f, .5f), true);
            Assert.That(CosmeticItem.MaterialSlot(rig.Mesh, internalHit.triangleIndex), Is.EqualTo(1));
            Assert.That(rig.Item.SurfaceFor(internalHit), Is.Null);
            Assert.That(rig.Item.BeginStroke(), Is.True);
            Assert.That(rig.Item.Stamp(internalHit, Color.red, .015f, 1, 0, null), Is.False);
            foreach (var name in new[] { "RX_Screen", "RX_PowerButton", "RX_Mainboard", "RX_FrontShell_Button" })
            {
                var blocker = rig.Root.GetComponentsInChildren<MeshCollider>(true).Single(c => c.name == name);
                Physics.SyncTransforms();
                Assert.That(blocker.Raycast(new Ray(blocker.transform.position + Vector3.forward, Vector3.back), out var hit, 2), Is.True, name);
                Assert.That(rig.Item.SurfaceFor(hit), Is.Null, name);
                Assert.That(rig.Item.Stamp(hit, Color.red, .015f, 1, 0, null), Is.False, name);
                blocker.gameObject.SetActive(false);
            }
            rig.Item.EndStroke();
            Assert.That(rig.Item.ActionCount, Is.Zero);
            Assert.That(rig.Item.CanUndo, Is.False);
            Assert.That(rig.Surface.Layer, Is.Null);
            AssertOriginalBound();
            rig.AssertOriginalsIntact();
        }

        [Test]
        public void BranchAfterUndo_DiscardsRedo_AndWholeReplacementNeverTouchesInternalSlot()
        {
            var first = rig.Texture(32, 32, new Color32(220, 30, 50, 255));
            var second = rig.Texture(32, 32, new Color32(20, 220, 80, 255));
            Assert.That(rig.Item.Replace(null, first, true), Is.True);
            rig.Item.Undo();
            AssertOriginalBound();
            Assert.That(rig.Item.Replace(null, second, true), Is.True);
            Assert.That(rig.Item.CanRedo, Is.False);
            Assert.That(rig.Surface.RuntimeMaterial.GetTexture("_BaseMap"), Is.SameAs(second));
            rig.Item.Clear();
            AssertOriginalBound();
            rig.Item.Undo();
            Assert.That(rig.Surface.RuntimeMaterial.GetTexture("_BaseMap"), Is.SameAs(second));
            rig.AssertOriginalsIntact();
        }

        void AssertOriginalBound()
        {
            Assert.That(rig.Renderer.sharedMaterials[0], Is.SameAs(rig.Original));
            Assert.That(rig.Surface.Edited, Is.False);
            rig.AssertOriginalsIntact();
        }
    }

    internal sealed class SyntheticCosmeticItem : IDisposable
    {
        public const float QuadSize = .2f;
        readonly List<Object> owned = new List<Object>();
        readonly Color32[] basePixels;
        readonly Color32[] normalPixels;
        readonly Color32[] internalPixels;
        public readonly GameObject Root;
        public readonly RepairPropFlow Flow;
        public readonly CosmeticItem Item;
        public readonly Mesh Mesh;
        public readonly MeshRenderer Renderer;
        public readonly MeshCollider Collider;
        public readonly Material Original;
        public readonly Material Internal;
        public readonly Texture2D OriginalBase;
        public readonly Texture2D OriginalNormal;
        public readonly Texture2D InternalBase;
        public CosmeticSurface Surface => Item.Surfaces[0];

        public SyntheticCosmeticItem()
        {
            Root = Own(new GameObject("CosmeticGpuFixture"));
            Root.transform.position = new Vector3(20, 20, 20);
            var definition = Own(ScriptableObject.CreateInstance<RepairPropFlowDefinition>());
            definition.kind = RepairPropKind.Receiver;
            Flow = Root.AddComponent<RepairPropFlow>();
            Flow.Configure(definition);
            OriginalBase = Texture(1024, 512, new Color32(31, 65, 99, 255));
            OriginalNormal = Texture(64, 64, new Color32(128, 128, 255, 255));
            InternalBase = Texture(16, 16, new Color32(13, 21, 34, 255));
            Original = Own(new Material(Shader.Find("Universal Render Pipeline/Lit")));
            Original.name = "Original_Exterior";
            Original.SetTexture("_BaseMap", OriginalBase);
            Original.SetTexture("_BumpMap", OriginalNormal);
            Original.SetFloat("_BumpScale", .6f);
            Original.EnableKeyword("_NORMALMAP");
            Internal = Own(new Material(Original));
            Internal.SetTexture("_BaseMap", InternalBase);
            Internal.name = "Original_Internal_Prohibited";
            Mesh = Own(new Mesh { name = "UV_Normal_TwoSlotQuad" });
            Mesh.vertices = new[] {
                new Vector3(-.1f,-.1f,0), new Vector3(.1f,-.1f,0), new Vector3(.1f,.1f,0), new Vector3(-.1f,.1f,0),
                new Vector3(.2f,-.1f,0), new Vector3(.4f,-.1f,0), new Vector3(.4f,.1f,0), new Vector3(.2f,.1f,0) };
            Mesh.normals = Enumerable.Repeat(Vector3.forward, 8).ToArray();
            Mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up, Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            Mesh.subMeshCount = 2;
            Mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            Mesh.SetTriangles(new[] { 4, 5, 6, 4, 6, 7 }, 1);
            Mesh.RecalculateBounds();
            Collider = AddQuad("RX_FrontShell", Vector3.zero);
            Renderer = Collider.GetComponent<MeshRenderer>();
            foreach (var name in new[] { "RX_Screen", "RX_PowerButton", "RX_Mainboard", "RX_FrontShell_Button" })
                AddQuad(name, Vector3.right * .6f);
            basePixels = OriginalBase.GetPixels32();
            normalPixels = OriginalNormal.GetPixels32();
            internalPixels = InternalBase.GetPixels32();
            Item = Root.AddComponent<CosmeticItem>();
            Item.Configure(Flow);
            Physics.SyncTransforms();
        }

        T Own<T>(T value) where T : Object { owned.Add(value); return value; }

        public Texture2D Texture(int width, int height, Color32 color)
        {
            var texture = Own(new Texture2D(width, height, TextureFormat.RGBA32, false, true));
            texture.SetPixels32(Enumerable.Repeat(color, width * height).ToArray());
            texture.Apply(false, false);
            return texture;
        }

        public MeshCollider AddQuad(string name, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(Root.transform, false);
            go.transform.localPosition = localPosition;
            go.AddComponent<MeshFilter>().sharedMesh = Mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = new[] { Original, Internal };
            var collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = Mesh;
            return collider;
        }

        public RaycastHit Hit(Vector2 uv, bool internalSlot = false)
        {
            var local = new Vector3((uv.x - .5f) * QuadSize + (internalSlot ? .3f : 0), (uv.y - .5f) * QuadSize, 0);
            Physics.SyncTransforms();
            var ray = new Ray(Collider.transform.TransformPoint(local) + Vector3.forward * .5f, Vector3.back);
            Assert.That(Collider.Raycast(ray, out var hit, 1), Is.True, "The fixture must use a real mesh raycast with UV and normal.");
            return hit;
        }

        public void Paint(RaycastHit hit)
        {
            Assert.That(Item.BeginStroke(), Is.True);
            Assert.That(Item.Stamp(hit, Color.red, .015f, .8f, 0, null), Is.True);
            Item.EndStroke();
        }

        public void AssertOriginalsIntact()
        {
            Assert.That(Renderer.sharedMaterials[1], Is.SameAs(Internal));
            Assert.That(Original.GetTexture("_BaseMap"), Is.SameAs(OriginalBase));
            Assert.That(Original.GetTexture("_BumpMap"), Is.SameAs(OriginalNormal));
            Assert.That(Internal.GetTexture("_BaseMap"), Is.SameAs(InternalBase));
            Assert.That(Internal.GetTexture("_BumpMap"), Is.SameAs(OriginalNormal));
            Assert.That(Original.GetFloat("_BumpScale"), Is.EqualTo(.6f));
            Assert.That(Original.IsKeywordEnabled("_NORMALMAP"), Is.True);
            CollectionAssert.AreEqual(basePixels, OriginalBase.GetPixels32());
            CollectionAssert.AreEqual(normalPixels, OriginalNormal.GetPixels32());
            CollectionAssert.AreEqual(internalPixels, InternalBase.GetPixels32());
            if (Surface.RuntimeMaterial != null)
                Assert.That(Surface.RuntimeMaterial.GetTexture("_BumpMap"), Is.SameAs(OriginalNormal));
        }

        public void Dispose()
        {
            // Dispose before destroying materials so the renderer can restore its original slot.
            if (Item != null) Object.DestroyImmediate(Item);
            for (int i = owned.Count - 1; i >= 0; i--)
                if (owned[i] != null) Object.DestroyImmediate(owned[i]);
            owned.Clear();
        }
    }

    internal static class CosmeticPixels
    {
        const string Evidence = "C:/Users/Administrator/Documents/Codex/2026-09-29/border-repair-station-art/work/repair-props-cosmetics-2026-10-18/Evidence";

        public static void RequireShaders()
        {
            foreach (var name in new[] { "RepairPropsCosmetics/CosmeticLit", "RepairPropsCosmetics/Stamp" })
            {
                var shader = Resources.Load<Shader>(name);
                Assert.That(shader, Is.Not.Null, name + " must be included as a resource.");
                Assert.That(shader.isSupported, Is.True, name + " must compile on the active graphics backend.");
            }
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            Assert.That(lit, Is.Not.Null);
            Assert.That(lit.isSupported, Is.True);
        }

        public static Color32[] Read(RenderTexture layer)
        {
            Assert.That(layer, Is.Not.Null);
            var previous = RenderTexture.active;
            var readback = new Texture2D(layer.width, layer.height, TextureFormat.RGBA32, false, true);
            try
            {
                RenderTexture.active = layer;
                readback.ReadPixels(new Rect(0, 0, layer.width, layer.height), 0, 0, false);
                readback.Apply(false, false);
                return readback.GetPixels32();
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(readback); }
        }

        public static void AssertClear(Color32[] pixels)
        {
            Assert.That(pixels.Max(p => (int)p.a), Is.Zero, "Every overlay pixel must be transparent.");
        }

        public static void AssertBrush(Color32[] pixels, Vector2 uv, float uvRadius)
        {
            const int size = 512;
            int x = Mathf.Clamp((int)(uv.x * size), 0, size - 1);
            int y = Mathf.Clamp((int)(uv.y * size), 0, size - 1);
            Assert.That(pixels[y * size + x].a, Is.GreaterThan(150), "Actual alpha must appear at the ray hit UV.");
            int painted = 0, stray = 0;
            for (int py = 0; py < size; py++)
                for (int px = 0; px < size; px++)
                {
                    if (pixels[py * size + px].a <= 1) continue;
                    painted++;
                    if (Vector2.Distance(new Vector2((px + .5f) / size, (py + .5f) / size), uv) > uvRadius + 2f / size) stray++;
                }
            Assert.That(painted, Is.GreaterThan(100));
            Assert.That(painted, Is.LessThan(size * size / 10), "A local brush must not fill the texture.");
            Assert.That(stray, Is.Zero, "No mirrored, flipped, or unrelated UV region may receive paint.");
        }

        public static void Capture(RenderTexture layer, string name)
        {
            if (Environment.GetEnvironmentVariable("BORDER_REPAIR_COSMETICS_CAPTURE") != "1") return;
            var texture = new Texture2D(layer.width, layer.height, TextureFormat.RGBA32, false, true);
            try
            {
                texture.SetPixels32(Read(layer));
                texture.Apply(false, false);
                Directory.CreateDirectory(Evidence);
                File.WriteAllBytes(Path.Combine(Evidence, name + ".png"), texture.EncodeToPNG());
            }
            finally { Object.DestroyImmediate(texture); }
        }
    }
}
