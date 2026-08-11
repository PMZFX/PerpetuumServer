using System;
using System.Drawing;
using Perpetuum.Services.Autonomous;
using Xunit;

namespace Perpetuum.Tests.Services.Autonomous
{
    public class AutonomousPathFollowerTests
    {
        [Fact]
        public void ProducesNormalizedMovementTowardNextCell()
        {
            var follower = new AutonomousPathFollower(
                new[] { new Point(10, 10), new Point(11, 10), new Point(12, 10) },
                new Position(12.5, 10.5),
                0.45);

            AutonomousNavigationStep step = follower.Update(
                new Position(10.5, 10.5), 5, TimeSpan.FromMilliseconds(500));

            Assert.Equal(AutonomousNavigationStatus.Moving, step.Status);
            Assert.Equal(0.25, step.Input.Direction, 3);
            Assert.InRange(step.Input.Throttle, 0.08, 0.45);
        }

        [Fact]
        public void StopsWhenDestinationIsReached()
        {
            var follower = new AutonomousPathFollower(
                new[] { new Point(10, 10), new Point(11, 10) },
                new Position(11.5, 10.5),
                0.45);

            AutonomousNavigationStep step = follower.Update(
                new Position(11.1, 10.5), 5, TimeSpan.FromMilliseconds(500));

            Assert.Equal(AutonomousNavigationStatus.Arrived, step.Status);
            Assert.Equal(0, step.Input.Throttle);
        }

        [Fact]
        public void ReportsStuckAfterNoMovement()
        {
            var follower = new AutonomousPathFollower(
                new[] { new Point(10, 10), new Point(11, 10), new Point(12, 10) },
                new Position(12.5, 10.5),
                0.45);
            var position = new Position(10.5, 10.5);

            AutonomousNavigationStep step = default;
            for (int i = 0; i < 7; i++)
                step = follower.Update(position, 5, TimeSpan.FromMilliseconds(500));

            Assert.Equal(AutonomousNavigationStatus.Stuck, step.Status);
            Assert.Equal(0, step.Input.Throttle);
        }

        [Fact]
        public void SmallRealProgressResetsStuckTimer()
        {
            var follower = new AutonomousPathFollower(
                new[] { new Point(10, 10), new Point(11, 10), new Point(12, 10) },
                new Position(12.5, 10.5),
                0.45);

            AutonomousNavigationStep step = default;
            for (int i = 0; i < 12; i++)
            {
                step = follower.Update(
                    new Position(10.5 + i * 0.04, 10.5), 5, TimeSpan.FromMilliseconds(500));
            }

            Assert.Equal(AutonomousNavigationStatus.Moving, step.Status);
        }
    }
}
