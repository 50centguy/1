using System;
using System.Collections.Generic;
using BorderRepair.Data;
using BorderRepair.Inspection;
using UnityEditor;
using UnityEngine;

namespace BorderRepair.RepairProps.Editor
{
    public static class RepairPropCaseFactory
    {
        public const string DataPath = "Assets/RepairProps/Data";
        public const string ShiftPath = DataPath + "/RepairProps_Shift.asset";

        [MenuItem("Border Repair/Repair Props/Build Cases And Adapters")]
        public static void BuildAll() { BuildCases(); }

        // Flow and case data only: for catalog changes that add no model nodes, so the prefabs stay untouched.
        [MenuItem("Border Repair/Repair Props/Rebuild Flow Data Only")]
        public static void RebuildFlowData()
        {
            foreach (RepairPropKind kind in Enum.GetValues(typeof(RepairPropKind)))
            {
                var repairCase = AssetDatabase.LoadAssetAtPath<RepairCaseData>(DataPath + "/" + kind + "_Case.asset");
                var definition = AssetDatabase.LoadAssetAtPath<RepairPropFlowDefinition>(DataPath + "/" + kind + "_Flow.asset");
                if (repairCase == null || definition == null) throw new InvalidOperationException("Run Build Cases And Adapters first: " + kind);
                var prefab = repairCase.itemPrefab;
                var authored = RepairPropFlowCatalog.Create(kind, repairCase);
                var before = new HashSet<string>(definition.nodes.ConvertAll(n => n.objectName));
                if (!before.SetEquals(authored.nodes.ConvertAll(n => n.objectName)))
                    throw new InvalidOperationException(kind + ": model nodes changed; use Build Cases And Adapters.");
                EditorUtility.CopySerialized(authored, definition);
                UnityEngine.Object.DestroyImmediate(authored);
                repairCase.itemPrefab = prefab;
                var errors = new List<string>();
                definition.Validate(errors); repairCase.Validate(errors);
                if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
                EditorUtility.SetDirty(repairCase); EditorUtility.SetDirty(definition);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[RepairProps] Rebuilt flow and case data (prefabs untouched).");
        }

        // Assets: Data/{Receiver,Cartridge,Power,Omamori}_{Case,Flow}.asset and RepairProps_Shift.asset.
        // Parent creates/imports the four model prefabs first. Re-running updates assets in place, preserving GUIDs.
        public static RepairShiftData BuildCases()
        {
            EnsureFolder(DataPath);
            var prefabs = new Dictionary<RepairPropKind, GameObject>();
            foreach (RepairPropKind kind in Enum.GetValues(typeof(RepairPropKind)))
            {
                string path = "Assets/RepairProps/Prefabs/" + kind + ".prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) throw new InvalidOperationException("Build imported model prefab first: " + path);
                prefabs[kind] = prefab;
            }
            var shift = LoadOrCreate<RepairShiftData>(ShiftPath);
            shift.shiftTitle = "边境维修站：保留原件";
            shift.cases.Clear();
            foreach (RepairPropKind kind in Enum.GetValues(typeof(RepairPropKind)))
            {
                var repairCase = LoadOrCreate<RepairCaseData>(DataPath + "/" + kind + "_Case.asset");
                var authored = RepairPropFlowCatalog.Create(kind, repairCase);
                var definition = LoadOrCreate<RepairPropFlowDefinition>(DataPath + "/" + kind + "_Flow.asset");
                EditorUtility.CopySerialized(authored, definition);
                UnityEngine.Object.DestroyImmediate(authored);
                repairCase.itemPrefab = prefabs[kind];
                var errors = new List<string>();
                definition.Validate(errors); repairCase.Validate(errors);
                if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
                ConfigurePrefab(AssetDatabase.GetAssetPath(prefabs[kind]), definition);
                shift.cases.Add(repairCase);
                EditorUtility.SetDirty(repairCase); EditorUtility.SetDirty(definition);
            }
            EditorUtility.SetDirty(shift);
            AssetDatabase.SaveAssets();
            Debug.Log("[RepairProps] Built four cases, flow definitions, prefab adapters and " + ShiftPath);
            return shift;
        }

        public static void ConfigurePrefab(string path, RepairPropFlowDefinition definition)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var map = new Dictionary<string, Transform>();
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (map.ContainsKey(t.name)) throw new InvalidOperationException("Ambiguous model name: " + t.name);
                    map.Add(t.name, t);
                }
                if (!map.ContainsKey(definition.rootObjectName)) map.Add(definition.rootObjectName, root.transform);
                foreach (var n in definition.nodes)
                    if (!map.ContainsKey(n.objectName)) throw new InvalidOperationException("Missing actual model object: " + n.hierarchyPath);
                foreach (var n in definition.nodes)
                {
                    var target = map[n.objectName].gameObject;
                    var point = target.GetComponent<InspectionPoint>();
                    if (point == null) point = target.AddComponent<InspectionPoint>();
                    point.Configure(n.objectName, null, null);
                    if (n.movable)
                    {
                        var part = target.GetComponent<RepairPart>();
                        if (part == null) part = target.AddComponent<RepairPart>();
                        part.Configure(n.objectName, null, null, null, null, null, null);
                    }
                }
                var flow = root.GetComponent<RepairPropFlow>();
                if (flow == null) flow = root.AddComponent<RepairPropFlow>();
                flow.Configure(definition);
                if (root.GetComponent<RepairPropStationBridge>() == null) root.AddComponent<RepairPropStationBridge>();
                foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh == null || filter.GetComponent<Collider>() != null) continue;
                    if (filter.name.Contains("ScreenState") || filter.name.Contains("LightCurtain") || filter.name.Contains("StatusLED_")) continue;
                    filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
