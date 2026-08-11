using System;
using System.Collections.Generic;
using System.Threading;
using Perpetuum.Builders;
using Perpetuum.Zones;
using Perpetuum.Zones.Beams;
using Perpetuum.Zones.Terrains;

namespace Perpetuum.Services.Autonomous
{
    /// <summary>
    /// Marks a legitimately controlled autonomous player as session-owned without
    /// creating a relay or zone socket. Outbound client presentation is discarded.
    /// </summary>
    public sealed class AutonomousZoneSession : IZoneSession
    {
        private static int _nextId;

        public AutonomousZoneSession()
        {
            Id = -Interlocked.Increment(ref _nextId);
        }

        public int Id { get; }
        public AccessLevel AccessLevel => AccessLevel.normal;
        public DateTime DisconnectTime => DateTime.MinValue;
        public TimeSpan InactiveTime => TimeSpan.Zero;

        public void SendPacket(IBuilder<Packet> packetBuilder) { }
        public void SendPacket(Packet packet) { }
        public void CancelLogout() { }
        public void ResetLogoutTimer() { }
        public void SendTerrainData() { }
        public void SendBeamIfVisible(Beam beam) { }
        public void SendBeam(IBuilder<Beam> builder) { }
        public void SendBeam(Beam beam) { }
        public void EnqueueLayerUpdates(IReadOnlyCollection<TerrainUpdateInfo> infos) { }
        public void Stop() { }
    }
}
