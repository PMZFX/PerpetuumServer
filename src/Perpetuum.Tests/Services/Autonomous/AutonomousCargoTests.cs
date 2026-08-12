using System.Linq;
using Perpetuum.Services.Autonomous;
using Xunit;

namespace Perpetuum.Tests.Services.Autonomous
{
    public class AutonomousCargoTests
    {
        [Fact]
        public void ReportsCapacityAndFiltersRawMaterials()
        {
            var snapshot = new AutonomousCargoSnapshot(
                50,
                100,
                75,
                new[]
                {
                    new AutonomousCargoItemSnapshot(2, 200, 5, 25, false),
                    new AutonomousCargoItemSnapshot(1, 100, 10, 50, true)
                });

            Assert.Equal(0.75, snapshot.FillRatio);
            Assert.Equal(25, snapshot.FreeCapacity);
            Assert.Equal(1, snapshot.Items[0].Eid);
            Assert.Equal(100, Assert.Single(snapshot.RawMaterials).Definition);
        }

        [Fact]
        public void ZeroCapacityIsTreatedAsFull()
        {
            var snapshot = new AutonomousCargoSnapshot(1, 0, 0, null);

            Assert.Equal(1.0, snapshot.FillRatio);
            Assert.Empty(snapshot.Items);
            Assert.Empty(snapshot.RawMaterials.ToArray());
        }
    }
}
