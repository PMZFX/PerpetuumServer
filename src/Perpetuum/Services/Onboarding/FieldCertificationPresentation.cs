using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Perpetuum.Accounting.Characters;
using Perpetuum.Log;
using Perpetuum.Services.Mentoring;
using Perpetuum.Services.MissionEngine;
using Perpetuum.Services.MissionEngine.AdministratorObjects;
using Perpetuum.Services.MissionEngine.MissionDataCacheObjects;
using Perpetuum.Services.MissionEngine.MissionProcessorObjects;
using Perpetuum.Services.MissionEngine.Missions;
using Perpetuum.Services.MissionEngine.MissionStructures;
using Perpetuum.Services.Sessions;

namespace Perpetuum.Services.Onboarding
{
    /// <summary>
    /// Presents an already persisted onboarding mission after character selection. Enrollment and
    /// presentation are deliberately separate: login can race the asynchronous post-create start,
    /// so presentation retries for a short bounded interval without starting or mutating missions.
    /// </summary>
    public sealed class FieldCertificationPresentation
    {
        private const int MaximumAttempts = 8;
        private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(500);

        private readonly ISessionManager _sessionManager;
        private readonly Lazy<MissionDataCache> _missionDataCache;
        private readonly Lazy<MissionProcessor> _missionProcessor;
        private readonly Lazy<IFieldCertificationStarterLoadout> _starterLoadout;
        private readonly CombatCertificationMissionContract _combatContract;
        private readonly IMentorResponseSink _mentorResponseSink;
        private readonly MentorOptions _mentorOptions;

        public FieldCertificationPresentation(
            ISessionManager sessionManager,
            Lazy<MissionDataCache> missionDataCache,
            Lazy<MissionProcessor> missionProcessor,
            Lazy<IFieldCertificationStarterLoadout> starterLoadout,
            CombatCertificationMissionContract combatContract,
            IMentorResponseSink mentorResponseSink,
            MentorOptions mentorOptions)
        {
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            _missionDataCache = missionDataCache ?? throw new ArgumentNullException(nameof(missionDataCache));
            _missionProcessor = missionProcessor ?? throw new ArgumentNullException(nameof(missionProcessor));
            _starterLoadout = starterLoadout ?? throw new ArgumentNullException(nameof(starterLoadout));
            _combatContract = combatContract ?? throw new ArgumentNullException(nameof(combatContract));
            _mentorResponseSink = mentorResponseSink ?? throw new ArgumentNullException(nameof(mentorResponseSink));
            _mentorOptions = mentorOptions ?? throw new ArgumentNullException(nameof(mentorOptions));

            _sessionManager.SessionAdded += OnSessionAdded;
        }

        private void OnSessionAdded(ISession session)
        {
            session.CharacterSelected += OnCharacterSelected;
        }

        private void OnCharacterSelected(ISession session, Character character)
        {
            if (character == null || character == Character.None || !character.IsInTraining())
                return;

            // Character selection may run inside a request transaction. Keep that ambient
            // transaction out of the delayed, read-only presentation task.
            using (ExecutionContext.SuppressFlow())
            {
                Task.Run(() => PresentWhenAvailableAsync(session, character)).LogExceptions();
            }
        }

        private async Task PresentWhenAvailableAsync(ISession session, Character character)
        {
            await Task.Delay(InitialDelay).ConfigureAwait(false);

            for (int attempt = 1; attempt <= MaximumAttempts; attempt++)
            {
                if (session.Character == null || session.Character.Id != character.Id)
                    return;

                if (TryGetActiveOnboardingMission(character, out MissionInProgress activeMission))
                {
                    Present(session, character, activeMission, attempt);
                    return;
                }

                await Task.Delay(RetryDelay).ConfigureAwait(false);
            }

            Logger.Warning(
                $"onboarding character_id={character.Id} event=field_certification_not_presented " +
                $"reason=mission_not_running attempts={MaximumAttempts}");
        }

        private bool TryGetActiveOnboardingMission(
            Character character,
            out MissionInProgress missionInProgress)
        {
            missionInProgress = null;
            if (!_missionProcessor.Value.MissionAdministrator.GetMissionInProgressCollector(
                    character,
                    out MissionInProgressCollector collector))
            {
                return false;
            }

            MissionInProgress[] running = collector.GetMissionsInProgress().ToArray();
            if (_missionDataCache.Value.GetMissionByName(
                    CombatCertificationMissionContract.MissionName,
                    out Mission combat) &&
                IsValidCombatMission(combat))
            {
                missionInProgress = running.FirstOrDefault(candidate => candidate.MissionId == combat.id);
                if (missionInProgress != null)
                    return true;
            }

            if (!_missionDataCache.Value.GetMissionByName(
                    FieldCertificationMissionContract.MissionName,
                    out Mission transport))
            {
                return false;
            }

            missionInProgress = running.FirstOrDefault(candidate => candidate.MissionId == transport.id);
            return missionInProgress != null;
        }

        private bool IsValidCombatMission(Mission mission)
        {
            if (!_missionDataCache.Value.GetLocationById(
                    mission.LocationId,
                    out MissionLocation location))
            {
                Logger.Warning(
                    $"onboarding event=combat_certification_invalid reason=training_location_not_found " +
                    $"mission_id={mission.id}");
                return false;
            }

            MissionTargetType[] targetTypes = mission.Targets
                .OrderBy(target => target.targetOrder)
                .ThenBy(target => target.displayOrder)
                .Select(target => target.Type)
                .ToArray();
            if (_combatContract.TryValidate(
                    mission.Name,
                    location.zoneId,
                    mission.rewardFee,
                    targetTypes,
                    out string failureReason))
            {
                return true;
            }

            Logger.Warning(
                $"onboarding event=combat_certification_invalid reason={failureReason} " +
                $"mission_id={mission.id}");
            return false;
        }

        private void Present(
            ISession session,
            Character character,
            MissionInProgress missionInProgress,
            int attempt)
        {
            bool isTransport = string.Equals(
                missionInProgress.myMission.Name,
                FieldCertificationMissionContract.MissionName,
                StringComparison.Ordinal);
            if (isTransport)
            {
                _starterLoadout.Value.EnsureMissionCargoCapacity(character, character.GetActiveRobot());
            }

            session.SendMessage(
                Message.Builder
                    .SetCommand(Commands.MissionListRunning)
                    .WithData(_missionProcessor.Value.RunningMissionList(character)));

            string briefing = isTransport
                ? "Welcome aboard. Your first shift is a courier pickup, and I have assigned and " +
                  "activated a training Arkhe for the job. Open Assignments if you want to review " +
                  "Transport training, then deploy when ready. At the pickup, activate " +
                  "Item Supply and remain still until the SynSec container is loaded; at the delivery " +
                  "point, open Item Delivery and drag the container from cargo into Submit items. " +
                  "Message me here whenever you need help with the step you are on."
                : "Your active certification is Target Acquisition. Travel to the marked shooting " +
                  "range, select a training Scarab, and press R to establish a primary lock. Once " +
                  "the lock completes, activate the fitted autocannon and destroy the target. " +
                  "Message me here if a control or combat indicator is unclear.";

            _mentorResponseSink.Send(new MentorResponse(
                Guid.NewGuid(),
                character.Id,
                _mentorOptions.ChannelName,
                briefing));

            Logger.Info(
                $"onboarding character_id={character.Id} event=field_certification_presented " +
                $"mission_id={missionInProgress.MissionId} mission_guid={missionInProgress.missionGuid} " +
                $"attempt={attempt}");
        }
    }
}
