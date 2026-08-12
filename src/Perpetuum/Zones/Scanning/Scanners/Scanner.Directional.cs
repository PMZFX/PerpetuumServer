using System;
using System.Drawing;
using Perpetuum.EntityFramework;
using Perpetuum.Zones.Scanning.Ammos;
using Perpetuum.Zones.Scanning.Results;
using Perpetuum.Zones.Terrains;
using Perpetuum.Zones.Terrains.Materials;

namespace Perpetuum.Zones.Scanning.Scanners
{
    public partial class Scanner : IEntityVisitor<DirectionalScannerAmmo>
    {
        private const int GOAL_RANGE = 5;
        private const double RANDOM_INTERVAL = 0.1;

        public void Visit(DirectionalScannerAmmo ammo)
        {
            var fromPosition = _player.CurrentPosition;

            var layer = _zone.Terrain.GetMineralLayerOrThrow(ammo.MaterialType);

            var nearestMineralPosition = Point.Empty;
            var nearestDist = int.MaxValue;

            foreach (var node in layer.Nodes)
            {
                var np = node.GetNearestMineralPosition(_player.CurrentPosition);
                var distance = np.SqrDistance(_player.CurrentPosition);
                if (distance >= nearestDist)
                    continue;

                nearestDist = distance;
                nearestMineralPosition = np;
            }

            var isInRange = fromPosition.IsInRangeOf2D(nearestMineralPosition, GOAL_RANGE);
            var direction = fromPosition.DirectionTo(nearestMineralPosition);
            direction = RandomizeDirection(direction);
            var encodedDirection = (byte)(direction * 255);

            var observation = new DirectionalMineralScanObservation(
                ammo.MaterialType,
                fromPosition,
                nearestMineralPosition != Point.Empty,
                encodedDirection,
                isInRange,
                DateTime.UtcNow);
            _module.LastObservation = observation;
            _player.Session.SendPacket(observation.ToPacket());

            if (!isInRange)
                return;

            OnMineralScanned(MaterialProbeType.Directional, ammo.MaterialType);
        }

        private double RandomizeDirection(double direction)
        {
            var randomModifier = (FastRandom.NextDouble(-(1 - _module.ScanAccuracy), (1 - _module.ScanAccuracy))) * RANDOM_INTERVAL;
            direction += randomModifier;
            MathHelper.NormalizeDirection(ref direction);
            return direction;
        }

    }
}
