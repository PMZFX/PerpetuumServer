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
        public void SurveySitesFormTwoDeterministicRingsWithoutHiddenWorldData()
        {
            var origin = new Position(100, 200);

            Assert.Equal(new Position(118, 200), AutonomousMiningSurveyPolicy.GetSite(origin, 0, 18));
            Assert.Equal(new Position(118, 182), AutonomousMiningSurveyPolicy.GetSite(origin, 7, 18));
            Assert.Equal(new Position(136, 200), AutonomousMiningSurveyPolicy.GetSite(origin, 8, 18));
            Assert.Equal(new Position(136, 164), AutonomousMiningSurveyPolicy.GetSite(origin, 15, 18));
        }

        [Fact]
        public void SurveySiteRejectsInvalidInputs()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                AutonomousMiningSurveyPolicy.GetSite(new Position(0, 0), -1, 18));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                AutonomousMiningSurveyPolicy.GetSite(new Position(0, 0), 0, 0));
        }

        private static AutonomousWorkState State(string phase, Position? target)
        {
            return new AutonomousWorkState(
                5,
                "mining",
                phase,
                100,
                7,
                new Position(10, 11),
                target,
                MaterialType.Titan);
        }
    }
}
