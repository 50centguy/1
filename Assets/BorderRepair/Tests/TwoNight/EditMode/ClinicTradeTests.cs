using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace BorderRepair.TwoNight.Tests
{
    public class ClinicTradeTests
    {
        const string Case = "case_n1_collector_communicator";
        TwoNightEconomy economy;
        string directory;

        [SetUp]
        public void SetUp()
        {
            economy = TwoNightEconomy.Defaults();
            directory = Path.Combine(Path.GetTempPath(), "ClinicTradeTests_" + Guid.NewGuid().ToString("N"));
            TwoNightSave.OverrideDirectory = directory;
            TwoNightRun.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            TwoNightSave.OverrideDirectory = null;
            TwoNightRun.Clear();
            UnityEngine.Object.DestroyImmediate(economy);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        [Test]
        public void ReceiveRepairDeliver_OnlyDeliveryCredits_AndEveryActionIsSingleUse()
        {
            var s = TwoNightRun.NewGame(economy, true);
            Assert.IsFalse(TwoNightRun.DeliverCommunicator(s, Case, economy));
            Assert.IsFalse(TwoNightRun.CompleteCommunicatorRepair(s, true, Case));
            Assert.IsFalse(TwoNightRun.SettleCommunicator(s, true, Case, economy), "Legacy Summary cannot bypass delivery.");
            Assert.IsFalse(TwoNightRun.ReceiveCommunicator(s, ""));
            Assert.IsTrue(TwoNightRun.ReceiveCommunicator(s, Case));
            Assert.IsFalse(TwoNightRun.ReceiveCommunicator(s, Case));
            Assert.IsFalse(TwoNightRun.DeliverCommunicator(s, Case, economy));
            Assert.IsFalse(TwoNightRun.CompleteCommunicatorRepair(s, false, Case));
            Assert.IsFalse(TwoNightRun.CompleteCommunicatorRepair(s, true, "wrong-case"));
            Assert.IsTrue(TwoNightRun.CompleteCommunicatorRepair(s, true, Case));
            Assert.IsFalse(TwoNightRun.CompleteCommunicatorRepair(s, true, Case));
            Assert.IsFalse(TwoNightRun.ReceiveCommunicator(s, "invented-next-case"));
            Assert.AreEqual(500, s.Cash);
            Assert.IsEmpty(s.transactions);
            Assert.IsFalse(TwoNightRun.ConfirmLedger(s));
            Assert.IsFalse(TwoNightRun.DeliverCommunicator(s, "wrong-case", economy));
            Assert.IsTrue(TwoNightRun.DeliverCommunicator(s, Case, economy));
            Assert.AreEqual(800, s.Cash);
            Assert.AreEqual(ClinicTradeState.Delivered, s.communicatorTrade);
            Assert.AreEqual(TwoNightPhase.Night1Ledger, s.phase);
            Assert.IsFalse(TwoNightRun.DeliverCommunicator(s, Case, economy));
            Assert.IsFalse(TwoNightRun.ReceiveCommunicator(s, Case));
            Assert.AreEqual(2, s.transactions.Count);
        }

        [Test]
        public void PartialExistingLedger_RefusesDeliveryWithoutAddingAnotherEntry()
        {
            var s = TwoNightRun.NewGame(economy, true);
            TwoNightRun.ReceiveCommunicator(s, Case);
            TwoNightRun.CompleteCommunicatorRepair(s, true, Case);
            TwoNightRun.Post(s, TwoNightRun.CommunicatorPartsId, -100, "partial", 1);
            Assert.IsFalse(TwoNightRun.DeliverCommunicator(s, Case, economy));
            Assert.AreEqual(ClinicTradeState.ReadyForDelivery, s.communicatorTrade);
            Assert.AreEqual(1, s.transactions.Count);
            Assert.IsFalse(s.communicatorSettled);
        }

        [Test]
        public void SharedCheckpoint_RoundTripsDeliveredItem_AndInternalOrderStaysSeparate()
        {
            var s = TwoNightRun.NewGame(economy, true);
            TwoNightRun.ReceiveCommunicator(s, Case);
            Assert.IsFalse(TwoNightSave.Write(s, out _));
            TwoNightRun.CompleteCommunicatorRepair(s, true, Case);
            Assert.IsFalse(TwoNightSave.Write(s, out _));
            TwoNightRun.DeliverCommunicator(s, Case, economy);
            TwoNightRun.ConfirmLedger(s);
            TwoNightRun.FinishIncident(s);
            var safe = new Unit07SafeState { seated = true, clamped = true, powerOff = true,
                rotorsStopped = true, trayStowed = true, robotUpright = true };
            Assert.IsTrue(TwoNightRun.RegisterUnit07(s, safe, out _));
            Assert.AreEqual(TwoNightRun.Unit07WorkOrderId, s.unit07WorkOrderId);
            Assert.AreEqual(Case, s.communicatorCaseId);
            Assert.AreEqual(2, s.transactions.Count);
            Assert.IsTrue(TwoNightSave.Write(s, out var message), message);
            for (int i = 0; i < 3; i++)
            {
                var r = TwoNightSave.Read();
                Assert.AreEqual(SaveStatus.Ok, r.status, r.message);
                Assert.IsTrue(r.state.unifiedClinic);
                Assert.AreEqual(ClinicTradeState.Delivered, r.state.communicatorTrade);
                Assert.IsTrue(TwoNightRun.BeginNight2(r.state));
                Assert.IsFalse(TwoNightRun.ReceiveCommunicator(r.state, "another-story"));
                Assert.IsFalse(TwoNightRun.DeliverCommunicator(r.state, Case, economy));
                Assert.AreEqual(800, r.state.Cash);
            }
            s.communicatorTrade = ClinicTradeState.ReadyForDelivery;
            Assert.IsFalse(TwoNightSave.Write(s, out _));
            File.WriteAllText(TwoNightSave.FilePath, JsonUtility.ToJson(s));
            Assert.AreEqual(SaveStatus.Corrupt, TwoNightSave.Read().status);
        }

        [TestCase("income")]
        [TestCase("parts")]
        [TestCase("case")]
        [TestCase("order")]
        public void LegacyCheckpoint_CorruptSettlementAndWorkOrderAreRejected(string corruption)
        {
            var s = TwoNightRun.NewGame(economy);
            TwoNightRun.SettleCommunicator(s, true, Case, economy);
            TwoNightRun.ConfirmLedger(s); TwoNightRun.FinishIncident(s);
            TwoNightRun.RegisterUnit07(s, new Unit07SafeState { seated = true, clamped = true, powerOff = true,
                rotorsStopped = true, trayStowed = true, robotUpright = true }, out _);
            if (corruption == "income") s.transactions[0].amount++;
            if (corruption == "parts") s.transactions[1].amount--;
            if (corruption == "case") s.communicatorCaseId = null;
            if (corruption == "order") s.unit07WorkOrderId = "customer-trade";
            Directory.CreateDirectory(directory);
            File.WriteAllText(TwoNightSave.FilePath, JsonUtility.ToJson(s));
            Assert.AreEqual(SaveStatus.Corrupt, TwoNightSave.Read().status);
        }

        [Test]
        public void VersionOneWithoutTradeFields_RemainsReadableAndCanMigrate()
        {
            var s = TwoNightRun.NewGame(economy);
            TwoNightRun.SettleCommunicator(s, true, Case, economy);
            TwoNightRun.ConfirmLedger(s); TwoNightRun.FinishIncident(s);
            TwoNightRun.RegisterUnit07(s, new Unit07SafeState { seated = true, clamped = true, powerOff = true,
                rotorsStopped = true, trayStowed = true, robotUpright = true }, out _);
            // This is the exact old checkpoint schema, with no newly introduced fields.
            var json = "{\"version\":1,\"night\":1,\"phase\":5,\"startingCash\":500,\"rent\":1200,\"rentDueNight\":3," +
                "\"transactions\":[{\"id\":\"N1-COMM-INCOME\",\"amount\":400,\"night\":1},{\"id\":\"N1-COMM-PARTS\",\"amount\":-100,\"night\":1}]," +
                "\"communicatorSettled\":true,\"communicatorCaseId\":\"case_n1_collector_communicator\",\"ledgerConfirmed\":true," +
                "\"unit07IncidentShown\":true,\"unit07Registered\":true,\"unit07WorkOrderId\":\"WO-U07-N1\",\"unit07Repaired\":false," +
                "\"unit07\":{\"seated\":true,\"clamped\":true,\"powerOff\":true,\"rotorsStopped\":true,\"trayStowed\":true,\"robotUpright\":true}}";
            Directory.CreateDirectory(directory);
            File.WriteAllText(TwoNightSave.FilePath, json);
            var restored = TwoNightSave.Read();
            Assert.AreEqual(SaveStatus.Ok, restored.status, restored.message);
            Assert.IsFalse(restored.state.unifiedClinic);
            restored.state.unifiedClinic = true;
            restored.state.communicatorTrade = ClinicTradeState.Delivered;
            Assert.IsTrue(TwoNightSave.Write(restored.state, out var message), message);
            Assert.IsTrue(TwoNightRun.BeginNight2(restored.state));
            Assert.AreEqual(800, restored.state.Cash);
        }

        [TestCase(false, "none")]
        [TestCase(false, "income")]
        [TestCase(false, "decision")]
        [TestCase(true, "none")]
        [TestCase(true, "income")]
        [TestCase(true, "decision")]
        public void AuthoredUnpaidReturnCheckpoint_StoresDecisionWithoutRepairReceipts(bool firstReturned, string corruption)
        {
            var beacon = UnityEditor.AssetDatabase.LoadAssetAtPath<BorderRepair.Data.RepairCaseData>(
                "Assets/BorderRepair/Data/Cases/Case_02_NavBeacon.asset");
            Assert.IsNotNull(beacon);
            Assert.IsTrue(beacon.FindOutcome(BorderRepair.Data.RepairDecision.Refuse).isCorrect);
            var s = TwoNightRun.NewGame(economy, true);
            if (!firstReturned)
            {
                s.customerQueueCount = 2;
                Assert.IsTrue(TwoNightRun.ReceiveCommunicator(s, Case));
                Assert.IsTrue(TwoNightRun.CompleteCommunicatorRepair(s, true, Case));
                Assert.IsTrue(TwoNightRun.DeliverCommunicator(s, Case, economy));
            }
            Assert.IsTrue(TwoNightRun.ReceiveCustomer(s, beacon.caseId, firstReturned ? 0 : 1, beacon.estimatedRepairCost, 0));
            Assert.IsFalse(TwoNightRun.CompleteCustomerDecision(s, false, beacon.caseId, BorderRepair.Data.RepairDecision.Refuse));
            Assert.IsTrue(TwoNightRun.CompleteCustomerDecision(s, true, beacon.caseId, BorderRepair.Data.RepairDecision.Refuse));
            Assert.AreEqual(ClinicTradeState.ReadyForReturn, s.ActiveTrade.state);
            Assert.IsTrue(TwoNightRun.DeliverCustomer(s, beacon.caseId));
            Assert.IsFalse(TwoNightRun.DeliverCustomer(s, beacon.caseId));
            Assert.AreEqual(firstReturned ? 0 : 2, s.transactions.Count);
            Assert.IsTrue(TwoNightRun.ConfirmLedger(s));
            Assert.IsTrue(TwoNightRun.FinishIncident(s));
            Assert.IsTrue(TwoNightRun.RegisterUnit07(s, new Unit07SafeState { seated = true, clamped = true,
                powerOff = true, rotorsStopped = true, trayStowed = true, robotUpright = true }, out _));
            Assert.IsTrue(TwoNightSave.Write(s, out var message), message);
            if (corruption == "income") s.ActiveTrade.income = 1;
            if (corruption == "decision") s.ActiveTrade.decision = BorderRepair.Data.RepairDecision.Repair;
            File.WriteAllText(TwoNightSave.FilePath, JsonUtility.ToJson(s));
            var loaded = TwoNightSave.Read();
            if (corruption != "none")
            {
                Assert.AreEqual(SaveStatus.Corrupt, loaded.status);
                Assert.IsFalse(TwoNightSave.Write(s, out _));
                return;
            }
            Assert.AreEqual(SaveStatus.Ok, loaded.status, loaded.message);
            Assert.IsTrue(loaded.state.ActiveTrade.returnedUnpaid);
            Assert.AreEqual(BorderRepair.Data.RepairDecision.Refuse, loaded.state.ActiveTrade.decision);
            Assert.AreEqual(firstReturned ? 500 : 800, loaded.state.Cash);
            Assert.IsTrue(TwoNightRun.BeginNight2(loaded.state));
            Assert.IsFalse(TwoNightRun.DeliverCustomer(loaded.state, beacon.caseId));
        }

        [TestCase("none")]
        [TestCase("history")]
        [TestCase("receipt")]
        [TestCase("case")]
        [TestCase("state")]
        public void TwoCaseCheckpoint_RequiresAllDeliveredHistoryAndMatchingReceipts(string corruption)
        {
            var s = TwoNightRun.NewGame(economy, true);
            s.customerQueueCount = 2;
            Assert.IsTrue(TwoNightRun.ReceiveCommunicator(s, Case));
            Assert.IsTrue(TwoNightRun.CompleteCommunicatorRepair(s, true, Case));
            Assert.IsTrue(TwoNightRun.DeliverCommunicator(s, Case, economy));
            Assert.IsFalse(TwoNightRun.ConfirmLedger(s));
            Assert.IsTrue(TwoNightRun.ReceiveCustomer(s, "authored-second-case", 1, 210, 0));
            Assert.IsTrue(TwoNightRun.CompleteCustomerRepair(s, true, "authored-second-case"));
            Assert.IsTrue(TwoNightRun.DeliverCustomer(s, "authored-second-case"));
            Assert.IsTrue(TwoNightRun.ConfirmLedger(s));
            Assert.IsTrue(TwoNightRun.FinishIncident(s));
            Assert.IsTrue(TwoNightRun.RegisterUnit07(s, new Unit07SafeState { seated = true, clamped = true,
                powerOff = true, rotorsStopped = true, trayStowed = true, robotUpright = true }, out _));
            Assert.IsTrue(TwoNightSave.Write(s, out var message), message);
            if (corruption == "history")
            {
                s.customerTrades.Clear();
                s.transactions.RemoveRange(2, 2);
            }
            if (corruption == "receipt") s.transactions[2].amount++;
            if (corruption == "case") s.customerTrades[1].caseId = "different-case";
            if (corruption == "state") s.customerTrades[1].state = ClinicTradeState.ReadyForDelivery;
            File.WriteAllText(TwoNightSave.FilePath, JsonUtility.ToJson(s));
            var restored = TwoNightSave.Read();
            if (corruption != "none")
            {
                Assert.AreEqual(SaveStatus.Corrupt, restored.status);
                Assert.IsFalse(TwoNightSave.Write(s, out _));
                return;
            }
            Assert.AreEqual(SaveStatus.Ok, restored.status, restored.message);
            Assert.AreEqual(1010, restored.state.Cash);
            Assert.AreEqual(2, restored.state.customerTrades.Count);
            Assert.IsTrue(TwoNightRun.BeginNight2(restored.state));
            Assert.IsFalse(TwoNightRun.DeliverCustomer(restored.state, "authored-second-case"));
            Assert.AreEqual(4, restored.state.transactions.Count);
        }
    }
}
