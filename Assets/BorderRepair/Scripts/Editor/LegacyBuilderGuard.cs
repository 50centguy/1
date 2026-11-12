using UnityEditor;
using UnityEngine;

namespace BorderRepair.EditorTools
{
    /// <summary>
    /// RepairProps_Clinic is the one authoritative scene: the playtest build ships it alone and it is no longer
    /// regenerated from UnifiedClinic. The old builders still write prefabs, materials and meshes that scene uses,
    /// so clicking one of them in the menu asks first. Only the menu entries are guarded; code and -executeMethod
    /// callers run the builders directly, as before.
    /// </summary>
    public static class LegacyBuilderGuard
    {
        public const string AuthoritativeScene = "Assets/RepairProps/Scenes/RepairProps_Clinic.unity";

        public static bool Confirm(string builder)
        {
            if (Application.isBatchMode) return true;
            return EditorUtility.DisplayDialog(
                "旧构建器：" + builder,
                "这个旧构建器会重新生成 RepairProps_Clinic 正在使用的共享素材（预制体、材质或网格）。\n\n" +
                "RepairProps_Clinic 是试玩包唯一的场景，已不再从 UnifiedClinic 生成；重新生成可能改掉试玩画面。\n\n" +
                "确定继续吗？之后可以用 git 查看并还原改动。",
                "继续", "取消");
        }
    }
}
