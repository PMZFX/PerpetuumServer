using System;
using System.Collections.Generic;
using Perpetuum.Accounting.Characters;
using Perpetuum.Services.Actions;
using Perpetuum.Services.Autonomous;
using Perpetuum.Services.MissionEngine;
using Xunit;

namespace Perpetuum.Tests.Services.Autonomous
{
    public class AutonomousPlayerBehaviorTests
    {
        [Fact]
        public void PolicyNeverSwitchesInFieldOrAcrossOutstandingCommitment()
        {
            Assert.Equal(
                AutonomousPlayerRoleDisposition.Continue,
                AutonomousPlayerPolicy.Evaluate(
                    false, false, true, TimeSpan.FromHours(1), 0, 5));
            Assert.Equal(
                AutonomousPlayerRoleDisposition.Continue,
                AutonomousPlayerPolicy.Evaluate(
                    true, true, true, TimeSpan.FromHours(1), 0, 5));
            Assert.Equal(
                AutonomousPlayerRoleDisposition.Yielded,
                AutonomousPlayerPolicy.Evaluate(
                    true, false, false, TimeSpan.FromSeconds(5), 0, 5));
        }

        [Fact]
        public void RestartResumesPersistedRoleWithoutStartingEarlierRoles()
        {
            var state = new MemoryPlayerStateStore
            {
                State = new AutonomousPlayerState(
                    7,
                    "mining,trader,manufacturer",
                    1,
                    "trader",
                    DateTime.UtcNow.AddMinutes(-1))
            };
            var roles = new RoleFactory();
            PlayerAutonomousActorBehavior behavior = Create(
                Definition("mining", "trader", "manufacturer"),
                roles,
                state,
                new MemoryMissionGoalStore(),
                new MemoryTradeStateStore(),
                new CharacterState {Docked = true});

            behavior.Start(Context());

            Assert.Equal(new[] {"trader"}, roles.Started);
            Assert.Equal("trader", state.State.ActiveRole);
            Assert.Equal(1, state.State.RoleIndex);
        }

        [Fact]
        public void MiningMustLeaveWorldAndReturnBeforeNextRoleStarts()
        {
            var states = new MemoryPlayerStateStore();
            var roles = new RoleFactory();
            var character = new CharacterState {Docked = false};
            PlayerAutonomousActorBehavior behavior = Create(
                Definition("mining", "manufacturer"),
                roles,
                states,
                new MemoryMissionGoalStore(),
                new MemoryTradeStateStore(),
                character);
            GameActionContext context = Context();
            behavior.Start(context);

            behavior.Update(context, TimeSpan.FromSeconds(1));
            Assert.Equal("mining", states.State.ActiveRole);
            Assert.True(states.State.ObservedWorldWork);

            character.Docked = true;
            behavior.Update(context, TimeSpan.FromSeconds(1));

            Assert.Equal("manufacturer", states.State.ActiveRole);
            Assert.Equal(1, states.State.CompletedRoles);
            Assert.Equal(new[] {"mining", "manufacturer"}, roles.Started);
            Assert.Equal(new[] {"mining"}, roles.Stopped);
        }

        [Fact]
        public void ShipmentCommitmentPreventsTimedYieldAcrossRestart()
        {
            var states = new MemoryPlayerStateStore
            {
                State = new AutonomousPlayerState(
                    7,
                    "trader,manufacturer",
                    0,
                    "trader",
                    DateTime.UtcNow.AddHours(-1),
                    true)
            };
            var trades = new MemoryTradeStateStore
            {
                State = new AutonomousTradeState(
                    7, 100, 1, 5, 10, 20, 1, 30, 40, 2, DateTime.UtcNow)
            };
            var roles = new RoleFactory();
            PlayerAutonomousActorBehavior behavior = Create(
                Definition("trader", "manufacturer"),
                roles,
                states,
                new MemoryMissionGoalStore(),
                trades,
                new CharacterState {Docked = true});
            GameActionContext context = Context();

            behavior.Start(context);
            behavior.Update(context, TimeSpan.FromSeconds(1));

            Assert.Equal("trader", states.State.ActiveRole);
            Assert.Empty(roles.Stopped);
        }

        [Fact]
        public void CompletedMissionAdvancesAndClearsOnlyCoordinatorGoalForRepeat()
        {
            var missions = new MemoryMissionGoalStore
            {
                State = new AutonomousMissionGoalState(
                    7, MissionCategory.Transport, 0, 1, 1,
                    "complete", 100)
            };
            var states = new MemoryPlayerStateStore
            {
                State = new AutonomousPlayerState(
                    7,
                    "mission,mining",
                    0,
                    "mission",
                    DateTime.UtcNow.AddMinutes(-1))
            };
            PlayerAutonomousActorBehavior behavior = Create(
                Definition("mission", "mining"),
                new RoleFactory(),
                states,
                missions,
                new MemoryTradeStateStore(),
                new CharacterState {Docked = true});
            GameActionContext context = Context();

            behavior.Start(context);
            behavior.Update(context, TimeSpan.Zero);

            Assert.Equal("mining", states.State.ActiveRole);
            Assert.Null(missions.State);
            Assert.Equal(1, missions.DeleteCount);
        }

        [Fact]
        public void CompletedMissionWaitsForConfiguredProgressionBeforeRoleChange()
        {
            var missions = new MemoryMissionGoalStore
            {
                State = new AutonomousMissionGoalState(
                    7, MissionCategory.Transport, 0, 1, 1,
                    "waiting_for_progression", 100,
                    blockedReason: "not_enough_extension_points")
            };
            var states = new MemoryPlayerStateStore
            {
                State = new AutonomousPlayerState(
                    7,
                    "mission,mining",
                    0,
                    "mission",
                    DateTime.UtcNow.AddSeconds(-1))
            };
            PlayerAutonomousActorBehavior behavior = Create(
                Definition("mission", "mining"),
                new RoleFactory(),
                states,
                missions,
                new MemoryTradeStateStore(),
                new CharacterState {Docked = true});
            GameActionContext context = Context();

            behavior.Start(context);
            behavior.Update(context, TimeSpan.Zero);

            Assert.Equal("mission", states.State.ActiveRole);
            Assert.Equal(0, missions.DeleteCount);
        }

        [Fact]
        public void FullScheduleConnectsAllBasicPlayerRolesAndWrapsDurably()
        {
            var equipment = new MemoryEquipmentGoalStore
            {
                State = new AutonomousEquipmentGoalState(7, 100, "ready")
            };
            var missions = new MemoryMissionGoalStore
            {
                State = new AutonomousMissionGoalState(
                    7, MissionCategory.Transport, 0, 1, 1, "complete", 100)
            };
            var industry = new MemoryIndustryGoalStore
            {
                State = new AutonomousIndustryGoalState(
                    7, 200, 1, 0, 300, "WaitingDemand")
            };
            var states = new MemoryPlayerStateStore();
            var roles = new RoleFactory();
            var character = new CharacterState {Docked = true};
            PlayerAutonomousActorBehavior behavior = Create(
                Definition("equipment", "mission", "mining", "trader", "manufacturer"),
                roles,
                states,
                missions,
                new MemoryTradeStateStore(),
                character,
                equipment,
                industry);
            GameActionContext context = Context();

            behavior.Start(context);
            behavior.Update(context, TimeSpan.Zero);
            behavior.Update(context, TimeSpan.Zero);

            character.Docked = false;
            behavior.Update(context, TimeSpan.Zero);
            character.Docked = true;
            behavior.Update(context, TimeSpan.Zero);

            character.Docked = false;
            behavior.Update(context, TimeSpan.Zero);
            character.Docked = true;
            behavior.Update(context, TimeSpan.Zero);

            behavior.Update(context, TimeSpan.Zero);

            Assert.Equal("equipment", states.State.ActiveRole);
            Assert.Equal(5, states.State.CompletedRoles);
            Assert.Equal(1, states.State.CompletedCycles);
            Assert.Equal(
                new[] {"equipment", "mission", "mining", "trader", "manufacturer", "equipment"},
                roles.Started);
        }

        [Fact]
        public void RoleLoadoutIsBoundToEachChildAndPersistsThroughRestart()
        {
            AutonomousActorDefinition definition = Definition("mission", "mining");
            var missionLoadout = Loadout("combat_robot");
            var miningLoadout = Loadout("mining_robot");
            definition.Player.Loadouts["MISSION"] = missionLoadout;
            definition.Player.Loadouts["mining"] = miningLoadout;
            var missions = new MemoryMissionGoalStore
            {
                State = new AutonomousMissionGoalState(
                    7, MissionCategory.Transport, 0, 1, 1, "complete", 100)
            };
            var states = new MemoryPlayerStateStore();
            var firstRoles = new RoleFactory();
            var character = new CharacterState {Docked = true};
            GameActionContext context = Context();
            PlayerAutonomousActorBehavior behavior = Create(
                definition,
                firstRoles,
                states,
                missions,
                new MemoryTradeStateStore(),
                character);

            behavior.Start(context);
            Assert.Same(missionLoadout, firstRoles.Loadouts["mission"]);
            behavior.Update(context, TimeSpan.Zero);
            Assert.Same(miningLoadout, firstRoles.Loadouts["mining"]);

            behavior.Stop(context);
            var restartedRoles = new RoleFactory();
            behavior = Create(
                definition,
                restartedRoles,
                states,
                missions,
                new MemoryTradeStateStore(),
                character);
            behavior.Start(context);

            Assert.Equal("mining", states.State.ActiveRole);
            Assert.Same(miningLoadout, restartedRoles.Loadouts["mining"]);
        }

        private static AutonomousActorDefinition Definition(params string[] roles)
        {
            return new AutonomousActorDefinition
            {
                CharacterId = 7,
                Behavior = "player",
                Player = new AutonomousPlayerOptions
                {
                    Roles = new List<string>(roles),
                    MinimumRoleSeconds = 0,
                    MaximumRoleSeconds = 5
                }
            };
        }

        private static AutonomousEquipmentOptions Loadout(string robot)
        {
            return new AutonomousEquipmentOptions
            {
                Enabled = true,
                Robot = robot,
                RepairFacilityEid = 100
            };
        }

        private static PlayerAutonomousActorBehavior Create(
            AutonomousActorDefinition definition,
            RoleFactory roles,
            MemoryPlayerStateStore states,
            MemoryMissionGoalStore missions,
            MemoryTradeStateStore trades,
            CharacterState character,
            MemoryEquipmentGoalStore equipment = null,
            MemoryIndustryGoalStore industry = null)
        {
            return new PlayerAutonomousActorBehavior(
                definition,
                roles.Create,
                states,
                missions,
                equipment ?? new MemoryEquipmentGoalStore(),
                industry ?? new MemoryIndustryGoalStore(),
                trades,
                character,
                new Audit());
        }

        private static GameActionContext Context()
        {
            return new GameActionContext(
                new Character(7, null, null, null, null, null, null, null, null, null, null, null, null),
                GameActionSource.Autonomous);
        }

        private sealed class RoleFactory
        {
            public List<string> Started { get; } = new List<string>();
            public List<string> Stopped { get; } = new List<string>();

            public Dictionary<string, AutonomousEquipmentOptions> Loadouts { get; } =
                new Dictionary<string, AutonomousEquipmentOptions>();

            public IAutonomousActorBehavior Create(string role, AutonomousActorDefinition definition)
            {
                Loadouts[role] = definition.Equipment;
                return new Role(role, Started, Stopped);
            }
        }

        private sealed class Role : IAutonomousActorBehavior
        {
            private readonly List<string> _started;
            private readonly List<string> _stopped;

            public Role(string name, List<string> started, List<string> stopped)
            {
                Name = name;
                _started = started;
                _stopped = stopped;
            }

            public string Name { get; }
            public void Start(GameActionContext context) => _started.Add(Name);
            public void Update(GameActionContext context, TimeSpan elapsed) { }
            public void Stop(GameActionContext context) => _stopped.Add(Name);
        }

        private sealed class CharacterState : IAutonomousPlayerCharacterObservationService
        {
            public bool Docked { get; set; }
            public bool IsDocked(GameActionContext context) => Docked;
        }

        private sealed class MemoryPlayerStateStore : IAutonomousPlayerStateStore
        {
            public AutonomousPlayerState State { get; set; }
            public AutonomousPlayerState Load(int characterId) => State;
            public void Save(AutonomousPlayerState state) => State = state;
        }

        private sealed class MemoryMissionGoalStore : IAutonomousMissionGoalStore
        {
            public AutonomousMissionGoalState State { get; set; }
            public int DeleteCount { get; private set; }
            public AutonomousMissionGoalState Load(int characterId) => State;
            public void Save(AutonomousMissionGoalState state) => State = state;
            public void Delete(int characterId)
            {
                DeleteCount++;
                State = null;
            }
        }

        private sealed class MemoryEquipmentGoalStore : IAutonomousEquipmentGoalStore
        {
            public AutonomousEquipmentGoalState State { get; set; }
            public AutonomousEquipmentGoalState Load(int characterId) => State;
            public void Save(AutonomousEquipmentGoalState state) => State = state;
            public void Delete(int characterId) => State = null;
        }

        private sealed class MemoryIndustryGoalStore : IAutonomousIndustryGoalStore
        {
            public AutonomousIndustryGoalState State { get; set; }
            public AutonomousIndustryGoalState Load(int characterId) => State;
            public void Save(AutonomousIndustryGoalState state) => State = state;
        }

        private sealed class MemoryTradeStateStore : IAutonomousTradeStateStore
        {
            public AutonomousTradeState State { get; set; }
            public AutonomousTradeState Load(int characterId) => State;
            public void Save(AutonomousTradeState state) => State = state;
            public void Delete(int characterId) => State = null;
        }

        private sealed class Audit : IAutonomousActorAudit
        {
            public void Write(int characterId, string eventName, AutonomousActorStatus status, string reason = null) { }
        }
    }
}
