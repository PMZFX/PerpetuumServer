using System.Linq;
using Perpetuum.Services.Autonomous;
using Perpetuum.Zones.Teleporting;
using Xunit;

namespace Perpetuum.Tests.Services.Autonomous
{
    public class AutonomousTeleportRoutePolicyTests
    {
        [Fact]
        public void FindsDeterministicShortestWorldRoute()
        {
            var route = AutonomousTeleportRoutePolicy.FindRoute(1, 4, new[]
            {
                Link(30, 1, 3),
                Link(20, 1, 2),
                Link(40, 3, 4),
                Link(21, 2, 4),
                Link(10, 1, 5),
                Link(11, 5, 6),
                Link(12, 6, 4)
            });

            Assert.Equal(new[] {20, 21}, route.Select(link => link.DescriptionId));
        }

        [Fact]
        public void IgnoresLinksAClientCannotCurrentlyUse()
        {
            var route = AutonomousTeleportRoutePolicy.FindRoute(1, 3, new[]
            {
                Link(1, 1, 3, active: false),
                Link(2, 1, 3, listable: false),
                Link(3, 1, 3, valid: false),
                Link(4, 1, 3, type: TeleportDescriptionType.WithinZone),
                Link(5, 1, 2),
                Link(6, 2, 3)
            });

            Assert.Equal(new[] {5, 6}, route.Select(link => link.DescriptionId));
        }

        [Fact]
        public void CyclesAndMissingRoutesTerminateCleanly()
        {
            var links = new[]
            {
                Link(1, 1, 2),
                Link(2, 2, 1),
                Link(3, 2, 3)
            };

            Assert.Empty(AutonomousTeleportRoutePolicy.FindRoute(1, 9, links));
            Assert.Empty(AutonomousTeleportRoutePolicy.FindRoute(1, 1, links));
        }

        [Fact]
        public void ZoneZeroIsAValidWorldEndpoint()
        {
            var outbound = AutonomousTeleportRoutePolicy.FindRoute(0, 6, new[]
            {
                Link(1, 0, 4),
                Link(2, 4, 6)
            });
            var inbound = AutonomousTeleportRoutePolicy.FindRoute(6, 0, new[]
            {
                Link(3, 6, 0)
            });

            Assert.Equal(new[] {1, 2}, outbound.Select(link => link.DescriptionId));
            Assert.Equal(new[] {3}, inbound.Select(link => link.DescriptionId));
        }

        [Fact]
        public void EqualHopRoutesPreferTheClosestFirstPublicExit()
        {
            var links = new[]
            {
                Link(1, 0, 8, position: new Position(900, 900)),
                Link(2, 0, 8, position: new Position(110, 100))
            };

            var route = AutonomousTeleportRoutePolicy.FindRouteFromPosition(
                0,
                8,
                new Position(100, 100),
                links);

            Assert.Equal(new[] {2}, route.Select(link => link.DescriptionId));
        }

        private static AutonomousTeleportLink Link(
            int id,
            int sourceZone,
            int targetZone,
            bool active = true,
            bool listable = true,
            bool valid = true,
            TeleportDescriptionType type = TeleportDescriptionType.AnotherZone,
            Position? position = null)
        {
            return new AutonomousTeleportLink(
                id,
                1000 + id,
                sourceZone,
                position ?? new Position(100 + id, 200 + id),
                7,
                targetZone,
                type,
                active,
                listable,
                valid);
        }
    }
}
