using System;
using Perpetuum.Robots;
using Perpetuum.Zones.Scanning.Modules;
using Perpetuum.Zones.Scanning.Results;

namespace Perpetuum.Services.Actions
{
    public interface IMineralScanObservationService
    {
        IMineralScanObservation GetLatest(
            GameActionContext context,
            RobotComponentType component,
            int slot);
    }

    /// <summary>
    /// Reads only the latest result produced by the actor's fitted geoscanner.
    /// It does not inspect terrain or mineral layers.
    /// </summary>
    public sealed class MineralScanObservationService : IMineralScanObservationService
    {
        public IMineralScanObservation GetLatest(
            GameActionContext context,
            RobotComponentType component,
            int slot)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            var player = context.Actor.GetPlayerRobotFromZone()
                .ThrowIfNull(ErrorCodes.PlayerNotFound);
            var robotComponent = player.GetRobotComponent(component)
                .ThrowIfNull(ErrorCodes.RobotComponentNotSupplied);
            var scanner = robotComponent.GetModule(slot)
                .ThrowIfNotType<GeoScannerModule>(ErrorCodes.GeoScannerModuleNotFound);
            return scanner.LastObservation;
        }
    }
}
