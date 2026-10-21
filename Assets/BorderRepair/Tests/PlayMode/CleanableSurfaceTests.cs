using System.Collections;
using System.Linq;
using BorderRepair.Cleaning;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BorderRepair.Tests
{
    public sealed class CleanableSurfaceTests
    {
        [UnityTest] public IEnumerator InstancesHaveIndependentMasks_EventsResetAndResourcesRestore()
        {
            var shader=Shader.Find("BorderRepair/DirtSurface"); Assert.IsNotNull(shader);
            var original=new Material(Shader.Find("Universal Render Pipeline/Simple Lit"));
            var template=new Material(shader);
            var mesh=new Mesh {vertices=new[] {new Vector3(-1,-1,0),new Vector3(1,-1,0),new Vector3(1,1,0),new Vector3(-1,1,0)},
                uv=new[] {Vector2.zero,Vector2.right,Vector2.one,Vector2.up},uv2=new[] {Vector2.zero,Vector2.right,Vector2.one,Vector2.up},triangles=new[] {0,2,1,0,3,2}};
            mesh.RecalculateNormals(); mesh.RecalculateTangents();
            GameObject Make(string name)
            {
                var g=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));
                g.GetComponent<MeshFilter>().sharedMesh=mesh; var r=g.GetComponent<Renderer>(); r.sharedMaterial=original;
                var block=new MaterialPropertyBlock(); block.SetColor("_BaseColor",Color.cyan); r.SetPropertyBlock(block);
                g.AddComponent<CleanableSurface>().Configure(r,mesh,template,32,Enumerable.Repeat((byte)255,1024).ToArray(),new[] {0},new[] {0,0});
                return g;
            }
            var first=Make("First cleanable"); var second=Make("Second cleanable");
            var a=first.GetComponent<CleanableSurface>(); var b=second.GetComponent<CleanableSurface>();
            Assert.AreNotSame(a.MaskTexture,b.MaskTexture);
            int completed=0; a.Cleaned+=()=>completed++;
            a.Wipe(new Vector2(.5f,.5f),.1f,1); yield return null;
            Assert.Greater(a.Progress,0); Assert.AreEqual(0,b.Progress);
            a.CleanAll(); a.CleanAll(); Assert.AreEqual(1,completed);
            a.ResetDirt(); Assert.AreEqual(0,a.Progress); a.CleanAll(); Assert.AreEqual(2,completed);
            var renderer=first.GetComponent<Renderer>(); var ownedMask=a.MaskTexture;
            Object.Destroy(a); yield return null;
            Assert.AreSame(original,renderer.sharedMaterial);
            var result=new MaterialPropertyBlock(); renderer.GetPropertyBlock(result); Assert.AreEqual(Color.cyan,result.GetColor("_BaseColor"));
            Assert.IsTrue(ownedMask==null);
            Object.Destroy(first); Object.Destroy(second); Object.Destroy(mesh); Object.Destroy(original); Object.Destroy(template);
            yield return null;
        }
    }
}
