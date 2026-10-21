using System;
using System.Collections.Generic;
using BorderRepair.Core;
using BorderRepair.Data;
using UnityEngine;

namespace BorderRepair.RepairProps.Tests
{
    // Synthetic transforms isolate state/pose tests from FBX import. Separate asset tests check real prefab bindings.
    public sealed class PropFlowTestRig : IDisposable
    {
        public readonly GameObject root;
        public readonly RepairCaseData repairCase;
        public readonly RepairPropFlowDefinition definition;
        public readonly RepairPropFlow flow;
        public readonly RepairShiftData shift;
        public readonly RepairSession session;
        readonly bool ownsSession;
        public readonly string coverScrewPrefix;
        public readonly string moduleScrewPrefix;

        public PropFlowTestRig(RepairPropKind kind, RepairSession sharedSession = null, RepairPropFlowDefinition supplied = null, bool initialize = true)
        {
            root = new GameObject("Synthetic_" + kind);
            repairCase = supplied != null ? supplied.repairCase : ScriptableObject.CreateInstance<RepairCaseData>();
            definition = supplied != null ? supplied : RepairPropFlowCatalog.Create(kind, repairCase);
            foreach (var n in definition.nodes)
            {
                var current = root.transform;
                foreach (string segment in n.hierarchyPath.Split('/'))
                {
                    var child = current.Find(segment);
                    if (child == null)
                    {
                        child = new GameObject(segment).transform;
                        child.SetParent(current, false);
                    }
                    current = child;
                }
            }
            repairCase.itemPrefab = root;
            flow = root.AddComponent<RepairPropFlow>();
            ownsSession = sharedSession == null;
            if (sharedSession == null)
            {
                shift = ScriptableObject.CreateInstance<RepairShiftData>();
                shift.cases.Add(repairCase);
                sharedSession = new RepairSession(shift, () => 1f);
                sharedSession.StartShift(); sharedSession.AcceptItem();
            }
            session = sharedSession;
            flow.Configure(definition);
            if (initialize) flow.Initialize(definition, root.transform, sharedSession);
            coverScrewPrefix = kind == RepairPropKind.Receiver ? "RX_CoverScrew_" : kind == RepairPropKind.Cartridge ? "MC_CoverScrew_" : kind == RepairPropKind.Power ? "PS_CoverScrew_" : "OM_CoverScrew_";
            moduleScrewPrefix = kind == RepairPropKind.Receiver ? "RX_RFScrew_" : kind == RepairPropKind.Cartridge ? "MC_IFScrew_" : kind == RepairPropKind.Power ? "PS_ModuleScrew_" : "OM_WakeScrew_";
        }

        public bool Try(string id)
        {
            var c = definition.FindCommand(id);
            if (c == null) throw new InvalidOperationException("Missing command: " + id);
            return flow.TryExecute(id, c.target, c.tool);
        }
        public void Do(string id)
        {
            if (!Try(id)) throw new InvalidOperationException(id + ": " + flow.LastFeedback);
        }
        public void Diagnose()
        {
            foreach (var t in definition.tests) Do("test." + t.id);
        }
        public void OpenToModule(bool replacement = false)
        {
            Do("power.off"); Do("fixture.disconnect");
            for (int i = 1; i <= 4; i++) Do("remove." + coverScrewPrefix + i.ToString("00"));
            Do("cover.remove");
            if (definition.kind != RepairPropKind.Cartridge) Do("isolate");
            Do("harness.release");
            if (definition.kind == RepairPropKind.Power)
            {
                Do("remove.PS_TrayScrew_01"); Do("remove.PS_TrayScrew_02"); Do("tray.remove");
            }
            for (int i = 1; i <= 2; i++) Do("remove." + moduleScrewPrefix + i.ToString("00"));
            Do(replacement ? "module.remove.new" : "module.remove.old");
        }
        public void CloseWithReplacement()
        {
            Do("module.install.new");
            for (int i = 1; i <= 2; i++) Do("install." + moduleScrewPrefix + i.ToString("00"));
            if (definition.kind == RepairPropKind.Power)
            {
                Do("tray.install"); Do("install.PS_TrayScrew_01"); Do("install.PS_TrayScrew_02");
            }
            Do("harness.connect");
            if (definition.kind != RepairPropKind.Cartridge) Do("supply.connect");
            Do("cover.install");
            for (int i = 1; i <= 4; i++) Do("install." + coverScrewPrefix + i.ToString("00"));
            Do("fixture.connect"); Do("power.on");
        }
        public void Retest()
        {
            foreach (var t in definition.tests)
                for (int i = 0; i < t.consecutivePasses; i++) Do("test." + t.id);
        }
        public void Dispose()
        {
            UnityEngine.Object.DestroyImmediate(root);
            if (ownsSession)
            {
                UnityEngine.Object.DestroyImmediate(shift);
                UnityEngine.Object.DestroyImmediate(definition);
                UnityEngine.Object.DestroyImmediate(repairCase);
            }
        }
    }
}
