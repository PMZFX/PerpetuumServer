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
        public void RestartTrailReconstructsDeterministicSurveyCandidatesWithoutMineralData()
        {
            var origin = new Position(100, 200);

            Position[] trail = AutonomousMiningSurveyPolicy.RebuildCandidateTrail(origin, 25, 11);

            Assert.Equal(25, trail.Length);
            Assert.Equal(AutonomousMiningSurveyPolicy.GetSite(origin, 0, 11), trail[0]);
            Assert.Equal(AutonomousMiningSurveyPolicy.GetSite(origin, 24, 11), trail[24]);
            Assert.Equal(11 * Math.Sqrt(2), AutonomousMiningSurveyPolicy.GetMaximumLegDistance(11), 8);
        }

        [Fact]
        public void ResupplyTransitFallsBackToEveryPriorCandidateWithoutARecordedRoute()
        {
            var origin = new Position(100, 200);

            Position[] transit = AutonomousMiningSurveyPolicy.RebuildCandidateTrail(origin, 49, 11);

            Assert.Equal(49, transit.Length);
            for (int index = 0; index < transit.Length; index++)
                Assert.Equal(AutonomousMiningSurveyPolicy.GetSite(origin, index, 11), transit[index]);
            for (int index = 1; index < transit.Length; index++)
                Assert.True(transit[index - 1].TotalDistance2D(transit[index]) <=
                            AutonomousMiningSurveyPolicy.GetMaximumLegDistance(11));
        }

        [Fact]
        public void ResupplyTransitPrefersPhysicallyReachedRouteAndRejectsInvalidData()
        {
            var origin = new Position(100, 200);
            var reached = new[]
            {
                new Position(111, 200),
                new Position(111, 211),
                new Position(100, 211)
            };

            Assert.Equal(
                reached,
                AutonomousMiningSurveyPolicy.RestoreTransitRoute(origin, 20, 11, reached));

            Position[] fallback = AutonomousMiningSurveyPolicy.RestoreTransitRoute(
                origin,
                2,
                11,
                new[] {new Position(double.NaN, 10)});
            Assert.Equal(2, fallback.Length);
            Assert.Equal(AutonomousMiningSurveyPolicy.GetSite(origin, 0, 11), fallback[0]);
        }

        [Fact]
        public void SurveyRoutePersistenceRoundTripsOnlyCoordinates()
        {
            var route = new[]
            {
                new Position(10.25, 20.5),
                new Position(30.75, 40.125)
            };

            string encoded = AutonomousSurveyRouteCodec.Serialize(route);

            Assert.Equal(route, AutonomousSurveyRouteCodec.Deserialize(encoded));
            Assert.Empty(AutonomousSurveyRouteCodec.Deserialize("not json"));
            Assert.Null(AutonomousSurveyRouteCodec.Serialize(Array.Empty<Position>()));
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
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                AutonomousDockingRecoveryPolicy.GetDirectionIndex(16, 0, 0));
        }

        [Fact]
        public void TerminalArcConnectsOppositeSpawnSidesWithoutCrossingTheBase()
        {
            var center = new Position(100, 100);
            var start = center.OffsetInDirection(0, 30);
            var target = center.OffsetInDirection(0.5, 24);

            Position[] route = AutonomousTerminalArcPolicy.Build(center, start, target, 30, 18);

            Assert.NotEmpty(route);
            Assert.Equal(target, route[route.Length - 1]);
            Position previous = start;
            foreach (Position waypoint in route)
            {
                Assert.True(previous.TotalDistance2D(waypoint) <= 18.5);
                if (waypoint != target)
                    Assert.True(center.TotalDistance2D(waypoint) >= 29);
                previous = waypoint;
            }
        }

        [Fact]
        public void MiningReturnRouteRetracesObservedSurveyPositionsBeforeOrigin()
        {
            var origin = new Position(10, 10);
            var traversed = new[]
            {
                new Position(11, 10),
                new Position(11, 11),
                new Position(10, 11)
            };

            Assert.Equal(
                new[] { traversed[2], traversed[1], traversed[0], origin },
                AutonomousMiningReturnRoutePolicy.Build(origin, traversed));
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
