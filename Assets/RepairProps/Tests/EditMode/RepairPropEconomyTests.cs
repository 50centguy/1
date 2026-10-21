using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BorderRepair.TwoNight;
using NUnit.Framework;
using UnityEngine;

namespace BorderRepair.RepairProps.Tests
{
    public sealed class RepairPropEconomyTests
    {
        TwoNightState previous, state;
        [SetUp] public void Setup()
        {
            previous = TwoNightRun.Current;
            state = TwoNightRun.NewGame(null, true);
            state.supplies = new ClinicSupplyState(); state.supplyRulesVersion = 1; state.customerQueueCount = 4;
        }
        [TearDown] public void Teardown() => TwoNightRun.Set(previous);
        static string Id() => Guid.NewGuid().ToString("N");
        static Dictionary<string, int> Cart(string sku = "rx-rf", int quantity = 1) => new Dictionary<string, int> { { sku, quantity } };

        [Test] public void BasketDebitsOnceAndArrivesAsCompatibleStock()
        {
            var cart = new Dictionary<string, int> { { "rx-rf", 2 }, { "ps-output", 1 } };
            string id = Id();
            Assert.That(ClinicSupplies.Purchase(state, cart, id, out _), Is.True);
            Assert.That(state.Cash, Is.EqualTo(389));
            Assert.That(ClinicSupplies.Available(state, "rx-rf"), Is.EqualTo(2));
            Assert.That(ClinicSupplies.Available(state, "ps-output"), Is.EqualTo(1));
            Assert.That(ClinicSupplies.Available(state, "mc-interface"), Is.Zero);
            Assert.That(ClinicSupplies.Purchase(state, cart, id, out _), Is.False);
            Assert.That(state.transactions.Count, Is.EqualTo(2));
            Assert.That(state.Cash, Is.EqualTo(389));
        }
        [TestCase(0)] [TestCase(-1)] [TestCase(21)] [TestCase(int.MaxValue)]
        public void BadQuantityHasNoPartialDebitOrInventory(int quantity)
        {
            var cart = Cart("rx-rf"); cart.Add("mc-interface", quantity);
            Assert.That(ClinicSupplies.Purchase(state, cart, Id(), out _), Is.False);
            Assert.That(state.Cash, Is.EqualTo(500)); Assert.That(state.supplies.receipts, Is.Empty);
        }
        [Test] public void InsufficientCashAndUnknownSkuRejectWholeCart()
        {
            Assert.That(ClinicSupplies.Purchase(state, Cart("ps-output", 20), Id(), out _), Is.False);
            Assert.That(ClinicSupplies.Purchase(state, Cart("unknown"), Id(), out _), Is.False);
            Assert.That(state.transactions, Is.Empty);
        }
        [Test] public void CanonicalRequestIdsRejectCaseChangedReplay()
        {
            string id = "abcdef1234567890abcdef1234567890";
            Assert.That(ClinicSupplies.Purchase(state, Cart("rx-rf", 2), id, out _), Is.True);
            Assert.That(ClinicSupplies.Purchase(state, Cart(), id.ToUpperInvariant(), out _), Is.False);
            string refund = "fedcba1234567890fedcba1234567890";
            Assert.That(ClinicSupplies.Refund(state, "rx-rf", 1, refund, out _), Is.True);
            Assert.That(ClinicSupplies.Refund(state, "rx-rf", 1, refund.ToUpperInvariant(), out _), Is.False);
            Assert.That(state.Cash, Is.EqualTo(472));
        }
        [Test] public void WrongSkuCannotBeReservedOrAcceptedInCheckpoint()
        {
            ClinicSupplies.Purchase(state, Cart("om-wake"), Id(), out _);
            TwoNightRun.ReceiveCustomer(state, "repair-prop-power", 0, 130, 0);
            Assert.That(ClinicSupplies.Reserve(state, "repair-prop-power", "om-wake", out _), Is.False);
            state.supplies.reservations.Add(new ClinicSupplyReservation { caseId = "repair-prop-power", sku = "om-wake" });
            var errors = new List<string>(); ClinicSupplies.Validate(state, errors);
            Assert.That(errors, Does.Contain("Invalid supply allocation"));
        }
        [Test] public void RemovingSupplyBranchAndBorrowingFutureIncomeAreInvalid()
        {
            TwoNightRun.ReceiveCustomer(state, "repair-prop-receiver", 0, 75, 0);
            state.supplies = null;
            var errors = new List<string>(); ClinicSupplies.Validate(state, errors);
            Assert.That(errors, Does.Contain("Supply rules or history missing"));
            state.supplies = new ClinicSupplyState(); state.startingCash = 0;
            TwoNightRun.Post(state, "test-income", 75, "Test", 1);
            ClinicSupplies.Purchase(state, Cart(), Id(), out _);
            state.transactions.Reverse(); errors.Clear(); ClinicSupplies.Validate(state, errors);
            Assert.That(errors, Does.Contain("Supply cash chronology invalid"));
        }
        [Test] public void AllocatedPartsCannotBeRefundedAndRemovalReturnsSameStock()
        {
            ClinicSupplies.Purchase(state, Cart(), Id(), out _);
            TwoNightRun.ReceiveCustomer(state, "repair-prop-receiver", 0, 75, 0);
            Assert.That(ClinicSupplies.Reserve(state, "repair-prop-receiver", "rx-rf", out _), Is.True);
            Assert.That(ClinicSupplies.Available(state, "rx-rf"), Is.Zero);
            Assert.That(ClinicSupplies.Refund(state, "rx-rf", 1, Id(), out _), Is.False);
            Assert.That(ClinicSupplies.Reserve(state, "repair-prop-receiver", "rx-rf", out _), Is.False);
            Assert.That(ClinicSupplies.Release(state, "repair-prop-receiver"), Is.True);
            Assert.That(ClinicSupplies.Release(state, "repair-prop-receiver"), Is.False);
            Assert.That(ClinicSupplies.Available(state, "rx-rf"), Is.EqualTo(1));
            string refund = Id();
            Assert.That(ClinicSupplies.Refund(state, "rx-rf", 1, refund, out _), Is.True);
            Assert.That(ClinicSupplies.Refund(state, "rx-rf", 1, refund, out _), Is.False);
            Assert.That(state.Cash, Is.EqualTo(500));
        }
        [Test] public void DeliveryConsumesOnlyAllocatedPartAndNeverDeductsPurchaseAgain()
        {
            ClinicSupplies.Purchase(state, Cart(), Id(), out _);
            TwoNightRun.ReceiveCustomer(state, "repair-prop-receiver", 0, 75, 0);
            TwoNightRun.CompleteCustomerRepair(state, true, "repair-prop-receiver");
            Assert.That(TwoNightRun.DeliverCustomer(state, "repair-prop-receiver"), Is.False, "No allocation cannot produce a sold part.");
            state.ActiveTrade.state = ClinicTradeState.InRepair;
            ClinicSupplies.Reserve(state, "repair-prop-receiver", "rx-rf", out _);
            TwoNightRun.CompleteCustomerRepair(state, true, "repair-prop-receiver");
            Assert.That(state.Cash, Is.EqualTo(472));
            Assert.That(TwoNightRun.DeliverCustomer(state, "repair-prop-receiver"), Is.True);
            Assert.That(TwoNightRun.DeliverCustomer(state, "repair-prop-receiver"), Is.False);
            Assert.That(state.Cash, Is.EqualTo(547));
            Assert.That(ClinicSupplies.Consumed(state, "rx-rf"), Is.EqualTo(1));
            Assert.That(ClinicSupplies.Release(state, "repair-prop-receiver"), Is.False);
            Assert.That(ClinicSupplies.Refund(state, "rx-rf", 1, Id(), out _), Is.False);
        }
        [TestCase(RepairPropKind.Receiver)] [TestCase(RepairPropKind.Cartridge)]
        [TestCase(RepairPropKind.Power)] [TestCase(RepairPropKind.Omamori)]
        public void ActualFlowRequiresCorrectPurchaseAndReinstallDoesNotChargeAgain(RepairPropKind kind)
        {
            using (var rig = new PropFlowTestRig(kind))
            {
                TwoNightRun.ReceiveCustomer(state, rig.repairCase.caseId, 0, 75, 0);
                var policy = new RepairPropSupplyPolicy(state, rig.repairCase.caseId, kind);
                rig.flow.CommandPolicy = policy;
                rig.Diagnose(); rig.OpenToModule();
                Assert.That(rig.Try("module.install.new"), Is.False);
                string wrong = policy.Sku == "rx-rf" ? "mc-interface" : "rx-rf";
                ClinicSupplies.Purchase(state, Cart(wrong), Id(), out _);
                Assert.That(rig.Try("module.install.new"), Is.False);
                ClinicSupplies.Purchase(state, Cart(policy.Sku), Id(), out _);
                int cash = state.Cash;
                rig.CloseWithReplacement();
                Assert.That(ClinicSupplies.Available(state, policy.Sku), Is.Zero);
                Assert.That(ClinicSupplies.Allocated(state, policy.Sku), Is.EqualTo(1));
                rig.OpenToModule(true);
                Assert.That(ClinicSupplies.Available(state, policy.Sku), Is.EqualTo(1));
                rig.CloseWithReplacement(); rig.Retest(); rig.Do("complete");
                Assert.That(state.Cash, Is.EqualTo(cash));
                Assert.That(ClinicSupplies.Allocated(state, policy.Sku), Is.EqualTo(1));
            }
        }
        [Test] public void SupplyCheckpointRoundTripAndTamperDetection()
        {
            state.customerQueueCount = 1;
            ClinicSupplies.Purchase(state, Cart("rx-rf", 2), Id(), out _);
            TwoNightRun.ReceiveCustomer(state, "repair-prop-receiver", 0, 75, 0);
            ClinicSupplies.Reserve(state, "repair-prop-receiver", "rx-rf", out _);
            TwoNightRun.CompleteCustomerRepair(state, true, "repair-prop-receiver"); TwoNightRun.DeliverCustomer(state, "repair-prop-receiver");
            TwoNightRun.ConfirmLedger(state); TwoNightRun.FinishIncident(state);
            var safe = new Unit07SafeState { seated = true, clamped = true, powerOff = true, rotorsStopped = true, trayStowed = true, robotUpright = true };
            Assert.That(TwoNightRun.RegisterUnit07(state, safe, out _), Is.True);
            string old = TwoNightSave.OverrideDirectory;
            string directory = Path.Combine(Path.GetTempPath(), "RepairPropEconomy_" + Id());
            try
            {
                TwoNightSave.OverrideDirectory = directory;
                Assert.That(TwoNightSave.Write(state, out var reason), Is.True, reason);
                var read = TwoNightSave.Read(); Assert.That(read.status, Is.EqualTo(SaveStatus.Ok), read.message);
                Assert.That(read.state.Cash, Is.EqualTo(519));
                Assert.That(ClinicSupplies.Available(read.state, "rx-rf"), Is.EqualTo(1));
                Assert.That(ClinicSupplies.Consumed(read.state, "rx-rf"), Is.EqualTo(1));
                state.supplies.receipts[0].quantity++;
                Assert.That(TwoNightSave.Write(state, out _), Is.False);
            }
            finally
            {
                TwoNightSave.OverrideDirectory = old;
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }
        [Test] public void AbandonedPolicyReturnsAllocationButNeverRefundsPurchasedCash()
        {
            string caseId;
            using (var rig = new PropFlowTestRig(RepairPropKind.Receiver))
            {
                caseId = rig.repairCase.caseId;
                TwoNightRun.ReceiveCustomer(state, caseId, 0, 75, 0);
                ClinicSupplies.Purchase(state, Cart(), Id(), out _);
                rig.flow.CommandPolicy = new RepairPropSupplyPolicy(state, caseId, RepairPropKind.Receiver);
                rig.Diagnose(); rig.OpenToModule(); rig.CloseWithReplacement();
                Assert.That(ClinicSupplies.Available(state, "rx-rf"), Is.Zero);
                // EditMode synthetic behaviours do not run the PlayMode destruction lifecycle.
                ((IDisposable)rig.flow.CommandPolicy).Dispose();
            }
            Assert.That(ClinicSupplies.Available(state, "rx-rf"), Is.EqualTo(1));
            Assert.That(state.Cash, Is.EqualTo(472));
            Assert.That(ClinicSupplies.HasAllocation(state, caseId), Is.False);
        }
        [Test] public void StockLimitAndRefundCashOverflowCannotMutateLedger()
        {
            state.startingCash = int.MaxValue;
            for (int i = 0; i < 4; i++) Assert.That(ClinicSupplies.Purchase(state, Cart("rx-rf", 20), Id(), out _), Is.True);
            Assert.That(ClinicSupplies.Purchase(state, Cart("rx-rf", 19), Id(), out _), Is.True);
            Assert.That(ClinicSupplies.Purchase(state, Cart(), Id(), out _), Is.False);
            state.startingCash = int.MaxValue;
            TwoNightRun.Post(state, "test-income", 2772, "Test only", 1);
            int count = state.transactions.Count;
            Assert.That(ClinicSupplies.Refund(state, "rx-rf", 1, Id(), out _), Is.False);
            Assert.That(state.transactions.Count, Is.EqualTo(count));
        }
    }
}
