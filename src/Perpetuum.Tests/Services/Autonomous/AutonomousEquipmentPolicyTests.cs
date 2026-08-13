using System;
using Perpetuum.Robots;
using Perpetuum.Services.Autonomous;
using Xunit;

namespace Perpetuum.Tests.Services.Autonomous
{
    public class AutonomousEquipmentPolicyTests
    {
        private static readonly AutonomousEquipmentTemplate Template =
            new AutonomousEquipmentTemplate(
                1000,
                new[]
                {
                    new AutonomousEquipmentSlotRequirement(2000, RobotComponentType.Head, 1)
                });

        [Fact]
        public void UndockedActorWaitsWithoutInspectingDockedInventory()
        {
            AutonomousEquipmentDirective directive = AutonomousEquipmentPolicy.Evaluate(
                Snapshot(false),
                Template);

            Assert.Equal(AutonomousEquipmentDirectiveType.WaitForDock, directive.Type);
        }

        [Fact]
        public void MissingRobotCreatesNormalSupplyRequirement()
        {
            AutonomousEquipmentDirective directive = AutonomousEquipmentPolicy.Evaluate(
                Snapshot(true),
                Template);

            Assert.Equal(AutonomousEquipmentDirectiveType.MissingRobot, directive.Type);
            Assert.Equal(1000, directive.Definition);
        }

        [Fact]
        public void OwnedRobotMustBeSelectedBeforeItCanBeChanged()
        {
            AutonomousEquipmentDirective directive = AutonomousEquipmentPolicy.Evaluate(
                Snapshot(true, Robot(active: false)),
                Template);

            Assert.Equal(AutonomousEquipmentDirectiveType.SelectRobot, directive.Type);
            Assert.Equal(10, directive.RobotEid);
        }

        [Fact]
        public void DamagedRobotIsRepairedBeforeFitting()
        {
            AutonomousEquipmentDirective directive = AutonomousEquipmentPolicy.Evaluate(
                Snapshot(true, Robot(active: true, healthRatio: 0.5)),
                Template);

            Assert.Equal(AutonomousEquipmentDirectiveType.RepairRobot, directive.Type);
            Assert.Equal(10, directive.RobotEid);
        }

        [Fact]
        public void WrongModuleIsRemovedBeforeReplacement()
        {
            var wrongModule = Module(20, 3000);
            AutonomousEquipmentDirective directive = AutonomousEquipmentPolicy.Evaluate(
                Snapshot(true, Robot(active: true, modules: new[] {wrongModule})),
                Template);

            Assert.Equal(AutonomousEquipmentDirectiveType.RemoveModule, directive.Type);
            Assert.Equal(20, directive.ItemEid);
        }

        [Fact]
        public void OwnedLooseModuleIsFittedThroughItsObservedIdentity()
        {
            var loose = new AutonomousEquipmentItemSnapshot(30, 2000, 1, 1);
            AutonomousEquipmentDirective directive = AutonomousEquipmentPolicy.Evaluate(
                Snapshot(true, Robot(active: true), looseItems: new[] {loose}),
                Template);

            Assert.Equal(AutonomousEquipmentDirectiveType.FitModule, directive.Type);
            Assert.Equal(30, directive.ItemEid);
            Assert.Equal(RobotComponentType.Head, directive.Component);
            Assert.Equal(1, directive.Slot);
        }

        [Fact]
        public void MissingModuleWaitsForSupplyInsteadOfGrantingOne()
        {
            AutonomousEquipmentDirective directive = AutonomousEquipmentPolicy.Evaluate(
                Snapshot(true, Robot(active: true)),
                Template);

            Assert.Equal(AutonomousEquipmentDirectiveType.MissingModule, directive.Type);
            Assert.Equal(2000, directive.Definition);
        }

        [Fact]
        public void MatchingHealthyFittingIsReady()
        {
            AutonomousEquipmentDirective directive = AutonomousEquipmentPolicy.Evaluate(
                Snapshot(true, Robot(active: true, modules: new[] {Module(20, 2000)})),
                Template);

            Assert.Equal(AutonomousEquipmentDirectiveType.Ready, directive.Type);
            Assert.Equal(10, directive.RobotEid);
        }

        [Fact]
        public void GoalProgressIsDurableAndMonotonic()
        {
            var initial = new AutonomousEquipmentGoalState(7, 1000, "observe");
            AutonomousEquipmentGoalState blocked = initial.WithProgress(
                "blocked",
                10,
                "missing_module_2000");
            AutonomousEquipmentGoalState resumed = blocked.WithProgress("fit", 10);

            Assert.Equal(1, blocked.Revision);
            Assert.Equal("missing_module_2000", blocked.BlockedReason);
            Assert.Equal(2, resumed.Revision);
            Assert.Equal(10, resumed.RobotEid);
            Assert.Null(resumed.BlockedReason);
        }

        [Fact]
        public void TemplateRejectsDuplicateSlots()
        {
            Assert.Throws<ArgumentException>(() => new AutonomousEquipmentTemplate(
                1000,
                new[]
                {
                    new AutonomousEquipmentSlotRequirement(2000, RobotComponentType.Head, 1),
                    new AutonomousEquipmentSlotRequirement(3000, RobotComponentType.Head, 1)
                }));
        }

        private static AutonomousEquipmentSnapshot Snapshot(
            bool docked,
            AutonomousRobotEquipmentSnapshot robot = null,
            AutonomousEquipmentItemSnapshot[] looseItems = null)
        {
            return new AutonomousEquipmentSnapshot(
                docked,
                docked ? 50 : 0,
                robot?.IsActive == true ? robot.RobotEid : 0,
                robot == null ? Array.Empty<AutonomousRobotEquipmentSnapshot>() : new[] {robot},
                looseItems ?? Array.Empty<AutonomousEquipmentItemSnapshot>());
        }

        private static AutonomousRobotEquipmentSnapshot Robot(
            bool active,
            double healthRatio = 1,
            AutonomousFittedModuleSnapshot[] modules = null)
        {
            return new AutonomousRobotEquipmentSnapshot(
                10,
                1000,
                50,
                active,
                false,
                healthRatio,
                0,
                100,
                modules ?? Array.Empty<AutonomousFittedModuleSnapshot>());
        }

        private static AutonomousFittedModuleSnapshot Module(long eid, int definition)
        {
            return new AutonomousFittedModuleSnapshot(
                eid,
                definition,
                RobotComponentType.Head,
                1,
                1,
                0,
                0);
        }
    }
}
