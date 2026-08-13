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
