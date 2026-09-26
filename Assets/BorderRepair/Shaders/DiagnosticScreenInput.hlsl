#ifndef BORDER_REPAIR_DIAGNOSTIC_SCREEN_INPUT_INCLUDED
#define BORDER_REPAIR_DIAGNOSTIC_SCREEN_INPUT_INCLUDED

// 所有 pass 共用同一个 UnityPerMaterial 常量缓冲（SRP Batcher 要求）；
// _BaseMap_ST / _BaseColor / _Cutoff 让 URP 自带的 ShadowCaster / DepthOnly / DepthNormals pass 可以直接复用。
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4 _BaseColor;
    half _Cutoff;
    half _BumpScale;
    float4 _ScreenRect;      // 屏幕面片在贴图集里烘焙的 UV 区域 (u0, v0, 宽, 高)：换算出面片自己的 0..1 坐标
    half4 _InkColor;         // 字的颜色（HDR）
    half4 _BackColor;        // 底色（背光下的暗色玻璃）
    half _InkThreshold;      // 贴图里“亮”到多少算字
    float4 _Grid;            // xy = 像素栅格格数（横、竖），z = 栅格强度，w = 格缝宽度
    float4 _Scan;            // x = 扫描线条数，y = 扫描线强度
    float4 _Backlight;       // x = 暗角强度，y = 不均匀强度，z = 整体亮度
    float4 _Switch;          // x = 切换后跳变持续时间（秒），y = 跳变强度
    float _SwitchTime;       // 最近一次切换读数的时间（_Time.y 同一时基）；由脚本通过属性块写入
CBUFFER_END

#endif
