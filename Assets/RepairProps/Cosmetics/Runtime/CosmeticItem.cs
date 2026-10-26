using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BorderRepair.RepairProps.Cosmetics
{
    public enum CosmeticMode { Off, Spray, Decal, Replace }

    public sealed class CosmeticItem : MonoBehaviour
    {
        public const int MaxActions=64;
        readonly List<CosmeticSurface> surfaces=new List<CosmeticSurface>();
        readonly List<Edit> history=new List<Edit>();
        int cursor;
        Edit stroke;
        public IReadOnlyList<CosmeticSurface> Surfaces=>surfaces;
        public bool CanUndo=>cursor>0;
        public bool CanRedo=>cursor<history.Count;
        public int ActionCount=>cursor;
        public bool AtLimit
        {
            get
            {
                int sinceClear=0;
                for(int i=cursor-1;i>=0&&!history[i].clear;i--) sinceClear++;
                return sinceClear>=MaxActions;
            }
        }
        sealed class Edit { public bool clear; public readonly List<Sample> samples=new List<Sample>(); }
        sealed class Sample
        {
            public CosmeticSurface surface;
            public Vector3 point,normal,up;
            public float radius,opacity;
            public Color color;
            public Texture image;
            public bool replace;
            public void Apply()
            {
                if(replace) surface.Replace(image);
                else surface.Paint(point,normal,up,radius,color,opacity,image);
            }
        }
        public void Configure(RepairPropFlow flow)
        {
            if(surfaces.Count>0) return;
            foreach(var renderer in GetComponentsInChildren<MeshRenderer>(true))
            {
                if(!Eligible(flow.Definition.kind,renderer.name)) continue;
                var filter=renderer.GetComponent<MeshFilter>();
                var materials=renderer.sharedMaterials;
                if(filter==null || filter.sharedMesh==null || materials.Length==0 || materials[0]==null ||
                    materials[0].shader.name!="Universal Render Pipeline/Lit" || !materials[0].HasProperty("_BaseMap")) continue;
                surfaces.Add(new CosmeticSurface(renderer,filter.sharedMesh,0));
            }
        }
        public static bool Eligible(RepairPropKind kind,string name)
        {
            switch(kind)
            {
                case RepairPropKind.Receiver: return name=="RX_FrontShell"||name=="RX_RearShell"||name=="RX_RearCover"||name=="RX_TopStrip";
                case RepairPropKind.Cartridge: return name=="MC_BaseTray"||name=="MC_TopCover";
                case RepairPropKind.Power: return name=="PS_ext_Shell_p21"||name=="PS_LeftServiceCover";
                case RepairPropKind.Omamori: return name=="OM_ext_Shell"||name=="OM_ext_Face"||name=="OM_RearCover";
                default: return false;
            }
        }
        public static int MaterialSlot(Mesh mesh,int triangleIndex)
        {
            if(mesh==null || triangleIndex<0) return -1;
            long remaining=triangleIndex;
            for(int i=0;i<mesh.subMeshCount;i++)
            {
                if(mesh.GetTopology(i)!=MeshTopology.Triangles) return -1;
                long count=mesh.GetIndexCount(i)/3;
                if(remaining<count) return i;
                remaining-=count;
            }
            return -1;
        }
        public CosmeticSurface SurfaceFor(RaycastHit hit)
        {
            if(!(hit.collider is MeshCollider collider) || collider.convex) return null;
            int slot=MaterialSlot(collider.sharedMesh,hit.triangleIndex);
            return surfaces.Find(s=>s.Renderer!=null && s.Renderer.gameObject==collider.gameObject &&
                s.Mesh==collider.sharedMesh && s.Slot==slot && s.Renderer.enabled && s.Renderer.gameObject.activeInHierarchy);
        }
        public bool BeginStroke()
        {
            EndStroke();
            if(AtLimit) return false;
            stroke=new Edit();return true;
        }
        public bool Stamp(RaycastHit hit,Color color,float radius,float opacity,float rotation,Texture decal)
        {
            var surface=SurfaceFor(hit);
            if(stroke==null || surface==null || stroke.samples.Count>=256 || !Finite(radius) || radius<.003f || radius>.04f ||
                !Finite(opacity)||opacity<=0||opacity>1||!Finite(rotation)) return false;
            Transform t=surface.Renderer.transform;
            Vector3 up=Vector3.ProjectOnPlane(Vector3.up,hit.normal).normalized;
            if(up.sqrMagnitude<.1f) up=Vector3.ProjectOnPlane(Vector3.forward,hit.normal).normalized;
            up=Quaternion.AngleAxis(rotation,hit.normal)*up;
            var sample=new Sample {surface=surface,point=t.InverseTransformPoint(hit.point),
                normal=t.localToWorldMatrix.transpose.MultiplyVector(hit.normal).normalized,
                up=t.InverseTransformDirection(up),color=color,radius=radius,opacity=opacity,image=decal};
            sample.Apply();stroke.samples.Add(sample);return true;
        }
        public void EndStroke()
        {
            if(stroke!=null && stroke.samples.Count>0) Commit(stroke);
            stroke=null;
        }
        public bool Replace(CosmeticSurface selected,Texture image,bool whole)
        {
            EndStroke();
            if(image==null||AtLimit||(!whole&&selected==null)) return false;
            var edit=new Edit();
            foreach(var surface in surfaces)
            {
                if(!whole&&surface!=selected) continue;
                var sample=new Sample {surface=surface,image=image,replace=true};
                sample.Apply();edit.samples.Add(sample);
            }
            if(edit.samples.Count==0) return false;
            Commit(edit);return true;
        }
        public void Clear()
        {
            EndStroke();
            // A clear is always reversible, including at the drawing limit.
            if(cursor==0) return;
            Commit(new Edit {clear=true});Replay();
        }
        public void Undo() {EndStroke();if(CanUndo){cursor--;Replay();}}
        public void Redo() {EndStroke();if(CanRedo){cursor++;Replay();}}
        void Commit(Edit edit)
        {
            if(cursor<history.Count) history.RemoveRange(cursor,history.Count-cursor);
            history.Add(edit);cursor=history.Count;
            if(history.Count>128)
            {
                int clear=history.FindIndex(e=>e.clear);
                if(clear>=0) {history.RemoveRange(0,clear+1);cursor=history.Count;}
            }
        }
        void Replay()
        {
            foreach(var surface in surfaces) surface.Reset();
            for(int i=0;i<cursor;i++)
            {
                if(history[i].clear) foreach(var surface in surfaces) surface.Reset();
                else foreach(var sample in history[i].samples) sample.Apply();
            }
        }
        static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
        void OnDestroy() {foreach(var surface in surfaces) surface.Dispose();surfaces.Clear();history.Clear();}
    }
}
