using System;
using System.Collections.Generic;
using System.Drawing;
using Perpetuum.Zones.Terrains.Materials;

namespace Perpetuum.Zones.Scanning.Results
{
    public class MineralScanResult : IMineralScanObservation
    {
        public int Id { private get; set; }
        public DateTime Creation { get; set; }
        public int ZoneId { get; set; }
        public MaterialType MaterialType { get; set; }
        public Area Area { get; set; }

        public string Folder { private get; set; }
        public bool FoundAny { get; set; }
        public double ScanAccuracy { get; set; }

        private readonly uint[] _scanData;

        public long Quality { get; set; }

        public MaterialProbeType ProbeType => MaterialProbeType.Tile;

        public MineralScanResult()
        {
        }

        public MineralScanResult(uint[] scanData)
        {
            _scanData = scanData == null ? null : (uint[])scanData.Clone();
        }

        /// <summary>
        /// Selects the strongest noisy sample in the exact grid sent to the
        /// client. Ties use client packet order (top-to-bottom, left-to-right).
        /// </summary>
        public bool TryGetRichestLocation(out Point location, out uint amount)
        {
            location = Point.Empty;
            amount = 0;

            int expectedLength = Area.Width * Area.Height;
            if (_scanData == null || _scanData.Length != expectedLength)
                return false;

            int richestOffset = -1;
            for (int offset = 0; offset < _scanData.Length; offset++)
            {
                if (_scanData[offset] <= amount)
                    continue;

                amount = _scanData[offset];
                richestOffset = offset;
            }

            if (richestOffset < 0)
                return false;

            location = new Point(
                Area.X1 + richestOffset % Area.Width,
                Area.Y1 + richestOffset / Area.Width);
            return true;
        }

        public override string ToString()
        {
            return $"Id: {Id}, MaterialType: {MaterialType}, Creation: {Creation}";
        }

        public IDictionary<string,object> ToDictionary()
        {
            var dictionary = new Dictionary<string, object>
            {
                {k.ID,Id}, 
                {k.materialProbeType, (int) MaterialProbeType.Tile}, 
                {k.creation, Creation}, 
                {k.zoneID, ZoneId}, 
                {k.materialType, (int) MaterialType}, 
                {k.area, Area}, 
                {k.scanAccuracy,ScanAccuracy},
                {k.quality,Quality},
                {k.folder, Folder},
            };

            return dictionary;
        }

        public Packet ToPacket()
        {
            var packet = new Packet(ZoneCommand.ScanMineralTileResult);

            packet.AppendInt(ZoneId);
            packet.AppendByte((byte)MaterialType);
            packet.AppendArea(Area);
            packet.AppendDouble(ScanAccuracy);
            packet.AppendUInt64Array(_scanData);

            return packet;            
        }

        public MineralScanResultItem ToItem()
        {
            var item = MineralScanResultItem.Create();

            var probeInfo = new Dictionary<string, object>
            {
                {k.date, Creation},
                {k.area, Area},
                {k.zone, ZoneId},
                {k.mineral, MaterialType.GetName()},
                {k.scanAccuracy, ScanAccuracy},
                {k.type, 1}
            };

            item.DynamicProperties.Update(k.probeInfo, probeInfo);
            return item;
        }
    }
}
