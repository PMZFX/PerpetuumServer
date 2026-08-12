using Perpetuum.Robots;
using Perpetuum.Services.Autonomous;
using Perpetuum.Zones.Scanning;
using Perpetuum.Zones.Terrains.Materials;
using Xunit;

namespace Perpetuum.Tests.Services.Autonomous
{
    public class AutonomousMiningEquipmentTests
    {
        [Fact]
        public void SelectsLoadedScannerAndDrillForRequestedMaterial()
        {
            var wrongMaterial = Module(
                RobotComponentType.Head, 1, 10, MaterialType.Crude, MaterialProbeType.Tile);
            var empty = Module(
                RobotComponentType.Head, 2, 0, MaterialType.Titan, MaterialProbeType.Tile);
            var scanner = Module(
                RobotComponentType.Head, 3, 4, MaterialType.Titan, MaterialProbeType.Tile);
            var drill = Module(
                RobotComponentType.Chassis, 2, 20, MaterialType.Titan, MaterialProbeType.Undefined);
            var equipment = new AutonomousMiningEquipmentSnapshot(
                new[] { scanner, empty, wrongMaterial },
                new[] { drill });

            Assert.Same(scanner, equipment.SelectScanner(MaterialType.Titan, MaterialProbeType.Tile));
            Assert.Same(drill, equipment.SelectDrill(MaterialType.Titan));
        }

        [Fact]
        public void SelectionNeverUsesEmptyOrWrongProbeAmmo()
        {
            var equipment = new AutonomousMiningEquipmentSnapshot(
                new[]
                {
                    Module(RobotComponentType.Head, 1, 5, MaterialType.Titan, MaterialProbeType.Directional),
                    Module(RobotComponentType.Head, 2, 0, MaterialType.Titan, MaterialProbeType.Tile)
                },
                new[]
                {
                    Module(RobotComponentType.Chassis, 1, 5, MaterialType.Crude, MaterialProbeType.Undefined)
                });

            Assert.Null(equipment.SelectScanner(MaterialType.Titan, MaterialProbeType.Tile));
            Assert.Null(equipment.SelectDrill(MaterialType.Titan));
        }

        [Fact]
        public void ModuleOrderIsStableByComponentAndSlot()
        {
            var head = Module(RobotComponentType.Head, 4, 1, MaterialType.Titan, MaterialProbeType.Tile);
            var chassis = Module(RobotComponentType.Chassis, 1, 1, MaterialType.Titan, MaterialProbeType.Tile);
            var equipment = new AutonomousMiningEquipmentSnapshot(
                new[] { chassis, head },
                null);

            Assert.Same(head, equipment.Scanners[0]);
            Assert.Same(chassis, equipment.Scanners[1]);
        }

        private static AutonomousMiningModuleSnapshot Module(
            RobotComponentType component,
            int slot,
            int ammoQuantity,
            MaterialType material,
            MaterialProbeType probe)
        {
            return new AutonomousMiningModuleSnapshot(
                slot,
                1000 + slot,
                component,
                slot,
                2000 + slot,
                ammoQuantity,
                material,
                probe);
        }
    }
}
