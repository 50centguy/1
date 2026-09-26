using System.Collections.Generic;
using UnityEngine;

namespace BorderRepair.UI
{
    /// <summary>运行时选一个本机已安装的中文字体；找不到时保留原字体。</summary>
    public static class UIFontProvider
    {
        static readonly string[] Candidates =
        {
            "Microsoft YaHei UI", "Microsoft YaHei", "PingFang SC", "Noto Sans CJK SC",
            "Source Han Sans SC", "SimHei", "WenQuanYi Micro Hei", "Arial Unicode MS"
        };

        static Font cached;
        static bool searched;

        public static Font GetCjkFont(Font fallback)
        {
            if (!searched)
            {
                searched = true;
                try
                {
                    var installed = new HashSet<string>(Font.GetOSInstalledFontNames());
                    foreach (var name in Candidates)
                    {
                        if (!installed.Contains(name)) continue;
                        cached = Font.CreateDynamicFontFromOSFont(name, 32);
                        break;
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[BorderRepair] 查找系统中文字体失败，使用默认字体：{e.Message}");
                }
            }
            return cached != null ? cached : fallback;
        }
    }
}
