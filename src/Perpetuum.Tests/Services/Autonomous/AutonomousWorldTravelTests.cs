using System;
using System.Linq;
using Perpetuum.Services.Autonomous;
using Xunit;

namespace Perpetuum.Tests.Services.Autonomous
{
    public class AutonomousWorldTravelTests
    {
        [Fact]
        public void LongRangeLegsAdvanceWithinTheLocalNavigationEnvelope()
        {
            var current = new Position(100, 100);
            var destination = new Position(300, 100);

            Position candidate = AutonomousWorldTravelLegPolicy.GetCandidate(
                current,
                destination,
                36,
                0);

            Assert.InRange(current.TotalDistance2D(candidate), 35.9, 36.1);
            Assert.True(candidate.TotalDistance2D(destination) < current.TotalDistance2D(destination));
            Assert.True(current.TotalDistance2D(candidate) < AutonomousNavigationService.MaximumStartDistance);
        }

        [Fact]
        public void NearbyDestinationIsExactBeforeDetourCandidatesFanOut()
        {
            var current = new Position(100, 100);
            var destination = new Position(120, 110);

            Assert.Equal(
                destination,
                AutonomousWorldTravelLegPolicy.GetCandidate(current, destination, 36, 0));

            Position[] detours = Enumerable.Range(1, 8)
                .Select(attempt => AutonomousWorldTravelLegPolicy.GetCandidate(
                    current,
                    destination,
                    36,
                    attempt))
                .ToArray();
            Assert.All(detours, candidate =>
                Assert.InRange(current.TotalDistance2D(candidate), 5.9, 36.1));
            Assert.Equal(8, detours.Distinct().Count());
        }

        [Fact]
        public void TeleportApproachStopsOutsideTheOccupiedSourceCell()
        {
            var current = new Position(100, 100);
            var source = new Position(200, 100);

            Position approach = AutonomousWorldTravelLegPolicy.GetApproachPosition(
                current,
                source,
                8);

            Assert.InRange(approach.TotalDistance2D(source), 5.1, 5.3);
            Assert.True(current.TotalDistance2D(approach) < current.TotalDistance2D(source));
            Assert.NotEqual(source, approach);
        }

        [Fact]
        public void TeleportEntryGraceWaitsOnlyForTheBoundedTransitionWindow()
        {
            TimeSpan timeout = TimeSpan.FromSeconds(20);

            Assert.True(AutonomousWorldEntryPolicy.ShouldWaitForTeleportEntry(
                true,
                TimeSpan.FromSeconds(19.5),
                timeout));
            Assert.False(AutonomousWorldEntryPolicy.ShouldWaitForTeleportEntry(
                true,
                timeout,
                timeout));
            Assert.False(AutonomousWorldEntryPolicy.ShouldWaitForTeleportEntry(
                false,
                TimeSpan.Zero,
                timeout));
        }
    }
}
