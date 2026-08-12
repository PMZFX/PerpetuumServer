using System;
using System.Collections.Generic;
using System.Linq;
using Perpetuum.ExportedTypes;
using Perpetuum.Items;
using Perpetuum.Robots;
using Perpetuum.Services.Actions;

namespace Perpetuum.Services.Autonomous
{
    public sealed class AutonomousCargoItemSnapshot
    {
        public AutonomousCargoItemSnapshot(
            long eid,
            int definition,
            int quantity,
            double volume,
            bool rawMaterial)
        {
            Eid = eid;
            Definition = definition;
            Quantity = quantity;
            Volume = volume;
            RawMaterial = rawMaterial;
        }

        public long Eid { get; }
        public int Definition { get; }
        public int Quantity { get; }
        public double Volume { get; }
        public bool RawMaterial { get; }
    }

    public sealed class AutonomousCargoSnapshot
    {
        public AutonomousCargoSnapshot(
            long containerEid,
            double capacity,
            double load,
            IEnumerable<AutonomousCargoItemSnapshot> items)
        {
            ContainerEid = containerEid;
            Capacity = capacity;
            Load = load;
            Items = (items ?? Enumerable.Empty<AutonomousCargoItemSnapshot>())
                .OrderBy(item => item.Eid)
                .ToArray();
        }

        public long ContainerEid { get; }
        public double Capacity { get; }
        public double Load { get; }
        public double FreeCapacity => Math.Max(0, Capacity - Load);
        public double FillRatio => Capacity <= 0 ? 1.0 : (Load / Capacity).Clamp();
        public IReadOnlyList<AutonomousCargoItemSnapshot> Items { get; }
        public IEnumerable<AutonomousCargoItemSnapshot> RawMaterials =>
            Items.Where(item => item.RawMaterial);
    }

    public interface IAutonomousCargoService
    {
        AutonomousCargoSnapshot Observe(GameActionContext context);
    }

    /// <summary>
    /// Projects only the autonomous character's active robot cargo, matching
    /// the inventory and capacity information available in the client UI.
    /// </summary>
    public sealed class AutonomousCargoService : IAutonomousCargoService
    {
        public AutonomousCargoSnapshot Observe(GameActionContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            Robot robot = context.Actor.GetPlayerRobotFromZone() ?? context.Actor.GetActiveRobot();
            robot = robot.ThrowIfNull(ErrorCodes.ARobotMustBeSelected);
            var inventory = robot.GetContainer().ThrowIfNull(ErrorCodes.ItemNotFound);
            AutonomousCargoItemSnapshot[] items = inventory.GetItems()
                .Select(CreateItemSnapshot)
                .ToArray();
            return new AutonomousCargoSnapshot(
                inventory.Eid,
                inventory.Capacity,
                inventory.Load,
                items);
        }

        private static AutonomousCargoItemSnapshot CreateItemSnapshot(Item item)
        {
            return new AutonomousCargoItemSnapshot(
                item.Eid,
                item.Definition,
                item.Quantity,
                item.Volume,
                item.IsCategory(CategoryFlags.cf_raw_material));
        }
    }
}
