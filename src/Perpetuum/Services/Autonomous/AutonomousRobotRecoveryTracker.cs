using System;

namespace Perpetuum.Services.Autonomous
{
    public enum AutonomousRecoveryStartDisposition
    {
        Ready,
        RecoveryRequired,
        RecoveryAcknowledged
    }

    public sealed class AutonomousRobotRecoveryTracker
    {
        private readonly int _characterId;
        private readonly string _behaviorName;
        private readonly IAutonomousActorStateStore _stateStore;
        private int _recoveryRevision;

        public AutonomousRobotRecoveryTracker(
            int characterId,
            string behaviorName,
            IAutonomousActorStateStore stateStore)
        {
            if (characterId <= 0)
                throw new ArgumentOutOfRangeException(nameof(characterId));
            if (string.IsNullOrWhiteSpace(behaviorName))
                throw new ArgumentException("A behavior name is required.", nameof(behaviorName));

            _characterId = characterId;
            _behaviorName = behaviorName;
            _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        }

        public long ExpectedRobotEid { get; private set; }
        public bool RecoveryRequired { get; private set; }
        public string RecoveryReason { get; private set; }

        public AutonomousRecoveryStartDisposition Start(long activeRobotEid, int configuredRecoveryRevision)
        {
            AutonomousActorState state = _stateStore.Load(_characterId);
            if (state == null)
            {
                _recoveryRevision = configuredRecoveryRevision;
                ExpectedRobotEid = activeRobotEid;
                if (activeRobotEid <= 0)
                {
                    RequireRecovery(AutonomousRobotRecoveryReason.NoActiveRobot, activeRobotEid);
                    return AutonomousRecoveryStartDisposition.RecoveryRequired;
                }

                Save(activeRobotEid, false, null);
                return AutonomousRecoveryStartDisposition.Ready;
            }

            _recoveryRevision = state.RecoveryRevision;
            ExpectedRobotEid = state.ExpectedRobotEid;
            RecoveryRequired = state.RecoveryRequired;
            RecoveryReason = state.RecoveryReason;

            if (RecoveryRequired)
            {
                if (configuredRecoveryRevision > _recoveryRevision && activeRobotEid > 0)
                {
                    _recoveryRevision = configuredRecoveryRevision;
                    ExpectedRobotEid = activeRobotEid;
                    RecoveryRequired = false;
                    RecoveryReason = null;
                    Save(activeRobotEid, false, null);
                    return AutonomousRecoveryStartDisposition.RecoveryAcknowledged;
                }

                return AutonomousRecoveryStartDisposition.RecoveryRequired;
            }

            AutonomousRobotRecoveryReason reason = AutonomousRobotRecoveryPolicy.Assess(
                ExpectedRobotEid,
                activeRobotEid,
                false);
            if (reason != AutonomousRobotRecoveryReason.None)
            {
                RequireRecovery(reason, activeRobotEid);
                return AutonomousRecoveryStartDisposition.RecoveryRequired;
            }

            if (configuredRecoveryRevision > _recoveryRevision)
            {
                _recoveryRevision = configuredRecoveryRevision;
                Save(activeRobotEid, false, null);
            }

            return AutonomousRecoveryStartDisposition.Ready;
        }

        public bool RequireRecovery(AutonomousRobotRecoveryReason reason, long observedRobotEid)
        {
            if (reason == AutonomousRobotRecoveryReason.None || RecoveryRequired)
                return false;

            RecoveryRequired = true;
            RecoveryReason = reason.ToString();
            Save(observedRobotEid, true, RecoveryReason);
            return true;
        }

        /// <summary>
        /// Accepts a replacement only after a caller has independently proven
        /// through authoritative gameplay observations that the selected robot
        /// is ready. This method changes durable lifecycle identity only; it
        /// neither selects, repairs, fits, creates, nor authorizes a robot.
        /// </summary>
        public bool AcknowledgeReadyRobot(long activeRobotEid)
        {
            if (!RecoveryRequired || activeRobotEid <= 0)
                return false;

            ExpectedRobotEid = activeRobotEid;
            RecoveryRequired = false;
            RecoveryReason = null;
            Save(activeRobotEid, false, null);
            return true;
        }

        private void Save(long observedRobotEid, bool recoveryRequired, string recoveryReason)
        {
            _stateStore.Save(new AutonomousActorState(
                _characterId,
                _behaviorName,
                ExpectedRobotEid,
                observedRobotEid,
                recoveryRequired,
                recoveryReason,
                _recoveryRevision));
        }
    }
}
