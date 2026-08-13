using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Perpetuum.Data;
using Perpetuum.Services.Actions;
using Perpetuum.Services.MissionEngine;
using Perpetuum.Services.MissionEngine.AdministratorObjects;
using Perpetuum.Services.MissionEngine.Missions;
using Perpetuum.Services.MissionEngine.MissionProcessorObjects;
using Perpetuum.Services.MissionEngine.MissionTargets;

namespace Perpetuum.Services.Autonomous
{
    public sealed class AutonomousMissionTargetSnapshot
    {
        public AutonomousMissionTargetSnapshot(
            int targetId,
            MissionTargetType type,
            int targetOrder,
            bool current,
            bool completed,
            int progress,
            int definition,
            int quantity,
            long destinationEid,
            int zoneId = -1,
            Position? targetPosition = null,
            int targetPositionRange = 0)
        {
            TargetId = targetId;
            Type = type;
            TargetOrder = targetOrder;
            Current = current;
            Completed = completed;
            Progress = progress;
            Definition = definition;
            Quantity = quantity;
            DestinationEid = destinationEid;
            ZoneId = zoneId;
            TargetPosition = targetPosition;
            TargetPositionRange = targetPositionRange;
        }

        public int TargetId { get; }
        public MissionTargetType Type { get; }
        public int TargetOrder { get; }
        public bool Current { get; }
        public bool Completed { get; }
        public int Progress { get; }
        public int Definition { get; }
        public int Quantity { get; }
        public int RemainingQuantity => Math.Max(0, Quantity - Progress);
        public long DestinationEid { get; }
        public int ZoneId { get; }
        public Position? TargetPosition { get; }
        public int TargetPositionRange { get; }
        public bool HasMapTarget => ZoneId >= 0 && TargetPosition.HasValue && TargetPositionRange > 0;
    }

    public sealed class AutonomousMissionSnapshot
    {
        public AutonomousMissionSnapshot(
            Guid missionGuid,
            int missionId,
            MissionCategory category,
            int level,
            long sourceEid,
            DateTime expires,
            IEnumerable<AutonomousMissionTargetSnapshot> targets)
        {
            MissionGuid = missionGuid;
            MissionId = missionId;
            Category = category;
            Level = level;
            SourceEid = sourceEid;
            Expires = expires;
            Targets = (targets ?? Enumerable.Empty<AutonomousMissionTargetSnapshot>()).ToArray();
        }

        public Guid MissionGuid { get; }
        public int MissionId { get; }
        public MissionCategory Category { get; }
        public int Level { get; }
        public long SourceEid { get; }
        public DateTime Expires { get; }
        public IReadOnlyList<AutonomousMissionTargetSnapshot> Targets { get; }
        public AutonomousMissionTargetSnapshot CurrentTarget =>
            Targets.FirstOrDefault(target => target.Current && !target.Completed);
    }

    public sealed class AutonomousMissionCompletion
    {
        public AutonomousMissionCompletion(Guid missionGuid, bool finished, bool succeeded)
        {
            MissionGuid = missionGuid;
            Finished = finished;
            Succeeded = succeeded;
        }

        public Guid MissionGuid { get; }
        public bool Finished { get; }
        public bool Succeeded { get; }
    }

    public interface IAutonomousMissionObservationService
    {
        IReadOnlyList<AutonomousMissionSnapshot> ObserveRunning(GameActionContext context);
        AutonomousMissionCompletion ObserveCompletion(GameActionContext context, Guid missionGuid);
    }

    /// <summary>
    /// Projects the same character-bound running mission state exposed by the
    /// mission list client command. Finished-state lookup is restricted by
    /// both mission GUID and acting character ID.
    /// </summary>
    public sealed class AutonomousMissionObservationService : IAutonomousMissionObservationService
    {
        private readonly MissionProcessor _missionProcessor;

        public AutonomousMissionObservationService(MissionProcessor missionProcessor)
        {
            _missionProcessor = missionProcessor ?? throw new ArgumentNullException(nameof(missionProcessor));
        }

        public IReadOnlyList<AutonomousMissionSnapshot> ObserveRunning(GameActionContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (!_missionProcessor.MissionAdministrator.GetMissionInProgressCollector(
                    context.Actor,
                    out MissionInProgressCollector collector))
                return Array.Empty<AutonomousMissionSnapshot>();
            return collector.GetMissionsInProgress()
                .Select(Project)
                .OrderBy(mission => mission.MissionGuid)
                .ToArray();
        }

        public AutonomousMissionCompletion ObserveCompletion(GameActionContext context, Guid missionGuid)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            missionGuid.ThrowIfEqual(Guid.Empty, ErrorCodes.SyntaxError);
            IDataRecord record = Db.Query()
                .CommandText(@"select finished, succeeded
                               from dbo.missionlog
                               where characterid = @characterId
                                 and missionguid = @missionGuid")
                .SetParameter("@characterId", context.Actor.Id)
                .SetParameter("@missionGuid", missionGuid)
                .ExecuteSingleRow();
            return record == null
                ? null
                : new AutonomousMissionCompletion(
                    missionGuid,
                    !record.IsDBNull(record.GetOrdinal("finished")),
                    !record.IsDBNull(record.GetOrdinal("succeeded")) && record.GetValue<bool>("succeeded"));
        }

        private static AutonomousMissionSnapshot Project(MissionInProgress mission)
        {
            return new AutonomousMissionSnapshot(
                mission.missionGuid,
                mission.MissionId,
                mission.myMission.missionCategory,
                mission.MissionLevel,
                mission.myLocation.LocationEid,
                mission.expire,
                mission.GetTargetsInProgress().Select(Project));
        }

        private static AutonomousMissionTargetSnapshot Project(MissionTargetInProgress target)
        {
            MissionTarget definition = target.myTarget;
            return new AutonomousMissionTargetSnapshot(
                target.MissionTargetId,
                target.TargetType,
                target.TargetOrder,
                target.IsMyTurn,
                target.completed,
                target.progressCount,
                definition.Definition,
                definition.Quantity,
                definition.MissionStructureEid,
                definition.ZoneId,
                definition.ValidPositionSet ? definition.targetPosition : (Position?)null,
                definition.TargetPositionRange);
        }
    }
}
