using UnityEditor;
using UnityEngine;

namespace BorderRepair.RepairProps.EditorTools
{
    // One-shot project setting fix, kept so it can be re-applied: SurgeryProcedureRuntime builds its beam
    // material with Shader.Find("Universal Render Pipeline/Unlit"), and Shader.Find only resolves in a
    // player when the shader is either referenced by a built material or listed as always included.
    // Batch entry: -executeMethod BorderRepair.RepairProps.EditorTools.EnsureUnlitIncluded.RunBatch
    public static class EnsureUnlitIncluded
    {
        const string ShaderName = "Universal Render Pipeline/Unlit";

        public static void RunBatch()
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null) { Debug.LogError("[UnlitInclude] '" + ShaderName + "' is not available in this project."); return; }

            var settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (settings == null || settings.Length == 0) { Debug.LogError("[UnlitInclude] GraphicsSettings.asset could not be loaded."); return; }

            var serialized = new SerializedObject(settings[0]);
            var list = serialized.FindProperty("m_AlwaysIncludedShaders");
            if (list == null || !list.isArray) { Debug.LogError("[UnlitInclude] m_AlwaysIncludedShaders was not found."); return; }

            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader)
                {
                    Debug.Log("[UnlitInclude] already included: " + ShaderName);
                    return;
                }

            list.InsertArrayElementAtIndex(list.arraySize);
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Debug.Log("[UnlitInclude] added to always included shaders: " + ShaderName);
        }
    }
}
