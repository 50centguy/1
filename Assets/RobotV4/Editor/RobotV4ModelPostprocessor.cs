using UnityEditor;
using UnityEngine;

/// <summary>
/// V4 机器人导入后处理（只作用于 Assets/RobotV4/ 下的 FBX）。
/// 问题：Unity 按 FBX 第一个动画栈（按名字排序是 Arm_Deploy_L）的第 0 帧取模型的静态姿态，
///       所以不播放动画时预制体的左臂是收起状态，与 Blender 静止姿态（两臂展开、左右对称）不一致。
/// 处理：导入时把 Idle_Hover 第 0 帧（= Blender 静止姿态）采样到模型上，作为预制体的静态姿态。
///       只改导入结果，不改 FBX、不改动作曲线。
/// </summary>
public class RobotV4ModelPostprocessor : AssetPostprocessor
{
    void OnPostprocessAnimation(GameObject root, AnimationClip clip)
    {
        if (!assetPath.StartsWith("Assets/RobotV4/")) return;
        if (clip.name != "Idle_Hover" && !clip.name.EndsWith("|Idle_Hover")) return;
        clip.SampleAnimation(root, 0f);
    }
}
