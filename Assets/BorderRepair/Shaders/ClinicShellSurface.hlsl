#ifndef BORDER_REPAIR_CLINIC_SHELL_SURFACE_INCLUDED
#define BORDER_REPAIR_CLINIC_SHELL_SURFACE_INCLUDED

// 诊所外壳表面图层（只用于 ForwardLit）。需要先 include ClinicShellInput.hlsl。
//
// 约定：
// - 颜色运算在近似 sRGB（gamma 2.2）空间进行，这样"每层差值 ≥ 0.06 sRGB（约 2 个 PS2 量化级）"可以直接控制；
//   最后再转回线性交给 URP 光照。材质上的 Color 属性按 sRGB 填写（Unity 线性空间下会自动转线性，这里再转回）。
// - 所有边缘都是 smoothstep 硬阈值，过渡宽度 = 约 0.75 像素（按世界坐标导数算），不做长软渐变，
//   否则 PS2 的 Bayer 抖色会把渐变切成网点。
// - 所有导数（ddx/ddy）都在片元入口一次算好再传进来；本文件内不用隐式导数采样（只用 GRAD / LOD），
//   所以分支里不会出现梯度指令。
// - 污渍一律压低饱和度（PS2 的色度溢出会把彩色污渍摊开）。

#define SHELL_LUMA float3(0.2126, 0.7152, 0.0722)
#define SHELL_PI 3.14159265

struct ShellCtx
{
    float3 posWS;
    float3 nGeo;      // 几何法线（已归一化）
    float3 dPdx;
    float3 dPdy;
    float  pixelM;    // 每像素对应的世界长度（米）
};

struct ShellSurface
{
    float3 albedo;    // sRGB
    float  smoothness;
    float3 normalWS;
};

float3 ShellToGamma(float3 c)  { return pow(max(c, float3(0.0, 0.0, 0.0)), float3(0.45454545, 0.45454545, 0.45454545)); }
float3 ShellToLinear(float3 c) { return pow(max(c, float3(0.0, 0.0, 0.0)), float3(2.2, 2.2, 2.2)); }
float  ShellLuma(float3 c)     { return dot(c, SHELL_LUMA); }
// keep = 保留的色度比例（0 = 完全去饱和）
float3 ShellChroma(float3 c, float keep)
{
    float l = ShellLuma(c);
    return lerp(float3(l, l, l), c, keep);
}

// Dave Hoskins hash（无 sin，跨平台稳定）
float ShellHash(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.zyx + 31.32);
    return frac((p.x + p.y) * p.z);
}

// 每块板的第 k 个随机数
float ShellHashId(float3 id, float k)
{
    return ShellHash(id + float3(k * 17.13 + _Seed * 3.71, k * 5.37 + 0.5, k * 11.91 + _Seed));
}

// 值噪声，输出 0..1
float ShellNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = ShellHash(float3(i, _Seed + 0.25));
    float b = ShellHash(float3(i + float2(1.0, 0.0), _Seed + 0.25));
    float c = ShellHash(float3(i + float2(0.0, 1.0), _Seed + 0.25));
    float d = ShellHash(float3(i + float2(1.0, 1.0), _Seed + 0.25));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

// 硬阈值：x 低于 edge 时为 1（Below）/ 高于 edge 时为 1（Above）；aa 与 x 同单位
float ShellBelow(float x, float edge, float aa) { return 1.0 - smoothstep(edge - aa, edge + aa, x); }
float ShellAbove(float x, float edge, float aa) { return smoothstep(edge - aa, edge + aa, x); }

// 三档抖动：25% 偏暗 / 50% 不变 / 25% 偏亮。相邻板差值为 0 或 ≥ 1 档，不会出现肉眼看不出的微差。
float ShellLevel3(float h) { return h < 0.25 ? -1.0 : (h > 0.75 ? 1.0 : 0.0); }

// 正八边形（边法线朝 0°/45°/90°…）的"距中心距离"，内壁处 = 边心距
float ShellOctDist(float2 p)
{
    float2 a = abs(p);
    return max(max(a.x, a.y), (a.x + a.y) * 0.70710678);
}

// 贴图细节比例（逐通道，sRGB）：贴图 / 整张平铺均值。1 = 平均亮度。
// 均值取最高 mip（LOD 10 会被钳到 1x1）。
float3 ShellDetailRatio(float2 uv, float2 duvdx, float2 duvdy)
{
    float3 t = ShellToGamma(SAMPLE_TEXTURE2D_GRAD(_BaseMap, sampler_BaseMap, uv, duvdx, duvdy).rgb);
    float3 m = ShellToGamma(SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, uv, 10.0).rgb);
    return t / max(m, 0.02);
}

// ---------------------------------------------------------------------------------------------
// 地面：旧环氧涂层混凝土。现有 T_UC_Floor 是 0.6 m 铆接钢板（256 px / 1.2 m，板缝和铆钉位置确定），
// 这里只取它的高频成分（相对 LOD4 局部均值的亮度比）当轻微叠加，并按源图像素坐标把板缝/铆钉屏蔽掉；
// 低频成分（逐钢板明暗）完全丢弃，由 F1~F4 重新组织。
// ---------------------------------------------------------------------------------------------
void ShellFloor(ShellCtx c, inout ShellSurface o)
{
    float2 rel = c.posWS.xz - _RoomCenter.xz;
    float slab = max(_FloorSlabSize, 0.1);
    float2 g = rel / slab;
    float2 cell = floor(g);
    float2 f = g - cell;
    float3 id = float3(cell, 0.0);
    float aa = c.pixelM * 0.75;

    float hNew = ShellHashId(id, 0.0);
    float hVal = ShellHashId(id, 1.0);
    float hHue = ShellHashId(id, 2.0);
    float hMirror = ShellHashId(id, 3.0);
    float hSwap = ShellHashId(id, 4.0);
    float hOffU = ShellHashId(id, 5.0);
    float hOffV = ShellHashId(id, 6.0);
    float hTiltX = ShellHashId(id, 7.0);
    float hTiltZ = ShellHashId(id, 8.0);

    // --- 贴图高频（逐板镜像/转 90°/错半块，打散 1.2 m 重复；地面无方向性，可任意镜像）
    // （向量选择一律用 lerp(a, b, 0/1)，不用向量三元运算，兼容各平台编译器）
    float swap = step(0.5, hSwap);
    float2 uvL = f;
    uvL.x = hMirror > 0.5 ? 1.0 - uvL.x : uvL.x;
    uvL = lerp(uvL, uvL.yx, swap);
    uvL += floor(float2(hOffU, hOffV) * 2.0) * 0.5;      // 只错半块，板缝网格仍对齐（贴图 wrap=repeat）
    float2 duvdx = c.dPdx.xz / slab;
    float2 duvdy = c.dPdy.xz / slab;
    duvdx = lerp(duvdx, duvdx.yx, swap);
    duvdy = lerp(duvdy, duvdy.yx, swap);

    float3 tFull = ShellToGamma(SAMPLE_TEXTURE2D_GRAD(_BaseMap, sampler_BaseMap, uvL, duvdx, duvdy).rgb);
    float3 tLow = ShellToGamma(SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, uvL, 4.0).rgb);
    float hp = ShellLuma(tFull) / max(ShellLuma(tLow), 0.02);
    // 源图：每 128 px 一块钢板；板缝 = 板边 0..3 px，铆钉 = 距两条板边约 7..10 px。
    // 用到板边的对称距离，镜像/转向后同样成立。
    float2 tp = frac(uvL * 2.0) * 128.0;
    float2 e = min(tp, 128.0 - tp);
    float seamMask = step(min(e.x, e.y), 3.5);
    float2 rv = abs(e - 8.5);
    float rivetMask = step(max(rv.x, rv.y), 3.0);
    hp = lerp(hp, 1.0, max(seamMask, rivetMask));
    hp = clamp(hp, 0.65, 1.35);

    // --- F1：逐板值/色相抖动 + 约 1/10 新替换板
    float isNew = step(hNew, _FloorNewRatio);
    float3 col = lerp(ShellToGamma(_FloorColor.rgb), ShellToGamma(_FloorNewColor.rgb), isNew);
    col += ShellLevel3(hVal) * _FloorSlabJitter * (1.0 - isNew);
    float j = _FloorHueJitter;
    col *= lerp(float3(1.0 + j, 1.0, 1.0 - j), float3(1.0 - j, 1.0, 1.0 + j), hHue);
    col *= 1.0 + (hp - 1.0) * _FloorDetail * (1.0 - 0.5 * isNew);

    // --- F3：两个尺度的世界 XZ 噪声。2.5 m：环氧磨穿露出浅色混凝土；4 m：深色油污（两档）。新板不带旧痕。
    float old = 1.0 - isNew;
    float wearN = ShellNoise(rel / max(_FloorWearScale, 0.1) + 13.7);
    float worn = ShellAbove(wearN, 0.66, 0.012) * old;
    col = lerp(col, ShellChroma(col, 0.35) + _FloorWearLight, worn);

    float stainN = ShellNoise(rel / max(_FloorStainScale, 0.1) + 41.3) * 0.75 + ShellNoise(rel / 1.1 + 7.9) * 0.25;
    float stain = ShellAbove(stainN, 0.60, 0.012) * old;
    float deep = ShellAbove(stainN, 0.70, 0.012) * old;
    col = lerp(col, ShellChroma(col, 0.5) * (1.0 - _FloorStainDark), stain);
    col *= 1.0 - _FloorStainDark2 * deep;

    // --- 切缝（板边细暗线，远处按像素覆盖率淡出防摩尔纹）
    float2 eM = min(f, 1.0 - f) * slab;
    float joint = ShellBelow(min(eM.x, eM.y), _FloorJointWidth * 0.5, aa);
    joint *= saturate(_FloorJointWidth / max(2.0 * aa, 1e-4));
    col *= lerp(1.0, _FloorJointDark, joint);

    // --- F4：墙脚 0~0.25 m 解析压暗（八边形距离，两档硬边，边缘用噪声打乱）。房间外（走廊）不做。
    float d = _RoomApothem - ShellOctDist(rel);
    float inside = ShellAbove(d, -0.05, 0.01);
    float dj = d + (ShellNoise(rel * 3.0 + 3.1) - 0.5) * 0.06;
    float foot = (_FloorFootDark2 * ShellBelow(dj, 0.25, aa) + (_FloorFootDark - _FloorFootDark2) * ShellBelow(dj, 0.09, aa)) * inside;
    col = lerp(col, ShellChroma(col, 0.5), saturate(foot * 2.0));
    col *= 1.0 - foot;

    // --- 光泽：旧环氧半光，新板更亮，磨穿处哑光
    float sm = lerp(_FloorSmoothness, _FloorNewSmoothness, isNew);
    sm *= 1.0 - 0.55 * worn;
    sm *= 1.0 - 0.4 * saturate(foot * 3.0);

    // --- F2：逐板法线倾斜（只对朝上的面）
    float tilt = tan(radians(_FloorTiltDeg));
    float2 tl = (float2(hTiltX, hTiltZ) * 2.0 - 1.0) * tilt;
    float up = step(0.5, c.nGeo.y);
    o.normalWS = normalize(c.nGeo + float3(tl.x, 0.0, tl.y) * up);

    o.albedo = col;
    o.smoothness = sm;
}

// ---------------------------------------------------------------------------------------------
// 墙面：Y = 1.1 m 以下深绿半光漆（多刮痕），以上浅灰绿亚光，中间 3 cm 深色腰线。
// 竖直面用"沿墙切线距离 s + 高度 y"作板坐标，与八边形朝向无关；水平面（走廊顶等）退化为 XZ。
// 贴图只允许水平镜像（渗漏痕必须朝下）。
// ---------------------------------------------------------------------------------------------
void ShellWall(ShellCtx c, inout ShellSurface o)
{
    float2 rel = c.posWS.xz - _RoomCenter.xz;
    float y = c.posWS.y - _RoomCenter.y;
    float aa = c.pixelM * 0.75;

    float horiz = step(0.5, abs(c.nGeo.y));
    float vert = 1.0 - horiz;
    float2 nH = c.nGeo.xz;
    float nHLen = length(nH);
    float2 tdir = lerp(float2(-nH.y, nH.x) / max(nHLen, 1e-4), float2(1.0, 0.0), step(nHLen, 1e-4));
    float sector = horiz > 0.5 ? 8.0 : fmod(round(atan2(nH.y, nH.x + 1e-6) / (SHELL_PI * 0.25)) + 8.0, 8.0);

    float s = dot(rel, tdir);
    float2 q = lerp(float2(s, y), rel, horiz);
    float2 dqdx = lerp(float2(dot(c.dPdx.xz, tdir), c.dPdx.y), c.dPdx.xz, horiz);
    float2 dqdy = lerp(float2(dot(c.dPdy.xz, tdir), c.dPdy.y), c.dPdy.xz, horiz);

    float2 psz = float2(max(_WallPanelWidth, 0.1), max(_WallPanelHeight, 0.1));
    float2 g = q / psz;
    float2 cell = floor(g);
    float2 f = g - cell;
    float3 id = float3(cell, sector);

    float hVal = ShellHashId(id, 0.0);
    float hType = ShellHashId(id, 1.0);
    float hMirror = ShellHashId(id, 2.0);
    float hCol = ShellHashId(id, 3.0);
    float hBulge = ShellHashId(id, 4.0);
    float hLeak = ShellHashId(id, 5.0);
    float hPrimerVal = ShellHashId(id, 6.0);

    // --- 贴图细节（W7：锈去饱和 60%）。逐板错半块 + 只做水平镜像，竖直方向保持连续、不翻转。
    float uL = hMirror > 0.5 ? 1.0 - f.x : f.x;
    float2 uvT = float2(uL * psz.x / 1.2 + floor(hCol * 2.0) * 0.5, q.y / 1.2);
    float3 ratio = ShellDetailRatio(uvT, dqdx / 1.2, dqdy / 1.2);
    ratio = ShellChroma(ratio, _WallRustChroma);
    ratio = clamp(ratio, 0.35, 1.6);
    float3 detail = lerp(float3(1.0, 1.0, 1.0), ratio, _WallDetail);

    // --- W8 两道色 + W1 亮度目标（颜色本身即目标均值）
    float lower = ShellBelow(y, _WallSplitHeight, aa) * vert;
    float3 upperC = ShellToGamma(_WallUpperColor.rgb);
    float3 lowerC = ShellToGamma(_WallLowerColor.rgb);
    float3 zone = lerp(upperC, lowerC, lower);

    // --- W2 逐板抖动 + 约 1/8 底漆灰 + 约 1/12 补漆矩形
    float3 col = zone + ShellLevel3(hVal) * _WallPanelJitter;

    float isPrimer = step(hType, _WallPrimerRatio);
    float isRepaint = step(_WallPrimerRatio, hType) * step(hType, _WallPrimerRatio + _WallRepaintRatio);
    float2 rc = 0.30 + 0.40 * float2(ShellHashId(id, 7.0), ShellHashId(id, 8.0));
    float2 rh = float2(0.12 + 0.22 * ShellHashId(id, 9.0), 0.10 + 0.20 * ShellHashId(id, 10.0));
    float2 dm = (abs(f - rc) - rh) * psz;
    float rect = ShellBelow(max(dm.x, dm.y), 0.0, aa) * isRepaint;
    float sgn = ShellHashId(id, 11.0) > 0.5 ? 1.0 : -1.0;
    col = lerp(col, zone + sgn * _WallRepaintShift * float3(0.85, 1.0, 0.9), rect);

    float3 primerC = ShellToGamma(_WallPrimerColor.rgb) + ShellLevel3(hPrimerVal) * _WallPanelJitter * 0.5;
    col = lerp(col, primerC, isPrimer);

    // --- 腰线（底漆板上没有）
    float stripe = ShellBelow(abs(y - _WallSplitHeight), _WallStripeWidth * 0.5, aa) * vert * (1.0 - isPrimer);
    col = lerp(col, ShellToGamma(_WallStripeColor.rgb), stripe);

    // --- 下段刮痕：拉长的值噪声硬阈值，两组略有角度
    float sc1 = ShellNoise(float2(q.x * 5.0, q.y * 45.0) + sector * 7.3);
    float sc2 = ShellNoise(float2((q.x + q.y * 0.35) * 7.0, (q.y - q.x * 0.35) * 55.0) + sector * 3.1 + 50.0);
    float thr = 1.0 - _WallScratchAmount;
    float scratch = max(ShellAbove(sc1, thr, 0.01), ShellAbove(sc2, thr + 0.02, 0.01));
    scratch *= lower * (1.0 - isPrimer) * (1.0 - stripe);
    col = lerp(col, ShellToGamma(_WallScratchColor.rgb), scratch);

    col *= detail;

    // --- W6 约 25% 的板有渗漏痕：从板顶接缝往下，宽度逐渐收窄，硬边
    float isLeaky = step(hLeak, _WallLeakRatio) * vert;
    float dTop = (1.0 - f.y) * psz.y;
    float cx1 = 0.15 + 0.70 * ShellHashId(id, 12.0);
    float len1 = (0.35 + 0.55 * ShellHashId(id, 13.0)) * psz.y;
    float w1 = (0.012 + 0.018 * ShellHashId(id, 14.0)) * (1.0 - 0.7 * saturate(dTop / len1));
    float leak1 = ShellBelow(abs(f.x - cx1) * psz.x, w1, aa) * ShellBelow(dTop, len1, aa);
    float cx2 = 0.15 + 0.70 * ShellHashId(id, 15.0);
    float len2 = (0.25 + 0.50 * ShellHashId(id, 16.0)) * psz.y;
    float w2 = (0.010 + 0.012 * ShellHashId(id, 17.0)) * (1.0 - 0.7 * saturate(dTop / len2));
    float leak2 = ShellBelow(abs(f.x - cx2) * psz.x, w2, aa) * ShellBelow(dTop, len2, aa) * step(0.5, ShellHashId(id, 18.0));
    float leak = max(leak1, leak2) * isLeaky;
    col = lerp(col, ShellChroma(col, 0.5) * _WallLeakDark * float3(1.0, 0.97, 0.93), leak);

    // --- W4 墙脚污带 0~0.35 m（两档），W5 顶部积灰 2.5~3.0 m（两档）；边缘沿墙用噪声打乱
    float yf = y + (ShellNoise(float2(q.x * 4.0, sector * 5.1)) - 0.5) * 0.06;
    float foot = (_WallFootDark2 * ShellBelow(yf, 0.35, aa) + (_WallFootDark - _WallFootDark2) * ShellBelow(yf, 0.22, aa)) * vert;
    col = lerp(col, ShellChroma(col, 0.5), saturate(foot * 2.0));
    col *= 1.0 - foot;

    float yd = y + (ShellNoise(float2(q.x * 3.0 + 31.0, sector * 2.3)) - 0.5) * 0.08;
    float dust = (_WallDustDark * ShellAbove(yd, 2.5, aa) + _WallDustDark2 * ShellAbove(yd, 2.8, aa)) * vert;
    col = lerp(col, ShellChroma(col, 0.4), saturate(dust * 2.5));
    col *= 1.0 - dust;

    // --- 光泽：下段半光、上段亚光；底漆 / 刮痕 / 污渍处更哑
    float sm = lerp(_WallUpperSmoothness, _WallLowerSmoothness, lower);
    sm = lerp(sm, 0.10, isPrimer);
    sm = lerp(sm, 0.15, scratch);
    sm *= 1.0 - 0.5 * saturate(foot * 3.0 + dust * 2.0 + leak);

    // --- W3 逐板鼓面：板边约 12% 宽的斜面，法线外倾 3~5°（硬边，不做整板渐变）
    float bulge = tan(radians(lerp(_WallBulgeMinDeg, _WallBulgeMaxDeg, hBulge)));
    float2 a = f * 2.0 - 1.0;
    float2 aaL = aa / (psz * 0.5);
    float2 bev = smoothstep(0.76 - aaL, 0.76 + aaL, a) - smoothstep(0.76 - aaL, 0.76 + aaL, -a);
    float3 t3 = float3(tdir.x, 0.0, tdir.y);
    float3 nW = c.nGeo + (t3 * bev.x + float3(0.0, 1.0, 0.0) * bev.y) * bulge;
    o.normalWS = normalize(lerp(c.nGeo, nW, vert));

    o.albedo = col;
    o.smoothness = sm;
}

// ---------------------------------------------------------------------------------------------
// 天花板：贴图去饱和 80%（锈点只剩极淡的灰斑），亮度约墙面 0.8×，逐板抖动 + 烟熏噪声（两档硬阈值）。
// ---------------------------------------------------------------------------------------------
void ShellCeiling(ShellCtx c, inout ShellSurface o)
{
    float2 rel = c.posWS.xz - _RoomCenter.xz;
    float pw = max(_WallPanelWidth, 0.1);
    float2 psz = float2(pw, pw);
    float2 g = rel / psz;
    float2 cell = floor(g);
    float2 f = g - cell;
    float3 id = float3(cell, 9.0);

    float hVal = ShellHashId(id, 0.0);
    float hMx = ShellHashId(id, 1.0);
    float hMy = ShellHashId(id, 2.0);
    float hOff = ShellHashId(id, 3.0);

    float2 fl = float2(hMx > 0.5 ? 1.0 - f.x : f.x, hMy > 0.5 ? 1.0 - f.y : f.y);
    float2 uvT = fl * psz / 1.2 + floor(float2(hOff, frac(hOff * 7.0)) * 2.0) * 0.5;
    float3 ratio = ShellDetailRatio(uvT, c.dPdx.xz / 1.2, c.dPdy.xz / 1.2);
    ratio = ShellChroma(ratio, _CeilChroma);
    ratio = clamp(ratio, 0.8, 1.2);
    float3 detail = lerp(float3(1.0, 1.0, 1.0), ratio, _CeilDetail);

    float3 col = ShellToGamma(_CeilColor.rgb) + ShellLevel3(hVal) * _CeilPanelJitter;
    col *= detail;

    float smoke = ShellNoise(rel / max(_CeilSmokeScaleA, 0.1) + 5.3) * 0.65 + ShellNoise(rel / max(_CeilSmokeScaleB, 0.1) + 17.1) * 0.35;
    float s1 = ShellAbove(smoke, 0.56, 0.012);
    float s2 = ShellAbove(smoke, 0.68, 0.012);
    col = lerp(col, ShellChroma(col, 0.3), s1);
    col *= 1.0 - _CeilSmokeDark * s1 - _CeilSmokeDark2 * s2;

    o.albedo = col;
    o.smoothness = _CeilSmoothness;
    o.normalWS = c.nGeo;
}

#endif
