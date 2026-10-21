using UnityEditor;

namespace BorderRepair.RepairProps.EditorTools
{
    // The HUD's pixel font (Fusion Pixel 12px, OFL) keeps hard pixel edges: hinted raster rendering, dynamic glyphs.
    public sealed class CyberFontImport : AssetPostprocessor
    {
        void OnPreprocessAsset()
        {
            if (!assetPath.Contains("BorderRepairCyber/Fonts/FusionPixel")) return;
            if (assetImporter is TrueTypeFontImporter font)
            {
                font.fontRenderingMode = FontRenderingMode.HintedRaster;
                font.fontTextureCase = FontTextureCase.Dynamic;
            }
        }
    }
}
