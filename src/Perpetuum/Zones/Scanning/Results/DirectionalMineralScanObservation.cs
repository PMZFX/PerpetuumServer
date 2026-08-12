using System;
using System.Drawing;
using Perpetuum.Zones.Terrains.Materials;

namespace Perpetuum.Zones.Scanning.Results
{
    public sealed class DirectionalMineralScanObservation : IMineralScanObservation
    {
        public DirectionalMineralScanObservation(
            MaterialType materialType,
            Point origin,
            bool foundAny,
            byte encodedDirection,
            bool inRange,
            DateTime creation)
        {
            MaterialType = materialType;
            Origin = origin;
            FoundAny = foundAny;
            EncodedDirection = encodedDirection;
            InRange = inRange;
            Creation = creation;
        }

        public MaterialProbeType ProbeType => MaterialProbeType.Directional;
        public DateTime Creation { get; }
        public MaterialType MaterialType { get; }
        public Point Origin { get; }
        public bool FoundAny { get; }
        public byte EncodedDirection { get; }
        public double Direction => EncodedDirection / 255.0;
        public bool InRange { get; }

        public Packet ToPacket()
        {
            var packet = new Packet(ZoneCommand.ScanMineralDirectionalResult);
            packet.AppendInt((int)MaterialType);
            packet.AppendPoint(Origin);
            packet.AppendBool(FoundAny);
            packet.AppendByte(EncodedDirection);
            packet.AppendBool(InRange);
            return packet;
        }
    }
}
