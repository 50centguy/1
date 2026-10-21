using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BorderRepair.RepairProps.Cosmetics.Tests
{
    public sealed class CosmeticPolicyTests
    {
        static readonly Dictionary<RepairPropKind, string[]> Allowlist = new Dictionary<RepairPropKind, string[]>
        {
            { RepairPropKind.Receiver, new[] { "RX_FrontShell", "RX_RearShell", "RX_RearCover", "RX_TopStrip" } },
            { RepairPropKind.Cartridge, new[] { "MC_BaseTray", "MC_TopCover" } },
            { RepairPropKind.Power, new[] { "PS_ext_Shell_p21", "PS_LeftServiceCover" } },
            { RepairPropKind.Omamori, new[] { "OM_ext_FrontPlate_p08", "OM_ext_RearHalfShell_p03", "OM_RearCover" } }
        };

        [Test]
        public void Allowlist_IsExact_CaseSensitive_AndSpecificToThePropKind()
        {
            foreach (var entry in Allowlist)
                foreach (string name in entry.Value)
                {
                    foreach (RepairPropKind kind in Enum.GetValues(typeof(RepairPropKind)))
                        Assert.That(CosmeticItem.Eligible(kind, name), Is.EqualTo(kind == entry.Key), kind + "/" + name);
                    foreach (string nearMatch in new[] { name.ToLowerInvariant(), name.ToUpperInvariant(), " " + name,
                        name + " ", name + "_Button", name + "_Screen", name + "_Core", name + "(Clone)", "Parent/" + name })
                        Assert.That(CosmeticItem.Eligible(entry.Key, nearMatch), Is.False, nearMatch);
                    Assert.That(CosmeticItem.Eligible((RepairPropKind)999, name), Is.False);
                }
        }

        [Test]
        public void Screens_Buttons_RetainedCores_AndInternalBoards_AreNeverEligible()
        {
            var names = new[] { null, "", "Shell", "Housing", "RX_Screen", "RX_ScreenState_Locked", "RX_PowerButton",
                "RX_Mainboard", "RX_BatteryCassette", "RX_RF_Daughterboard", "RX_TopStrip_Hinge", "RX_CoverScrew_01",
                "MC_Screen", "MC_Button", "MC_DataCore", "MC_InterfaceBoard", "MC_StatusLED_OK", "PS_Screen",
                "PS_Button", "PS_SealedBatteryCassette", "PS_FrontElectronics", "PS_OutputModule", "PS_OutputTray",
                "OM_Screen", "OM_WakeButton", "OM_MemoryCore", "OM_MainBoard", "OM_Coil", "OM_PowerCell", "OM_WakeModule" };
            foreach (RepairPropKind kind in Enum.GetValues(typeof(RepairPropKind)))
                foreach (var name in names)
                    Assert.That(CosmeticItem.Eligible(kind, name), Is.False, kind + "/" + name);
        }

        [Test]
        public void MaterialSlot_UsesIndexMetadata_AfterCpuMeshDataHasBeenDiscarded()
        {
            var mesh = new Mesh { name = "NonReadable_MaterialSlot_Metadata" };
            try
            {
                mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.one };
                mesh.subMeshCount = 3;
                mesh.SetTriangles(new[] { 0, 1, 2, 1, 3, 2 }, 0);
                mesh.SetTriangles(Array.Empty<int>(), 1);
                mesh.SetTriangles(new[] { 2, 1, 0 }, 2);
                mesh.UploadMeshData(true);
                Assert.That(mesh.isReadable, Is.False);
                Assert.That(mesh.GetIndexCount(0), Is.EqualTo(6u));
                Assert.That(mesh.GetIndexCount(1), Is.EqualTo(0u));
                Assert.That(mesh.GetIndexCount(2), Is.EqualTo(3u));
                Assert.That(CosmeticItem.MaterialSlot(mesh, 0), Is.Zero);
                Assert.That(CosmeticItem.MaterialSlot(mesh, 1), Is.Zero);
                Assert.That(CosmeticItem.MaterialSlot(mesh, 2), Is.EqualTo(2));
                Assert.That(CosmeticItem.MaterialSlot(mesh, 3), Is.EqualTo(-1));
                Assert.That(CosmeticItem.MaterialSlot(mesh, int.MaxValue), Is.EqualTo(-1));
                Assert.That(CosmeticItem.MaterialSlot(mesh, -1), Is.EqualTo(-1));
            }
            finally { Object.DestroyImmediate(mesh); }
            Assert.That(CosmeticItem.MaterialSlot(null, 0), Is.EqualTo(-1));
        }

        [Test]
        public void MaterialSlot_RejectsNonTriangleTopology_WithoutReadingVerticesOrIndices()
        {
            var mesh = new Mesh();
            try
            {
                mesh.vertices = new[] { Vector3.zero, Vector3.right };
                mesh.SetIndices(new[] { 0, 1 }, MeshTopology.Lines, 0);
                mesh.UploadMeshData(true);
                Assert.That(mesh.isReadable, Is.False);
                Assert.That(mesh.GetIndexCount(0), Is.EqualTo(2u));
                Assert.That(CosmeticItem.MaterialSlot(mesh, 0), Is.EqualTo(-1));
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void CosmeticShaders_AreAvailableAndSupported()
        {
            foreach (string path in new[] { "RepairPropsCosmetics/CosmeticLit", "RepairPropsCosmetics/Stamp" })
            {
                var shader = Resources.Load<Shader>(path);
                Assert.That(shader, Is.Not.Null, path);
                Assert.That(shader.isSupported, Is.True, path);
            }
        }
    }
}
