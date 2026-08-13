using System;
using System.Collections.Generic;
using Perpetuum.Services.Autonomous;
using Xunit;

namespace Perpetuum.Tests.Services.Autonomous
{
    public class AutonomousSupplyCoordinationTests
    {
        [Fact]
        public void DisabledDemandPublishesNoStrategicState()
        {
            var store = new MemorySupplyStore();
            var coordinator = new AutonomousSupplyCoordinator(store);

            coordinator.PublishDemand(
                7,
                900,
                100,
                new Dictionary<int, long> {{200, 12}},
                new AutonomousSupplyDemandOptions(),
                UtcNow);

            Assert.Equal(0, store.ReconcileCalls);
            Assert.Empty(store.Requirements);
        }

        [Fact]
        public void MissingInputBecomesBoundedDurableReservationWithoutGameplayMutation()
        {
            var store = new MemorySupplyStore();
            var publisher = new AutonomousSupplyCoordinator(store);
            publisher.PublishDemand(
                7,
                900,
                100,
                new Dictionary<int, long> {{200, 12}},
                new AutonomousSupplyDemandOptions
                {
                    Enabled = true,
                    RequestLifetimeMinutes = 30
                },
                UtcNow);

            var supplier = new AutonomousSupplyCoordinator(store);
            AutonomousSupplyReservation reservation = supplier.Reserve(
                8,
                200,
                100,
                new AutonomousSupplyFulfillmentOptions
                {
                    Enabled = true,
                    MaximumReservationQuantity = 5,
                    ReservationLifetimeMinutes = 45
                },
                UtcNow);

            Assert.NotNull(reservation);
            Assert.Equal(7, reservation.RequesterCharacterId);
            Assert.Equal(8, reservation.SupplierCharacterId);
            Assert.Equal(5, reservation.Quantity);
            Assert.Equal(100, reservation.DestinationBaseEid);
            Assert.Equal(UtcNow.AddMinutes(30), store.RequestExpiresAtUtc);
            Assert.Equal(UtcNow.AddMinutes(45), reservation.ExpiresAtUtc);
            Assert.Equal(0, store.GameplayActions);
        }

        [Fact]
        public void ReservationAndListedCheckpointSurviveCoordinatorRestart()
        {
            var store = new MemorySupplyStore();
            var first = new AutonomousSupplyCoordinator(store);
            first.PublishDemand(
                7,
                900,
                100,
                new Dictionary<int, long> {{200, 3}},
                new AutonomousSupplyDemandOptions {Enabled = true},
                UtcNow);
            AutonomousSupplyReservation reservation = first.Reserve(
                8,
                200,
                100,
                new AutonomousSupplyFulfillmentOptions {Enabled = true},
                UtcNow);
            first.MarkListed(reservation, 2, TimeSpan.FromHours(2), UtcNow);

            var restarted = new AutonomousSupplyCoordinator(store);
            AutonomousSupplyReservation restored = restarted.Load(8);

            Assert.NotNull(restored);
            Assert.True(restored.IsListed);
            Assert.Equal(2, restored.Quantity);
            restarted.Release(restored, "order_completed");
            Assert.Null(restarted.Load(8));
        }

        [Fact]
        public void SupplierCannotClaimItsOwnRequestOrARequestAtAnotherBase()
        {
            var store = new MemorySupplyStore();
            var coordinator = new AutonomousSupplyCoordinator(store);
            coordinator.PublishDemand(
                7,
                900,
                100,
                new Dictionary<int, long> {{200, 3}},
                new AutonomousSupplyDemandOptions {Enabled = true},
                UtcNow);
            var options = new AutonomousSupplyFulfillmentOptions {Enabled = true};

            Assert.Null(coordinator.Reserve(7, 200, 100, options, UtcNow));
            Assert.Null(coordinator.Reserve(8, 200, 101, options, UtcNow));
            Assert.Null(coordinator.Reserve(8, 201, 100, options, UtcNow));
        }

        private static readonly DateTime UtcNow =
            new DateTime(2026, 8, 13, 23, 0, 0, DateTimeKind.Utc);

        private sealed class MemorySupplyStore : IAutonomousSupplyRequestStore
        {
            private int _requester;
            private int _goal;
            private long _base;
            private AutonomousSupplyReservation _reservation;

            public int ReconcileCalls { get; private set; }
            public int GameplayActions { get; private set; }
            public DateTime RequestExpiresAtUtc { get; private set; }
            public IReadOnlyDictionary<int, long> Requirements { get; private set; } =
                new Dictionary<int, long>();

            public void Reconcile(
                int requesterCharacterId,
                int goalDefinition,
                long destinationBaseEid,
                IReadOnlyDictionary<int, long> requirements,
                DateTime expiresAtUtc)
            {
                ReconcileCalls++;
                _requester = requesterCharacterId;
                _goal = goalDefinition;
                _base = destinationBaseEid;
                Requirements = new Dictionary<int, long>(requirements);
                RequestExpiresAtUtc = expiresAtUtc;
                if (_reservation != null && !Requirements.ContainsKey(_reservation.ItemDefinition))
                    _reservation = null;
            }

            public AutonomousSupplyReservation LoadActiveReservation(int supplierCharacterId) =>
                _reservation?.SupplierCharacterId == supplierCharacterId ? _reservation : null;

            public AutonomousSupplyReservation Claim(
                int supplierCharacterId,
                int itemDefinition,
                long destinationBaseEid,
                int maximumQuantity,
                DateTime expiresAtUtc)
            {
                if (_reservation != null || supplierCharacterId == _requester ||
                    destinationBaseEid != _base ||
                    !Requirements.TryGetValue(itemDefinition, out long required))
                    return null;
                int quantity = (int)Math.Min(required, maximumQuantity);
                _reservation = new AutonomousSupplyReservation(
                    1,
                    1,
                    _requester,
                    supplierCharacterId,
                    _goal,
                    itemDefinition,
                    quantity,
                    _base,
                    "Reserved",
                    expiresAtUtc);
                return _reservation;
            }

            public void Renew(long reservationId, int supplierCharacterId, DateTime expiresAtUtc)
            {
                Replace(reservationId, supplierCharacterId, _reservation.Quantity, _reservation.Phase, expiresAtUtc);
            }

            public void MarkListed(
                long reservationId,
                int supplierCharacterId,
                int quantity,
                DateTime expiresAtUtc)
            {
                Replace(reservationId, supplierCharacterId, quantity, "Listed", expiresAtUtc);
            }

            public void Release(long reservationId, int supplierCharacterId, string reason)
            {
                AssertReservation(reservationId, supplierCharacterId);
                _reservation = null;
            }

            private void Replace(
                long reservationId,
                int supplierCharacterId,
                int quantity,
                string phase,
                DateTime expiresAtUtc)
            {
                AssertReservation(reservationId, supplierCharacterId);
                _reservation = new AutonomousSupplyReservation(
                    reservationId,
                    _reservation.RequestId,
                    _reservation.RequesterCharacterId,
                    supplierCharacterId,
                    _reservation.GoalDefinition,
                    _reservation.ItemDefinition,
                    Math.Min(quantity, _reservation.Quantity),
                    _reservation.DestinationBaseEid,
                    phase,
                    expiresAtUtc);
            }

            private void AssertReservation(long reservationId, int supplierCharacterId)
            {
                if (_reservation == null || _reservation.ReservationId != reservationId ||
                    _reservation.SupplierCharacterId != supplierCharacterId)
                    throw new InvalidOperationException("Reservation ownership mismatch.");
            }
        }
    }
}
