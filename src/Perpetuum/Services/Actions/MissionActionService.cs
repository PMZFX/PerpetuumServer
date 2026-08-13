using System;
using System.Collections.Generic;
using System.Linq;
using Perpetuum.Data;
using Perpetuum.EntityFramework;
using Perpetuum.ExportedTypes;
using Perpetuum.Services.MissionEngine;
using Perpetuum.Services.MissionEngine.MissionDataCacheObjects;
using Perpetuum.Services.MissionEngine.MissionProcessorObjects;
using Perpetuum.Services.MissionEngine.MissionStructures;
using Perpetuum.Units.FieldTerminals;

namespace Perpetuum.Services.Actions
{
    public sealed class MissionLocationAction
    {
        public MissionLocationAction(long locationEid = 0)
        {
            LocationEid = locationEid;
        }

        public long LocationEid { get; }
    }

    public sealed class MissionStartAction
    {
        public MissionStartAction(MissionCategory category, int level, long locationEid = 0)
        {
            Category = category;
            Level = level;
            LocationEid = locationEid;
        }

        public MissionCategory Category { get; }
        public int Level { get; }
        public long LocationEid { get; }
    }

    public sealed class MissionGuidAction
    {
        public MissionGuidAction(Guid missionGuid, long locationEid = 0)
        {
            MissionGuid = missionGuid;
            LocationEid = locationEid;
        }

        public Guid MissionGuid { get; }
        public long LocationEid { get; }
    }

    public sealed class MissionAvailability
    {
        public MissionAvailability(
            MissionCategory category,
            int level,
            bool random,
            int availableCount,
            bool standingBlocked)
        {
            Category = category;
            Level = level;
            Random = random;
            AvailableCount = availableCount;
            StandingBlocked = standingBlocked;
        }

        public MissionCategory Category { get; }
        public int Level { get; }
        public bool Random { get; }
        public int AvailableCount { get; }
        public bool StandingBlocked { get; }
        public bool Available => AvailableCount > 0;
    }

    public sealed class MissionOptionsResult
    {
        public MissionOptionsResult(
            int locationId,
            IDictionary<string, object> payload,
            IReadOnlyList<MissionAvailability> options)
        {
            LocationId = locationId;
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
            Options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public int LocationId { get; }
        public IDictionary<string, object> Payload { get; }
        public IReadOnlyList<MissionAvailability> Options { get; }
    }

    public sealed class MissionStartResult
    {
        public MissionStartResult(Guid missionGuid, IDictionary<string, object> payload)
        {
            missionGuid.ThrowIfEqual(Guid.Empty, ErrorCodes.ConsistencyError);
            MissionGuid = missionGuid;
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        }

        public Guid MissionGuid { get; }
        public IDictionary<string, object> Payload { get; }
    }

    public interface IMissionActionService
    {
        MissionOptionsResult ObserveOptions(GameActionContext context, MissionLocationAction action);
        MissionStartResult Start(GameActionContext context, MissionStartAction action);
        void Deliver(GameActionContext context, MissionGuidAction action);
        void Abort(GameActionContext context, MissionGuidAction action);
    }

    public sealed class MissionActionService : IMissionActionService
    {
        private readonly MissionProcessor _missionProcessor;
        private readonly MissionDataCache _missionDataCache;
        private readonly IEntityRepository _entityRepository;
        private readonly IGameActionAudit _audit;

        public MissionActionService(
            MissionProcessor missionProcessor,
            MissionDataCache missionDataCache,
            IEntityRepository entityRepository,
            IGameActionAudit audit)
        {
            _missionProcessor = missionProcessor ?? throw new ArgumentNullException(nameof(missionProcessor));
            _missionDataCache = missionDataCache ?? throw new ArgumentNullException(nameof(missionDataCache));
            _entityRepository = entityRepository ?? throw new ArgumentNullException(nameof(entityRepository));
            _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        }

        public MissionOptionsResult ObserveOptions(GameActionContext context, MissionLocationAction action)
        {
            return _audit.Execute(context, "missionOptionsObserve", () => ObserveOptionsCore(context, action));
        }

        public MissionStartResult Start(GameActionContext context, MissionStartAction action)
        {
            return _audit.Execute(context, "missionStart", () => StartCore(context, action));
        }

        public void Deliver(GameActionContext context, MissionGuidAction action)
        {
            _audit.Execute(context, "missionDeliver", () =>
            {
                Validate(context, action);
                using (var scope = Db.CreateTransaction())
                {
                    int locationId = ResolveDeliveryLocationId(context, action.LocationEid);
                    _missionProcessor.DeliverSingleMission(context.Actor, action.MissionGuid, locationId);
                    scope.Complete();
                }
            });
        }

        public void Abort(GameActionContext context, MissionGuidAction action)
        {
            _audit.Execute(context, "missionAbort", () =>
            {
                Validate(context, action);
                using (var scope = Db.CreateTransaction())
                {
                    _missionProcessor.AbortMissionByRequest(
                        context.Actor,
                        action.MissionGuid,
                        ErrorCodes.MissionAbortedByOwner);
                    scope.Complete();
                }
            });
        }

        private MissionOptionsResult ObserveOptionsCore(GameActionContext context, MissionLocationAction action)
        {
            Validate(context, action);
            MissionLocation location = ResolveOptionsLocation(context, action.LocationEid);
            IDictionary<string, object> payload = _missionProcessor.GetOptions(context.Actor, location);
            return new MissionOptionsResult(location.id, payload, MissionOptionProjection.Parse(payload));
        }

        private MissionStartResult StartCore(GameActionContext context, MissionStartAction action)
        {
            Validate(context, action);
            int category = (int)action.Category;
            Enum.IsDefined(typeof(MissionCategory), category)
                .ThrowIfFalse(ErrorCodes.MissionCategoryNotDefined);
            int level = action.Level.Clamp(-1, 9);

            using (var scope = Db.CreateTransaction())
            {
                MissionLocation location = context.Actor.IsDocked
                    ? _missionDataCache.GetLocationByEid(context.Actor.CurrentDockingBaseEid)
                        .ThrowIfNull(ErrorCodes.ItemNotFound)
                    : _missionDataCache.GetLocationByEid(action.LocationEid)
                        .ThrowIfNull(ErrorCodes.ItemNotFound);
                IDictionary<string, object> result = _missionProcessor.MissionStartForRequest(
                    context.Actor,
                    action.Category,
                    level,
                    location);
                Guid missionGuid = ReadMissionGuid(result);
                scope.Complete();
                return new MissionStartResult(missionGuid, result);
            }
        }

        private static Guid ReadMissionGuid(IDictionary<string, object> payload)
        {
            IDictionary<string, object> mission = payload.GetValue<IDictionary<string, object>>(k.mission);
            string guid = mission.GetValue<string>(k.guid);
            Guid.TryParse(guid, out Guid result).ThrowIfFalse(ErrorCodes.ConsistencyError);
            return result;
        }

        private MissionLocation ResolveOptionsLocation(GameActionContext context, long locationEid)
        {
            if (context.Actor.IsDocked)
                return _missionDataCache.GetLocationByEid(context.Actor.CurrentDockingBaseEid)
                    .ThrowIfNull(ErrorCodes.ItemNotFound);

            FieldTerminal terminal = _entityRepository.Load(locationEid)
                .ThrowIfNull(ErrorCodes.TargetNotFound)
                .ThrowIfNotType<FieldTerminal>(ErrorCodes.MissionNotAvailable);
            terminal.ED.Name.ThrowIfEqual(
                DefinitionNames.FIELD_TERMINAL_RALLY,
                ErrorCodes.MissionNotAvailable);
            return _missionDataCache.GetLocationByEid(locationEid)
                .ThrowIfNull(ErrorCodes.ItemNotFound);
        }

        private int ResolveDeliveryLocationId(GameActionContext context, long locationEid)
        {
            if (context.Actor.IsDocked)
                return 0;
            return _missionDataCache.GetLocationByEid(locationEid)
                .ThrowIfNull(ErrorCodes.InvalidMissionLocation)
                .id;
        }

        private static void Validate(GameActionContext context, object action)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (action == null)
                throw new ArgumentNullException(nameof(action));
        }

        private static void Validate(GameActionContext context, MissionGuidAction action)
        {
            Validate(context, (object)action);
            action.MissionGuid.ThrowIfEqual(Guid.Empty, ErrorCodes.SyntaxError);
        }

    }

    public static class MissionOptionProjection
    {
        public static IReadOnlyList<MissionAvailability> Parse(IDictionary<string, object> payload)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));
            var result = new List<MissionAvailability>();
            ParseGroup(payload, k.options, false, result);
            ParseGroup(payload, "randomMissions", true, result);
            return result
                .OrderBy(option => option.Random)
                .ThenBy(option => option.Category)
                .ThenBy(option => option.Level)
                .ToArray();
        }

        private static void ParseGroup(
            IDictionary<string, object> payload,
            string key,
            bool random,
            ICollection<MissionAvailability> result)
        {
            if (!payload.TryGetValue(key, out object rawGroup) ||
                !(rawGroup is IDictionary<string, object> group))
                return;

            foreach (IDictionary<string, object> option in group.Values.OfType<IDictionary<string, object>>())
            {
                int category = option.GetValue<int>(k.missionCategory);
                if (!Enum.IsDefined(typeof(MissionCategory), category))
                    continue;
                int available = random
                    ? option.GetValue<int>(k.oke)
                    : option.GetValue<int>("availableCount");
                result.Add(new MissionAvailability(
                    (MissionCategory)category,
                    option.GetValue<int>(k.missionLevel),
                    random,
                    available,
                    !random && option.GetValue<bool>("standingBlocked")));
            }
        }

    }
}
