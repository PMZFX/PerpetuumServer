using System;
using Perpetuum.Accounting.Characters;
using Perpetuum.Players;
using Perpetuum.Services.Actions;
using Perpetuum.Services.Sessions;
using Perpetuum.Zones;

namespace Perpetuum.Services.Autonomous
{
    public sealed class AutonomousActor : IAutonomousActor
    {
        private static readonly TimeSpan TeleportWorldEntryTimeout = TimeSpan.FromSeconds(20);

        private readonly Character _character;
        private readonly ISessionManager _sessionManager;
        private readonly IZoneManager _zoneManager;
        private readonly IAutonomousActorBehavior _behavior;
        private readonly IAutonomousActorAudit _audit;
        private readonly AutonomousZoneSession _zoneSession = new AutonomousZoneSession();
        private readonly GameActionContext _context;
        private Player _initializedPlayer;
        private bool _behaviorRunning;
        private string _reason;
        private TimeSpan _missingPlayerElapsed;

        public AutonomousActor(
            AutonomousActorDefinition definition,
            CharacterFactory characterFactory,
            ISessionManager sessionManager,
            IZoneManager zoneManager,
            AutonomousActorBehaviorFactory behaviorFactory,
            IAutonomousActorAudit audit)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));

            _character = characterFactory(definition.CharacterId);
            _sessionManager = sessionManager;
            _zoneManager = zoneManager;
            _behavior = behaviorFactory(definition);
            _audit = audit;
            _context = new GameActionContext(_character, GameActionSource.Autonomous);
        }

        public int CharacterId => _character.Id;
        public AutonomousActorStatus Status { get; private set; } = AutonomousActorStatus.Stopped;
        public AutonomousActorSnapshot Snapshot =>
            new AutonomousActorSnapshot(CharacterId, _behavior.Name, Status, _reason);

        public void Start()
        {
            if (Status != AutonomousActorStatus.Stopped)
                return;

            if (!_character.IsActive)
                throw new InvalidOperationException($"Autonomous character {CharacterId} is not active.");

            _reason = null;
            _missingPlayerElapsed = TimeSpan.Zero;
            Status = AutonomousActorStatus.Standby;
            _audit.Write(CharacterId, "started", Status);
        }

        public void Stop()
        {
            if (Status == AutonomousActorStatus.Stopped)
                return;

            StopBehavior();
            StopPlayerMovementAndReleaseSession();
            _reason = null;
            Status = AutonomousActorStatus.Stopped;
            _audit.Write(CharacterId, "stopped", Status);
        }

        public void Update(TimeSpan elapsed)
        {
            if (Status == AutonomousActorStatus.Stopped || Status == AutonomousActorStatus.Faulted)
                return;

            if (_sessionManager.GetByCharacter(_character) != null)
            {
                Suspend("human_session");
                return;
            }

            Player player = _zoneManager.GetPlayer(_character);
            if (player == null && !_character.IsDocked && _character.ZoneId.HasValue)
            {
                bool teleportEntryPending = _initializedPlayer != null && _initializedPlayer.States.Teleport;
                if (AutonomousWorldEntryPolicy.ShouldWaitForTeleportEntry(
                        teleportEntryPending,
                        _missingPlayerElapsed,
                        TeleportWorldEntryTimeout))
                {
                    _missingPlayerElapsed += elapsed;
                    return;
                }

                IZone persistedZone = _zoneManager.GetZone(_character.ZoneId.Value);
                if (persistedZone == null)
                    throw new InvalidOperationException($"Autonomous character {CharacterId} has unknown persisted zone {_character.ZoneId.Value}.");

                player = Player.LoadPlayerAndAddToZone(persistedZone, _character);
                _audit.Write(CharacterId, "world_restored", Status, $"zone_{persistedZone.Id}");
            }
            if (player == null)
            {
                _initializedPlayer = null;
                _missingPlayerElapsed = TimeSpan.Zero;
            }
            else
            {
                _missingPlayerElapsed = TimeSpan.Zero;
            }
            if (player != null && player.Session != ZoneSession.None && player.Session != _zoneSession)
            {
                Suspend("external_zone_session");
                return;
            }

            if (player != null && player.Session == ZoneSession.None)
            {
                player.SetSession(_zoneSession);
                if (player != _initializedPlayer)
                {
                    player.ApplyTeleportSicknessEffect();
                    player.ApplyInvulnerableEffect();
                    _initializedPlayer = player;
                }
            }

            if (Status != AutonomousActorStatus.Active)
            {
                _behavior.Start(_context);
                _behaviorRunning = true;
                _reason = null;
                Status = AutonomousActorStatus.Active;
                _audit.Write(CharacterId, "resumed", Status);
            }

            _behavior.Update(_context, elapsed);
        }

        public void Fault(string reason)
        {
            StopBehavior();
            StopPlayerMovementAndReleaseSession();
            _reason = string.IsNullOrWhiteSpace(reason) ? "behavior_failure" : reason;
            Status = AutonomousActorStatus.Faulted;
            _audit.Write(CharacterId, "faulted", Status, _reason);
        }

        private void Suspend(string reason)
        {
            if (Status == AutonomousActorStatus.Suspended && _reason == reason)
                return;

            StopBehavior();
            StopPlayerMovementAndReleaseSession();
            _reason = reason;
            Status = AutonomousActorStatus.Suspended;
            _audit.Write(CharacterId, "suspended", Status, reason);
        }

        private void StopPlayerMovementAndReleaseSession()
        {
            Player player = _zoneManager.GetPlayer(_character);
            if (player == null || player.Session != _zoneSession)
                return;

            player.CurrentSpeed = 0;
            player.SetSession(ZoneSession.None);
        }

        private void StopBehavior()
        {
            if (!_behaviorRunning)
                return;

            _behavior.Stop(_context);
            _behaviorRunning = false;
        }
    }
}
