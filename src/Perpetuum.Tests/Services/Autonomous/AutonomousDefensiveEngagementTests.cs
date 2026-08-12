using System;
using Perpetuum.Services.Autonomous;
using Xunit;

namespace Perpetuum.Tests.Services.Autonomous
{
    public class AutonomousDefensiveEngagementTests
    {
        private static AutonomousDefensiveEngagement CreatePolicy()
        {
            return new AutonomousDefensiveEngagement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(12));
        }

        [Fact]
        public void DamageStartsOneBoundedLockRequest()
        {
            var policy = CreatePolicy();

            Assert.True(policy.RecordDamage(42));
            AutonomousDefenseDecision first = policy.Update(
                TimeSpan.FromSeconds(1), true, true, AutonomousDefenseLockState.Missing);
            AutonomousDefenseDecision second = policy.Update(
                TimeSpan.FromSeconds(1), true, true, AutonomousDefenseLockState.Missing);

            Assert.Equal(AutonomousDefenseDirective.RequestLock, first.Directive);
            Assert.Equal(42, first.TargetEid);
            Assert.Equal(AutonomousDefenseDirective.None, second.Directive);
        }

        [Fact]
        public void CompletedLockStartsEngagement()
        {
            var policy = CreatePolicy();
            policy.RecordDamage(42);

            AutonomousDefenseDecision decision = policy.Update(
                TimeSpan.FromSeconds(1), true, true, AutonomousDefenseLockState.Locked);

            Assert.Equal(AutonomousDefenseDirective.Engage, decision.Directive);
            Assert.Equal(AutonomousDefenseState.Engaging, policy.State);
        }

        [Fact]
        public void UnseenDamageSourceCannotBeEngaged()
        {
            var policy = CreatePolicy();
            policy.RecordDamage(42);

            AutonomousDefenseDecision decision = policy.Update(
                TimeSpan.FromSeconds(1), false, true, AutonomousDefenseLockState.Missing);

            Assert.Equal(AutonomousDefenseDirective.Disengage, decision.Directive);
            Assert.Equal(AutonomousDefenseEndReason.TargetUnavailable, decision.EndReason);
        }

        [Fact]
        public void UnarmedActorDisengagesWithoutLocking()
        {
            var policy = CreatePolicy();
            policy.RecordDamage(42);

            AutonomousDefenseDecision decision = policy.Update(
                TimeSpan.FromSeconds(1), true, false, AutonomousDefenseLockState.Missing);

            Assert.Equal(AutonomousDefenseDirective.Disengage, decision.Directive);
            Assert.Equal(AutonomousDefenseEndReason.NoUsableWeapon, decision.EndReason);
        }

        [Fact]
        public void LockAcquisitionHasHardTimeout()
        {
            var policy = CreatePolicy();
            policy.RecordDamage(42);
            policy.Update(TimeSpan.FromSeconds(1), true, true, AutonomousDefenseLockState.Missing);

            AutonomousDefenseDecision decision = policy.Update(
                TimeSpan.FromSeconds(4), true, true, AutonomousDefenseLockState.Missing);

            Assert.Equal(AutonomousDefenseDirective.Disengage, decision.Directive);
            Assert.Equal(AutonomousDefenseEndReason.LockTimeout, decision.EndReason);
        }

        [Fact]
        public void EngagementHasHardLimitThatDamageCannotExtend()
        {
            var policy = CreatePolicy();
            policy.RecordDamage(42);
            policy.Update(TimeSpan.FromSeconds(1), true, true, AutonomousDefenseLockState.Locked);
            Assert.False(policy.RecordDamage(42));

            AutonomousDefenseDecision decision = policy.Update(
                TimeSpan.FromSeconds(11), true, true, AutonomousDefenseLockState.Locked);

            Assert.Equal(AutonomousDefenseDirective.Disengage, decision.Directive);
            Assert.Equal(AutonomousDefenseEndReason.EngagementLimit, decision.EndReason);
        }

        [Fact]
        public void NewAttackerCannotCauseTargetThrashing()
        {
            var policy = CreatePolicy();
            policy.RecordDamage(42);

            Assert.False(policy.RecordDamage(99));
            Assert.Equal(42, policy.TargetEid);
        }

        [Fact]
        public void LostCombatLockEndsEngagement()
        {
            var policy = CreatePolicy();
            policy.RecordDamage(42);
            policy.Update(TimeSpan.FromSeconds(1), true, true, AutonomousDefenseLockState.Locked);

            AutonomousDefenseDecision decision = policy.Update(
                TimeSpan.FromSeconds(1), true, true, AutonomousDefenseLockState.Missing);

            Assert.Equal(AutonomousDefenseDirective.Disengage, decision.Directive);
            Assert.Equal(AutonomousDefenseEndReason.LockLost, decision.EndReason);
        }
    }
}
