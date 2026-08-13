using System;
using Perpetuum.Services.Autonomous;
using Xunit;

namespace Perpetuum.Tests.Services.Autonomous
{
    public class AutonomousPerceptionTests
    {
        [Fact]
        public void SnapshotOrdersVisibleUnitsByDistanceThenEid()
        {
            var snapshot = new AutonomousPerceptionSnapshot(
                false,
                8,
                new Position(10, 10),
                new[]
                {
                    Unit(30, 12, false),
                    Unit(20, 5, false),
                    Unit(10, 5, true)
                });

            Assert.Collection(snapshot.VisibleUnits,
                unit => Assert.Equal(10, unit.Eid),
                unit => Assert.Equal(20, unit.Eid),
                unit => Assert.Equal(30, unit.Eid));
        }

        [Fact]
        public void ThreatAssessmentUsesOnlyHostileUnitsInsideResponseRange()
        {
            var snapshot = new AutonomousPerceptionSnapshot(
                false,
                8,
                new Position(10, 10),
                new[]
                {
                    Unit(1, 8, false),
                    Unit(2, 40, true),
                    Unit(3, 20, true),
                    Unit(4, 10, true)
                });

            AutonomousThreatAssessment assessment = AutonomousThreatAssessment.From(snapshot, 25);

            Assert.True(assessment.HasThreat);
            Assert.Equal(2, assessment.Threats.Count);
            Assert.Equal(4, assessment.Nearest.Eid);
        }

        [Fact]
        public void ThreatAssessmentCanExcludeOnlyTheActiveCombatTarget()
        {
            var snapshot = new AutonomousPerceptionSnapshot(
                false,
                1,
                new Position(0, 0),
                new[]
                {
                    new AutonomousVisibleUnitSnapshot(
                        11, AutonomousVisibleUnitKind.Npc, new Position(4, 0), 4, true),
                    new AutonomousVisibleUnitSnapshot(
                        12, AutonomousVisibleUnitKind.Npc, new Position(5, 0), 5, true)
                });

            AutonomousThreatAssessment assessment = AutonomousThreatAssessment.From(snapshot, 10, 11);

            Assert.True(assessment.HasThreat);
            Assert.Single(assessment.Threats);
            Assert.Equal(12, assessment.Nearest.Eid);
        }

        [Fact]
        public void ThreatAssessmentIsEmptyWhenNoVisibleThreatIsInRange()
        {
            var snapshot = new AutonomousPerceptionSnapshot(
                false,
                8,
                new Position(10, 10),
                new[] { Unit(1, 5, false), Unit(2, 50, true) });

            AutonomousThreatAssessment assessment = AutonomousThreatAssessment.From(snapshot, 25);

            Assert.False(assessment.HasThreat);
            Assert.Empty(assessment.Threats);
            Assert.Null(assessment.Nearest);
        }

        [Fact]
        public void InvalidThreatRangeIsRejected()
        {
            var snapshot = new AutonomousPerceptionSnapshot(true, null, null, null);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                AutonomousThreatAssessment.From(snapshot, double.NaN));
        }

        [Theory]
        [InlineData(AutonomousFieldActivity.Deploying, AutonomousThreatDirective.Dock)]
        [InlineData(AutonomousFieldActivity.TravellingOutbound, AutonomousThreatDirective.Return)]
        [InlineData(AutonomousFieldActivity.Dwelling, AutonomousThreatDirective.Return)]
        [InlineData(AutonomousFieldActivity.Returning, AutonomousThreatDirective.None)]
        [InlineData(AutonomousFieldActivity.Docking, AutonomousThreatDirective.None)]
        public void ThreatPolicySelectsSafeDirectiveForFieldActivity(
            AutonomousFieldActivity activity,
            AutonomousThreatDirective expected)
        {
            var snapshot = new AutonomousPerceptionSnapshot(
                false,
                8,
                new Position(10, 10),
                new[] { Unit(9, 10, true) });
            AutonomousThreatAssessment assessment = AutonomousThreatAssessment.From(snapshot, 25);

            Assert.Equal(expected, AutonomousThreatResponsePolicy.Select(assessment, activity));
        }

        [Fact]
        public void ThreatPolicyDoesNothingWithoutThreat()
        {
            var snapshot = new AutonomousPerceptionSnapshot(false, 8, new Position(10, 10), null);
            AutonomousThreatAssessment assessment = AutonomousThreatAssessment.From(snapshot, 25);

            Assert.Equal(
                AutonomousThreatDirective.None,
                AutonomousThreatResponsePolicy.Select(assessment, AutonomousFieldActivity.TravellingOutbound));
        }

        private static AutonomousVisibleUnitSnapshot Unit(long eid, double distance, bool hostile)
        {
            return new AutonomousVisibleUnitSnapshot(
                eid,
                AutonomousVisibleUnitKind.Npc,
                new Position(distance, 0),
                distance,
                hostile);
        }
    }
}
