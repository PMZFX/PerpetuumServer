using System;
using System.Collections.Generic;
using Perpetuum.Accounting.Characters;
using Perpetuum.EntityFramework;
using Perpetuum.Robots;
using Perpetuum.Services.Actions;
using Perpetuum.Services.Autonomous;
using Perpetuum.Services.ProductionEngine;
using Xunit;

namespace Perpetuum.Tests.Services.Autonomous
{
    public class AutonomousEquipmentControllerTests
    {
        [Fact]
        public void RestartReobservesSelectionAndDoesNotRepeatIt()
        {
            var observations = new QueueObservationService(
                Snapshot(Robot(active: false)),
                Snapshot(Robot(active: true, modules: new[] {Module()})));
            var goals = new RecordingGoalStore();
            var selection = new RecordingSelectService();
            AutonomousEquipmentController controller = Controller(observations, goals, selection: selection);

            Assert.Equal(AutonomousEquipmentUpdateResult.Acted, controller.Update(Context(), Options()));
            Assert.Equal(1, selection.Calls);
            Assert.Equal("robot_selected", goals.State.Phase);

            controller = Controller(observations, goals, selection: selection);
            Assert.Equal(AutonomousEquipmentUpdateResult.Ready, controller.Update(Context(), Options()));
            Assert.Equal(1, selection.Calls);
            Assert.Equal("ready", goals.State.Phase);
        }

        [Fact]
        public void RepairIsQuotedImmediatelyBeforeExecution()
        {
            var observations = new QueueObservationService(Snapshot(Robot(active: true, healthRatio: 0.5)));
            var repair = new RecordingRepairService();
            AutonomousEquipmentController controller = Controller(
                observations,
                new RecordingGoalStore(),
                repair: repair);

            Assert.Equal(AutonomousEquipmentUpdateResult.Acted, controller.Update(Context(), Options()));
            Assert.Equal(new[] {"quote", "execute"}, repair.Calls);
            Assert.Equal(10, repair.Action.TargetEids[0]);
            Assert.Equal(400, repair.Action.FacilityEid);
        }

        [Fact]
        public void MissingSupplyWaitsDurablyWithoutRepeatingWritesOrActions()
        {
            var observation = Snapshot(Robot(active: true));
            var observations = new QueueObservationService(observation, observation);
            var goals = new RecordingGoalStore();
            var selection = new RecordingSelectService();
            var fitting = new RecordingFittingService();
            var repair = new RecordingRepairService();
            AutonomousEquipmentController controller = Controller(
                observations,
                goals,
                selection,
                fitting,
                repair: repair);

            Assert.Equal(AutonomousEquipmentUpdateResult.Blocked, controller.Update(Context(), Options()));
            int writesAfterFirstUpdate = goals.SaveCount;
            Assert.Equal("missing_module_2000", goals.State.BlockedReason);

            Assert.Equal(AutonomousEquipmentUpdateResult.Blocked, controller.Update(Context(), Options()));
            Assert.Equal(writesAfterFirstUpdate, goals.SaveCount);
            Assert.Equal(0, selection.Calls);
            Assert.Equal(0, fitting.Calls);
            Assert.Empty(repair.Calls);
        }

        [Fact]
        public void MissingModuleCanBuyOneThenFitOnlyAfterItIsObserved()
        {
            var missing = Snapshot(Robot(active: true));
            var loose = new AutonomousEquipmentItemSnapshot(30, 2000, 1, 1);
            var purchased = new AutonomousEquipmentSnapshot(
                true,
                50,
                10,
                new[] {Robot(active: true)},
                new[] {loose});
            var observations = new QueueObservationService(missing, purchased);
            var procurement = new RecordingProcurementService(
                AutonomousEquipmentProcurement.Purchased(2000, 1, 25));
            var fitting = new RecordingFittingService();
            AutonomousEquipmentOptions options = Options();
            options.Procurement.Enabled = true;
            options.Procurement.MaximumUnitPrice = 30;
            AutonomousEquipmentController controller = Controller(
                observations,
                new RecordingGoalStore(),
                fitting: fitting,
                procurement: procurement);

            Assert.Equal(AutonomousEquipmentUpdateResult.Acted, controller.Update(Context(), options));
            Assert.Equal(1, procurement.Calls);
            Assert.Equal(0, fitting.Calls);

            Assert.Equal(AutonomousEquipmentUpdateResult.Acted, controller.Update(Context(), options));
            Assert.Equal(1, procurement.Calls);
            Assert.Equal(1, fitting.Calls);
        }

        [Fact]
        public void ConfiguredAmmoLoadsOnlyFromFreshlyObservedInventory()
        {
            var looseAmmo = new AutonomousEquipmentItemSnapshot(40, 3000, 10, 1);
            var observation = new AutonomousEquipmentSnapshot(
                true,
                50,
                10,
                new[] {Robot(active: true, modules: new[] {Module()})},
                new[] {looseAmmo});
            var equipAmmo = new RecordingEquipAmmoService();
            AutonomousEquipmentOptions options = Options();
            options.Slots[0].Ammo = "ammo";
            AutonomousEquipmentController controller = Controller(
                new QueueObservationService(observation),
                new RecordingGoalStore(),
                equipAmmo: equipAmmo);

            Assert.Equal(AutonomousEquipmentUpdateResult.Acted, controller.Update(Context(), options));
            Assert.Equal(1, equipAmmo.Calls);
            Assert.Equal(20, equipAmmo.Action.ModuleEid);
            Assert.Equal(40, equipAmmo.Action.AmmoEid);
        }

        [Fact]
        public void DockedEquipmentLoopResumesAndCompletesOneLegitimateStepAtATime()
        {
            var looseModule = new AutonomousEquipmentItemSnapshot(30, 2000, 1, 1);
            var looseAmmo = new AutonomousEquipmentItemSnapshot(40, 3000, 20, 1);
            var observations = new QueueObservationService(
                Snapshot(Robot(active: false)),
                Snapshot(Robot(active: true, healthRatio: 0.5)),
                Snapshot(Robot(active: true, modules: new[] {Module(2500)})),
                new AutonomousEquipmentSnapshot(
                    true, 50, 10, new[] {Robot(active: true)}, new[] {looseModule}),
                new AutonomousEquipmentSnapshot(
                    true, 50, 10, new[] {Robot(active: true, modules: new[] {Module()})}, new[] {looseAmmo}),
                Snapshot(Robot(active: true, modules: new[] {Module(ammoDefinition: 3000, ammoQuantity: 20)})));
            var actions = new List<string>();
            var goals = new RecordingGoalStore();
            var selection = new RecordingSelectService(actions);
            var fitting = new RecordingFittingService(actions);
            var equipAmmo = new RecordingEquipAmmoService(actions);
            var repair = new RecordingRepairService(actions);
            AutonomousEquipmentOptions options = Options();
            options.Slots[0].Ammo = "ammo";
            AutonomousEquipmentController controller = Controller(
                observations,
                goals,
                selection,
                fitting,
                equipAmmo,
                repair);

            Assert.Equal(AutonomousEquipmentUpdateResult.Acted, controller.Update(Context(), options));
            Assert.Equal(AutonomousEquipmentUpdateResult.Acted, controller.Update(Context(), options));
            Assert.Equal(AutonomousEquipmentUpdateResult.Acted, controller.Update(Context(), options));

            controller = Controller(
                observations,
                goals,
                selection,
                fitting,
                equipAmmo,
                repair);
            Assert.Equal(AutonomousEquipmentUpdateResult.Acted, controller.Update(Context(), options));
            Assert.Equal(AutonomousEquipmentUpdateResult.Acted, controller.Update(Context(), options));
            Assert.Equal(AutonomousEquipmentUpdateResult.Ready, controller.Update(Context(), options));

            Assert.Equal(
                new[] {"select", "quote", "repair", "remove", "equip", "ammo"},
                actions);
            Assert.Equal("ready", goals.State.Phase);
        }

        private static AutonomousEquipmentController Controller(
            IAutonomousEquipmentObservationService observations,
            IAutonomousEquipmentGoalStore goals,
            ISelectActiveRobotActionService selection = null,
            IRobotFittingActionService fitting = null,
            IEquipAmmoActionService equipAmmo = null,
            IProductionRepairActionService repair = null,
            IAutonomousEquipmentProcurementService procurement = null)
        {
            return new AutonomousEquipmentController(
                new EquipmentDefaults(),
                observations,
                goals,
                selection ?? new RecordingSelectService(),
                fitting ?? new RecordingFittingService(),
                equipAmmo ?? new RecordingEquipAmmoService(),
                repair ?? new RecordingRepairService(),
                procurement ?? new RecordingProcurementService(
                    AutonomousEquipmentProcurement.For(AutonomousEquipmentProcurementResult.Disabled)),
                new RecordingAudit());
        }

        private static GameActionContext Context()
        {
            var actor = new Character(
                7,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null);
            return new GameActionContext(actor, GameActionSource.Autonomous);
        }

        private static AutonomousEquipmentOptions Options()
        {
            return new AutonomousEquipmentOptions
            {
                Enabled = true,
                Robot = "robot",
                RepairFacilityEid = 400,
                Slots = new List<AutonomousEquipmentSlotOptions>
                {
                    new AutonomousEquipmentSlotOptions
                    {
                        Module = "module",
                        Component = "Head",
                        Slot = 1
                    }
                }
            };
        }

        private static AutonomousEquipmentSnapshot Snapshot(AutonomousRobotEquipmentSnapshot robot)
        {
            return new AutonomousEquipmentSnapshot(
                true,
                50,
                robot.IsActive ? robot.RobotEid : 0,
                new[] {robot},
                Array.Empty<AutonomousEquipmentItemSnapshot>());
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

        private static AutonomousFittedModuleSnapshot Module(
            int definition = 2000,
            int ammoDefinition = 0,
            int ammoQuantity = 0)
        {
            return new AutonomousFittedModuleSnapshot(
                20,
                definition,
                RobotComponentType.Head,
                1,
                1,
                ammoDefinition,
                ammoQuantity);
        }

        private sealed class EquipmentDefaults : IEntityDefaultReader
        {
            private static readonly EntityDefault[] Defaults =
            {
                new EntityDefault {Definition = 1000, Name = "robot"},
                new EntityDefault {Definition = 2000, Name = "module"},
                new EntityDefault {Definition = 3000, Name = "ammo"}
            };

            public bool Exists(int definition) => Get(definition) != EntityDefault.None;
            public EntityDefault Get(int definition) =>
                Array.Find(Defaults, item => item.Definition == definition) ?? EntityDefault.None;
            public EntityDefault GetByEid(long eid) => EntityDefault.None;
            public bool TryGet(int definition, out EntityDefault entityDefault)
            {
                entityDefault = Get(definition);
                return entityDefault != EntityDefault.None;
            }
            public IEnumerable<EntityDefault> GetAll() => Defaults;
        }

        private sealed class QueueObservationService : IAutonomousEquipmentObservationService
        {
            private readonly Queue<AutonomousEquipmentSnapshot> _snapshots;

            public QueueObservationService(params AutonomousEquipmentSnapshot[] snapshots)
            {
                _snapshots = new Queue<AutonomousEquipmentSnapshot>(snapshots);
            }

            public AutonomousEquipmentSnapshot Observe(GameActionContext context) => _snapshots.Dequeue();
        }

        private sealed class RecordingGoalStore : IAutonomousEquipmentGoalStore
        {
            public AutonomousEquipmentGoalState State { get; private set; }
            public int SaveCount { get; private set; }
            public AutonomousEquipmentGoalState Load(int characterId) => State;
            public void Save(AutonomousEquipmentGoalState state)
            {
                State = state;
                SaveCount++;
            }
            public void Delete(int characterId) => State = null;
        }

        private sealed class RecordingSelectService : ISelectActiveRobotActionService
        {
            private readonly List<string> _actions;

            public RecordingSelectService(List<string> actions = null)
            {
                _actions = actions;
            }

            public int Calls { get; private set; }
            public Robot Execute(GameActionContext context, SelectActiveRobotAction action)
            {
                Calls++;
                _actions?.Add("select");
                return null;
            }
        }

        private sealed class RecordingFittingService : IRobotFittingActionService
        {
            private readonly List<string> _actions;

            public RecordingFittingService(List<string> actions = null)
            {
                _actions = actions;
            }

            public int Calls { get; private set; }
            public RobotFittingResult Equip(GameActionContext context, EquipModuleAction action)
            {
                Calls++;
                _actions?.Add("equip");
                return null;
            }
            public RobotFittingResult Remove(GameActionContext context, RemoveModuleAction action)
            {
                Calls++;
                _actions?.Add("remove");
                return null;
            }
        }

        private sealed class RecordingRepairService : IProductionRepairActionService
        {
            private readonly List<string> _actions;

            public RecordingRepairService(List<string> actions = null)
            {
                _actions = actions;
            }

            public List<string> Calls { get; } = new List<string>();
            public ProductionRepairAction Action { get; private set; }
            public RepairQuote Quote(GameActionContext context, ProductionRepairAction action)
            {
                Calls.Add("quote");
                _actions?.Add("quote");
                Action = action;
                return new RepairQuote(action.FacilityEid, Array.Empty<RepairItemQuote>());
            }
            public ProductionRepairResult Execute(GameActionContext context, ProductionRepairAction action)
            {
                Calls.Add("execute");
                _actions?.Add("repair");
                Action = action;
                return null;
            }
        }

        private sealed class RecordingEquipAmmoService : IEquipAmmoActionService
        {
            private readonly List<string> _actions;

            public RecordingEquipAmmoService(List<string> actions = null)
            {
                _actions = actions;
            }

            public int Calls { get; private set; }
            public EquipAmmoAction Action { get; private set; }

            public EquipAmmoResult Execute(GameActionContext context, EquipAmmoAction action)
            {
                Calls++;
                _actions?.Add("ammo");
                Action = action;
                return null;
            }
        }

        private sealed class RecordingProcurementService : IAutonomousEquipmentProcurementService
        {
            private readonly AutonomousEquipmentProcurement _result;

            public RecordingProcurementService(AutonomousEquipmentProcurement result)
            {
                _result = result;
            }

            public int Calls { get; private set; }

            public AutonomousEquipmentProcurement PurchaseOne(
                GameActionContext context,
                int definition,
                AutonomousEquipmentProcurementOptions options,
                bool useCorporationWallet)
            {
                Calls++;
                return _result;
            }
        }

        private sealed class RecordingAudit : IAutonomousActorAudit
        {
            public void Write(int characterId, string eventName, AutonomousActorStatus status, string reason = null)
            {
            }
        }
    }
}
