using System;
using System.Drawing;
using Perpetuum.Zones;
using Perpetuum.Zones.Scanning;
using Perpetuum.Zones.Scanning.Results;
using Perpetuum.Zones.Terrains.Materials;
using Xunit;

namespace Perpetuum.Tests.Zones.Scanning
{
    public class MineralScanObservationTests
    {
        [Fact]
        public void TileScanSelectsRichestPlayerVisibleSample()
        {
            var result = new MineralScanResult(new uint[]
            {
                0, 10, 0,
                4, 25, 2
            })
            {
                Area = new Area(20, 30, 22, 31),
                Creation = DateTime.UtcNow
            };

            bool found = result.TryGetRichestLocation(out Point location, out uint amount);

            Assert.True(found);
            Assert.Equal(new Point(21, 31), location);
            Assert.Equal((uint)25, amount);
            Assert.Equal(MaterialProbeType.Tile, result.ProbeType);
        }

        [Fact]
        public void TileScanUsesPacketOrderForEqualSamples()
        {
            var result = new MineralScanResult(new uint[] { 8, 8, 1 })
            {
                Area = new Area(4, 7, 6, 7)
            };

            Assert.True(result.TryGetRichestLocation(out Point location, out uint amount));
            Assert.Equal(new Point(4, 7), location);
            Assert.Equal((uint)8, amount);
        }

        [Fact]
        public void EmptyOrIncompleteTileScanHasNoCandidate()
        {
            var empty = new MineralScanResult(new uint[] { 0, 0 })
            {
                Area = new Area(1, 1, 2, 1)
            };
            var incomplete = new MineralScanResult(new uint[] { 9 })
            {
                Area = new Area(1, 1, 2, 1)
            };

            Assert.False(empty.TryGetRichestLocation(out _, out _));
            Assert.False(incomplete.TryGetRichestLocation(out _, out _));
        }

        [Fact]
        public void DirectionalObservationExposesOnlyEncodedClientDirection()
        {
            var observation = new DirectionalMineralScanObservation(
                MaterialType.Titan,
                new Point(10, 11),
                true,
                127,
                false,
                DateTime.UtcNow);

            Assert.Equal(MaterialProbeType.Directional, observation.ProbeType);
            Assert.Equal(127, observation.EncodedDirection);
            Assert.Equal(127 / 255.0, observation.Direction, 8);
            Assert.True(observation.FoundAny);
            Assert.False(observation.InRange);

            var packet = new Packet(observation.ToPacket().ToArray());
            Assert.Equal(ZoneCommand.ScanMineralDirectionalResult, packet.Command);
            Assert.Equal((int)MaterialType.Titan, packet.ReadInt());
            Assert.Equal(10, packet.ReadInt());
            Assert.Equal(11, packet.ReadInt());
            Assert.Equal(1, packet.ReadByte());
            Assert.Equal(127, packet.ReadByte());
            Assert.Equal(0, packet.ReadByte());
            Assert.True(packet.AtEnd());
        }

        [Fact]
        public void OneTileObservationCopiesSamples()
        {
            var samples = new[] { new OneTileMineralSample(100, 250) };
            var observation = new OneTileMineralScanObservation(
                42,
                new Point(7, 9),
                samples,
                DateTime.UtcNow);

            samples[0] = new OneTileMineralSample(200, 1);

            Assert.Equal(MaterialProbeType.OneTile, observation.ProbeType);
            Assert.Single(observation.Samples);
            Assert.Equal(100, observation.Samples[0].Definition);
            Assert.Equal(250, observation.Samples[0].Amount);

            var packet = new Packet(observation.ToPacket().ToArray());
            Assert.Equal(ZoneCommand.ScanOneTileResult, packet.Command);
            Assert.Equal(42, packet.ReadLong());
            Assert.Equal(7, packet.ReadInt());
            Assert.Equal(9, packet.ReadInt());
            Assert.Equal(1, packet.ReadByte());
            Assert.Equal(100, packet.ReadInt());
            Assert.Equal(250, packet.ReadInt());
            Assert.True(packet.AtEnd());
        }
    }
}
