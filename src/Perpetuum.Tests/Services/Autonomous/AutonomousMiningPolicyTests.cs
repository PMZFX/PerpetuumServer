using System;
using Perpetuum.Services.Autonomous;
using Perpetuum.Zones.Terrains.Materials;
using Xunit;

namespace Perpetuum.Tests.Services.Autonomous
{
    public class AutonomousMiningPolicyTests
    {
        [Fact]
        public void DockedActorAlwaysStartsFreshAtItsCurrentBase()
        {
            Assert.Equal(
                AutonomousMiningResumeDirective.StartDocked,
                AutonomousMiningResumePolicy.Select(true, 99, MaterialType.Crude, State("Mining", new Position(20, 30))));
        }

        [Fact]
        public void ValidInterruptedMiningResumesItsObservedTarget()
        {
            AutonomousWorkState state = State("Mining", new Position(20, 30));

            Assert.Equal(
                AutonomousMiningResumeDirective.ResumeTarget,
                AutonomousMiningResumePolicy.Select(false, 7, MaterialType.Titan, state));
        }

        [Theory]
        [InlineData("Returning")]
        [InlineData("WaitingToDock")]
        [InlineData("WaitingForScan")]
        public void NonTargetPhaseReturnsToPersistedOrigin(string phase)
        {
            AutonomousWorkState state = State(phase, null);

            Assert.Equal(
                AutonomousMiningResumeDirective.ReturnToOrigin,
                AutonomousMiningResumePolicy.Select(false, 7, MaterialType.Titan, state));
        }

        [Fact]
        public void MismatchedZoneOrMaterialNeverUsesStaleCoordinates()
        {
            AutonomousWorkState state = State("Mining", new Position(20, 30));

            Assert.Equal(
                AutonomousMiningResumeDirective.RecoverToBase,
                AutonomousMiningResumePolicy.Select(false, 8, MaterialType.Titan, state));
            Assert.Equal(
                AutonomousMiningResumeDirective.RecoverToBase,
                AutonomousMiningResumePolicy.Select(false, 7, MaterialType.Crude, state));
        }

        [Fact]
        public void ReturnPolicyPrioritizesThreatAndNormalLimits()
        {
            Assert.Equal(
                AutonomousMiningReturnReason.Threat,
                AutonomousMiningReturnPolicy.Assess(0.9, 0.75, TimeSpan.Zero, TimeSpan.FromMinutes(1), true, false));
            Assert.Equal(
                AutonomousMiningReturnReason.EquipmentUnavailable,
                AutonomousMiningReturnPolicy.Assess(0.1, 0.75, TimeSpan.Zero, TimeSpan.FromMinutes(1), false, false));
            Assert.Equal(
                AutonomousMiningReturnReason.CargoThreshold,
                AutonomousMiningReturnPolicy.Assess(0.75, 0.75, TimeSpan.Zero, TimeSpan.FromMinutes(1), false, true));
            Assert.Equal(
                AutonomousMiningReturnReason.DurationLimit,
                AutonomousMiningReturnPolicy.Assess(0.1, 0.75, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1), false, true));
        }

        [Fact]
        public void SurveySitesFormThreeContiguousDeterministicRingsWithoutHiddenWorldData()
        {
            var origin = new Position(100, 200);

            Assert.Equal(new Position(111, 200), AutonomousMiningSurveyPolicy.GetSite(origin, 0, 11));
            Assert.Equal(new Position(111, 189), AutonomousMiningSurveyPolicy.GetSite(origin, 7, 11));
            Assert.Equal(new Position(122, 200), AutonomousMiningSurveyPolicy.GetSite(origin, 8, 11));
            Assert.Equal(new Position(122, 189), AutonomousMiningSurveyPolicy.GetSite(origin, 23, 11));
            Assert.Equal(new Position(133, 200), AutonomousMiningSurveyPolicy.GetSite(origin, 24, 11));
            Assert.Equal(new Position(133, 189), AutonomousMiningSurveyPolicy.GetSite(origin, 47, 11));
            Assert.Equal(3, AutonomousMiningSurveyPolicy.GetRingCount(48));
        }

        [Fact]
        public void SurveySiteRejectsInvalidInputs()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                AutonomousMiningSurveyPolicy.GetSite(new Position(0, 0), -1, 18));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                AutonomousMiningSurveyPolicy.GetSite(new Position(0, 0), 0, 0));
        }

        [Fact]
        public void MatchingPersistedSurveyProgressSurvivesAResupplyTrip()
        {
            AutonomousWorkState state = State("Docked", null, 17);

            Assert.Equal(17, AutonomousMiningSurveyPolicy.SelectResumeSite(MaterialType.Titan, 48, state));
            Assert.Equal(0, AutonomousMiningSurveyPolicy.SelectResumeSite(MaterialType.Crude, 48, state));
            Assert.Equal(0, AutonomousMiningSurveyPolicy.SelectResumeSite(MaterialType.Titan, 17, state));
        }

        [Fact]
        public void DockingRecoveryRotatesThroughDifferentApproachDirections()
        {
            Assert.Equal(4, AutonomousDockingRecoveryPolicy.GetDirectionIndex(4, 0, 0));
            Assert.Equal(9, AutonomousDockingRecoveryPolicy.GetDirectionIndex(4, 1, 0));
            Assert.Equal(14, AutonomousDockingRecoveryPolicy.GetDirectionIndex(4, 2, 0));
            Assert.Equal(3, AutonomousDockingRecoveryPolicy.GetDirectionIndex(4, 3, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                AutonomousDockingRecoveryPolicy.GetDirectionIndex(4, -1, 0));
        }

        private static AutonomousWorkState State(string phase, Position? target, int surveySiteIndex = 0)
        {
            return new AutonomousWorkState(
                5,
                "mining",
                phase,
                100,
                7,
                new Position(10, 11),
                target,
                MaterialType.Titan,
                surveySiteIndex);
        }
    }
}
