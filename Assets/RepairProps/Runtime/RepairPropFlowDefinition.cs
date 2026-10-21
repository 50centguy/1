using System;
using System.Collections.Generic;
using BorderRepair.Data;
using UnityEngine;

namespace BorderRepair.RepairProps
{
    public enum RepairPropKind { Receiver, Cartridge, Power, Omamori }
    public enum PropCommandKind { Manipulate, Power, Fixture, Test, Inspect, Complete, Pose }

    [Serializable]
    public struct PropCondition
    {
        public string key;
        public int value;
        public PropCondition(string key, int value) { this.key = key; this.value = value; }
    }

    [Serializable]
    public sealed class PropCommand
    {
        public string id;
        public string label;
        public string target;
        public PropCommandKind kind;
        public RepairActionType tool;
        public List<PropCondition> requires = new List<PropCondition>();
        public List<PropCondition> effects = new List<PropCondition>();
        public bool invalidatesTests;
        public string testId;
        public string coreStepId;
    }

    [Serializable]
    public sealed class PropNode
    {
        public string objectName;
        public string displayName;
        [TextArea] public string motionNote;
        public string hierarchyPath;
        public bool immutable;
        public bool movable;
        public string outKey;
        public int outValue = 1;
        public Vector3 parkingOffset;
        public bool inventoryOnly;
        public string hideWhenKey;
        public int hideWhenValue;
        public bool followsRemovedWakeModule;
        public string visibleKey;
        public int visibleValue = 1;
        public string rotationKey;
        public Vector3 rotationAxis = Vector3.up;
        public float rotationDegrees;
        public List<PropCondition> inspectRequires = new List<PropCondition>();
    }

    [Serializable]
    public sealed class PropTest
    {
        public string id;
        public string label;
        [TextArea] public string faultFinding;
        [TextArea] public string passFinding;
        public bool functional = true;
        public bool checksRetainedIdentity;
        public int consecutivePasses = 1;
    }

    [CreateAssetMenu(menuName = "Border Repair/Repair Prop Flow")]
    public sealed class RepairPropFlowDefinition : ScriptableObject
    {
        public RepairPropKind kind;
        public RepairCaseData repairCase;
        public string rootObjectName;
        public string retainedObjectName;
        public string completionStepId = "flow.verified";
        public string completionEndingId = "return.repaired";
        public List<PropCondition> initialState = new List<PropCondition>();
        public List<PropCondition> assembled = new List<PropCondition>();
        public List<PropNode> nodes = new List<PropNode>();
        public List<PropCommand> commands = new List<PropCommand>();
        public List<PropTest> tests = new List<PropTest>();

        public PropCommand FindCommand(string id) => commands.Find(c => c.id == id);
        public PropNode FindNode(string objectName) => nodes.Find(n => n.objectName == objectName);
        public PropTest FindTest(string id) => tests.Find(t => t.id == id);

        public bool Validate(List<string> errors)
        {
            int before = errors.Count;
            if (repairCase == null) errors.Add("Missing RepairCaseData.");
            if (FindNode(rootObjectName) == null) errors.Add("Missing root node.");
            var retained = FindNode(retainedObjectName);
            if (retained == null || !retained.immutable) errors.Add("Retained core must be immutable.");
            var ids = new HashSet<string>();
            foreach (var n in nodes)
            {
                if (!ids.Add(n.objectName)) errors.Add("Duplicate model node: " + n.objectName);
                if (n.immutable && (n.movable || !string.IsNullOrEmpty(n.visibleKey)))
                    errors.Add("Retained node cannot move or disappear: " + n.objectName);
            }
            ids.Clear();
            foreach (var c in commands)
            {
                if (!ids.Add(c.id)) errors.Add("Duplicate command: " + c.id);
                var n = FindNode(c.target);
                if (n == null) errors.Add("Unknown target: " + c.target);
                if (n != null && n.immutable && c.kind != PropCommandKind.Inspect)
                    errors.Add("Only inspection is allowed on retained parts: " + c.target);
                if (c.kind == PropCommandKind.Test && FindTest(c.testId) == null)
                    errors.Add("Unknown test: " + c.testId);
            }
            if (tests.Count == 0) errors.Add("No retest defined.");
            return before == errors.Count;
        }
    }

    public readonly struct PropTestResult
    {
        public readonly bool functionalPass;
        public readonly bool retainedIdentityMatches;
        public readonly bool readOnly;
        public readonly string message;
        public PropTestResult(bool pass, bool retained = true, bool readOnly = true, string message = null)
        {
            functionalPass = pass;
            retainedIdentityMatches = retained;
            this.readOnly = readOnly;
            this.message = message;
        }
    }

    // A bench sensor may replace the deterministic game-device evaluator. It cannot bypass assembly guards.
    public interface IRepairPropTestProbe
    {
        PropTestResult Measure(RepairPropFlow flow, PropTest test);
    }
}
