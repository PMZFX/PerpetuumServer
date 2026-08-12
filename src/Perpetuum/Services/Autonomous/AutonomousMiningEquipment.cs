using System;
using System.Collections.Generic;
using System.Linq;
using Perpetuum.Items.Ammos;
using Perpetuum.Modules;
using Perpetuum.Robots;
using Perpetuum.Services.Actions;
using Perpetuum.Zones.Scanning;
using Perpetuum.Zones.Scanning.Ammos;
using Perpetuum.Zones.Scanning.Modules;
using Perpetuum.Zones.Terrains.Materials;

namespace Perpetuum.Services.Autonomous
{
    public sealed class AutonomousMiningModuleSnapshot
    {
        public AutonomousMiningModuleSnapshot(
            long moduleEid,
            int moduleDefinition,
            RobotComponentType component,
            int slot,
            int ammoDefinition,
            int ammoQuantity,
            MaterialType materialType,
            MaterialProbeType probeType)
        {
            ModuleEid = moduleEid;
            ModuleDefinition = moduleDefinition;
            Component = component;
            Slot = slot;
            AmmoDefinition = ammoDefinition;
            AmmoQuantity = ammoQuantity;
            MaterialType = materialType;
            ProbeType = probeType;
        }

        public long ModuleEid { get; }
        public int ModuleDefinition { get; }
        public RobotComponentType Component { get; }
        public int Slot { get; }
        public int AmmoDefinition { get; }
        public int AmmoQuantity { get; }
        public MaterialType MaterialType { get; }
        public MaterialProbeType ProbeType { get; }
        public bool HasAmmo => AmmoDefinition > 0 && AmmoQuantity > 0;
    }

    public sealed class AutonomousMiningEquipmentSnapshot
    {
        public AutonomousMiningEquipmentSnapshot(
            IEnumerable<AutonomousMiningModuleSnapshot> scanners,
            IEnumerable<AutonomousMiningModuleSnapshot> drills)
        {
            Scanners = Order(scanners);
            Drills = Order(drills);
        }

        public IReadOnlyList<AutonomousMiningModuleSnapshot> Scanners { get; }
        public IReadOnlyList<AutonomousMiningModuleSnapshot> Drills { get; }

        public AutonomousMiningModuleSnapshot SelectScanner(
            MaterialType materialType,
            MaterialProbeType probeType)
        {
            return Scanners.FirstOrDefault(module =>
                module.HasAmmo &&
                module.MaterialType == materialType &&
                module.ProbeType == probeType);
        }

        public AutonomousMiningModuleSnapshot SelectDrill(MaterialType materialType)
        {
            return Drills.FirstOrDefault(module =>
                module.HasAmmo && module.MaterialType == materialType);
        }

        private static IReadOnlyList<AutonomousMiningModuleSnapshot> Order(
            IEnumerable<AutonomousMiningModuleSnapshot> modules)
        {
            return (modules ?? Enumerable.Empty<AutonomousMiningModuleSnapshot>())
                .OrderBy(module => module.Component)
                .ThenBy(module => module.Slot)
                .ToArray();
        }
    }

    public interface IAutonomousMiningEquipmentService
    {
        AutonomousMiningEquipmentSnapshot Observe(GameActionContext context);
    }

    /// <summary>
    /// Describes fitted mining equipment and its currently loaded ammunition.
    /// These are the same module/ammo facts exposed by the robot fitting UI.
    /// </summary>
    public sealed class AutonomousMiningEquipmentService : IAutonomousMiningEquipmentService
    {
        public AutonomousMiningEquipmentSnapshot Observe(GameActionContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            Robot robot = context.Actor.GetPlayerRobotFromZone() ?? context.Actor.GetActiveRobot();
            robot = robot.ThrowIfNull(ErrorCodes.ARobotMustBeSelected);
            var scanners = robot.ActiveModules
                .OfType<GeoScannerModule>()
                .Select(CreateScannerSnapshot)
                .ToArray();
            var drills = robot.ActiveModules
                .OfType<DrillerModule>()
                .Select(CreateDrillSnapshot)
                .ToArray();
            return new AutonomousMiningEquipmentSnapshot(scanners, drills);
        }

        private static AutonomousMiningModuleSnapshot CreateScannerSnapshot(GeoScannerModule module)
        {
            var ammo = module.GetAmmo() as GeoScannerAmmo;
            return CreateSnapshot(
                module,
                ammo,
                ammo?.MaterialType ?? MaterialType.Undefined,
                GetProbeType(ammo));
        }

        private static AutonomousMiningModuleSnapshot CreateDrillSnapshot(DrillerModule module)
        {
            var ammo = module.GetAmmo() as MiningAmmo;
            return CreateSnapshot(
                module,
                ammo,
                ammo?.MaterialType ?? MaterialType.Undefined,
                MaterialProbeType.Undefined);
        }

        private static AutonomousMiningModuleSnapshot CreateSnapshot(
            ActiveModule module,
            Ammo ammo,
            MaterialType materialType,
            MaterialProbeType probeType)
        {
            return new AutonomousMiningModuleSnapshot(
                module.Eid,
                module.Definition,
                module.ParentComponent.Type,
                module.Slot,
                ammo?.Definition ?? 0,
                ammo?.Quantity ?? 0,
                materialType,
                probeType);
        }

        private static MaterialProbeType GetProbeType(GeoScannerAmmo ammo)
        {
            if (ammo is TileScannerAmmo)
                return MaterialProbeType.Tile;
            if (ammo is DirectionalScannerAmmo)
                return MaterialProbeType.Directional;
            if (ammo is OneTileScannerAmmo)
                return MaterialProbeType.OneTile;
            return MaterialProbeType.Undefined;
        }
    }
}
