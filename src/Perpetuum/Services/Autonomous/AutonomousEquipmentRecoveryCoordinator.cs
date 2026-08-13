using System;
using Perpetuum.Services.Actions;

namespace Perpetuum.Services.Autonomous
{
    public interface IAutonomousEquipmentRecoveryCoordinator
    {
        bool PrepareDocked(
            GameActionContext context,
            AutonomousEquipmentOptions options,
            AutonomousRobotRecoveryTracker recovery,
            string behaviorName,
            long activeRobotEid);
    }

    /// <summary>
    /// Bridges ordinary equipment work to lifecycle recovery. Persisted robot
    /// identity changes only after the equipment controller freshly observes
    /// the selected robot as healthy and matching the configured fitting.
    /// </summary>
    public sealed class AutonomousEquipmentRecoveryCoordinator : IAutonomousEquipmentRecoveryCoordinator
    {
        private readonly IAutonomousEquipmentController _equipment;
        private readonly IAutonomousActorAudit _audit;

        public AutonomousEquipmentRecoveryCoordinator(
            IAutonomousEquipmentController equipment,
            IAutonomousActorAudit audit)
        {
            _equipment = equipment ?? throw new ArgumentNullException(nameof(equipment));
            _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        }

        public bool PrepareDocked(
            GameActionContext context,
            AutonomousEquipmentOptions options,
            AutonomousRobotRecoveryTracker recovery,
            string behaviorName,
            long activeRobotEid)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            if (recovery == null)
                throw new ArgumentNullException(nameof(recovery));
            if (string.IsNullOrWhiteSpace(behaviorName))
                throw new ArgumentException("A behavior name is required.", nameof(behaviorName));

            if (!options.Enabled)
                return !recovery.RecoveryRequired;

            AutonomousEquipmentUpdateResult result = _equipment.Update(context, options);
            if (result != AutonomousEquipmentUpdateResult.Ready)
                return false;
            if (!recovery.RecoveryRequired)
                return true;
            if (!recovery.AcknowledgeReadyRobot(activeRobotEid))
                return false;

            _audit.Write(
                context.Actor.Id,
                $"{behaviorName}_equipment_recovery_completed",
                AutonomousActorStatus.Active,
                $"robot_{activeRobotEid}");
            return true;
        }
    }
}
