using System;
using Perpetuum.Services.Actions;
using Perpetuum.Services.Autonomous;
using Perpetuum.Services.MissionEngine;
using Xunit;

namespace Perpetuum.Tests.Services.Autonomous
{
    public class AutonomousMissionPolicyTests
    {
        [Fact]
        public void SelectsOnlyAvailableUnblockedConfiguredMission()
        {
            MissionAvailability selected = AutonomousMissionPolicy.SelectAvailability(
                new[]
                {
                    new MissionAvailability(MissionCategory.Transport, 1, false, 2, true),
                    new MissionAvailability(MissionCategory.Transport, 1, true, 1, false),
                    new MissionAvailability(MissionCategory.Transport, 1, false, 0, false),
                    new MissionAvailability(MissionCategory.Transport, 1, false, 3, false)
                },
                MissionCategory.Transport,
                1);

            Assert.NotNull(selected);
            Assert.False(selected.Random);
            Assert.Equal(3, selected.AvailableCount);
        }

        [Fact]
        public void FetchItemWithResolvedCargoAndDestinationIsSupported()
        {
            var target = Target(MissionTargetType.fetch_item, 100, 2, 500);

            Assert.True(AutonomousMissionPolicy.IsSupported(target));
            Assert.Equal(2, target.RemainingQuantity);
        }

        [Fact]
        public void CombatTargetsRequireTheSameMapDataExposedToTheClient()
        {
            var supported = new AutonomousMissionTargetSnapshot(
                1,
                MissionTargetType.kill_definition,
                0,
                true,
                false,
                0,
                200,
                2,
                0,
                8,
                new Position(100, 120),
                15);
            var hiddenMap = new AutonomousMissionTargetSnapshot(
                1,
                MissionTargetType.kill_definition,
                0,
                true,
                false,
                0,
                200,
                2,
                0);

            Assert.True(AutonomousMissionPolicy.IsFieldSupported(supported));
            Assert.True(supported.HasMapTarget);
            Assert.False(AutonomousMissionPolicy.IsSupported(hiddenMap));
        }

        [Fact]
        public void RandomMissionAvailabilityRequiresExplicitOptIn()
        {
            var availability = new[]
            {
                new MissionAvailability(MissionCategory.Combat, 0, true, 1, false)
            };

            Assert.Null(AutonomousMissionPolicy.SelectAvailability(
                availability,
                MissionCategory.Combat,
                0));
            Assert.NotNull(AutonomousMissionPolicy.SelectAvailability(
                availability,
                MissionCategory.Combat,
                0,
                allowRandom: true));
        }

        [Theory]
        [InlineData(MissionTargetType.submit_item, 100, 2, 500)]
        [InlineData(MissionTargetType.fetch_item, 0, 2, 500)]
        [InlineData(MissionTargetType.fetch_item, 100, 2, 0)]
        [InlineData(MissionTargetType.fetch_item, 100, 0, 500)]
        public void UnsupportedTargetWaitsInsteadOfReceivingSpecialTreatment(
            MissionTargetType type,
            int definition,
            int quantity,
            long destination)
        {
            Assert.False(AutonomousMissionPolicy.IsSupported(
                Target(type, definition, quantity, destination)));
        }

        [Fact]
        public void MissionGoalRetainsGuidAndAdvancesDurably()
        {
            Guid guid = Guid.NewGuid();
            var initial = new AutonomousMissionGoalState(
                7,
                MissionCategory.Transport,
                1,
                2,
                0,
                "ready_for_mission",
                100);
            AutonomousMissionGoalState running = initial.WithProgress("mission_started", guid, 200);
            AutonomousMissionGoalState completed = running.WithProgress(
                "ready_for_mission",
                completedCount: 1);

            Assert.Equal(guid, running.MissionGuid);
            Assert.Equal(200, running.TargetEid);
            Assert.Equal(1, running.Revision);
            Assert.Null(completed.MissionGuid);
            Assert.Equal(1, completed.CompletedCount);
            Assert.Equal(2, completed.Revision);
            Assert.False(completed.Complete);
        }

        [Fact]
        public void MissionOptionsAreSafeAndDisabledByDefault()
        {
            var options = new AutonomousMissionOptions();

            options.Validate(7);

            Assert.False(options.Enabled);
            Assert.Equal("Transport", options.Category);
            Assert.Equal(1, options.TargetCount);
            Assert.Equal(0, options.ProgressionExtensionId);
        }

        [Fact]
        public void EnabledMissionRequiresSourceAndPairedProgressionTarget()
        {
            var options = new AutonomousMissionOptions {Enabled = true};
            Assert.Throws<InvalidOperationException>(() => options.Validate(7));

            options.SourceBaseEid = 100;
            options.ProgressionExtensionId = 50;
            Assert.Throws<InvalidOperationException>(() => options.Validate(7));

            options.ProgressionExtensionLevel = 1;
            options.Validate(7);
        }

        private static AutonomousMissionTargetSnapshot Target(
            MissionTargetType type,
            int definition,
            int quantity,
            long destination)
        {
            return new AutonomousMissionTargetSnapshot(
                1,
                type,
                0,
                true,
                false,
                0,
                definition,
                quantity,
                destination);
        }
    }
}
