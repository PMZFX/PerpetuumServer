using System;
using System.Collections.Generic;
using System.Linq;
using Perpetuum.Data;
using Perpetuum.Services.Actions;
using Perpetuum.Services.MarketEngine;
using Perpetuum.Zones.Terrains.Materials;

namespace Perpetuum.Services.Autonomous
{
    public sealed class AutonomousSupplyReservation
    {
        public AutonomousSupplyReservation(
            long reservationId,
            long requestId,
            int requesterCharacterId,
            int supplierCharacterId,
            int goalDefinition,
            int itemDefinition,
            int quantity,
            long destinationBaseEid,
            string phase,
            DateTime expiresAtUtc)
        {
            if (reservationId <= 0)
                throw new ArgumentOutOfRangeException(nameof(reservationId));
            if (requestId <= 0)
                throw new ArgumentOutOfRangeException(nameof(requestId));
            if (requesterCharacterId <= 0)
                throw new ArgumentOutOfRangeException(nameof(requesterCharacterId));
            if (supplierCharacterId <= 0)
                throw new ArgumentOutOfRangeException(nameof(supplierCharacterId));
            if (goalDefinition <= 0 || itemDefinition <= 0)
                throw new ArgumentOutOfRangeException(nameof(itemDefinition));
            if (quantity <= 0)
                throw new ArgumentOutOfRangeException(nameof(quantity));
            if (destinationBaseEid <= 0)
                throw new ArgumentOutOfRangeException(nameof(destinationBaseEid));
            if (string.IsNullOrWhiteSpace(phase))
                throw new ArgumentException("A supply reservation phase is required.", nameof(phase));

            ReservationId = reservationId;
            RequestId = requestId;
            RequesterCharacterId = requesterCharacterId;
            SupplierCharacterId = supplierCharacterId;
            GoalDefinition = goalDefinition;
            ItemDefinition = itemDefinition;
            Quantity = quantity;
            DestinationBaseEid = destinationBaseEid;
            Phase = phase;
            ExpiresAtUtc = expiresAtUtc;
        }

        public long ReservationId { get; }
        public long RequestId { get; }
        public int RequesterCharacterId { get; }
        public int SupplierCharacterId { get; }
        public int GoalDefinition { get; }
        public int ItemDefinition { get; }
        public int Quantity { get; }
        public long DestinationBaseEid { get; }
        public string Phase { get; }
        public DateTime ExpiresAtUtc { get; }
        public bool IsListed => string.Equals(Phase, "Listed", StringComparison.OrdinalIgnoreCase);
    }

    public interface IAutonomousSupplyRequestStore
    {
        void Reconcile(
            int requesterCharacterId,
            int goalDefinition,
            long destinationBaseEid,
            IReadOnlyDictionary<int, long> requirements,
            DateTime expiresAtUtc);

        AutonomousSupplyReservation LoadActiveReservation(int supplierCharacterId);

        AutonomousSupplyReservation Claim(
            int supplierCharacterId,
            int itemDefinition,
            long destinationBaseEid,
            int maximumQuantity,
            DateTime expiresAtUtc);

        void Renew(long reservationId, int supplierCharacterId, DateTime expiresAtUtc);
        void MarkListed(long reservationId, int supplierCharacterId, int quantity, DateTime expiresAtUtc);
        void Release(long reservationId, int supplierCharacterId, string reason);
    }

    /// <summary>
    /// Stores strategic demand and supplier intent only. These records never
    /// move an item, debit a wallet, create a market order, or authorize a
    /// gameplay action.
    /// </summary>
    public sealed class DatabaseAutonomousSupplyRequestStore : IAutonomousSupplyRequestStore
    {
        public void Reconcile(
            int requesterCharacterId,
            int goalDefinition,
            long destinationBaseEid,
            IReadOnlyDictionary<int, long> requirements,
            DateTime expiresAtUtc)
        {
            ValidateDemand(requesterCharacterId, goalDefinition, destinationBaseEid, requirements);

            using (var scope = Db.CreateTransaction())
            {
                Db.Query()
                    .CommandText(@"update dbo.ai_supply_request with (updlock, serializable)
                                  set active = 0, updated_at = sysutcdatetime()
                                  where requester_character_id = @requesterCharacterId
                                    and goal_definition = @goalDefinition
                                    and active = 1;")
                    .SetParameter("@requesterCharacterId", requesterCharacterId)
                    .SetParameter("@goalDefinition", goalDefinition)
                    .ExecuteNonQuery();

                foreach (KeyValuePair<int, long> requirement in requirements.OrderBy(item => item.Key))
                {
                    Db.Query()
                        .CommandText(@"update dbo.ai_supply_request with (updlock, serializable)
                                      set quantity_required = @quantityRequired,
                                          destination_base_eid = @destinationBaseEid,
                                          active = 1,
                                          expires_at = @expiresAt,
                                          updated_at = sysutcdatetime()
                                      where requester_character_id = @requesterCharacterId
                                        and goal_definition = @goalDefinition
                                        and item_definition = @itemDefinition;
                                      if @@rowcount = 0
                                      begin
                                          insert dbo.ai_supply_request
                                              (requester_character_id, goal_definition,
                                               item_definition, quantity_required,
                                               destination_base_eid, active, expires_at)
                                          values
                                              (@requesterCharacterId, @goalDefinition,
                                               @itemDefinition, @quantityRequired,
                                               @destinationBaseEid, 1, @expiresAt);
                                      end;")
                        .SetParameter("@requesterCharacterId", requesterCharacterId)
                        .SetParameter("@goalDefinition", goalDefinition)
                        .SetParameter("@itemDefinition", requirement.Key)
                        .SetParameter("@quantityRequired", requirement.Value)
                        .SetParameter("@destinationBaseEid", destinationBaseEid)
                        .SetParameter("@expiresAt", expiresAtUtc)
                        .ExecuteNonQuery();
                }

                Db.Query()
                    .CommandText(@"update sr
                                  set active = 0, phase = 'Released', release_reason = 'request_reconciled',
                                      updated_at = sysutcdatetime()
                                  from dbo.ai_supply_reservation sr
                                  join dbo.ai_supply_request r on r.request_id = sr.request_id
                                  where r.requester_character_id = @requesterCharacterId
                                    and r.goal_definition = @goalDefinition
                                    and r.active = 0
                                    and sr.active = 1;")
                    .SetParameter("@requesterCharacterId", requesterCharacterId)
                    .SetParameter("@goalDefinition", goalDefinition)
                    .ExecuteNonQuery();
                scope.Complete();
            }
        }

        public AutonomousSupplyReservation LoadActiveReservation(int supplierCharacterId)
        {
            if (supplierCharacterId <= 0)
                throw new ArgumentOutOfRangeException(nameof(supplierCharacterId));

            return ReadReservation(Db.Query()
                .CommandText(ReservationSelect + @"
                              where sr.supplier_character_id = @supplierCharacterId
                                and sr.active = 1
                                and sr.expires_at > sysutcdatetime()
                                and r.active = 1
                                and r.expires_at > sysutcdatetime()
                              order by sr.reservation_id")
                .SetParameter("@supplierCharacterId", supplierCharacterId)
                .ExecuteSingleRow());
        }

        public AutonomousSupplyReservation Claim(
            int supplierCharacterId,
            int itemDefinition,
            long destinationBaseEid,
            int maximumQuantity,
            DateTime expiresAtUtc)
        {
            if (supplierCharacterId <= 0 || itemDefinition <= 0 ||
                destinationBaseEid <= 0 || maximumQuantity <= 0)
                throw new ArgumentOutOfRangeException(nameof(supplierCharacterId));

            using (var scope = Db.CreateTransaction())
            {
                Db.Query()
                    .CommandText(@"update sr
                                  set active = 0, phase = 'Released', release_reason = 'expired',
                                      updated_at = sysutcdatetime()
                                  from dbo.ai_supply_reservation sr
                                  left join dbo.ai_supply_request r on r.request_id = sr.request_id
                                  where sr.supplier_character_id = @supplierCharacterId
                                    and sr.active = 1
                                    and (sr.expires_at <= sysutcdatetime()
                                         or r.request_id is null
                                         or r.active = 0
                                         or r.expires_at <= sysutcdatetime());")
                    .SetParameter("@supplierCharacterId", supplierCharacterId)
                    .ExecuteNonQuery();

                var request = Db.Query()
                    .CommandText(@"select top 1 r.request_id, r.requester_character_id,
                                         r.goal_definition, r.item_definition,
                                         r.quantity_required, r.destination_base_eid,
                                         coalesce((select sum(cast(sr.quantity as bigint))
                                                   from dbo.ai_supply_reservation sr
                                                   where sr.request_id = r.request_id
                                                     and sr.active = 1), 0) as reserved_quantity
                                  from dbo.ai_supply_request r with (updlock, holdlock)
                                  where r.active = 1
                                    and r.expires_at > sysutcdatetime()
                                    and r.item_definition = @itemDefinition
                                    and r.destination_base_eid = @destinationBaseEid
                                    and r.requester_character_id <> @supplierCharacterId
                                    and r.quantity_required >
                                        coalesce((select sum(cast(sr.quantity as bigint))
                                                  from dbo.ai_supply_reservation sr
                                                  where sr.request_id = r.request_id
                                                    and sr.active = 1), 0)
                                  order by r.created_at, r.request_id")
                    .SetParameter("@supplierCharacterId", supplierCharacterId)
                    .SetParameter("@itemDefinition", itemDefinition)
                    .SetParameter("@destinationBaseEid", destinationBaseEid)
                    .ExecuteSingleRow();
                if (request == null)
                {
                    scope.Complete();
                    return null;
                }

                long available = request.GetValue<long>("quantity_required") -
                                 request.GetValue<long>("reserved_quantity");
                int quantity = (int)Math.Min(maximumQuantity, Math.Min(available, int.MaxValue));
                if (quantity <= 0)
                {
                    scope.Complete();
                    return null;
                }

                long reservationId = Db.Query()
                    .CommandText(@"insert dbo.ai_supply_reservation
                                      (request_id, supplier_character_id, quantity,
                                       phase, active, expires_at)
                                  values
                                      (@requestId, @supplierCharacterId, @quantity,
                                       'Reserved', 1, @expiresAt);
                                  select cast(scope_identity() as bigint);")
                    .SetParameter("@requestId", request.GetValue<long>("request_id"))
                    .SetParameter("@supplierCharacterId", supplierCharacterId)
                    .SetParameter("@quantity", quantity)
                    .SetParameter("@expiresAt", expiresAtUtc)
                    .ExecuteScalar<long>();
                scope.Complete();

                return new AutonomousSupplyReservation(
                    reservationId,
                    request.GetValue<long>("request_id"),
                    request.GetValue<int>("requester_character_id"),
                    supplierCharacterId,
                    request.GetValue<int>("goal_definition"),
                    request.GetValue<int>("item_definition"),
                    quantity,
                    request.GetValue<long>("destination_base_eid"),
                    "Reserved",
                    expiresAtUtc);
            }
        }

        public void Renew(long reservationId, int supplierCharacterId, DateTime expiresAtUtc)
        {
            UpdateReservation(
                reservationId,
                supplierCharacterId,
                "expires_at = @expiresAt, updated_at = sysutcdatetime()",
                query => query.SetParameter("@expiresAt", expiresAtUtc));
        }

        public void MarkListed(
            long reservationId,
            int supplierCharacterId,
            int quantity,
            DateTime expiresAtUtc)
        {
            if (quantity <= 0)
                throw new ArgumentOutOfRangeException(nameof(quantity));
            UpdateReservation(
                reservationId,
                supplierCharacterId,
                @"quantity = case when quantity < @quantity then quantity else @quantity end,
                  phase = 'Listed', expires_at = @expiresAt,
                  updated_at = sysutcdatetime()",
                query => query
                    .SetParameter("@quantity", quantity)
                    .SetParameter("@expiresAt", expiresAtUtc));
        }

        public void Release(long reservationId, int supplierCharacterId, string reason)
        {
            UpdateReservation(
                reservationId,
                supplierCharacterId,
                @"active = 0, phase = 'Released', release_reason = @reason,
                  updated_at = sysutcdatetime()",
                query => query.SetParameter("@reason", reason));
        }

        private static void UpdateReservation(
            long reservationId,
            int supplierCharacterId,
            string assignments,
            Func<DbQuery, DbQuery> configure)
        {
            if (reservationId <= 0 || supplierCharacterId <= 0)
                throw new ArgumentOutOfRangeException(nameof(reservationId));
            DbQuery query = Db.Query()
                .CommandText($@"update dbo.ai_supply_reservation with (updlock)
                               set {assignments}
                               where reservation_id = @reservationId
                                 and supplier_character_id = @supplierCharacterId
                                 and active = 1;
                               select @@rowcount;")
                .SetParameter("@reservationId", reservationId)
                .SetParameter("@supplierCharacterId", supplierCharacterId);
            int rows = configure(query).ExecuteScalar<int>();
            if (rows != 1)
                throw new InvalidOperationException("The active supply reservation was not found for its supplier.");
        }

        private static AutonomousSupplyReservation ReadReservation(System.Data.IDataRecord record)
        {
            return record == null
                ? null
                : new AutonomousSupplyReservation(
                    record.GetValue<long>("reservation_id"),
                    record.GetValue<long>("request_id"),
                    record.GetValue<int>("requester_character_id"),
                    record.GetValue<int>("supplier_character_id"),
                    record.GetValue<int>("goal_definition"),
                    record.GetValue<int>("item_definition"),
                    record.GetValue<int>("quantity"),
                    record.GetValue<long>("destination_base_eid"),
                    record.GetValue<string>("phase"),
                    record.GetValue<DateTime>("expires_at"));
        }

        private static void ValidateDemand(
            int requesterCharacterId,
            int goalDefinition,
            long destinationBaseEid,
            IReadOnlyDictionary<int, long> requirements)
        {
            if (requesterCharacterId <= 0 || goalDefinition <= 0 || destinationBaseEid <= 0)
                throw new ArgumentOutOfRangeException(nameof(requesterCharacterId));
            if (requirements == null)
                throw new ArgumentNullException(nameof(requirements));
            if (requirements.Any(item => item.Key <= 0 || item.Value <= 0))
                throw new ArgumentOutOfRangeException(nameof(requirements));
        }

        private const string ReservationSelect = @"select sr.reservation_id, sr.request_id,
                                                          r.requester_character_id,
                                                          sr.supplier_character_id,
                                                          r.goal_definition, r.item_definition,
                                                          sr.quantity, r.destination_base_eid,
                                                          sr.phase, sr.expires_at
                                                   from dbo.ai_supply_reservation sr
                                                   join dbo.ai_supply_request r
                                                     on r.request_id = sr.request_id";
    }

    public interface IAutonomousSupplyCoordinator
    {
        void PublishDemand(
            int requesterCharacterId,
            int goalDefinition,
            long destinationBaseEid,
            IReadOnlyDictionary<int, long> requirements,
            AutonomousSupplyDemandOptions options,
            DateTime utcNow);

        AutonomousSupplyReservation Reserve(
            int supplierCharacterId,
            int itemDefinition,
            long destinationBaseEid,
            AutonomousSupplyFulfillmentOptions options,
            DateTime utcNow);

        AutonomousSupplyReservation Load(int supplierCharacterId);
        void Renew(AutonomousSupplyReservation reservation, TimeSpan lifetime, DateTime utcNow);
        void MarkListed(AutonomousSupplyReservation reservation, int quantity, TimeSpan lifetime, DateTime utcNow);
        void Release(AutonomousSupplyReservation reservation, string reason);
    }

    public sealed class AutonomousSupplyCoordinator : IAutonomousSupplyCoordinator
    {
        private readonly IAutonomousSupplyRequestStore _store;

        public AutonomousSupplyCoordinator(IAutonomousSupplyRequestStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public void PublishDemand(
            int requesterCharacterId,
            int goalDefinition,
            long destinationBaseEid,
            IReadOnlyDictionary<int, long> requirements,
            AutonomousSupplyDemandOptions options,
            DateTime utcNow)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            if (!options.Enabled)
                return;
            _store.Reconcile(
                requesterCharacterId,
                goalDefinition,
                destinationBaseEid,
                requirements,
                utcNow.AddMinutes(options.RequestLifetimeMinutes));
        }

        public AutonomousSupplyReservation Reserve(
            int supplierCharacterId,
            int itemDefinition,
            long destinationBaseEid,
            AutonomousSupplyFulfillmentOptions options,
            DateTime utcNow)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            if (!options.Enabled)
                return null;
            return _store.Claim(
                supplierCharacterId,
                itemDefinition,
                destinationBaseEid,
                options.MaximumReservationQuantity,
                utcNow.AddMinutes(options.ReservationLifetimeMinutes));
        }

        public AutonomousSupplyReservation Load(int supplierCharacterId) =>
            _store.LoadActiveReservation(supplierCharacterId);

        public void Renew(AutonomousSupplyReservation reservation, TimeSpan lifetime, DateTime utcNow)
        {
            if (reservation == null)
                throw new ArgumentNullException(nameof(reservation));
            _store.Renew(reservation.ReservationId, reservation.SupplierCharacterId, utcNow.Add(lifetime));
        }

        public void MarkListed(
            AutonomousSupplyReservation reservation,
            int quantity,
            TimeSpan lifetime,
            DateTime utcNow)
        {
            if (reservation == null)
                throw new ArgumentNullException(nameof(reservation));
            _store.MarkListed(
                reservation.ReservationId,
                reservation.SupplierCharacterId,
                quantity,
                utcNow.Add(lifetime));
        }

        public void Release(AutonomousSupplyReservation reservation, string reason)
        {
            if (reservation == null)
                throw new ArgumentNullException(nameof(reservation));
            _store.Release(reservation.ReservationId, reservation.SupplierCharacterId, reason);
        }
    }

    public enum AutonomousSupplyFulfillmentResult
    {
        Disabled,
        NoRequest,
        WaitingForCargo,
        WaitingForListedOrder,
        Listed
    }

    public sealed class AutonomousSupplyFulfillment
    {
        private AutonomousSupplyFulfillment(
            AutonomousSupplyFulfillmentResult result,
            int definition = 0,
            int quantity = 0,
            double unitPrice = 0)
        {
            Result = result;
            Definition = definition;
            Quantity = quantity;
            UnitPrice = unitPrice;
        }

        public AutonomousSupplyFulfillmentResult Result { get; }
        public int Definition { get; }
        public int Quantity { get; }
        public double UnitPrice { get; }

        public static AutonomousSupplyFulfillment For(AutonomousSupplyFulfillmentResult result) =>
            new AutonomousSupplyFulfillment(result);

        public static AutonomousSupplyFulfillment Listed(int definition, int quantity, double unitPrice) =>
            new AutonomousSupplyFulfillment(
                AutonomousSupplyFulfillmentResult.Listed,
                definition,
                quantity,
                unitPrice);
    }

    public interface IAutonomousSupplyFulfillmentService
    {
        AutonomousSupplyFulfillment ListReserved(
            GameActionContext context,
            MaterialType material,
            AutonomousSupplyFulfillmentOptions options,
            AutonomousMarketOptions marketOptions);
    }

    /// <summary>
    /// Turns one strategic reservation into one ordinary public market listing.
    /// The market action remains authoritative for docking, ownership, item
    /// quantity, fees, order limits, price policy, transactions, and persistence.
    /// </summary>
    public sealed class AutonomousSupplyFulfillmentService : IAutonomousSupplyFulfillmentService
    {
        private readonly IAutonomousSupplyCoordinator _coordination;
        private readonly IAutonomousCargoService _cargo;
        private readonly IAutonomousMarketObservationService _market;
        private readonly IMarketCreateSellOrderActionService _sellOrders;
        private readonly IMarketOrderRepository _orders;
        private readonly MaterialHelper _materials;

        public AutonomousSupplyFulfillmentService(
            IAutonomousSupplyCoordinator coordination,
            IAutonomousCargoService cargo,
            IAutonomousMarketObservationService market,
            IMarketCreateSellOrderActionService sellOrders,
            IMarketOrderRepository orders,
            MaterialHelper materials)
        {
            _coordination = coordination ?? throw new ArgumentNullException(nameof(coordination));
            _cargo = cargo ?? throw new ArgumentNullException(nameof(cargo));
            _market = market ?? throw new ArgumentNullException(nameof(market));
            _sellOrders = sellOrders ?? throw new ArgumentNullException(nameof(sellOrders));
            _orders = orders ?? throw new ArgumentNullException(nameof(orders));
            _materials = materials ?? throw new ArgumentNullException(nameof(materials));
        }

        public AutonomousSupplyFulfillment ListReserved(
            GameActionContext context,
            MaterialType material,
            AutonomousSupplyFulfillmentOptions options,
            AutonomousMarketOptions marketOptions)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            if (marketOptions == null)
                throw new ArgumentNullException(nameof(marketOptions));
            if (!options.Enabled)
                return AutonomousSupplyFulfillment.For(AutonomousSupplyFulfillmentResult.Disabled);
            context.Actor.IsDocked.ThrowIfFalse(ErrorCodes.CharacterHasToBeDocked);

            DateTime now = DateTime.UtcNow;
            int definition = _materials.GetMaterialInfo(material).EntityDefault.Definition;
            long baseEid = context.Actor.CurrentDockingBaseEid;
            Market currentMarket = context.Actor.GetCurrentDockingBase().GetMarketOrThrow();
            AutonomousSupplyReservation reservation = _coordination.Load(context.Actor.Id);
            if (reservation != null &&
                (reservation.ItemDefinition != definition || reservation.DestinationBaseEid != baseEid))
            {
                _coordination.Release(reservation, "supplier_location_or_material_changed");
                reservation = null;
            }

            if (reservation?.IsListed == true)
            {
                bool listingExists = _orders.GetByCharacter(context.Actor).Any(order =>
                    order.isSell && !order.isVendorItem && order.quantity > 0 &&
                    order.itemDefinition == reservation.ItemDefinition &&
                    order.marketEID == currentMarket.Eid);
                if (listingExists)
                {
                    _coordination.Renew(
                        reservation,
                        TimeSpan.FromMinutes(options.ReservationLifetimeMinutes),
                        now);
                    return AutonomousSupplyFulfillment.For(
                        AutonomousSupplyFulfillmentResult.WaitingForListedOrder);
                }

                _coordination.Release(reservation, "listed_order_no_longer_active");
                reservation = null;
            }

            if (reservation == null)
                reservation = _coordination.Reserve(
                    context.Actor.Id,
                    definition,
                    baseEid,
                    options,
                    now);
            if (reservation == null)
                return AutonomousSupplyFulfillment.For(AutonomousSupplyFulfillmentResult.NoRequest);

            AutonomousCargoSnapshot cargo = _cargo.Observe(context);
            AutonomousCargoItemSnapshot item = cargo.RawMaterials
                .Where(candidate => candidate.Definition == reservation.ItemDefinition)
                .OrderBy(candidate => candidate.Eid)
                .FirstOrDefault();
            if (item == null)
            {
                _coordination.Renew(
                    reservation,
                    TimeSpan.FromMinutes(options.ReservationLifetimeMinutes),
                    now);
                return AutonomousSupplyFulfillment.For(AutonomousSupplyFulfillmentResult.WaitingForCargo);
            }

            int quantity = Math.Min(item.Quantity, reservation.Quantity);
            AutonomousMarketQuote quote = _market.Observe(context, item.Definition);
            double unitPrice = AutonomousMarketPricingPolicy.SelectUnitPrice(
                marketOptions.MinimumUnitPrice,
                marketOptions.ListPriceFactor,
                quote.BestBuyPrice,
                quote.AveragePrice);
            using (var scope = Db.CreateTransaction())
            {
                _sellOrders.Execute(context, new MarketCreateSellOrderAction(
                    item.Eid,
                    marketOptions.OrderDurationHours,
                    unitPrice,
                    quantity,
                    false,
                    cargo.ContainerEid,
                    false,
                    0));
                _coordination.MarkListed(
                    reservation,
                    quantity,
                    TimeSpan.FromMinutes(options.ReservationLifetimeMinutes),
                    now);
                scope.Complete();
            }
            return AutonomousSupplyFulfillment.Listed(item.Definition, quantity, unitPrice);
        }
    }
}
