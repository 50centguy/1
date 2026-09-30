using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace BorderRepair.Dock
{
    /// <summary>
    /// FBX 自定义属性（Blender 中写入的 dock_role、unity_open_deg 等）。
    /// 由编辑器导入后处理 Unit07DockAssetPostprocessor 在导入维修座 FBX 时逐对象写入，运行时只读。
    /// </summary>
    public class DockPartProperties : MonoBehaviour
    {
        [Serializable]
        public struct Entry
        {
            public string key;
            public string value;
        }

        [SerializeField] List<Entry> entries = new List<Entry>();

        public IReadOnlyList<Entry> Entries => entries;

        public void Set(string key, string value)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].key == key)
                {
                    entries[i] = new Entry { key = key, value = value };
                    return;
                }
            }
            entries.Add(new Entry { key = key, value = value });
        }

        public bool TryGetString(string key, out string value)
        {
            foreach (var e in entries)
            {
                if (e.key == key)
                {
                    value = e.value;
                    return true;
                }
            }
            value = null;
            return false;
        }

        public bool TryGetFloat(string key, out float value)
        {
            value = 0f;
            return TryGetString(key, out var s) && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        public string Role => TryGetString("dock_role", out var r) ? r : string.Empty;
    }
}
