using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using Perpetuum.Accounting.Characters;
using Perpetuum.Data;
using Perpetuum.Log;
using Perpetuum.Services.MissionEngine;
using Perpetuum.Services.MissionEngine.AdministratorObjects;
using Perpetuum.Services.MissionEngine.MissionDataCacheObjects;
using Perpetuum.Services.MissionEngine.MissionProcessorObjects;
using Perpetuum.Services.MissionEngine.Missions;
using Perpetuum.Services.MissionEngine.MissionStructures;
using Perpetuum.Services.MissionEngine.MissionTargets;

namespace Perpetuum.Services.Onboarding
{
    public sealed class FieldCertificationEnrollment : IFieldCertificationEnrollment
    {
        private readonly MissionDataCache _missionDataCache;
        private readonly MissionProcessor _missionProcessor;
        private readonly FieldCertificationMissionContract _contract;

        public FieldCertificationEnrollment(
            MissionDataCache missionDataCache,
            MissionProcessor missionProcessor,
            FieldCertificationMissionContract contract)
        {
            _missionDataCache = missionDataCache ?? throw new ArgumentNullException(nameof(missionDataCache));
            _missionProcessor = missionProcessor ?? throw new ArgumentNullException(nameof(missionProcessor));
            _contract = contract ?? throw new ArgumentNullException(nameof(contract));
        }

        public void Schedule(Character character, long trainingDockingBaseEid)
        {
            if (character == null || character == Character.None)
                throw new ArgumentNullException(nameof(character));

            // Character creation invokes this immediately after its transaction commits. Do not
            // capture that completed ambient transaction into the background enrollment task.
            using (ExecutionContext.SuppressFlow())
            {
                Task.Run(() => Enroll(character, trainingDockingBaseEid)).LogExceptions();
            }
        }

        private void Enroll(Character character, long trainingDockingBaseEid)
        {
            if (!_missionDataCache.GetMissionByName(
                    FieldCertificationMissionContract.MissionName,
                    out Mission mission))
            {
                LogSkipped(character, "mission_not_found");
                return;
            }

            MissionLocation location = _missionDataCache.GetLocationByEid(trainingDockingBaseEid);
            if (location == null)
            {
                LogSkipped(character, "training_location_not_found");
                return;
            }

            MissionTargetType[] targetTypes = mission.Targets
                .OrderBy(target => target.targetOrder)
                .ThenBy(target => target.displayOrder)
                .Select(target => target.Type)
                .ToArray();
            if (!_contract.TryValidate(
                    mission.Name,
                    location.zoneId,
                    mission.rewardFee,
                    targetTypes,
                    out string failureReason))
            {
                LogSkipped(character, failureReason);
                return;
            }

            if (_missionProcessor.MissionAdministrator.GetMissionInProgressCollector(
                    character,
                    out MissionInProgressCollector collector) &&
                collector.IsMissionCurrentlyRunning(mission.id))
            {
                LogSkipped(character, "already_running");
                return;
            }

            using (var scope = Db.CreateTransaction())
            {
                bool started = _missionProcessor.TriggeredMissionStart(
                    character,
                    false,
                    mission.id,
                    location,
                    mission.MissionLevel,
                    out MissionInProgress missionInProgress);
                if (!started)
                {
                    LogSkipped(character, "mission_start_rejected");
                    return;
                }

                Transaction.Current.OnCommited(() =>
                {
                    Logger.Info(
                        $"onboarding character_id={character.Id} event=field_certification_enrolled " +
                        $"mission_id={mission.id} mission_guid={missionInProgress.missionGuid} " +
                        $"zone_id={location.zoneId} reward_fee={mission.rewardFee}");
                    _missionProcessor.SendRunningMissionList(character);
                });
                scope.Complete();
            }
        }

        private static void LogSkipped(Character character, string reason)
        {
            Logger.Warning(
                $"onboarding character_id={character.Id} event=field_certification_skipped " +
                $"reason={reason}");
        }
    }
}
