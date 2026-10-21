using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BorderRepair.RepairProps.Cosmetics
{
    // Owns one material slot only; base/normal/metallic assets are never rewritten or downsampled.
    public sealed class CosmeticSurface : IDisposable
    {
        public readonly Renderer Renderer;
        public readonly Mesh Mesh;
        public readonly int Slot;
        public readonly Material Original;
        public RenderTexture Layer { get; private set; }
        public Material RuntimeMaterial { get; private set; }
        public bool Edited => RuntimeMaterial != null && Renderer != null && Renderer.sharedMaterials[Slot]==RuntimeMaterial;
        Material stamp;
        public CosmeticSurface(Renderer renderer, Mesh mesh, int slot)
        { Renderer=renderer; Mesh=mesh; Slot=slot; Original=renderer.sharedMaterials[slot]; }

        void Initialize()
        {
            if (RuntimeMaterial != null) {Bind(RuntimeMaterial);return;}
            var shader=Resources.Load<Shader>("RepairPropsCosmetics/CosmeticLit");
            var brush=Resources.Load<Shader>("RepairPropsCosmetics/Stamp");
            if (shader==null || brush==null || !shader.isSupported || !brush.isSupported)
                throw new InvalidOperationException("Cosmetic shaders unavailable.");
            Layer=new RenderTexture(512,512,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear)
            { name="CosmeticLayer_Runtime",wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.DontSave };
            Layer.Create();
            ClearLayer();
            RuntimeMaterial=new Material(Original) {name=Original.name+"_Cosmetic_Runtime",hideFlags=HideFlags.DontSave};
            RuntimeMaterial.shader=shader;
            RuntimeMaterial.shaderKeywords=Original.shaderKeywords;
            RuntimeMaterial.SetTexture("_CosmeticLayer",Layer);
            stamp=new Material(brush) {hideFlags=HideFlags.DontSave};
            Bind(RuntimeMaterial);
        }

        public void Replace(Texture image)
        {
            Initialize();
            RuntimeMaterial.SetTexture("_BaseMap",image!=null ? image : Original.GetTexture("_BaseMap"));
        }

        public void Paint(Vector3 localPoint,Vector3 localNormal,Vector3 localUp,float radius,
            Color color,float opacity,Texture image)
        {
            Initialize();
            Transform t=Renderer.transform;
            Vector3 normal=t.localToWorldMatrix.inverse.transpose.MultiplyVector(localNormal).normalized;
            Vector3 up=t.TransformDirection(localUp).normalized;
            Vector3 right=Vector3.Cross(up,normal).normalized;
            up=Vector3.Cross(normal,right).normalized;
            var scale=Original.GetTextureScale("_BaseMap"); var offset=Original.GetTextureOffset("_BaseMap");
            stamp.SetVector("_UVTransform",new Vector4(scale.x,scale.y,offset.x,offset.y));
            stamp.SetVector("_Center",t.TransformPoint(localPoint)); stamp.SetVector("_Normal",normal);
            stamp.SetVector("_Right",right); stamp.SetVector("_Up",up);
            stamp.SetFloat("_Radius",radius); stamp.SetColor("_BrushColor",color.linear);
            stamp.SetFloat("_Opacity",opacity); stamp.SetFloat("_Decal",image!=null ? 1:0);
            stamp.SetTexture("_Stamp",image!=null ? image : Texture2D.whiteTexture);
            using(var commands=new CommandBuffer {name="Cosmetic stamp"})
            {
                var previous=RenderTexture.active;
                commands.SetRenderTarget(Layer);
                commands.SetViewport(new Rect(0,0,Layer.width,Layer.height));
                commands.DrawMesh(Mesh,t.localToWorldMatrix,stamp,Slot,0);
                try {Graphics.ExecuteCommandBuffer(commands);}
                finally {RenderTexture.active=previous;}
            }
        }

        public void Reset()
        {
            if(RuntimeMaterial==null) return;
            ClearLayer();
            RuntimeMaterial.SetTexture("_BaseMap",Original.GetTexture("_BaseMap"));
            Bind(Original);
        }
        void Bind(Material material)
        {
            if(Renderer==null) return;
            var slots=Renderer.sharedMaterials;
            if(slots[Slot]!=material) {slots[Slot]=material;Renderer.sharedMaterials=slots;}
        }
        void ClearLayer()
        {
            var active=RenderTexture.active;
            try {RenderTexture.active=Layer; GL.Clear(false,true,Color.clear);}
            finally {RenderTexture.active=active;}
        }
        public void Dispose()
        {
            if(Renderer!=null && RuntimeMaterial!=null)
            {
                var slots=Renderer.sharedMaterials;
                if(Slot<slots.Length && slots[Slot]==RuntimeMaterial) {slots[Slot]=Original;Renderer.sharedMaterials=slots;}
            }
            if(Layer!=null) {Layer.Release();Destroy(Layer);}
            Destroy(RuntimeMaterial);Destroy(stamp);
            Layer=null;RuntimeMaterial=null;stamp=null;
        }
        static void Destroy(UnityEngine.Object o)
        {if(o!=null) {if(Application.isPlaying) UnityEngine.Object.Destroy(o);else UnityEngine.Object.DestroyImmediate(o);}}
    }
}
