using System;
using System.Collections.Generic;
using Perpetuum.Accounting.Characters;
using Perpetuum.Services.Actions;
using Perpetuum.Services.Autonomous;
using Perpetuum.Services.MissionEngine;
using Xunit;

namespace Perpetuum.Tests.Services.Autonomous
{
    public class AutonomousMissionControllerTests
    {
        [Fact]
        public void TransportMissionCompletesAcrossRestartWithoutDuplicateStart()
        {
            var character = new CharacterState {IsDocked = true, DockingBaseEid = 100};
            var observations = new MissionObservations();
            var goals = new GoalStore();
            var cargo = new CargoService();
            var actions = new MissionActions(observations);
            var relocation = new RelocationService(cargo);
            var undock = new UndockService(character);
            var travel = new TravelService(character);
            GameActionContext context = Context();
            AutonomousMissionOptions options = Options();

            AutonomousMissionController controller = Controller(
                actions,
                observations,
                goals,
                character,
                cargo,
                relocation,
                undock,
                travel);
            controller.Start(context, options);
            Assert.Equal(
                AutonomousMissionUpdateResult.Acted,
                controller.Update(context, options, TimeSpan.Zero));
            Assert.Equal(1, actions.StartCalls);
            Assert.Equal("mission_started", goals.State.Phase);

            controller = Controller(
                actions,
                observations,
                goals,
                character,
                cargo,
                relocation,
                undock,
                new TravelService(character));
            controller.Start(context, options);

            Assert.Equal(
                AutonomousMissionUpdateResult.Acted,
                controller.Update(context, options, TimeSpan.Zero));
            Assert.Equal(1, relocation.Calls);
            Assert.Equal(0, undock.Calls);

            Assert.Equal(
                AutonomousMissionUpdateResult.Acted,
                controller.Update(context, options, TimeSpan.FromSeconds(5)));
            Assert.Equal(1, undock.Calls);
            Assert.False(character.IsDocked);

            Assert.Equal(
                AutonomousMissionUpdateResult.Travelling,
                controller.Update(context, options, TimeSpan.FromSeconds(1)));
            Assert.Equal(
                AutonomousMissionUpdateResult.Travelling,
                controller.Update(context, options, TimeSpan.FromSeconds(1)));
            Assert.True(character.IsDocked);
            Assert.Equal(200, character.DockingBaseEid);

            Assert.Equal(
                AutonomousMissionUpdateResult.Acted,
                controller.Update(context, options, TimeSpan.FromSeconds(5)));
            Assert.Equal(1, actions.DeliverCalls);

            Assert.Equal(
                AutonomousMissionUpdateResult.Complete,
                controller.Update(context, options, TimeSpan.Zero));
            Assert.Equal(1, actions.StartCalls);
            Assert.Equal(1, goals.State.CompletedCount);
            Assert.True(goals.State.Complete);
            Assert.Equal("complete", goals.State.Phase);
        }

        [Fact]
        public void CombatMissionTravelsFightsAcrossRestartAndWaitsForAuthoritativeCredit()
        {
            var character = new CharacterState {IsDocked = true, DockingBaseEid = 100};
            var observations = new CombatMissionObservations();
            var goals = new GoalStore();
            var world = new CombatPerception();
            var combat = new CombatService(
                AutonomousPveCombatUpdateResult.TargetSelected,
                AutonomousPveCombatUpdateResult.Acted,
                AutonomousPveCombatUpdateResult.Engaging,
                AutonomousPveCombatUpdateResult.Complete);
            var actions = new CombatMissionActions(observations);
            var undock = new UndockService(character);
            GameActionContext context = Context();
            var options = new AutonomousMissionOptions
            {
                Enabled = true,
                Category = "Combat",
                Level = 0,
                SourceBaseEid = 100,
                TargetCount = 1,
                DockedDwellSeconds = 5,
                RetrySeconds = 5,
                AllowRandom = true,
                Pve = Options().Pve
            };
            options.Pve.Enabled = true;

            AutonomousMissionController controller = CombatController(
                actions,
                observations,
                goals,
                character,
                undock,
                new CombatPositionTravel(world),
                world,
                combat);
            controller.Start(context, options);

            Assert.Equal(
                AutonomousMissionUpdateResult.Acted,
                controller.Update(context, options, TimeSpan.Zero));
            Assert.Equal(1, actions.StartCalls);

            Assert.Equal(
                AutonomousMissionUpdateResult.Acted,
                controller.Update(context, options, TimeSpan.FromSeconds(5)));
            Assert.False(character.IsDocked);

            Assert.Equal(
                AutonomousMissionUpdateResult.Travelling,
                controller.Update(context, options, TimeSpan.FromSeconds(1)));
            Assert.Equal(
                AutonomousMissionUpdateResult.Waiting,
                controller.Update(context, options, TimeSpan.FromSeconds(1)));

            observations.Stage = CombatMissionStage.Kill;
            Assert.Equal(
                AutonomousMissionUpdateResult.Travelling,
                controller.Update(context, options, TimeSpan.FromSeconds(1)));
            Assert.Equal(200, combat.LastObjective.RequiredDefinition);

            controller = CombatController(
                actions,
                observations,
                goals,
                character,
                undock,
                new CombatPositionTravel(world),
                world,
                combat);
            controller.Start(context, options);

            Assert.Equal(
                AutonomousMissionUpdateResult.Acted,
                controller.Update(context, options, TimeSpan.FromSeconds(1)));
            Assert.Equal(
                AutonomousMissionUpdateResult.Waiting,
                controller.Update(context, options, TimeSpan.FromSeconds(1)));
            Assert.Equal(
                AutonomousMissionUpdateResult.Waiting,
                controller.Update(context, options, TimeSpan.FromSeconds(1)));
            Assert.True(observations.Running);
            Assert.Equal(0, actions.DeliverCalls);
            Assert.Equal("combat_waiting_for_mission_credit", goals.State.Phase);

            observations.FinishSuccessfully();
            Assert.Equal(
                AutonomousMissionUpdateResult.Complete,
                controller.Update(context, options, TimeSpan.Zero));
            Assert.Equal(1, goals.State.CompletedCount);
            Assert.Equal(1, actions.StartCalls);
            Assert.Equal(0, actions.DeliverCalls);
            Assert.Equal(0, actions.AbortCalls);
        }

        private static AutonomousMissionController CombatController(
            CombatMissionActions actions,
            CombatMissionObservations observations,
            GoalStore goals,
            CharacterState character,
            UndockService undock,
            CombatPositionTravel positionTravel,
            CombatPerception perception,
            CombatService combat)
        {
            return new AutonomousMissionController(
                actions,
                observations,
                goals,
                character,
                new EquipmentService(),
                new CargoService(),
                new RelocationService(new CargoService()),
                undock,
                new TravelService(character),
                positionTravel,
                perception,
                combat,
                new ExtensionService(),
                new Audit());
        }

        private static AutonomousMissionController Controller(
            MissionActions actions,
            MissionObservations observations,
            GoalStore goals,
            CharacterState character,
            CargoService cargo,
            RelocationService relocation,
            UndockService undock,
            TravelService travel)
        {
            return new AutonomousMissionController(
                actions,
                observations,
                goals,
                character,
                new EquipmentService(),
                cargo,
                relocation,
                undock,
                travel,
                new PositionTravelService(),
                new PerceptionService(),
                new CombatService(),
                new ExtensionService(),
                new Audit());
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

        private static AutonomousMissionOptions Options()
        {
            return new AutonomousMissionOptions
            {
                Enabled = true,
                Category = "Transport",
                Level = 0,
                SourceBaseEid = 100,
                TargetCount = 1,
                DockedDwellSeconds = 5,
                RetrySeconds = 5
            };
        }

        private sealed class CharacterState : IAutonomousMissionCharacterObservationService
        {
            public bool IsDocked { get; set; }
            public long DockingBaseEid { get; set; }

            public AutonomousMissionCharacterSnapshot Observe(
                GameActionContext context,
                int progressionExtensionId)
            {
                return new AutonomousMissionCharacterSnapshot(
                    IsDocked,
                    IsDocked ? DockingBaseEid : 0,
                    0);
            }
        }

        private sealed class MissionObservations : IAutonomousMissionObservationService
        {
            public Guid Guid { get; } = Guid.NewGuid();
            public bool Running { get; set; }
            public bool Delivered { get; set; }

            public IReadOnlyList<AutonomousMissionSnapshot> ObserveRunning(GameActionContext context)
            {
                return Running
                    ? new[]
                    {
                        new AutonomousMissionSnapshot(
                            Guid,
                            10,
                            MissionCategory.Transport,
                            0,
                            100,
                            DateTime.UtcNow.AddHours(1),
                            new[]
                            {
                                new AutonomousMissionTargetSnapshot(
                                    20,
                                    MissionTargetType.fetch_item,
                                    0,
                                    true,
                                    false,
                                    0,
                                    300,
                                    2,
                                    200)
                            })
                    }
                    : Array.Empty<AutonomousMissionSnapshot>();
            }

            public AutonomousMissionCompletion ObserveCompletion(
                GameActionContext context,
                Guid missionGuid)
            {
                return Delivered && missionGuid == Guid
                    ? new AutonomousMissionCompletion(Guid, true, true)
                    : null;
            }
        }

        private sealed class MissionActions : IMissionActionService
        {
            private readonly MissionObservations _observations;

            public MissionActions(MissionObservations observations)
            {
                _observations = observations;
            }

            public int StartCalls { get; private set; }
            public int DeliverCalls { get; private set; }

            public MissionOptionsResult ObserveOptions(
                GameActionContext context,
                MissionLocationAction action)
            {
                return new MissionOptionsResult(
                    1,
                    new Dictionary<string, object>(),
                    new[]
                    {
                        new MissionAvailability(MissionCategory.Transport, 0, false, 1, false)
                    });
            }

            public MissionStartResult Start(GameActionContext context, MissionStartAction action)
            {
                StartCalls++;
                _observations.Running = true;
                return new MissionStartResult(
                    _observations.Guid,
                    new Dictionary<string, object>());
            }

            public void Deliver(GameActionContext context, MissionGuidAction action)
            {
                DeliverCalls++;
                _observations.Running = false;
                _observations.Delivered = true;
            }

            public void Abort(GameActionContext context, MissionGuidAction action)
            {
                throw new InvalidOperationException("The alpha controller must not abort automatically.");
            }
        }

        private sealed class GoalStore : IAutonomousMissionGoalStore
        {
            public AutonomousMissionGoalState State { get; private set; }

            public AutonomousMissionGoalState Load(int characterId) => State;

            public void Save(AutonomousMissionGoalState state)
            {
                State = state;
            }

            public void Delete(int characterId)
            {
                State = null;
            }
        }

        private sealed class EquipmentService : IAutonomousEquipmentObservationService
        {
            public AutonomousEquipmentSnapshot Observe(GameActionContext context)
            {
                return new AutonomousEquipmentSnapshot(
                    true,
                    400,
                    500,
                    Array.Empty<AutonomousRobotEquipmentSnapshot>(),
                    new[] {new AutonomousEquipmentItemSnapshot(600, 300, 2, 1)});
            }
        }

        private sealed class CargoService : IAutonomousCargoService
        {
            public bool Loaded { get; set; }

            public AutonomousCargoSnapshot Observe(GameActionContext context)
            {
                return new AutonomousCargoSnapshot(
                    700,
                    100,
                    Loaded ? 2 : 0,
                    Loaded
                        ? new[] {new AutonomousCargoItemSnapshot(600, 300, 2, 1, false)}
                        : Array.Empty<AutonomousCargoItemSnapshot>());
            }
        }

        private sealed class RelocationService : IRelocateItemsActionService
        {
            private readonly CargoService _cargo;

            public RelocationService(CargoService cargo)
            {
                _cargo = cargo;
            }

            public int Calls { get; private set; }

            public RelocateItemsResult Execute(GameActionContext context, RelocateItemsAction action)
            {
                Calls++;
                Assert.Equal(400, action.SourceContainerEid);
                Assert.Equal(700, action.TargetContainerEid);
                Assert.Equal(new long[] {600}, action.ItemEids);
                _cargo.Loaded = true;
                return RelocateItemsResult.NoChange;
            }
        }

        private sealed class UndockService : IUndockActionService
        {
            private readonly CharacterState _character;

            public UndockService(CharacterState character)
            {
                _character = character;
            }

            public int Calls { get; private set; }

            public void Execute(GameActionContext context)
            {
                Calls++;
                _character.IsDocked = false;
            }
        }

        private sealed class TravelService : IAutonomousDestinationTravelService
        {
            private readonly CharacterState _character;

            public TravelService(CharacterState character)
            {
                _character = character;
            }

            public AutonomousDestinationTravelStatus Status { get; private set; } =
                AutonomousDestinationTravelStatus.Idle;
            public long TargetBaseEid { get; private set; }
            public int Starts { get; private set; }

            public bool TryStart(GameActionContext context, long targetBaseEid, double throttle)
            {
                Starts++;
                TargetBaseEid = targetBaseEid;
                Status = AutonomousDestinationTravelStatus.WorldTravel;
                return true;
            }

            public AutonomousDestinationTravelStatus Update(GameActionContext context, TimeSpan elapsed)
            {
                _character.IsDocked = true;
                _character.DockingBaseEid = TargetBaseEid;
                Status = AutonomousDestinationTravelStatus.Arrived;
                return Status;
            }

            public void Stop(GameActionContext context)
            {
                TargetBaseEid = 0;
                Status = AutonomousDestinationTravelStatus.Idle;
            }
        }

        private sealed class ExtensionService : IExtensionTrainingActionService
        {
            public ExtensionTrainingQuote Quote(GameActionContext context, ExtensionTrainingAction action)
            {
                throw new InvalidOperationException("Progression spending is disabled in this scenario.");
            }

            public ExtensionTrainingResult Execute(GameActionContext context, ExtensionTrainingAction action)
            {
                throw new InvalidOperationException("Progression spending is disabled in this scenario.");
            }
        }

        private sealed class PositionTravelService : IAutonomousPositionTravelService
        {
            public AutonomousPositionTravelStatus Status => AutonomousPositionTravelStatus.Idle;
            public int TargetZoneId => 0;
            public Position TargetPosition => default(Position);

            public bool TryStart(
                GameActionContext context,
                int targetZoneId,
                Position targetPosition,
                double targetRange,
                double throttle) => false;

            public AutonomousPositionTravelStatus Update(GameActionContext context, TimeSpan elapsed) =>
                AutonomousPositionTravelStatus.Idle;

            public void Stop(GameActionContext context)
            {
            }
        }

        private sealed class PerceptionService : IAutonomousPerceptionService
        {
            public AutonomousPerceptionSnapshot Observe(GameActionContext context) =>
                new AutonomousPerceptionSnapshot(true, null, null, null);
        }

        private sealed class CombatService : IAutonomousPveCombatController
        {
            private readonly Queue<AutonomousPveCombatUpdateResult> _results;

            public CombatService(params AutonomousPveCombatUpdateResult[] results)
            {
                _results = new Queue<AutonomousPveCombatUpdateResult>(
                    results ?? Array.Empty<AutonomousPveCombatUpdateResult>());
            }

            public AutonomousPveCombatObjective LastObjective { get; private set; }

            public AutonomousPveCombatUpdate Update(
                GameActionContext context,
                AutonomousPveOptions options) =>
                new AutonomousPveCombatUpdate(AutonomousPveCombatUpdateResult.Waiting);

            public AutonomousPveCombatUpdate UpdateObjective(
                GameActionContext context,
                AutonomousPveOptions options,
                AutonomousPveCombatObjective objective)
            {
                LastObjective = objective;
                AutonomousPveCombatUpdateResult result = _results.Count > 0
                    ? _results.Dequeue()
                    : AutonomousPveCombatUpdateResult.Waiting;
                return new AutonomousPveCombatUpdate(
                    result,
                    900,
                    result == AutonomousPveCombatUpdateResult.TargetSelected ||
                    result == AutonomousPveCombatUpdateResult.Approach
                        ? new Position(105, 100)
                        : (Position?)null);
            }

            public void Stop(GameActionContext context)
            {
            }

            public bool RecordLoss(
                GameActionContext context,
                AutonomousPveOptions options,
                long lostRobotEid) => false;

            public void AcknowledgeDockedPreparation(GameActionContext context)
            {
            }

            public long GetTargetEid(int characterId) => 0;
            public bool ShouldDeploy(int characterId) => true;
        }

        private enum CombatMissionStage
        {
            Pop,
            Kill
        }

        private sealed class CombatMissionObservations : IAutonomousMissionObservationService
        {
            public Guid Guid { get; } = Guid.NewGuid();
            public bool Running { get; set; }
            public bool Finished { get; private set; }
            public CombatMissionStage Stage { get; set; }

            public IReadOnlyList<AutonomousMissionSnapshot> ObserveRunning(GameActionContext context)
            {
                if (!Running)
                    return Array.Empty<AutonomousMissionSnapshot>();
                MissionTargetType type = Stage == CombatMissionStage.Pop
                    ? MissionTargetType.pop_npc
                    : MissionTargetType.kill_definition;
                return new[]
                {
                    new AutonomousMissionSnapshot(
                        Guid,
                        20,
                        MissionCategory.Combat,
                        0,
                        100,
                        DateTime.UtcNow.AddHours(1),
                        new[]
                        {
                            new AutonomousMissionTargetSnapshot(
                                Stage == CombatMissionStage.Pop ? 30 : 31,
                                type,
                                0,
                                true,
                                false,
                                0,
                                Stage == CombatMissionStage.Pop ? 0 : 200,
                                1,
                                0,
                                8,
                                new Position(100, 100),
                                10)
                        })
                };
            }

            public AutonomousMissionCompletion ObserveCompletion(
                GameActionContext context,
                Guid missionGuid) =>
                Finished && missionGuid == Guid
                    ? new AutonomousMissionCompletion(Guid, true, true)
                    : null;

            public void FinishSuccessfully()
            {
                Running = false;
                Finished = true;
            }
        }

        private sealed class CombatMissionActions : IMissionActionService
        {
            private readonly CombatMissionObservations _observations;

            public CombatMissionActions(CombatMissionObservations observations)
            {
                _observations = observations;
            }

            public int StartCalls { get; private set; }
            public int DeliverCalls { get; private set; }
            public int AbortCalls { get; private set; }

            public MissionOptionsResult ObserveOptions(
                GameActionContext context,
                MissionLocationAction action) =>
                new MissionOptionsResult(
                    1,
                    new Dictionary<string, object>(),
                    new[] {new MissionAvailability(MissionCategory.Combat, 0, true, 1, false)});

            public MissionStartResult Start(GameActionContext context, MissionStartAction action)
            {
                StartCalls++;
                _observations.Running = true;
                return new MissionStartResult(
                    _observations.Guid,
                    new Dictionary<string, object>());
            }

            public void Deliver(GameActionContext context, MissionGuidAction action) => DeliverCalls++;
            public void Abort(GameActionContext context, MissionGuidAction action) => AbortCalls++;
        }

        private sealed class CombatPerception : IAutonomousPerceptionService
        {
            public int ZoneId { get; set; } = 8;
            public Position Position { get; set; } = new Position(0, 0);

            public AutonomousPerceptionSnapshot Observe(GameActionContext context) =>
                new AutonomousPerceptionSnapshot(false, ZoneId, Position, null);
        }

        private sealed class CombatPositionTravel : IAutonomousPositionTravelService
        {
            private readonly CombatPerception _world;
            private Position _destination;

            public CombatPositionTravel(CombatPerception world)
            {
                _world = world;
            }

            public AutonomousPositionTravelStatus Status { get; private set; } =
                AutonomousPositionTravelStatus.Idle;
            public int TargetZoneId { get; private set; }
            public Position TargetPosition => _destination;

            public bool TryStart(
                GameActionContext context,
                int targetZoneId,
                Position targetPosition,
                double targetRange,
                double throttle)
            {
                TargetZoneId = targetZoneId;
                _destination = targetPosition;
                Status = AutonomousPositionTravelStatus.SurfaceTravel;
                return true;
            }

            public AutonomousPositionTravelStatus Update(GameActionContext context, TimeSpan elapsed)
            {
                _world.ZoneId = TargetZoneId;
                _world.Position = _destination;
                Status = AutonomousPositionTravelStatus.Arrived;
                return Status;
            }

            public void Stop(GameActionContext context)
            {
                Status = AutonomousPositionTravelStatus.Idle;
            }
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
