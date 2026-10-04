using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace BorderRepair.TwoNight.Tests
{
    /// <summary>两晚切片核心：账本幂等、阶段顺序、安全存档、跨夜序列化、损坏 / 版本不符的存档、“重新开始”只删本切片存档。</summary>
    public class TwoNightCoreTests
    {
        string dir;
        TwoNightEconomy eco;

        [SetUp]
        public void SetUp()
        {
            dir = Path.Combine(Path.GetTempPath(), "TwoNightTests_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            TwoNightSave.OverrideDirectory = dir;
            eco = TwoNightEconomy.Defaults();
            TwoNightRun.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            TwoNightSave.OverrideDirectory = null;
            TwoNightRun.Clear();
            Object.DestroyImmediate(eco);
            try { Directory.Delete(dir, true); } catch { }
        }

        static Unit07SafeState Safe() => new Unit07SafeState { seated = true, clamped = true, powerOff = true, rotorsStopped = true, trayStowed = true, robotUpright = true };

        TwoNightState PlayNight1()
        {
            var s = TwoNightRun.NewGame(eco);
            Assert.IsTrue(TwoNightRun.SettleCommunicator(s, true, "case_n1", eco));
            Assert.IsTrue(TwoNightRun.ConfirmLedger(s));
            Assert.IsTrue(TwoNightRun.FinishIncident(s));
            Assert.IsTrue(TwoNightRun.RegisterUnit07(s, Safe(), out _));
            return s;
        }

        [Test]
        public void Ledger_CorrectSettlement_Is800_AndOnlyOnce()
        {
            var s = TwoNightRun.NewGame(eco);
            Assert.AreEqual(500, s.Cash);
            Assert.IsTrue(TwoNightRun.SettleCommunicator(s, true, "case_n1", eco));
            Assert.AreEqual(800, s.Cash, "500 + 400 − 100");
            Assert.AreEqual(400, s.RentShortfall, "房租 1200，尚差 400");
            Assert.AreEqual(3, s.rentDueNight);
            Assert.IsFalse(TwoNightRun.SettleCommunicator(s, true, "case_n1", eco), "重复结单");
            Assert.IsFalse(TwoNightRun.Post(s, TwoNightRun.CommunicatorIncomeId, 400, "x", 1), "同 id 再记一次");
            Assert.AreEqual(800, s.Cash);
            Assert.AreEqual(2, s.transactions.Count);
            Assert.IsTrue(TwoNightRun.ConfirmLedger(s));
            Assert.IsFalse(TwoNightRun.ConfirmLedger(s), "重复确认账本");
            Assert.AreEqual(800, s.Cash);
        }

        [Test]
        public void Ledger_WrongDecision_PostsNothing_AndCanStillSettleLater()
        {
            var s = TwoNightRun.NewGame(eco);
            Assert.IsFalse(TwoNightRun.SettleCommunicator(s, false, "case_n1", eco));
            Assert.AreEqual(500, s.Cash);
            Assert.AreEqual(TwoNightPhase.Night1Counter, s.phase);
            Assert.IsFalse(s.communicatorSettled);
            Assert.IsTrue(TwoNightRun.SettleCommunicator(s, true, "case_n1", eco));
            Assert.AreEqual(800, s.Cash);
        }

        [Test]
        public void Phases_OutOfOrder_AreRejected_WithoutChangingState()
        {
            var s = TwoNightRun.NewGame(eco);
            Assert.IsFalse(TwoNightRun.ConfirmLedger(s), "没结单不能确认账本");
            Assert.IsFalse(TwoNightRun.FinishIncident(s), "没确认账本不能触发七号事故");
            Assert.IsFalse(TwoNightRun.RegisterUnit07(s, Safe(), out _));
            Assert.IsFalse(TwoNightRun.BeginNight2(s));
            Assert.AreEqual(TwoNightPhase.Night1Counter, s.phase);
            TwoNightRun.SettleCommunicator(s, true, "c", eco);
            Assert.IsFalse(TwoNightRun.FinishIncident(s), "账本没确认，事故不触发");
            TwoNightRun.ConfirmLedger(s);
            Assert.IsTrue(TwoNightRun.FinishIncident(s));
            Assert.IsFalse(TwoNightRun.FinishIncident(s), "事故只触发一次");
            var unsafeState = Safe(); unsafeState.rotorsStopped = false; unsafeState.trayStowed = false;
            Assert.IsFalse(TwoNightRun.RegisterUnit07(s, unsafeState, out var why));
            StringAssert.Contains("叶轮还没停稳", why);
            StringAssert.Contains("零件盘没放回托盘架", why);
            Assert.AreEqual(TwoNightPhase.Night1Docking, s.phase);
        }

        [Test]
        public void Save_OnlyAtSafeNightEnd()
        {
            var s = TwoNightRun.NewGame(eco);
            Assert.IsFalse(TwoNightSave.Write(s, out _), "第一晚中途不能存");
            Assert.IsFalse(TwoNightSave.Exists);
            s = PlayNight1();
            s.unit07.powerOff = false;
            Assert.IsFalse(TwoNightSave.Write(s, out var msg));
            StringAssert.Contains("还没断电", msg);
            s.unit07.powerOff = true;
            Assert.IsTrue(TwoNightSave.Write(s, out msg), msg);
            Assert.IsTrue(TwoNightSave.Exists);
        }

        [Test]
        public void Save_RoundTrip_AndRepeatedContinue_DoNotAddMoney()
        {
            var s = PlayNight1();
            Assert.IsTrue(TwoNightSave.Write(s, out _));
            for (int i = 0; i < 3; i++)
            {
                var r = TwoNightSave.Read();
                Assert.AreEqual(SaveStatus.Ok, r.status, r.message);
                Assert.AreEqual(800, r.state.Cash);
                Assert.AreEqual(2, r.state.transactions.Count);
                Assert.IsTrue(r.state.unit07.IsSafe);
                Assert.IsFalse(r.state.unit07Repaired);
                Assert.IsTrue(TwoNightRun.BeginNight2(r.state));
                Assert.AreEqual(2, r.state.night);
                Assert.AreEqual(800, r.state.Cash, "继续第二晚不入账");
                Assert.IsFalse(TwoNightRun.SettleCommunicator(r.state, true, "c", eco), "读档后不能再结一次通讯器");
                Assert.AreEqual(800, r.state.Cash);
            }
        }

        [Test]
        public void Load_MissingCorruptVersionDuplicate_AreHandled()
        {
            Assert.AreEqual(SaveStatus.None, TwoNightSave.Read().status);
            File.WriteAllText(TwoNightSave.FilePath, "{ not json");
            Assert.AreEqual(SaveStatus.Corrupt, TwoNightSave.Read().status);
            File.WriteAllText(TwoNightSave.FilePath, "");
            Assert.AreNotEqual(SaveStatus.Ok, TwoNightSave.Read().status);

            var s = PlayNight1();
            s.version = 99;
            File.WriteAllText(TwoNightSave.FilePath, JsonUtility.ToJson(s));
            Assert.AreEqual(SaveStatus.VersionMismatch, TwoNightSave.Read().status);

            s = PlayNight1();
            s.transactions.Add(new TwoNightTransaction { id = TwoNightRun.CommunicatorIncomeId, amount = 400, label = "dup", night = 1 });
            File.WriteAllText(TwoNightSave.FilePath, JsonUtility.ToJson(s));
            Assert.AreEqual(SaveStatus.Corrupt, TwoNightSave.Read().status, "重复账目");

            s = PlayNight1();
            s.unit07Repaired = true;
            File.WriteAllText(TwoNightSave.FilePath, JsonUtility.ToJson(s));
            Assert.AreEqual(SaveStatus.Corrupt, TwoNightSave.Read().status, "第一晚不可能已修好");

            s = PlayNight1();
            s.unit07.clamped = false;
            File.WriteAllText(TwoNightSave.FilePath, JsonUtility.ToJson(s));
            Assert.AreEqual(SaveStatus.Unsafe, TwoNightSave.Read().status);
        }

        [Test]
        public void Delete_RemovesOnlyThisSlicesCheckpoint()
        {
            var s = PlayNight1();
            Assert.IsTrue(TwoNightSave.Write(s, out _));
            var other = Path.Combine(dir, "other_game.json");
            File.WriteAllText(other, "keep");
            var parentOther = Path.Combine(Path.GetDirectoryName(dir), Path.GetFileName(dir) + "_sibling.txt");
            File.WriteAllText(parentOther, "keep");
            TwoNightSave.Delete();
            Assert.IsFalse(TwoNightSave.Exists);
            Assert.IsTrue(File.Exists(other));
            Assert.IsTrue(File.Exists(parentOther));
            File.Delete(parentOther);
        }

        [Test]
        public void Save_WriteFailure_ReturnsReadableMessage()
        {
            var s = PlayNight1();
            // 把存档路径占成一个目录：写入必然失败
            Directory.CreateDirectory(TwoNightSave.FilePath);
            Assert.IsFalse(TwoNightSave.Write(s, out var msg));
            StringAssert.Contains("保存失败", msg);
            Directory.Delete(TwoNightSave.FilePath);
        }

        static MeshClearance.WMesh Seg(Vector3 p, Vector3 q) => new MeshClearance.WMesh { name = "seg", v = new[] { p, q, q }, tri = new[] { 0, 1, 2 }, b = new Bounds((p + q) / 2f, new Vector3(Mathf.Abs(p.x - q.x), Mathf.Abs(p.y - q.y), Mathf.Abs(p.z - q.z))) };

        [Test]
        public void MeshClearance_SegmentCrossing_IsTwoSided_AndNoMirrorFalsePositive()
        {
            var tri = new MeshClearance.WMesh { name = "tri", v = new[] { Vector3.zero, Vector3.right, Vector3.up }, tri = new[] { 0, 1, 2 }, b = new Bounds(new Vector3(0.5f, 0.5f, 0f), new Vector3(1f, 1f, 0f)) };
            Assert.AreEqual(0f, MeshClearance.Distance(Seg(new Vector3(0.2f, 0.2f, 0.5f), new Vector3(0.2f, 0.2f, -0.5f)), tri, 1f), "顺法线反向穿过：穿插");
            Assert.AreEqual(0f, MeshClearance.Distance(Seg(new Vector3(0.2f, 0.2f, -0.5f), new Vector3(0.2f, 0.2f, 0.5f)), tri, 1f), "逆法线方向穿过：也是穿插（旧写法漏报）");
            Assert.Greater(MeshClearance.Distance(Seg(new Vector3(-0.2f, -0.3f, -0.5f), new Vector3(-0.2f, -0.3f, 0.5f)), tri, 1f), 0.1f, "在三角形外穿过平面：不是穿插（旧写法按顶点镜像误报）");
        }
    }
}
