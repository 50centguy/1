using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace WorkbenchArea
{
    /// <summary>Blender 写入的 FBX 自定义属性（wb_role、grab_point、hinge_axis_local 等），由导入后处理逐对象写入，运行时只读。</summary>
    public class WbPartProperties : MonoBehaviour
    {
        [Serializable]
        public struct Entry { public string key; public string value; }

        [SerializeField] List<Entry> entries = new List<Entry>();
        public IReadOnlyList<Entry> Entries => entries;

        public void Set(string key, string value)
        {
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].key == key) { entries[i] = new Entry { key = key, value = value }; return; }
            entries.Add(new Entry { key = key, value = value });
        }

        public string Get(string key)
        {
            foreach (var e in entries) if (e.key == key) return e.value;
            return null;
        }

        public bool TryGetFloat(string key, out float v)
        {
            v = 0f;
            var s = Get(key);
            return s != null && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        public string Role => Get("wb_role") ?? string.Empty;
    }
}
