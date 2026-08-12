using System;
using System.Linq;
using System.Transactions;
using Perpetuum.Data;
using Perpetuum.ExportedTypes;
using Perpetuum.Items;
using Perpetuum.Players;
using Perpetuum.Services.MissionEngine;
using Perpetuum.Services.MissionEngine.MissionProcessorObjects;
using Perpetuum.Zones;
using Perpetuum.Zones.Teleporting;
using Perpetuum.Zones.Teleporting.Strategies;

namespace Perpetuum.Services.Actions
{
    public sealed class TeleportAction
    {
        public TeleportAction(long teleportEid, int descriptionId, int trainingRewardLevel = 0)
        {
            TeleportEid = teleportEid;
            DescriptionId = descriptionId;
            TrainingRewardLevel = trainingRewardLevel;
        }

        public long TeleportEid { get; }
        public int DescriptionId { get; }
        public int TrainingRewardLevel { get; }
    }

    public interface ITeleportActionService
    {
        void Execute(GameActionContext context, TeleportAction action);
    }

    /// <summary>
    /// Uses an in-world teleport through the same validation, transaction,
    /// mission, cooldown, and activation path used by a connected client.
    /// </summary>
    public sealed class TeleportActionService : ITeleportActionService
    {
        private readonly ITeleportStrategyFactories _teleportStrategyFactories;
        private readonly MissionProcessor _missionProcessor;
        private readonly IGameActionAudit _audit;

        public TeleportActionService(
            ITeleportStrategyFactories teleportStrategyFactories,
            MissionProcessor missionProcessor,
            IGameActionAudit audit)
        {
            _teleportStrategyFactories = teleportStrategyFactories ??
                                         throw new ArgumentNullException(nameof(teleportStrategyFactories));
            _missionProcessor = missionProcessor ?? throw new ArgumentNullException(nameof(missionProcessor));
            _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        }

        public void Execute(GameActionContext context, TeleportAction action)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            _audit.Execute(context, "teleportUse", () => ExecuteCore(context, action));
        }

        private void ExecuteCore(GameActionContext context, TeleportAction action)
        {
            using (var scope = Db.CreateTransaction())
            {
                Player player = context.Actor.GetPlayerRobotFromZone()
                    .ThrowIfNull(ErrorCodes.PlayerNotFound);
                Teleport teleport = (player.Zone.GetUnitOrThrow(action.TeleportEid) as Teleport)
                    .ThrowIfNull(ErrorCodes.TeleportNotFound);
                teleport.IsEnabled.ThrowIfFalse(ErrorCodes.TeleportDisabled);

                teleport.AcceptVisitor(new TeleportPlayerValidator(player));

                TeleportDescription description = teleport.GetTeleportDescriptions()
                    .FirstOrDefault(candidate => candidate.id == action.DescriptionId)
                    .ThrowIfNull(ErrorCodes.TeleportDescriptionNotFound);
                description.active.ThrowIfFalse(ErrorCodes.TeleportChannelInactive);
                description.IsValid().ThrowIfFalse(ErrorCodes.InvalidTeleportChannel);

                int sourceZoneId = player.Zone.Id;
                Position playerPosition = player.CurrentPosition;
                ITeleportStrategy strategy = CreateTeleportStrategy(description, action.TrainingRewardLevel)
                    .ThrowIfNull(ErrorCodes.InvalidTeleportChannel);
                strategy.DoTeleport(player);

                Transaction.Current.OnCommited(() =>
                {
                    _missionProcessor.EnqueueMissionTargetAsync(
                        context.Actor,
                        MissionTargetType.teleport,
                        data =>
                        {
                            data.Add(k.channel, description.id);
                            data.Add(k.zoneID, sourceZoneId);
                            data.Add(k.position, playerPosition);
                        });

                    switch (teleport)
                    {
                        case MobileStrongholdTeleport mobileStrongholdTeleport:
                            mobileStrongholdTeleport.ApplyTeleportCooldownEffect();
                            mobileStrongholdTeleport.Activate(player, description);
                            break;
                        case MobileWorldTeleport mobileWorldTeleport:
                            mobileWorldTeleport.ApplyTeleportCooldownEffect();
                            mobileWorldTeleport.Activate(player, description);
                            break;
                        case MobileTeleport mobileTeleport:
                            mobileTeleport.ApplyTeleportCooldownEffect();
                            break;
                    }
                });

                scope.Complete();
            }
        }

        private ITeleportStrategy CreateTeleportStrategy(
            TeleportDescription description,
            int trainingRewardLevel)
        {
            switch (description.descriptionType)
            {
                case TeleportDescriptionType.WithinZone:
                    var withinZone = _teleportStrategyFactories.TeleportWithinZoneFactory();
                    withinZone.TargetPosition = description.GetRandomTargetPosition();
                    return withinZone;
                case TeleportDescriptionType.AnotherZone:
                    var anotherZone = _teleportStrategyFactories
                        .TeleportToAnotherZoneFactory(description.TargetZone);
                    anotherZone.TargetPosition = description.GetRandomTargetPosition();
                    return anotherZone;
                case TeleportDescriptionType.TrainingExit:
                    var trainingExit = _teleportStrategyFactories.TrainingExitStrategyFactory(description);
                    trainingExit.TrainingRewardLevel = trainingRewardLevel;
                    return trainingExit;
                default:
                    return null;
            }
        }

        private sealed class TeleportPlayerValidator : TeleportVisitor
        {
            private readonly Player _player;

            public TeleportPlayerValidator(Player player)
            {
                _player = player;
            }

            public override void VisitTeleport(Teleport teleport)
            {
                _player.HasTeleportSicknessEffect.ThrowIfTrue(ErrorCodes.TeleportTimerStillRunning);
                (_player.HasPvpEffect && _player.HasNoTeleportWhilePVP)
                    .ThrowIfTrue(ErrorCodes.CantBeUsedInPvp);
                _player.CurrentPosition
                    .IsInRangeOf3D(teleport.CurrentPosition, Teleport.TeleportRange)
                    .ThrowIfFalse(ErrorCodes.TeleportOutOfRange);
                base.VisitTeleport(teleport);
            }

            public override void VisitMobileTeleport(MobileTeleport teleport)
            {
                if (!_player.Session.AccessLevel.IsAdminOrGm())
                {
                    _player.HasPvpEffect.ThrowIfTrue(ErrorCodes.CantBeUsedInPvp);
                    teleport.EffectHandler.ContainsEffect(EffectType.effect_teleport_cooldown)
                        .ThrowIfTrue(ErrorCodes.TeleportSourceNotUsable);
                }

                var ownerCharacter = teleport.GetOwnerAsCharacter();
                if (_player.Character != ownerCharacter)
                {
                    var playerGang = _player.Gang;
                    playerGang.ThrowIfNull(ErrorCodes.CharacterNotInGang);
                    playerGang.IsMember(ownerCharacter)
                        .ThrowIfFalse(ErrorCodes.CharacterNotInTheOwnerGang);
                }

                VisitTeleport(teleport);
            }

            public override void VisitMobileWorldTeleport(MobileWorldTeleport teleport)
            {
                VisitMobileTeleport(teleport);
            }

            public override void VisitMobileStrongholdTeleport(MobileStrongholdTeleport teleport)
            {
                VisitMobileTeleport(teleport);
            }
        }
    }
}
