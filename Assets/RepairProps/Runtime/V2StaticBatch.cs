using System;
using System.Linq;
using UnityEngine;

namespace BorderRepair.RepairProps
{
    // Runtime static batching for the V2 clinic assets: every mesh under `root` that never moves is combined (per
    // material) into the root's batch, so the baked atlas turns dozens of objects into a few draws. Anything under a
    // transform `movable` accepts (drawers, doors, tools, posable pads, arms) stays a separate renderer.
    public static class V2StaticBatch
    {
        public static int Combine(GameObject root, Func<Transform, bool> movable)
        {
            var statics = root.GetComponentsInChildren<MeshRenderer>(true)
                .Where(r => r.GetComponent<MeshFilter>() != null && r.GetComponent<MeshFilter>().sharedMesh != null
                    && r.GetComponent<MeshFilter>().sharedMesh.isReadable && !UnderMovable(r.transform, root.transform, movable))
                .Select(r => r.gameObject).ToArray();
            if (statics.Length > 1) StaticBatchingUtility.Combine(statics, root);
            return statics.Length;
        }

        static bool UnderMovable(Transform t, Transform root, Func<Transform, bool> movable)
        {
            for (var p = t; p != null && p != root; p = p.parent)
                if (movable(p)) return true;
            return false;
        }
    }
}
