using System;
using System.Collections.Generic;
using System.Linq;
using Perpetuum.Services.MissionEngine;

namespace Perpetuum.Services.Onboarding
{
    /// <summary>
    /// Guards the stock training-hub mission that backs the first server-owned onboarding slice.
    /// The content is deliberately reused, but enrollment stops safely if a database version has
    /// changed its location, reward, or objective sequence.
    /// </summary>
    public sealed class FieldCertificationMissionContract
    {
        public const string MissionName = "mission_tutorialchecklist_transport";
        public const int TrainingZoneId = 45;

        private static readonly MissionTargetType[] ExpectedTargets =
        {
            MissionTargetType.reach_position,
            MissionTargetType.use_itemsupply,
            MissionTargetType.submit_item
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

            if (zoneId != TrainingZoneId)
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
