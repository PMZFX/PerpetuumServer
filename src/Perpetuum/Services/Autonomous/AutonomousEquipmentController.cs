using System;
using System.Linq;
using Perpetuum.EntityFramework;
using Perpetuum.Services.Actions;
using Perpetuum.Services.ProductionEngine;

namespace Perpetuum.Services.Autonomous
{
    public enum AutonomousEquipmentUpdateResult
    {
        Waiting,
        Blocked,
        Acted,
        Ready
    }

    public interface IAutonomousEquipmentController
    {
        AutonomousEquipmentUpdateResult Update(
            GameActionContext context,
            AutonomousEquipmentOptions options);
    }

    /// <summary>
    /// Re-observes authoritative character state on every update and performs
    /// at most one normal gameplay action. Persisted phases describe intent;
    /// they are never treated as proof that selection, repair, or fitting
    /// succeeded.
    /// </summary>
    public sealed class AutonomousEquipmentController : IAutonomousEquipmentController
    {
        private readonly IEntityDefaultReader _entityDefaults;
        private readonly IAutonomousEquipmentObservationService _observations;
        private readonly IAutonomousEquipmentGoalStore _goals;
        private readonly ISelectActiveRobotActionService _selectRobot;
        private readonly IRobotFittingActionService _fitting;
        private readonly IProductionRepairActionService _repair;
        private readonly IAutonomousActorAudit _audit;

        public AutonomousEquipmentController(
            IEntityDefaultReader entityDefaults,
            IAutonomousEquipmentObservationService observations,
            IAutonomousEquipmentGoalStore goals,
            ISelectActiveRobotActionService selectRobot,
            IRobotFittingActionService fitting,
            IProductionRepairActionService repair,
            IAutonomousActorAudit audit)
        {
            _entityDefaults = entityDefaults ?? throw new ArgumentNullException(nameof(entityDefaults));
            _observations = observations ?? throw new ArgumentNullException(nameof(observations));
            _goals = goals ?? throw new ArgumentNullException(nameof(goals));
            _selectRobot = selectRobot ?? throw new ArgumentNullException(nameof(selectRobot));
            _fitting = fitting ?? throw new ArgumentNullException(nameof(fitting));
            _repair = repair ?? throw new ArgumentNullException(nameof(repair));
            _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        }

        public AutonomousEquipmentUpdateResult Update(
            GameActionContext context,
            AutonomousEquipmentOptions options)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            options.Validate(context.Actor.Id);

            AutonomousEquipmentTemplate template = BuildTemplate(options);
            AutonomousEquipmentSnapshot observation = _observations.Observe(context);
            AutonomousEquipmentGoalState state = LoadOrCreateState(context, template);
            AutonomousEquipmentDirective directive = AutonomousEquipmentPolicy.Evaluate(
                observation,
                template);

            try
            {
                switch (directive.Type)
                {
                    case AutonomousEquipmentDirectiveType.WaitForDock:
                        WriteProgress(ref state, "waiting_for_dock", blockedReason: "not_docked");
                        return AutonomousEquipmentUpdateResult.Waiting;

                    case AutonomousEquipmentDirectiveType.MissingRobot:
                        WriteBlocked(ref state, "missing_robot", directive, $"missing_robot_{directive.Definition}");
                        return AutonomousEquipmentUpdateResult.Blocked;

                    case AutonomousEquipmentDirectiveType.MissingModule:
                        WriteBlocked(ref state, "missing_module", directive, $"missing_module_{directive.Definition}");
                        return AutonomousEquipmentUpdateResult.Blocked;

                    case AutonomousEquipmentDirectiveType.SelectRobot:
                        WriteProgress(ref state, "selecting_robot", directive.RobotEid);
                        _selectRobot.Execute(
                            context,
                            new SelectActiveRobotAction(
                                observation.PublicContainerEid,
                                directive.RobotEid));
                        WriteProgress(ref state, "robot_selected", directive.RobotEid);
                        return AutonomousEquipmentUpdateResult.Acted;

                    case AutonomousEquipmentDirectiveType.RepairRobot:
                        WriteProgress(ref state, "quoting_repair", directive.RobotEid);
                        var repairAction = new ProductionRepairAction(
                            options.RepairFacilityEid,
                            new[] {directive.RobotEid},
                            options.UseCorporationWallet);
                        RepairQuote quote = _repair.Quote(context, repairAction);
                        WriteProgress(ref state,
                            "repair_quoted",
                            directive.RobotEid,
                            $"price_{quote.TotalPrice}");
                        _repair.Execute(context, repairAction);
                        WriteProgress(ref state, "repair_requested", directive.RobotEid);
                        return AutonomousEquipmentUpdateResult.Acted;

                    case AutonomousEquipmentDirectiveType.RemoveModule:
                        WriteProgress(ref state, "removing_module", directive.RobotEid);
                        _fitting.Remove(
                            context,
                            new RemoveModuleAction(
                                observation.PublicContainerEid,
                                directive.RobotEid,
                                directive.ItemEid));
                        WriteProgress(ref state, "module_removed", directive.RobotEid);
                        return AutonomousEquipmentUpdateResult.Acted;

                    case AutonomousEquipmentDirectiveType.FitModule:
                        WriteProgress(ref state, "fitting_module", directive.RobotEid);
                        _fitting.Equip(
                            context,
                            new EquipModuleAction(
                                observation.PublicContainerEid,
                                directive.RobotEid,
                                directive.ItemEid,
                                directive.Component,
                                directive.Slot));
                        WriteProgress(ref state, "module_fitted", directive.RobotEid);
                        return AutonomousEquipmentUpdateResult.Acted;

                    default:
                        WriteProgress(ref state, "ready", directive.RobotEid);
                        return AutonomousEquipmentUpdateResult.Ready;
                }
            }
            catch (PerpetuumException exception)
            {
                WriteProgress(ref state,
                    "blocked",
                    directive.RobotEid,
                    exception.error.ToString());
                _audit.Write(
                    context.Actor.Id,
                    "equipment_action_blocked",
                    AutonomousActorStatus.Active,
                    exception.error.ToString());
                return AutonomousEquipmentUpdateResult.Blocked;
            }
        }

        private AutonomousEquipmentTemplate BuildTemplate(AutonomousEquipmentOptions options)
        {
            EntityDefault robot = _entityDefaults.GetByName(options.Robot);
            robot.ThrowIfEqual(EntityDefault.None, ErrorCodes.DefinitionNotSupported);
            var slots = options.Slots.Select(slot =>
            {
                EntityDefault module = _entityDefaults.GetByName(slot.Module);
                module.ThrowIfEqual(EntityDefault.None, ErrorCodes.DefinitionNotSupported);
                return new AutonomousEquipmentSlotRequirement(
                    module.Definition,
                    slot.GetComponentType(),
                    slot.Slot);
            });
            return new AutonomousEquipmentTemplate(
                robot.Definition,
                slots,
                options.RepairBelowRatio);
        }

        private AutonomousEquipmentGoalState LoadOrCreateState(
            GameActionContext context,
            AutonomousEquipmentTemplate template)
        {
            AutonomousEquipmentGoalState state = _goals.Load(context.Actor.Id);
            if (state != null && state.RobotDefinition == template.RobotDefinition)
                return state;

            state = new AutonomousEquipmentGoalState(
                context.Actor.Id,
                template.RobotDefinition,
                "observe");
            _goals.Save(state);
            return state;
        }

        private void WriteBlocked(
            ref AutonomousEquipmentGoalState state,
            string phase,
            AutonomousEquipmentDirective directive,
            string reason)
        {
            bool changed = WriteProgress(ref state, phase, directive.RobotEid, reason);
            if (changed)
            {
                _audit.Write(
                    state.CharacterId,
                    "equipment_supply_required",
                    AutonomousActorStatus.Active,
                    reason);
            }
        }

        private bool WriteProgress(
            ref AutonomousEquipmentGoalState state,
            string phase,
            long robotEid = 0,
            string blockedReason = null)
        {
            state = state.WithProgress(phase, robotEid, blockedReason);
            return WriteState(state);
        }

        private bool WriteState(AutonomousEquipmentGoalState state)
        {
            AutonomousEquipmentGoalState previous = _goals.Load(state.CharacterId);
            if (previous != null &&
                previous.RobotDefinition == state.RobotDefinition &&
                previous.Phase == state.Phase &&
                previous.RobotEid == state.RobotEid &&
                previous.BlockedReason == state.BlockedReason)
                return false;

            _goals.Save(state);
            return true;
        }
    }
}
