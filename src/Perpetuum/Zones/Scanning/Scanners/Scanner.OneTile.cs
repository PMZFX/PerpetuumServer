using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Perpetuum.EntityFramework;
using Perpetuum.Zones.Scanning.Ammos;
using Perpetuum.Zones.Scanning.Results;
using Perpetuum.Zones.Terrains.Materials.Minerals;

namespace Perpetuum.Zones.Scanning.Scanners
{
    public partial class Scanner : IEntityVisitor<OneTileScannerAmmo>
    {
        public void Visit(OneTileScannerAmmo ammo)
        {
            OneTileMineralScanObservation observation = BuildObservation(_player.CurrentPosition);
            _module.LastObservation = observation;
            _player.Session.SendPacket(observation.ToPacket());

            //do mission
            OnMineralScanned(MaterialProbeType.OneTile);
        }

        private OneTileMineralScanObservation BuildObservation(Point location)
        {
            var samples = new List<OneTileMineralSample>();

            foreach (var layer in _zone.Terrain.Materials.OfType<MineralLayer>())
            {
                if (!layer.TryGetNode(location, out MineralNode node))
                    continue;

                var amount = node.GetValue(location);
                if (amount <= 0)
                    continue;

                var material = _materialHelper.GetMaterialInfo(layer.Type);
                samples.Add(new OneTileMineralSample(material.EntityDefault.Definition, (int)amount));
            }

            return new OneTileMineralScanObservation(
                _module.Eid,
                location,
                samples,
                DateTime.UtcNow);
        }
    }
}
