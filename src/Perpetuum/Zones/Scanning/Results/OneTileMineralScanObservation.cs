using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Linq;

namespace Perpetuum.Zones.Scanning.Results
{
    public sealed class OneTileMineralSample
    {
        public OneTileMineralSample(int definition, int amount)
        {
            Definition = definition;
            Amount = amount;
        }

        public int Definition { get; }
        public int Amount { get; }
    }

    public sealed class OneTileMineralScanObservation : IMineralScanObservation
    {
        private readonly OneTileMineralSample[] _samples;
        private readonly ReadOnlyCollection<OneTileMineralSample> _readOnlySamples;

        public OneTileMineralScanObservation(
            long moduleEid,
            Point location,
            IEnumerable<OneTileMineralSample> samples,
            DateTime creation)
        {
            ModuleEid = moduleEid;
            Location = location;
            _samples = (samples ?? Enumerable.Empty<OneTileMineralSample>()).ToArray();
            _readOnlySamples = Array.AsReadOnly(_samples);
            Creation = creation;
        }

        public MaterialProbeType ProbeType => MaterialProbeType.OneTile;
        public DateTime Creation { get; }
        public long ModuleEid { get; }
        public Point Location { get; }
        public IReadOnlyList<OneTileMineralSample> Samples => _readOnlySamples;

        public Packet ToPacket()
        {
            var packet = new Packet(ZoneCommand.ScanOneTileResult);
            packet.AppendLong(ModuleEid);
            packet.AppendPoint(Location);

            using (var stream = new BinaryStream())
            {
                foreach (OneTileMineralSample sample in _samples)
                {
                    stream.AppendInt(sample.Definition);
                    stream.AppendInt(sample.Amount);
                }

                packet.AppendByte((byte)_samples.Length);
                packet.AppendStream(stream);
            }

            return packet;
        }
    }
}
