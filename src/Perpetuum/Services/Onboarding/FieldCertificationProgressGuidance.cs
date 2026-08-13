using System;
using System.Threading;
using Perpetuum.Log;
using Perpetuum.Services.Mentoring;
using Perpetuum.Services.MissionEngine;
using Perpetuum.Services.MissionEngine.MissionProcessorObjects;
using Perpetuum.Services.Sessions;

namespace Perpetuum.Services.Onboarding
{
    /// <summary>
    /// Turns committed, server-owned certification progress into short Mentor handoffs. These
    /// messages are deterministic navigation aids; the language model remains available for the
    /// player's questions and diagnostics.
    /// </summary>
    public sealed class FieldCertificationProgressGuidance
    {
        private readonly Lazy<MissionProcessor> _missionProcessor;
        private readonly IMentorResponseSink _mentorResponseSink;
        private readonly MentorOptions _mentorOptions;
        private int _attached;

        public FieldCertificationProgressGuidance(
            ISessionManager sessionManager,
            Lazy<MissionProcessor> missionProcessor,
            IMentorResponseSink mentorResponseSink,
            MentorOptions mentorOptions)
        {
            if (sessionManager == null)
                throw new ArgumentNullException(nameof(sessionManager));
            _missionProcessor = missionProcessor ?? throw new ArgumentNullException(nameof(missionProcessor));
            _mentorResponseSink = mentorResponseSink ?? throw new ArgumentNullException(nameof(mentorResponseSink));
            _mentorOptions = mentorOptions ?? throw new ArgumentNullException(nameof(mentorOptions));

            // Resolving MissionProcessor during Autofac auto-activation is too early: its process
            // registration depends on the fully built root container. SessionAdded happens after
            // bootstrap and before a selected character can advance an objective.
            sessionManager.SessionAdded += OnSessionAdded;
        }

        private void OnSessionAdded(ISession session)
        {
            if (Interlocked.Exchange(ref _attached, 1) != 0)
                return;

            try
            {
                _missionProcessor.Value.MissionProgressed += OnMissionProgressed;
            }
            catch
            {
                Interlocked.Exchange(ref _attached, 0);
                throw;
            }
        }

        private void OnMissionProgressed(MissionProgressEvent progress)
        {
            string message = SelectMessage(
                progress.MissionName,
                progress.TargetType,
                progress.TargetCompleted,
                progress.MissionCompleted);

            if (message == null)
                return;

            _mentorResponseSink.Send(new MentorResponse(
                Guid.NewGuid(),
                progress.CharacterId,
                _mentorOptions.ChannelName,
                message));

            Logger.Info(
                $"onboarding character_id={progress.CharacterId} event=field_certification_guidance " +
                $"mission_id={progress.MissionId} mission_guid={progress.MissionGuid} " +
                $"target_type={progress.TargetType} mission_completed={progress.MissionCompleted}");
        }

        public static string SelectMessage(
            string missionName,
            MissionTargetType targetType,
            bool targetCompleted,
            bool missionCompleted)
        {
            if (!targetCompleted ||
                (string.IsNullOrEmpty(missionName)))
            {
                return null;
            }

            if (string.Equals(
                    missionName,
                    FieldCertificationMissionContract.MissionName,
                    StringComparison.Ordinal))
            {
                if (missionCompleted)
                {
                    return "Delivery accepted. First shift complete; the assignment's 10,000 NIC " +
                           "reward has been issued. Keep the Arkhe—it is your starter robot. Your " +
                           "next assignment, Target Acquisition, is being added now.";
                }

                switch (targetType)
                {
                    case MissionTargetType.reach_position:
                        return "You are at the pickup. Double-click Item Supply once to approach it, " +
                               "then again when you are in range to activate it. Stop moving and wait " +
                               "for the SynSec container to finish loading into cargo.";

                    case MissionTargetType.use_itemsupply:
                        return "Cargo confirmed. Follow objective C to Item Delivery. Double-click it " +
                               "to approach, open it when you are in range, then drag the SynSec " +
                               "container from your robot cargo into Submit items.";

                    default:
                        return null;
                }
            }

            if (!string.Equals(
                    missionName,
                    CombatCertificationMissionContract.MissionName,
                    StringComparison.Ordinal))
            {
                return null;
            }

            if (missionCompleted)
            {
                return "Training Scarab destroyed. Target Acquisition is complete and its 15,000 " +
                       "NIC reward has been issued. You now have the essential combat loop: select, " +
                       "lock, then activate the appropriate module.";
            }

            switch (targetType)
            {
                case MissionTargetType.reach_position:
                    return "You are at the shooting range. Select one of the training Scarabs, then " +
                           "press R to establish it as your primary locked target. Wait for the lock " +
                           "to finish before activating a weapon.";

                case MissionTargetType.lock_unit:
                    return "Primary lock confirmed. Activate the fitted autocannon to destroy that " +
                           "training Scarab. If it does not fire, check that the target is still " +
                           "locked and that the weapon is loaded and in range.";

                default:
                    return null;
            }
        }
    }
}
