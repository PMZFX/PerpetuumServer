using System;

namespace Perpetuum.Services.Autonomous
{
    public enum AutonomousDefenseState
    {
        Idle,
        Locking,
        Engaging,
        Disengaging
    }

    public enum AutonomousDefenseLockState
    {
        Missing,
        InProgress,
        Locked
    }

    public enum AutonomousDefenseDirective
    {
        None,
        RequestLock,
        Engage,
        Disengage
    }

    public enum AutonomousDefenseEndReason
    {
        None,
        TargetUnavailable,
        NoUsableWeapon,
        LockTimeout,
        LockLost,
        EngagementLimit
    }

    public sealed class AutonomousDefenseDecision
    {
        public AutonomousDefenseDecision(
            AutonomousDefenseDirective directive,
            long targetEid,
            AutonomousDefenseEndReason endReason = AutonomousDefenseEndReason.None)
        {
            Directive = directive;
            TargetEid = targetEid;
            EndReason = endReason;
        }

        public AutonomousDefenseDirective Directive { get; }
        public long TargetEid { get; }
        public AutonomousDefenseEndReason EndReason { get; }
    }

    /// <summary>
    /// A bounded single-attacker response. New attackers never cause target
    /// thrashing during an engagement, and repeated damage never extends the
    /// hard engagement limit.
    /// </summary>
    public sealed class AutonomousDefensiveEngagement
    {
        private readonly TimeSpan _lockTimeout;
        private readonly TimeSpan _engagementLimit;
        private TimeSpan _elapsed;
        private bool _lockRequested;

        public AutonomousDefensiveEngagement(TimeSpan lockTimeout, TimeSpan engagementLimit)
        {
            if (lockTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(lockTimeout));
            if (engagementLimit <= lockTimeout)
                throw new ArgumentOutOfRangeException(nameof(engagementLimit));

            _lockTimeout = lockTimeout;
            _engagementLimit = engagementLimit;
        }

        public AutonomousDefenseState State { get; private set; }
        public long TargetEid { get; private set; }

        public bool RecordDamage(long attackerEid)
        {
            if (attackerEid <= 0)
                return false;
            if (State != AutonomousDefenseState.Idle)
                return false;

            TargetEid = attackerEid;
            State = AutonomousDefenseState.Locking;
            _elapsed = TimeSpan.Zero;
            _lockRequested = false;
            return true;
        }

        public AutonomousDefenseDecision Update(
            TimeSpan elapsed,
            bool targetAvailable,
            bool hasUsableWeapon,
            AutonomousDefenseLockState lockState)
        {
            if (elapsed < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(elapsed));
            if (State == AutonomousDefenseState.Idle)
                return Decision(AutonomousDefenseDirective.None);
            if (State == AutonomousDefenseState.Disengaging)
                return Decision(AutonomousDefenseDirective.Disengage);

            _elapsed += elapsed;
            if (!targetAvailable)
                return BeginDisengagement(AutonomousDefenseEndReason.TargetUnavailable);
            if (!hasUsableWeapon)
                return BeginDisengagement(AutonomousDefenseEndReason.NoUsableWeapon);
            if (_elapsed >= _engagementLimit)
                return BeginDisengagement(AutonomousDefenseEndReason.EngagementLimit);

            if (State == AutonomousDefenseState.Locking)
            {
                if (lockState == AutonomousDefenseLockState.Locked)
                {
                    State = AutonomousDefenseState.Engaging;
                    return Decision(AutonomousDefenseDirective.Engage);
                }

                if (!_lockRequested)
                {
                    _lockRequested = true;
                    return Decision(AutonomousDefenseDirective.RequestLock);
                }

                if (_elapsed >= _lockTimeout)
                    return BeginDisengagement(AutonomousDefenseEndReason.LockTimeout);

                return Decision(AutonomousDefenseDirective.None);
            }

            if (lockState != AutonomousDefenseLockState.Locked)
                return BeginDisengagement(AutonomousDefenseEndReason.LockLost);

            return Decision(AutonomousDefenseDirective.None);
        }

        public void CompleteDisengagement()
        {
            Reset();
        }

        public void Reset()
        {
            State = AutonomousDefenseState.Idle;
            TargetEid = 0;
            _elapsed = TimeSpan.Zero;
            _lockRequested = false;
        }

        private AutonomousDefenseDecision BeginDisengagement(AutonomousDefenseEndReason reason)
        {
            State = AutonomousDefenseState.Disengaging;
            return Decision(AutonomousDefenseDirective.Disengage, reason);
        }

        private AutonomousDefenseDecision Decision(
            AutonomousDefenseDirective directive,
            AutonomousDefenseEndReason reason = AutonomousDefenseEndReason.None)
        {
            return new AutonomousDefenseDecision(directive, TargetEid, reason);
        }
    }
}
