using System;
using System.Collections.Generic;
using System.Linq;
using Perpetuum.Services.MissionEngine;

namespace Perpetuum.Services.Onboarding
{
    /// <summary>
    /// Guards the authored target-acquisition mission before onboarding presentation relies on it.
    /// The SQL overlay performs the stricter definition and coordinate checks at deployment time.
    /// </summary>
    public sealed class CombatCertificationMissionContract
    {
        public const string MissionName = "mission_syndicate_field_certification_combat";

        private static readonly MissionTargetType[] ExpectedTargets =
        {
            MissionTargetType.reach_position,
            MissionTargetType.lock_unit,
            MissionTargetType.kill_definition
        };

        public bool TryValidate(
            string missionName,
            int zoneId,
            double rewardFee,
            IReadOnlyList<MissionTargetType> targets,
            out string failureReason)
        {
            if (!string.Equals(missionName, MissionName, StringComparison.Ordinal))
            {
                failureReason = "mission_name_mismatch";
                return false;
            }

            if (zoneId != FieldCertificationMissionContract.TrainingZoneId)
            {
                failureReason = "training_zone_mismatch";
                return false;
            }

            if (rewardFee <= 0)
            {
                failureReason = "missing_verified_reward";
                return false;
            }

            if (targets == null || !targets.SequenceEqual(ExpectedTargets))
            {
                failureReason = "objective_sequence_mismatch";
                return false;
            }

            failureReason = null;
            return true;
        }
    }
}
