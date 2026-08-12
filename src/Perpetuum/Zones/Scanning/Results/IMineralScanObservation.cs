using System;

namespace Perpetuum.Zones.Scanning.Results
{
    /// <summary>
    /// A structured copy of a mineral scan result already revealed by a fitted
    /// geoscanner. Implementations must not contain facts absent from the
    /// corresponding client packet.
    /// </summary>
    public interface IMineralScanObservation
    {
        MaterialProbeType ProbeType { get; }
        DateTime Creation { get; }
    }
}
