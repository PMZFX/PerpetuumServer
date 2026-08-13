using System;
using System.Collections.Generic;
using System.Linq;
using Perpetuum.Containers;
using Perpetuum.Items;
using Perpetuum.Items.Ammos;
using Perpetuum.Modules;
using Perpetuum.Robots;
using Perpetuum.Services.Actions;

namespace Perpetuum.Services.Autonomous
{
    public sealed class AutonomousEquipmentItemSnapshot
    {
        public AutonomousEquipmentItemSnapshot(
            long itemEid,
            int definition,
            int quantity,
            double healthRatio)
        {
            ItemEid = itemEid;
            Definition = definition;
            Quantity = quantity;
            HealthRatio = healthRatio;
        }

        public long ItemEid { get; }
        public int Definition { get; }
        public int Quantity { get; }
        public double HealthRatio { get; }
    }

    public sealed class AutonomousFittedModuleSnapshot
    {
        public AutonomousFittedModuleSnapshot(
            long moduleEid,
            int definition,
            RobotComponentType component,
            int slot,
            double healthRatio,
            int ammoDefinition,
            int ammoQuantity)
        {
            ModuleEid = moduleEid;
            Definition = definition;
            Component = component;
            Slot = slot;
            HealthRatio = healthRatio;
            AmmoDefinition = ammoDefinition;
            AmmoQuantity = ammoQuantity;
        }

        public long ModuleEid { get; }
        public int Definition { get; }
        public RobotComponentType Component { get; }
        public int Slot { get; }
        public double HealthRatio { get; }
        public int AmmoDefinition { get; }
        public int AmmoQuantity { get; }
    }

    public sealed class AutonomousRobotEquipmentSnapshot
    {
        public AutonomousRobotEquipmentSnapshot(
            long robotEid,
            int definition,
            long containerEid,
            bool isActive,
            bool isRepackaged,
            double healthRatio,
            double cargoLoad,
            double cargoCapacity,
            IEnumerable<AutonomousFittedModuleSnapshot> modules)
        {
            RobotEid = robotEid;
            Definition = definition;
            ContainerEid = containerEid;
            IsActive = isActive;
            IsRepackaged = isRepackaged;
            HealthRatio = healthRatio;
            CargoLoad = cargoLoad;
            CargoCapacity = cargoCapacity;
            Modules = (modules ?? Enumerable.Empty<AutonomousFittedModuleSnapshot>())
                .OrderBy(module => module.Component)
                .ThenBy(module => module.Slot)
                .ToArray();
        }

        public long RobotEid { get; }
        public int Definition { get; }
        public long ContainerEid { get; }
        public bool IsActive { get; }
        public bool IsRepackaged { get; }
        public double HealthRatio { get; }
        public double CargoLoad { get; }
        public double CargoCapacity { get; }
        public IReadOnlyList<AutonomousFittedModuleSnapshot> Modules { get; }
    }

    public sealed class AutonomousEquipmentSnapshot
    {
        public AutonomousEquipmentSnapshot(
            bool isDocked,
            long publicContainerEid,
            long activeRobotEid,
            IEnumerable<AutonomousRobotEquipmentSnapshot> robots,
            IEnumerable<AutonomousEquipmentItemSnapshot> looseItems)
        {
            IsDocked = isDocked;
            PublicContainerEid = publicContainerEid;
            ActiveRobotEid = activeRobotEid;
            Robots = (robots ?? Enumerable.Empty<AutonomousRobotEquipmentSnapshot>())
                .OrderByDescending(robot => robot.IsActive)
                .ThenBy(robot => robot.RobotEid)
                .ToArray();
            LooseItems = (looseItems ?? Enumerable.Empty<AutonomousEquipmentItemSnapshot>())
                .OrderBy(item => item.Definition)
                .ThenBy(item => item.ItemEid)
                .ToArray();
        }

        public bool IsDocked { get; }
        public long PublicContainerEid { get; }
        public long ActiveRobotEid { get; }
        public IReadOnlyList<AutonomousRobotEquipmentSnapshot> Robots { get; }
        public IReadOnlyList<AutonomousEquipmentItemSnapshot> LooseItems { get; }
    }

    public interface IAutonomousEquipmentObservationService
    {
        AutonomousEquipmentSnapshot Observe(GameActionContext context);
    }

    /// <summary>
    /// Projects only the acting character's normal docked public-container,
    /// robot, fitting, ammunition, damage, and cargo facts. The public
    /// container is loaded with the same owner filter used by client handlers.
    /// </summary>
    public sealed class AutonomousEquipmentObservationService : IAutonomousEquipmentObservationService
    {
        public AutonomousEquipmentSnapshot Observe(GameActionContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            if (!context.Actor.IsDocked)
                return new AutonomousEquipmentSnapshot(
                    false,
                    0,
                    context.Actor.ActiveRobotEid,
                    Array.Empty<AutonomousRobotEquipmentSnapshot>(),
                    Array.Empty<AutonomousEquipmentItemSnapshot>());

            PublicContainer container = context.Actor.GetPublicContainerWithItems();
            Robot[] robots = container.GetItems()
                .OfType<Robot>()
                .Where(robot => robot.Owner == context.Actor.Eid)
                .ToArray();
            foreach (Robot robot in robots)
                robot.Initialize(context.Actor);

            return new AutonomousEquipmentSnapshot(
                true,
                container.Eid,
                context.Actor.ActiveRobotEid,
                robots.Select(robot => CreateRobotSnapshot(robot, context.Actor.ActiveRobotEid)),
                container.GetItems()
                    .Where(item => !(item is Robot))
                    .Select(item => new AutonomousEquipmentItemSnapshot(
                        item.Eid,
                        item.Definition,
                        item.Quantity,
                        item.HealthRatio)));
        }

        private static AutonomousRobotEquipmentSnapshot CreateRobotSnapshot(
            Robot robot,
            long activeRobotEid)
        {
            RobotInventory cargo = robot.GetContainer();
            return new AutonomousRobotEquipmentSnapshot(
                robot.Eid,
                robot.Definition,
                robot.Parent,
                robot.Eid == activeRobotEid,
                robot.IsRepackaged,
                robot.HealthRatio,
                cargo?.Load ?? 0,
                cargo?.Capacity ?? 0,
                robot.Modules.Select(CreateModuleSnapshot));
        }

        private static AutonomousFittedModuleSnapshot CreateModuleSnapshot(Module module)
        {
            Ammo ammo = (module as ActiveModule)?.GetAmmo();
            return new AutonomousFittedModuleSnapshot(
                module.Eid,
                module.Definition,
                module.ParentComponent.Type,
                module.Slot,
                module.HealthRatio,
                ammo?.Definition ?? 0,
                ammo?.Quantity ?? 0);
        }
    }

    public sealed class AutonomousEquipmentSlotRequirement
    {
        public AutonomousEquipmentSlotRequirement(
            int moduleDefinition,
            RobotComponentType component,
            int slot,
            int ammoDefinition = 0)
        {
            if (moduleDefinition <= 0)
                throw new ArgumentOutOfRangeException(nameof(moduleDefinition));
            if (slot < 0)
                throw new ArgumentOutOfRangeException(nameof(slot));
            if (ammoDefinition < 0)
                throw new ArgumentOutOfRangeException(nameof(ammoDefinition));

            ModuleDefinition = moduleDefinition;
            Component = component;
            Slot = slot;
            AmmoDefinition = ammoDefinition;
        }

        public int ModuleDefinition { get; }
        public RobotComponentType Component { get; }
        public int Slot { get; }
        public int AmmoDefinition { get; }
    }

    public sealed class AutonomousEquipmentTemplate
    {
        public AutonomousEquipmentTemplate(
            int robotDefinition,
            IEnumerable<AutonomousEquipmentSlotRequirement> slots,
            double repairBelowRatio = 0.95)
        {
            if (robotDefinition <= 0)
                throw new ArgumentOutOfRangeException(nameof(robotDefinition));
            if (double.IsNaN(repairBelowRatio) || double.IsInfinity(repairBelowRatio) ||
                repairBelowRatio < 0 || repairBelowRatio > 1)
                throw new ArgumentOutOfRangeException(nameof(repairBelowRatio));

            RobotDefinition = robotDefinition;
            Slots = (slots ?? Enumerable.Empty<AutonomousEquipmentSlotRequirement>())
                .OrderBy(slot => slot.Component)
                .ThenBy(slot => slot.Slot)
                .ToArray();
            if (Slots.GroupBy(slot => new {slot.Component, slot.Slot}).Any(group => group.Count() > 1))
                throw new ArgumentException("An equipment template cannot configure the same slot twice.", nameof(slots));
            RepairBelowRatio = repairBelowRatio;
        }

        public int RobotDefinition { get; }
        public IReadOnlyList<AutonomousEquipmentSlotRequirement> Slots { get; }
        public double RepairBelowRatio { get; }
    }

    public enum AutonomousEquipmentDirectiveType
    {
        Ready,
        WaitForDock,
        MissingRobot,
        SelectRobot,
        RepairRobot,
        RemoveModule,
        FitModule,
        MissingModule,
        LoadAmmo,
        MissingAmmo
    }

    public sealed class AutonomousEquipmentDirective
    {
        public AutonomousEquipmentDirective(
            AutonomousEquipmentDirectiveType type,
            long robotEid = 0,
            long itemEid = 0,
            int definition = 0,
            RobotComponentType component = default,
            int slot = 0,
            long moduleEid = 0)
        {
            Type = type;
            RobotEid = robotEid;
            ItemEid = itemEid;
            Definition = definition;
            Component = component;
            Slot = slot;
            ModuleEid = moduleEid;
        }

        public AutonomousEquipmentDirectiveType Type { get; }
        public long RobotEid { get; }
        public long ItemEid { get; }
        public int Definition { get; }
        public RobotComponentType Component { get; }
        public int Slot { get; }
        public long ModuleEid { get; }
    }

    public static class AutonomousEquipmentPolicy
    {
        public static AutonomousEquipmentDirective Evaluate(
            AutonomousEquipmentSnapshot observation,
            AutonomousEquipmentTemplate template)
        {
            if (observation == null)
                throw new ArgumentNullException(nameof(observation));
            if (template == null)
                throw new ArgumentNullException(nameof(template));
            if (!observation.IsDocked)
                return new AutonomousEquipmentDirective(AutonomousEquipmentDirectiveType.WaitForDock);

            AutonomousRobotEquipmentSnapshot robot = observation.Robots
                .FirstOrDefault(candidate =>
                    candidate.Definition == template.RobotDefinition &&
                    !candidate.IsRepackaged &&
                    candidate.IsActive)
                ?? observation.Robots.FirstOrDefault(candidate =>
                    candidate.Definition == template.RobotDefinition &&
                    !candidate.IsRepackaged);
            if (robot == null)
                return new AutonomousEquipmentDirective(
                    AutonomousEquipmentDirectiveType.MissingRobot,
                    definition: template.RobotDefinition);
            if (!robot.IsActive)
                return new AutonomousEquipmentDirective(
                    AutonomousEquipmentDirectiveType.SelectRobot,
                    robot.RobotEid,
                    definition: robot.Definition);
            if (robot.HealthRatio < template.RepairBelowRatio)
                return new AutonomousEquipmentDirective(
                    AutonomousEquipmentDirectiveType.RepairRobot,
                    robot.RobotEid,
                    definition: robot.Definition);

            foreach (AutonomousEquipmentSlotRequirement requirement in template.Slots)
            {
                AutonomousFittedModuleSnapshot fitted = robot.Modules.FirstOrDefault(module =>
                    module.Component == requirement.Component && module.Slot == requirement.Slot);
                if (fitted != null && fitted.Definition != requirement.ModuleDefinition)
                {
                    return new AutonomousEquipmentDirective(
                        AutonomousEquipmentDirectiveType.RemoveModule,
                        robot.RobotEid,
                        fitted.ModuleEid,
                        fitted.Definition,
                        fitted.Component,
                        fitted.Slot);
                }
                if (fitted != null)
                {
                    if (requirement.AmmoDefinition <= 0 ||
                        (fitted.AmmoDefinition == requirement.AmmoDefinition &&
                         fitted.AmmoQuantity > 0))
                        continue;

                    AutonomousEquipmentItemSnapshot looseAmmo = observation.LooseItems
                        .FirstOrDefault(item =>
                            item.Definition == requirement.AmmoDefinition &&
                            item.Quantity > 0);
                    if (looseAmmo == null)
                    {
                        return new AutonomousEquipmentDirective(
                            AutonomousEquipmentDirectiveType.MissingAmmo,
                            robot.RobotEid,
                            fitted.ModuleEid,
                            requirement.AmmoDefinition,
                            requirement.Component,
                            requirement.Slot);
                    }

                    return new AutonomousEquipmentDirective(
                        AutonomousEquipmentDirectiveType.LoadAmmo,
                        robot.RobotEid,
                        looseAmmo.ItemEid,
                        looseAmmo.Definition,
                        requirement.Component,
                        requirement.Slot,
                        fitted.ModuleEid);
                }

                AutonomousEquipmentItemSnapshot loose = observation.LooseItems
                    .FirstOrDefault(item =>
                        item.Definition == requirement.ModuleDefinition &&
                        item.Quantity > 0);
                if (loose == null)
                {
                    return new AutonomousEquipmentDirective(
                        AutonomousEquipmentDirectiveType.MissingModule,
                        robot.RobotEid,
                        definition: requirement.ModuleDefinition,
                        component: requirement.Component,
                        slot: requirement.Slot);
                }

                return new AutonomousEquipmentDirective(
                    AutonomousEquipmentDirectiveType.FitModule,
                    robot.RobotEid,
                    loose.ItemEid,
                    loose.Definition,
                    requirement.Component,
                    requirement.Slot);
            }

            return new AutonomousEquipmentDirective(
                AutonomousEquipmentDirectiveType.Ready,
                robot.RobotEid,
                definition: robot.Definition);
        }
    }
}
