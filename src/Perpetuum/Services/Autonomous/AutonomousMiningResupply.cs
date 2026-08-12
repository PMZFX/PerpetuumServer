using System;
using System.Collections.Generic;
using System.Linq;
using Perpetuum.Items.Ammos;
using Perpetuum.Modules;
using Perpetuum.Robots;
using Perpetuum.Services.Actions;
using Perpetuum.Zones.Scanning.Ammos;
using Perpetuum.Zones.Scanning.Modules;
using Perpetuum.Zones.Terrains.Materials;

namespace Perpetuum.Services.Autonomous
{
    public enum AutonomousMiningResupplyResult
    {
        Disabled,
        NoReloadAvailable,
        Reloaded
    }

    public sealed class AutonomousMiningResupply
    {
        private AutonomousMiningResupply(
            AutonomousMiningResupplyResult result,
            long moduleEid = 0,
            int ammoDefinition = 0)
        {
            Result = result;
            ModuleEid = moduleEid;
            AmmoDefinition = ammoDefinition;
        }

        public AutonomousMiningResupplyResult Result { get; }
        public long ModuleEid { get; }
        public int AmmoDefinition { get; }

        public static AutonomousMiningResupply Disabled() =>
            new AutonomousMiningResupply(AutonomousMiningResupplyResult.Disabled);

        public static AutonomousMiningResupply NoReloadAvailable() =>
            new AutonomousMiningResupply(AutonomousMiningResupplyResult.NoReloadAvailable);

        public static AutonomousMiningResupply Reloaded(long moduleEid, int ammoDefinition) =>
            new AutonomousMiningResupply(AutonomousMiningResupplyResult.Reloaded, moduleEid, ammoDefinition);
    }

    public interface IAutonomousMiningResupplyService
    {
        AutonomousMiningResupply ReloadNext(
            GameActionContext context,
            MaterialType material,
            AutonomousMiningResupplyOptions options);
    }

    /// <summary>
    /// Reloads fitted mining equipment only from ammunition already carried in
    /// the active robot's cargo, through the same docked equip-ammo action used
    /// by the client.
    /// </summary>
    public sealed class AutonomousMiningResupplyService : IAutonomousMiningResupplyService
    {
        private readonly IEquipAmmoActionService _equipAmmo;

        public AutonomousMiningResupplyService(IEquipAmmoActionService equipAmmo)
        {
            _equipAmmo = equipAmmo ?? throw new ArgumentNullException(nameof(equipAmmo));
        }

        public AutonomousMiningResupply ReloadNext(
            GameActionContext context,
            MaterialType material,
            AutonomousMiningResupplyOptions options)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            if (!options.Enabled)
                return AutonomousMiningResupply.Disabled();
            context.Actor.IsDocked.ThrowIfFalse(ErrorCodes.CharacterHasToBeDocked);

            Robot robot = context.Actor.GetActiveRobot().ThrowIfNull(ErrorCodes.ARobotMustBeSelected);
            var inventory = robot.GetContainer().ThrowIfNull(ErrorCodes.ItemNotFound);
            Ammo[] cargoAmmo = inventory.GetItems().OfType<Ammo>().OrderBy(ammo => ammo.Eid).ToArray();

            ReloadCandidate candidate = FindScannerCandidate(robot, cargoAmmo, material, options.ReloadBelowRatio) ??
                                        FindDrillCandidate(robot, cargoAmmo, material, options.ReloadBelowRatio);
            if (candidate == null)
                return AutonomousMiningResupply.NoReloadAvailable();

            _equipAmmo.Execute(context, new EquipAmmoAction(
                inventory.Eid,
                robot.Eid,
                candidate.Module.Eid,
                candidate.Ammo.Eid));
            return AutonomousMiningResupply.Reloaded(candidate.Module.Eid, candidate.Ammo.Definition);
        }

        public static bool NeedsReload(
            int desiredAmmoDefinition,
            int loadedAmmoDefinition,
            int loadedQuantity,
            int capacity,
            double reloadBelowRatio)
        {
            if (capacity <= 0 || desiredAmmoDefinition <= 0)
                return false;
            if (loadedAmmoDefinition != desiredAmmoDefinition)
                return true;
            return loadedQuantity < capacity * reloadBelowRatio;
        }

        private static ReloadCandidate FindScannerCandidate(
            Robot robot,
            IReadOnlyCollection<Ammo> cargoAmmo,
            MaterialType material,
            double reloadBelowRatio)
        {
            foreach (GeoScannerModule module in Order(robot.ActiveModules.OfType<GeoScannerModule>()))
            {
                TileScannerAmmo spare = cargoAmmo
                    .OfType<TileScannerAmmo>()
                    .FirstOrDefault(ammo => ammo.MaterialType == material && module.CheckLoadableAmmo(ammo.Definition));
                if (spare != null && NeedsReload(module, spare.Definition, reloadBelowRatio))
                    return new ReloadCandidate(module, spare);
            }
            return null;
        }

        private static ReloadCandidate FindDrillCandidate(
            Robot robot,
            IReadOnlyCollection<Ammo> cargoAmmo,
            MaterialType material,
            double reloadBelowRatio)
        {
            foreach (DrillerModule module in Order(robot.ActiveModules.OfType<DrillerModule>()))
            {
                MiningAmmo spare = cargoAmmo
                    .OfType<MiningAmmo>()
                    .FirstOrDefault(ammo => ammo.MaterialType == material && module.CheckLoadableAmmo(ammo.Definition));
                if (spare != null && NeedsReload(module, spare.Definition, reloadBelowRatio))
                    return new ReloadCandidate(module, spare);
            }
            return null;
        }

        private static bool NeedsReload(ActiveModule module, int desiredAmmoDefinition, double reloadBelowRatio)
        {
            Ammo loaded = module.GetAmmo();
            return NeedsReload(
                desiredAmmoDefinition,
                loaded?.Definition ?? 0,
                loaded?.Quantity ?? 0,
                module.AmmoCapacity,
                reloadBelowRatio);
        }

        private static IEnumerable<T> Order<T>(IEnumerable<T> modules) where T : ActiveModule
        {
            return modules.OrderBy(module => module.ParentComponent.Type).ThenBy(module => module.Slot);
        }

        private sealed class ReloadCandidate
        {
            public ReloadCandidate(ActiveModule module, Ammo ammo)
            {
                Module = module;
                Ammo = ammo;
            }

            public ActiveModule Module { get; }
            public Ammo Ammo { get; }
        }
    }
}
