using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BorderRepair.Cleaning;
using BorderRepair.TwoNight.EditorTools;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace BorderRepair.Art.UnifiedClinic.EditorTools
{
    public static class ClinicCleaningInstaller
    {
        public const string Root = "Assets/BorderRepair/Cleaning";
        public const string TemplatePath = Root + "/DirtSurface_Template.mat";
        const int Resolution = 128;
        sealed class SurfaceBake
        {
            public CleanableSurface surface;
            public Mesh mesh;
            public byte[] coverage;
            public int[] slots, triangles;
        }
        public static readonly string[] Prefabs = {
            "Assets/BorderRepair/Prefabs/Items/Item_Communicator_Final.prefab",
            "Assets/BorderRepair/Prefabs/Items/Item_NavBeacon_Final.prefab",
            "Assets/BorderRepair/Prefabs/Items/Item_SalvageDrone_Final.prefab"
        };

        [MenuItem("Border Repair/Cleaning/Install Customer Item Cleaning")]
        public static void Install()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before installing cleaning.");
            Directory.CreateDirectory(Root + "/Meshes"); AssetDatabase.Refresh();
            var shader = Shader.Find("BorderRepair/DirtSurface");
            if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("DirtSurface shader is missing or does not compile.");
            var template = AssetDatabase.LoadAssetAtPath<Material>(TemplatePath);
            if (template == null) { template = new Material(shader) { name = "DirtSurface_Template" }; AssetDatabase.CreateAsset(template,TemplatePath); }
            var reports = new List<string>();
            foreach (string path in Prefabs)
            {
                var prefab = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    int count = 0;
                    var bakes = new List<SurfaceBake>();
                    foreach (var renderer in prefab.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        var mf = renderer.GetComponent<MeshFilter>();
                        if (mf == null || mf.sharedMesh == null) continue;
                        var slots = renderer.sharedMaterials.Select((m,i) => (m,i)).Where(p => IsPaintable(p.m)).Select(p => p.i).ToArray();
                        string description = (renderer.name + " " + string.Join(" ",slots.Select(i => renderer.sharedMaterials[i].name))).ToLowerInvariant();
                        if (slots.Length == 0 || !new[] {"body","shell","housing","paint","enamel","case"}.Any(description.Contains))
                        {
                            // Other opaque parts must block cleaning; convex gameplay proxies are too approximate for this.
                            if (renderer.sharedMaterials.Any(m => m != null && m.renderQueue <= 2500))
                            {
                                var occluder = renderer.GetComponent<CleaningOccluder>();
                                if (occluder == null) occluder = renderer.gameObject.AddComponent<CleaningOccluder>();
                                occluder.Configure(renderer,ReadableCopy(mf.sharedMesh,false));
                            }
                            continue;
                        }
                        var readable = ReadableCopy(mf.sharedMesh);
                        var coverage = Coverage(readable,slots,Resolution,out var triangles);
                        if (!coverage.Any(p => p > 0)) { reports.Add("Skipped no UV coverage: " + renderer.name); continue; }
                        var surface = renderer.GetComponent<CleanableSurface>();
                        if (surface == null) surface = renderer.gameObject.AddComponent<CleanableSurface>();
                        surface.Configure(renderer,readable,template,Resolution,coverage,slots,triangles);
                        surface.SetDirtAppearance(new Color(.32f,.215f,.105f,1),.95f,.07f);
                        bakes.Add(new SurfaceBake {surface=surface,mesh=readable,coverage=coverage,slots=slots,triangles=triangles});
                        count++;
                    }
                    if (count == 0) throw new InvalidOperationException("No suitable body surfaces in " + path + ": " +
                        string.Join("; ",prefab.GetComponentsInChildren<MeshRenderer>(true).Select(r => r.name + " [" +
                        string.Join(",",r.sharedMaterials.Where(m => m != null).Select(m => m.name + "/" + m.shader.name)) + "]")));
                    BakeExposedCoverage(prefab,bakes);
                    foreach(var bake in bakes)
                    {
                        if(!bake.coverage.Any(p=>p>0)) { reports.Add(path+" | hidden part skipped: "+bake.surface.name); UnityEngine.Object.DestroyImmediate(bake.surface); continue; }
                        bake.surface.Configure(bake.surface.Target,bake.mesh,template,Resolution,bake.coverage,bake.slots,bake.triangles);
                        reports.Add(path + " | " + bake.surface.Target.name + " | material slots " + string.Join(",",bake.slots) + " | exposed texels " + bake.coverage.Count(p => p > 0));
                    }
                    PrefabUtility.SaveAsPrefabAsset(prefab,path);
                }
                finally { PrefabUtility.UnloadPrefabContents(prefab); }
            }
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Docs/Integration/UnifiedClinic/Cleaning");
            File.WriteAllText("Docs/Integration/UnifiedClinic/Cleaning/installed.txt",DateTime.UtcNow.ToString("O")+"\n"+string.Join("\n",reports));
            Debug.Log("[ClinicCleaning] " + string.Join("\n",reports));
        }

        public static void InstallBatch()
        {
            try { Install(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        [MenuItem("Border Repair/Cleaning/Build Preview Player")]
        public static void BuildPlayer()
        {
            const string output = "Builds/UnifiedClinic-20261011-Cleaning";
            Directory.CreateDirectory(output);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { UnifiedClinicBuilder.MenuPath, UnifiedClinicBuilder.ClinicPath },
                locationPathName = output + "/BorderRepairClinic.exe", target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });
            Directory.CreateDirectory("Docs/Integration/UnifiedClinic/Cleaning");
            File.WriteAllText("Docs/Integration/UnifiedClinic/Cleaning/player_build.txt", $"{DateTime.UtcNow:O}\nResult: {report.summary.result}\n" +
                $"Errors: {report.summary.totalErrors}\nWarnings: {report.summary.totalWarnings}\nPath: {report.summary.outputPath}\n");
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Cleaning preview build failed.");
        }

        public static void BuildPlayerBatch()
        {
            try { BuildPlayer(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        static bool IsPaintable(Material material)
        {
            if (material == null || material.shader == null || material.renderQueue > 2500) return false;
            string name=material.name.ToLowerInvariant();
            if (new[] {"screen","label","seal","paper","pcb","mainboard","glass","led","text"}.Any(name.Contains)) return false;
            return material.shader.name == "BorderRepair/WornSurface" || material.shader.name == "Universal Render Pipeline/Lit" ||
                material.shader.name == "Universal Render Pipeline/Simple Lit";
        }

        static void BakeExposedCoverage(GameObject prefab,List<SurfaceBake> surfaces)
        {
            // An isolated preview scene also works when the editor's current scene is untitled/unsaved.
            var bakeScene=EditorSceneManager.NewPreviewScene();
            var root=new GameObject("CleaningVisibilityBake_Temporary") {hideFlags=HideFlags.HideAndDontSave};
            SceneManager.MoveGameObjectToScene(root,bakeScene);
            var probes=new List<MeshCollider>();
            try
            {
                void Probe(Renderer renderer,Mesh mesh)
                {
                    if(renderer==null || mesh==null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) return;
                    var obj=new GameObject("Visibility probe") {hideFlags=HideFlags.HideAndDontSave};
                    obj.transform.SetParent(root.transform,false);
                    obj.transform.SetPositionAndRotation(renderer.transform.position,renderer.transform.rotation);
                    obj.transform.localScale=renderer.transform.lossyScale;
                    var collider=obj.AddComponent<MeshCollider>(); collider.sharedMesh=mesh; probes.Add(collider);
                }
                foreach(var surface in surfaces) Probe(surface.surface.Target,surface.mesh);
                foreach(var occluder in prefab.GetComponentsInChildren<CleaningOccluder>(true)) Probe(occluder.Target,occluder.ProbeMesh);
                Physics.SyncTransforms();
                foreach(var bake in surfaces)
                {
                    var result=new byte[bake.coverage.Length];
                    var positions=bake.mesh.vertices; var uv=bake.mesh.uv2;
                    var transform=bake.surface.Target.transform;
                    var normalMatrix=transform.localToWorldMatrix.inverse.transpose;
                    float reach=Mathf.Max(.02f,prefab.GetComponentsInChildren<Renderer>().Max(r=>r.bounds.size.magnitude)*2);
                    float epsilon=reach*.00005f;
                    foreach(int slot in bake.slots)
                    {
                        var indices=bake.mesh.GetTriangles(slot);
                        for(int i=0;i<indices.Length;i+=3)
                        {
                            int ia=indices[i],ib=indices[i+1],ic=indices[i+2];
                            Vector2 a=uv[ia],b=uv[ib],c=uv[ic];
                            float Cross(Vector2 v,Vector2 w)=>v.x*w.y-v.y*w.x;
                            float area=Cross(b-a,c-a); if(Mathf.Abs(area)<1e-8f) continue;
                            Vector3 rawNormal=normalMatrix.MultiplyVector(Vector3.Cross(positions[ib]-positions[ia],positions[ic]-positions[ia]));
                            // Vector3.normalized returns zero for the very small authored model units; normalize explicitly.
                            float normalLengthSquared=rawNormal.sqrMagnitude;
                            if(!float.IsFinite(normalLengthSquared) || normalLengthSquared<1e-24f) continue;
                            Vector3 normal=rawNormal/Mathf.Sqrt(normalLengthSquared);
                            int minX=Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x,b.x,c.x)*Resolution),0,Resolution-1);
                            int maxX=Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x,b.x,c.x)*Resolution),0,Resolution-1);
                            int minY=Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y,b.y,c.y)*Resolution),0,Resolution-1);
                            int maxY=Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.y,b.y,c.y)*Resolution),0,Resolution-1);
                            for(int y=minY;y<=maxY;y++) for(int x=minX;x<=maxX;x++)
                            {
                                int pixel=y*Resolution+x; if(bake.coverage[pixel]==0 || result[pixel]!=0) continue;
                                Vector2 p=new Vector2((x+.5f)/Resolution,(y+.5f)/Resolution);
                                float u=Cross(b-p,c-p)/area,v=Cross(c-p,a-p)/area;
                                if(u<0 || v<0 || u+v>1.00001f) continue;
                                Vector3 point=transform.TransformPoint(positions[ia]*u+positions[ib]*v+positions[ic]*(1-u-v));
                                var ray=new Ray(point+normal*epsilon,normal);
                                if(!probes.Any(probe=>probe.Raycast(ray,out _,reach))) result[pixel]=255;
                            }
                        }
                    }
                    bake.coverage=result;
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(bakeScene);
            }
        }

        static Mesh ReadableCopy(Mesh original,bool cleaningUv = true)
        {
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(original,out string guid,out long localId);
            string path=Root+"/Meshes/"+original.name+"_"+guid.Substring(0,8)+"_"+localId+(cleaningUv?"":"_Occluder")+".asset";
            var cached=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            using var snapshot=MeshUtility.AcquireReadOnlyMeshData(original);
            var data=snapshot[0];
            if (cleaningUv && !data.HasVertexAttribute(VertexAttribute.TexCoord0)) throw new InvalidOperationException("Mesh has no UV0: "+original.name);
            var mesh=new Mesh {name=Path.GetFileNameWithoutExtension(path), indexFormat=original.indexFormat};
            using (var values=new NativeArray<Vector3>(data.vertexCount,Allocator.Temp)) { data.GetVertices(values); mesh.SetVertices(values); }
            if (data.HasVertexAttribute(VertexAttribute.Normal))
                using (var values=new NativeArray<Vector3>(data.vertexCount,Allocator.Temp)) { data.GetNormals(values); mesh.SetNormals(values); }
            if (data.HasVertexAttribute(VertexAttribute.Tangent))
                using (var values=new NativeArray<Vector4>(data.vertexCount,Allocator.Temp)) { data.GetTangents(values); mesh.SetTangents(values); }
            if (data.HasVertexAttribute(VertexAttribute.TexCoord0))
                using (var values=new NativeArray<Vector2>(data.vertexCount,Allocator.Temp)) { data.GetUVs(0,values); mesh.SetUVs(0,values); }
            mesh.subMeshCount=data.subMeshCount;
            for(int sub=0;sub<data.subMeshCount;sub++)
            {
                var descriptor=data.GetSubMesh(sub);
                using var indices=new NativeArray<int>(descriptor.indexCount,Allocator.Temp);
                data.GetIndices(indices,sub);
                mesh.SetIndices(indices,descriptor.topology,sub,false);
            }
            if (!data.HasVertexAttribute(VertexAttribute.Normal)) mesh.RecalculateNormals();
            if (!data.HasVertexAttribute(VertexAttribute.Tangent)) mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            if (cleaningUv && !Unwrapping.GenerateSecondaryUVSet(mesh)) throw new InvalidOperationException("Cannot generate unique cleaning UV1: " + original.name);
            if (cached == null) { AssetDatabase.CreateAsset(mesh,path); return mesh; }
            EditorUtility.CopySerialized(mesh,cached); EditorUtility.SetDirty(cached);
            UnityEngine.Object.DestroyImmediate(mesh); return cached;
        }

        public static byte[] Coverage(Mesh mesh,int[] materialSlots,int resolution,out int[] triangleSlots)
        {
            var result=new byte[resolution*resolution];
            var slots=new HashSet<int>(materialSlots); var triangleMap=new List<int>();
            var uv=mesh.uv2;
            for(int sub=0;sub<mesh.subMeshCount;sub++)
            {
                if (mesh.GetTopology(sub) != MeshTopology.Triangles) throw new InvalidOperationException("Cleaning requires triangle meshes.");
                var indices=mesh.GetTriangles(sub);
                for(int i=0;i<indices.Length;i+=3)
                {
                    triangleMap.Add(sub);
                    if (!slots.Contains(sub)) continue;
                    Vector2 a=uv[indices[i]],b=uv[indices[i+1]],c=uv[indices[i+2]];
                    if (!float.IsFinite(a.x+a.y+b.x+b.y+c.x+c.y)) continue;
                    int minX=Mathf.FloorToInt(Mathf.Min(a.x,b.x,c.x)),maxX=Mathf.FloorToInt(Mathf.Max(a.x,b.x,c.x));
                    int minY=Mathf.FloorToInt(Mathf.Min(a.y,b.y,c.y)),maxY=Mathf.FloorToInt(Mathf.Max(a.y,b.y,c.y));
                    if (maxX-minX>16 || maxY-minY>16) throw new InvalidOperationException("UV tiling exceeds supported cleaning atlas range.");
                    for(int ty=minY;ty<=maxY;ty++) for(int tx=minX;tx<=maxX;tx++)
                        Raster(a-new Vector2(tx,ty),b-new Vector2(tx,ty),c-new Vector2(tx,ty),result,resolution);
                }
            }
            triangleSlots=triangleMap.ToArray(); return result;
        }

        static void Raster(Vector2 a,Vector2 b,Vector2 c,byte[] mask,int size)
        {
            float Cross(Vector2 v,Vector2 w)=>v.x*w.y-v.y*w.x;
            float area=Cross(b-a,c-a); if (Mathf.Abs(area)<1e-8f) return;
            int minX=Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x,b.x,c.x)*size),0,size-1);
            int maxX=Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x,b.x,c.x)*size),0,size-1);
            int minY=Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y,b.y,c.y)*size),0,size-1);
            int maxY=Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.y,b.y,c.y)*size),0,size-1);
            for(int y=minY;y<=maxY;y++) for(int x=minX;x<=maxX;x++)
            {
                var p=new Vector2((x+.5f)/size,(y+.5f)/size);
                float u=Cross(b-p,c-p)/area,v=Cross(c-p,a-p)/area;
                if (u>=0 && v>=0 && u+v<=1.00001f) mask[y*size+x]=255;
            }
        }

        [MenuItem("Border Repair/Cleaning/Remove Customer Item Cleaning")]
        public static void Uninstall()
        {
            foreach(string path in Prefabs)
            {
                var prefab=PrefabUtility.LoadPrefabContents(path);
                try
                {
                    foreach(var surface in prefab.GetComponentsInChildren<CleanableSurface>(true)) UnityEngine.Object.DestroyImmediate(surface);
                    foreach(var occluder in prefab.GetComponentsInChildren<CleaningOccluder>(true)) UnityEngine.Object.DestroyImmediate(occluder);
                    PrefabUtility.SaveAsPrefabAsset(prefab,path);
                }
                finally { PrefabUtility.UnloadPrefabContents(prefab); }
            }
            AssetDatabase.SaveAssets(); // Keep generated readable copies for recovery/reuse.
        }
    }
}
