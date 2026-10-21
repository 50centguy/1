using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace BorderRepair.RepairProps.Cosmetics.Editor
{
    public static class CosmeticValidation
    {
        [MenuItem("Border Repair/Repair Props Cosmetics/Open Desktop Test Scene")]
        public static void Open()
        {
            if(EditorApplication.isPlaying || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene("Assets/RepairProps/Scenes/RepairProps_Clinic.unity");
        }
        [MenuItem("Border Repair/Repair Props Cosmetics/Audit Sources")]
        public static void Audit()
        {
            var report=new Report {imageFolder=CosmeticCatalog.Folder};
            var cosmeticShader=Resources.Load<Shader>("RepairPropsCosmetics/CosmeticLit");
            const string variantsPath="Assets/RepairProps/Cosmetics/Resources/RepairPropsCosmetics/RuntimeVariants.shadervariants";
            var variants=AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(variantsPath);
            bool create=variants==null;
            if(create) variants=new ShaderVariantCollection();
            variants.Clear();
            foreach(var kind in (RepairPropKind[])Enum.GetValues(typeof(RepairPropKind)))
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RepairProps/Prefabs/"+kind+".prefab");
                if(prefab==null) throw new InvalidOperationException("Missing prefab "+kind);
                foreach(var renderer in prefab.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if(!CosmeticItem.Eligible(kind,renderer.name)) continue;
                    var mesh=renderer.GetComponent<MeshFilter>().sharedMesh;
                    variants.Add(new ShaderVariantCollection.ShaderVariant(cosmeticShader,PassType.ScriptableRenderPipeline,
                        renderer.sharedMaterials[0].shaderKeywords));
                    report.surfaces.Add(new Surface {kind=kind.ToString(),node=renderer.name,slot=0,
                        material=renderer.sharedMaterials[0].name,meshReadable=mesh.isReadable,
                        baseSize=renderer.sharedMaterials[0].GetTexture("_BaseMap")!=null ? renderer.sharedMaterials[0].GetTexture("_BaseMap").width : 0});
                }
            }
            if(create) AssetDatabase.CreateAsset(variants,variantsPath);
            else EditorUtility.SetDirty(variants);
            AssetDatabase.SaveAssetIfDirty(variants);
            report.shaderVariants=variants.variantCount;
            foreach(var shader in new[]{Resources.Load<Shader>("RepairPropsCosmetics/CosmeticLit"),Resources.Load<Shader>("RepairPropsCosmetics/Stamp")})
            {
                if(shader==null || !shader.isSupported || ShaderUtil.GetShaderMessages(shader).Any(m=>m.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error))
                    throw new InvalidOperationException("Cosmetic shader missing or invalid: "+shader);
            }
            const string folder="C:/Users/Administrator/Documents/Codex/2026-09-29/border-repair-station-art/work/repair-props-cosmetics-2026-10-18/Evidence";
            Directory.CreateDirectory(folder);
            File.WriteAllText(folder+"/source_audit.json",JsonUtility.ToJson(report,true));
            Debug.Log("Cosmetic audit: "+report.surfaces.Count+" exact exterior slots; no source assets modified.");
        }
        [Serializable] sealed class Report {public string imageFolder;public int shaderVariants;public List<Surface> surfaces=new List<Surface>();}
        [Serializable] sealed class Surface {public string kind,node,material;public int slot,baseSize;public bool meshReadable;}
    }
}
