using System;
using System.Collections.Generic;
using Perpetuum.Accounting.Characters;
using Perpetuum.ExportedTypes;
using Perpetuum.Modules;
using Perpetuum.Robots;
using Perpetuum.Services.Actions;
using Perpetuum.Services.Autonomous;
using Xunit;

namespace Perpetuum.Tests.Services.Autonomous
{
    public class AutonomousPveCombatControllerTests
    {
        [Fact]
        public void VisibleNpcLoopCompletesAcrossRestartWithoutDuplicateLock()
        {
            var observations = new Observations();
            var goals = new GoalStore();
            var locks = new TargetLocks();
            var modules = new Modules(observations);
            GameActionContext context = Context();
            AutonomousPveOptions options = Options();
            observations.VisibleTargets = new[]
            {
                Target(1, 5, hostile: true, npc: false),
                Target(2, 10, hostile: false, npc: true),
                Target(42, 60, hostile: true, npc: true),
                Target(99, 90, hostile: true, npc: true)
            };
            observations.TrackedTarget = Target(42, 60, hostile: true, npc: true);

            AutonomousPveCombatController controller = Controller(observations, goals, locks, modules);
            AutonomousPveCombatUpdate selected = controller.Update(context, options);
            Assert.Equal(AutonomousPveCombatUpdateResult.TargetSelected, selected.Result);
            Assert.Equal(42, selected.TargetEid);

            controller = Controller(observations, goals, locks, modules);
            Assert.Equal(
                AutonomousPveCombatUpdateResult.Approach,
                controller.Update(context, options).Result);

            observations.TrackedTarget = Target(42, 30, hostile: true, npc: true);
            Assert.Equal(
                AutonomousPveCombatUpdateResult.Acted,
                controller.Update(context, options).Result);
            Assert.Equal(1, locks.LockCalls);

            Assert.Equal(
                AutonomousPveCombatUpdateResult.Waiting,
                controller.Update(context, options).Result);
            Assert.Equal(1, locks.LockCalls);

            observations.TrackedTarget = Target(
                42,
                30,
                hostile: true,
                npc: true,
                lockState: AutonomousDefenseLockState.InProgress,
                lockId: 7);
            Assert.Equal(
                AutonomousPveCombatUpdateResult.Waiting,
                controller.Update(context, options).Result);

            controller = Controller(observations, goals, locks, modules);
            observations.TrackedTarget = Target(
                42,
                30,
                hostile: true,
                npc: true,
                lockState: AutonomousDefenseLockState.Locked,
                lockId: 7);
            Assert.Equal(
                AutonomousPveCombatUpdateResult.Acted,
                controller.Update(context, options).Result);
            Assert.Equal(1, locks.LockCalls);
            Assert.Equal(1, locks.PrimaryCalls);
            Assert.Equal(1, modules.UseCalls);

            observations.TrackedTarget = Target(
                42,
                30,
                hostile: false,
                npc: true,
                visible: false,
                dead: true,
                lockState: AutonomousDefenseLockState.Locked,
                lockId: 7);
            Assert.Equal(
                AutonomousPveCombatUpdateResult.Complete,
                controller.Update(context, options).Result);
            Assert.True(goals.State.Complete);
            Assert.Equal(1, goals.State.CompletedCount);
            Assert.Equal(0, goals.State.TargetEid);

            controller = Controller(observations, goals, locks, modules);
            Assert.Equal(
                AutonomousPveCombatUpdateResult.Complete,
                controller.Update(context, options).Result);
            Assert.Equal(1, locks.LockCalls);
            Assert.Equal(1, modules.UseCalls);
        }

        [Fact]
        public void ArmorThresholdStopsWeaponsAndOwnedLockBeforeRetreat()
        {
            DateTime now = DateTime.UtcNow;
            var goals = new GoalStore
            {
                State = new AutonomousPveCombatGoalState(
                    7, 2, 0, 2, 0, "engaging", now,
                    targetEid: 42,
                    lockId: 7,
                    engagementStartedAt: now)
            };
            var observations = new Observations
            {
                ArmorRatio = 0.4,
                TrackedTarget = Target(
                    42, 20, hostile: true, npc: true,
                    lockState: AutonomousDefenseLockState.Locked, lockId: 7),
                Weapons = new[] {Weapon(active: true)}
            };
            var locks = new TargetLocks();
            var modules = new Modules(observations);

            AutonomousPveCombatUpdate update = Controller(observations, goals, locks, modules)
                .Update(Context(), Options(targetCount: 2, maxLosses: 2));

            Assert.Equal(AutonomousPveCombatUpdateResult.Retreat, update.Result);
            Assert.Equal("armor_threshold", update.Reason);
            Assert.Equal(1, modules.DeactivateCalls);
            Assert.Equal(1, locks.CancelCalls);
            Assert.Equal("retreating", goals.State.Phase);
            Assert.Equal(0, goals.State.TargetEid);
        }

        [Fact]
        public void EmptyLoadedWeaponUsesNormalReloadActionBeforeReturning()
        {
            DateTime now = DateTime.UtcNow;
            var goals = new GoalStore
            {
                State = new AutonomousPveCombatGoalState(
                    7, 1, 0, 1, 0, "target_selected", now,
                    targetEid: 42,
                    engagementStartedAt: now)
            };
            var observations = new Observations
            {
                TrackedTarget = Target(42, 30, hostile: true, npc: true),
                Weapons = new[] {Weapon(ammoQuantity: 0, ammoDefinition: 500)}
            };
            var modules = new Modules(observations);

            AutonomousPveCombatUpdate update = Controller(
                    observations,
                    goals,
                    new TargetLocks(),
                    modules)
                .Update(Context(), Options());

            Assert.Equal(AutonomousPveCombatUpdateResult.Acted, update.Result);
            Assert.Equal(1, modules.LoadCalls);
            Assert.Equal(500, modules.LastAmmoDefinition);
            Assert.Equal("reloading", goals.State.Phase);
        }

        [Fact]
        public void MissingAmmunitionBlocksDeploymentUntilDockedPreparationIsReady()
        {
            DateTime now = DateTime.UtcNow;
            var goals = new GoalStore
            {
                State = new AutonomousPveCombatGoalState(
                    7, 1, 0, 1, 0, "target_selected", now,
                    targetEid: 42,
                    engagementStartedAt: now)
            };
            var observations = new Observations
            {
                TrackedTarget = Target(42, 30, hostile: true, npc: true),
                Weapons = new[] {Weapon(ammoQuantity: 0, ammoDefinition: 0)}
            };
            AutonomousPveCombatController controller = Controller(
                observations,
                goals,
                new TargetLocks(),
                new Modules(observations));
            GameActionContext context = Context();

            Assert.Equal(
                AutonomousPveCombatUpdateResult.Resupply,
                controller.Update(context, Options()).Result);
            Assert.False(controller.ShouldDeploy(7));

            controller = Controller(
                observations,
                goals,
                new TargetLocks(),
                new Modules(observations));
            Assert.False(controller.ShouldDeploy(7));
            controller.AcknowledgeDockedPreparation(context);

            Assert.True(controller.ShouldDeploy(7));
            Assert.Equal("searching", goals.State.Phase);
        }

        [Fact]
        public void LockTimeoutAndEngagementLimitArePersistentHardBounds()
        {
            DateTime now = DateTime.UtcNow;
            var observations = new Observations
            {
                TrackedTarget = Target(
                    42, 30, hostile: true, npc: true,
                    lockState: AutonomousDefenseLockState.InProgress, lockId: 7)
            };
            var locks = new TargetLocks();
            var modules = new Modules(observations);
            var lockGoals = new GoalStore
            {
                State = new AutonomousPveCombatGoalState(
                    7, 1, 0, 1, 0, "locking", now.AddSeconds(-9),
                    targetEid: 42, lockId: 7, engagementStartedAt: now.AddSeconds(-9))
            };

            AutonomousPveCombatUpdate lockTimeout = Controller(observations, lockGoals, locks, modules)
                .Update(Context(), Options());
            Assert.Equal(AutonomousPveCombatUpdateResult.Retreat, lockTimeout.Result);
            Assert.Equal("lock_timeout", lockTimeout.Reason);
            Assert.Equal(0, locks.LockCalls);

            var engagementGoals = new GoalStore
            {
                State = new AutonomousPveCombatGoalState(
                    7, 1, 0, 1, 0, "engaging", now.AddSeconds(-91),
                    targetEid: 42, lockId: 7, engagementStartedAt: now.AddSeconds(-91))
            };
            AutonomousPveCombatUpdate engagementTimeout = Controller(
                    observations,
                    engagementGoals,
                    locks,
                    modules)
                .Update(Context(), Options());
            Assert.Equal(AutonomousPveCombatUpdateResult.Retreat, engagementTimeout.Result);
            Assert.Equal("engagement_limit", engagementTimeout.Reason);
        }

        [Fact]
        public void LossBudgetIsDurableAndSameRobotCannotBeCountedTwice()
        {
            var observations = new Observations();
            var goals = new GoalStore();
            AutonomousPveCombatController controller = Controller(
                observations,
                goals,
                new TargetLocks(),
                new Modules(observations));
            GameActionContext context = Context();
            AutonomousPveOptions options = Options(targetCount: 3, maxLosses: 2);

            Assert.False(controller.RecordLoss(context, options, 100));
            Assert.False(controller.RecordLoss(context, options, 100));
            Assert.Equal(1, goals.State.LossCount);

            controller = Controller(observations, goals, new TargetLocks(), new Modules(observations));
            Assert.True(controller.RecordLoss(context, options, 101));
            Assert.Equal(2, goals.State.LossCount);
            Assert.False(controller.ShouldDeploy(7));
            Assert.Equal("loss_budget_reached", goals.State.Phase);
        }

        private static AutonomousPveCombatController Controller(
            Observations observations,
            GoalStore goals,
            TargetLocks locks,
            Modules modules)
        {
            return new AutonomousPveCombatController(
                observations,
                goals,
                locks,
                modules,
                new Audit());
        }

        private static GameActionContext Context()
        {
            var actor = new Character(
                7, null, null, null, null, null, null,
                null, null, null, null, null, null);
            return new GameActionContext(actor, GameActionSource.Autonomous);
        }

        private static AutonomousPveOptions Options(int targetCount = 1, int maxLosses = 1)
        {
            return new AutonomousPveOptions
            {
                Enabled = true,
                AcquisitionRange = 75,
                EngagementRange = 45,
                SearchSeconds = 30,
                LockTimeoutSeconds = 8,
                MaxEngagementSeconds = 90,
                RetreatArmorRatio = 0.45,
                RetreatCoreRatio = 0.15,
                TargetCount = targetCount,
                MaxLosses = maxLosses
            };
        }

        private static AutonomousPveTargetSnapshot Target(
            long eid,
            double distance,
            bool hostile,
            bool npc,
            bool visible = true,
            bool dead = false,
            AutonomousDefenseLockState lockState = AutonomousDefenseLockState.Missing,
            long lockId = 0)
        {
            return new AutonomousPveTargetSnapshot(
                eid,
                new Position(distance, 0),
                distance,
                visible,
                hostile,
                npc,
                dead,
                lockState,
                lockId);
        }

        private static AutonomousPveWeaponSnapshot Weapon(
            int ammoQuantity = 10,
            int ammoDefinition = 500,
            bool active = false)
        {
            return new AutonomousPveWeaponSnapshot(
                RobotComponentType.Head,
                0,
                true,
                ammoDefinition,
                ammoQuantity,
                active);
        }

        private sealed class Observations : IAutonomousPveCombatObservationService
        {
            public double ArmorRatio { get; set; } = 1;
            public double CoreRatio { get; set; } = 1;
            public IReadOnlyList<AutonomousPveTargetSnapshot> VisibleTargets { get; set; } =
                Array.Empty<AutonomousPveTargetSnapshot>();
            public IReadOnlyList<AutonomousPveWeaponSnapshot> Weapons { get; set; } =
                new[] {Weapon()};
            public AutonomousPveTargetSnapshot TrackedTarget { get; set; }

            public AutonomousPveCombatSnapshot Observe(GameActionContext context, long trackedTargetEid)
            {
                return new AutonomousPveCombatSnapshot(
                    true,
                    ArmorRatio,
                    CoreRatio,
                    VisibleTargets,
                    Weapons,
                    trackedTargetEid > 0 && TrackedTarget?.Eid == trackedTargetEid
                        ? TrackedTarget
                        : null);
            }
        }

        private sealed class GoalStore : IAutonomousPveCombatGoalStore
        {
            public AutonomousPveCombatGoalState State { get; set; }
            public AutonomousPveCombatGoalState Load(int characterId) => State;
            public void Save(AutonomousPveCombatGoalState state) => State = state;
            public void Delete(int characterId) => State = null;
        }

        private sealed class TargetLocks : ITargetLockActionService
        {
            public int LockCalls { get; private set; }
            public int PrimaryCalls { get; private set; }
            public int CancelCalls { get; private set; }

            public void LockUnit(GameActionContext context, UnitTargetLockAction action) => LockCalls++;
            public void LockTerrain(GameActionContext context, TerrainTargetLockAction action) =>
                throw new InvalidOperationException();
            public void SetPrimary(GameActionContext context, long lockId) => PrimaryCalls++;
            public void Cancel(GameActionContext context, long lockId) => CancelCalls++;
        }

        private sealed class Modules : IModuleActionService
        {
            private readonly Observations _observations;

            public Modules(Observations observations)
            {
                _observations = observations;
            }

            public int UseCalls { get; private set; }
            public int DeactivateCalls { get; private set; }
            public int LoadCalls { get; private set; }
            public int LastAmmoDefinition { get; private set; }

            public void Use(GameActionContext context, ModuleUseAction action) =>
                throw new InvalidOperationException();

            public void UseByCategory(GameActionContext context, ModuleCategoryUseAction action)
            {
                UseCalls++;
                _observations.Weapons = new[] {Weapon(active: true)};
            }

            public void DeactivateByCategory(GameActionContext context, CategoryFlags categoryFlags)
            {
                DeactivateCalls++;
                _observations.Weapons = new[] {Weapon(active: false)};
            }

            public void LoadAmmo(GameActionContext context, ModuleAmmoLoadAction action)
            {
                LoadCalls++;
                LastAmmoDefinition = action.AmmoDefinition;
            }

            public void UnloadAmmo(GameActionContext context, ModuleAmmoUnloadAction action) =>
                throw new InvalidOperationException();
        }

        private sealed class Audit : IAutonomousActorAudit
        {
            public void Write(
                int characterId,
                string eventName,
                AutonomousActorStatus status,
                string reason = null)
            {
            }
        }
    }
}
