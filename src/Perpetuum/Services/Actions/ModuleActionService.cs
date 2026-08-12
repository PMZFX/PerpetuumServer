using System;
using System.Diagnostics;
using System.Transactions;
using Perpetuum.Data;
using Perpetuum.EntityFramework;
using Perpetuum.ExportedTypes;
using Perpetuum.Items;
using Perpetuum.Items.Ammos;
using Perpetuum.Modules;
using Perpetuum.Players;
using Perpetuum.Robots;

namespace Perpetuum.Services.Actions
{
    public sealed class ModuleUseAction
    {
        public ModuleUseAction(
            long lockId,
            RobotComponentType component,
            int slot,
            ModuleStateType state)
        {
            LockId = lockId;
            Component = component;
            Slot = slot;
            State = state;
        }

        public long LockId { get; }
        public RobotComponentType Component { get; }
        public int Slot { get; }
        public ModuleStateType State { get; }
    }

    public sealed class ModuleCategoryUseAction
    {
        public ModuleCategoryUseAction(long lockId, CategoryFlags categoryFlags, ModuleStateType state)
        {
            LockId = lockId;
            CategoryFlags = categoryFlags;
            State = state;
        }

        public long LockId { get; }
        public CategoryFlags CategoryFlags { get; }
        public ModuleStateType State { get; }
    }

    public sealed class ModuleAmmoLoadAction
    {
        public ModuleAmmoLoadAction(int ammoDefinition, RobotComponentType component, int slot)
        {
            AmmoDefinition = ammoDefinition;
            Component = component;
            Slot = slot;
        }

        public int AmmoDefinition { get; }
        public RobotComponentType Component { get; }
        public int Slot { get; }
    }

    public sealed class ModuleAmmoUnloadAction
    {
        public ModuleAmmoUnloadAction(RobotComponentType component, int slot)
        {
            Component = component;
            Slot = slot;
        }

        public RobotComponentType Component { get; }
        public int Slot { get; }
    }

    public interface IModuleActionService
    {
        void Use(GameActionContext context, ModuleUseAction action);
        void UseByCategory(GameActionContext context, ModuleCategoryUseAction action);
        void DeactivateByCategory(GameActionContext context, CategoryFlags categoryFlags);
        void LoadAmmo(GameActionContext context, ModuleAmmoLoadAction action);
        void UnloadAmmo(GameActionContext context, ModuleAmmoUnloadAction action);
    }

    /// <summary>
    /// Selects an equipped module and requests a normal module-state transition.
    /// The module state machine remains authoritative for completed locks, range,
    /// line of sight, core, ammunition, aggression, PvP, cycle timing, and damage.
    /// </summary>
    public sealed class ModuleActionService : IModuleActionService
    {
        private readonly IGameActionAudit _audit;

        public ModuleActionService(IGameActionAudit audit)
        {
            _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        }

        public void Use(GameActionContext context, ModuleUseAction action)
        {
            _audit.Execute(context, "moduleUse", () =>
            {
                if (action == null)
                    throw new ArgumentNullException(nameof(action));

                Player player = GetPlayer(context);
                ActiveModule module = GetActiveModule(player, action.Component, action.Slot);

                if (module.IsAmmoable)
                {
                    Ammo ammo = module.GetAmmo();
                    if (ammo == null || ammo.Definition == 0)
                    {
                        player.SendModuleProcessError(module, ErrorCodes.AmmoNotFound);
                        return;
                    }
                }

                module.Lock = player.GetLock(action.LockId);
                module.State.SwitchTo(action.State);
            });
        }

        public void UseByCategory(GameActionContext context, ModuleCategoryUseAction action)
        {
            _audit.Execute(context, "moduleUseByCategory", () =>
            {
                if (action == null)
                    throw new ArgumentNullException(nameof(action));

                Player player = GetPlayer(context);
                foreach (ActiveModule module in player.ActiveModules)
                {
                    if (!module.IsCategory(action.CategoryFlags))
                        continue;

                    if (module.IsAmmoable)
                    {
                        Ammo ammo = module.GetAmmo();
                        if (ammo == null || ammo.Quantity == 0)
                            continue;
                    }

                    var targetLock = module.ED.AttributeFlags.PrimaryLockedTarget
                        ? player.GetPrimaryLock().ThrowIfNull(ErrorCodes.PrimaryLockTargetNotFound)
                        : player.GetLock(action.LockId).ThrowIfNull(ErrorCodes.LockTargetNotFound);
                    module.Lock = targetLock;

                    try
                    {
                        module.State.SwitchTo(action.State);
                    }
                    catch (PerpetuumException exception)
                    {
                        player.SendModuleProcessError(module, exception.error);
                    }
                }
            });
        }

        public void LoadAmmo(GameActionContext context, ModuleAmmoLoadAction action)
        {
            _audit.Execute(context, "loadAmmo", () =>
            {
                if (action == null)
                    throw new ArgumentNullException(nameof(action));

                Player player = GetPlayer(context);
                ActiveModule module = GetActiveModule(player, action.Component, action.Slot);
                if (!module.IsAmmoable)
                    return;

                Ammo ammo = module.GetAmmo();
                if (action.AmmoDefinition == 0)
                {
                    if (ammo != null)
                        module.State.UnloadAmmo();
                    return;
                }

                if (ammo?.Definition == action.AmmoDefinition && ammo.Quantity == module.AmmoCapacity)
                    return;

                module.CheckLoadableAmmo(action.AmmoDefinition).ThrowIfFalse(ErrorCodes.InvalidAmmoDefinition);
                var temporaryAmmo = (Ammo)Entity.Factory.CreateWithRandomEID(action.AmmoDefinition);
                temporaryAmmo.CheckEnablerExtensionsAndThrowIfFailed(
                    player.Character,
                    ErrorCodes.ExtensionLevelMismatchTerrain);
                module.State.LoadAmmo(action.AmmoDefinition);
            });
        }

        public void DeactivateByCategory(GameActionContext context, CategoryFlags categoryFlags)
        {
            _audit.Execute(context, "moduleDeactivateByCategory", () =>
            {
                Player player = GetPlayer(context);
                foreach (ActiveModule module in player.ActiveModules)
                {
                    if (!module.IsCategory(categoryFlags))
                        continue;

                    module.State.SwitchTo(ModuleStateType.Idle);
                    module.Lock = null;
                }
            });
        }

        public void UnloadAmmo(GameActionContext context, ModuleAmmoUnloadAction action)
        {
            _audit.Execute(context, "unloadAmmo", () =>
            {
                if (action == null)
                    throw new ArgumentNullException(nameof(action));

                Player player = GetPlayer(context);
                var component = player.GetRobotComponent(action.Component);
                var module = component?.GetModule(action.Slot) as ActiveModule;
                if (module == null)
                    return;

                using (var scope = Db.CreateTransaction())
                {
                    var container = player.GetContainer();
                    Debug.Assert(container != null, "container != null");
                    container.EnlistTransaction();
                    module.UnequipAmmoToContainer(container);

                    module.Save();
                    container.Save();

                    Transaction.Current.OnCompleted(completed => container.SendUpdateToOwner());
                    scope.Complete();
                }
            });
        }

        private static Player GetPlayer(GameActionContext context)
        {
            return context.Actor.GetPlayerRobotFromZone().ThrowIfNull(ErrorCodes.PlayerNotFound);
        }

        private static ActiveModule GetActiveModule(Player player, RobotComponentType componentType, int slot)
        {
            var component = player.GetRobotComponent(componentType)
                .ThrowIfNull(ErrorCodes.RobotComponentNotSupplied);
            return component.GetModule(slot).ThrowIfNotType<ActiveModule>(ErrorCodes.ModuleNotFound);
        }
    }
}
