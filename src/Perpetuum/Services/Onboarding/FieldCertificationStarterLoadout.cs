using System;
using System.Linq;
using System.Transactions;
using Perpetuum.Accounting.Characters;
using Perpetuum.Containers;
using Perpetuum.Data;
using Perpetuum.Log;
using Perpetuum.Robots;
using Perpetuum.Services.MissionEngine;
using Perpetuum.Services.MissionEngine.MissionDataCacheObjects;
using Perpetuum.Services.MissionEngine.Missions;
using Perpetuum.Services.MissionEngine.MissionStructures;

namespace Perpetuum.Services.Onboarding
{
    /// <summary>
    /// Gives the specific starter robot enough per-instance cargo capacity for the validated first
    /// assignment. The stock transport objective predates immediate enrollment and requires a
    /// 50-volume mission container, while a fresh Arkhe has only 3 capacity. The override is stored
    /// on the robot inventory entity and does not alter the global Arkhe definition or other robots.
    /// </summary>
    public sealed class FieldCertificationStarterLoadout : IFieldCertificationStarterLoadout
    {
        private readonly MissionDataCache _missionDataCache;
        private readonly FieldCertificationMissionContract _contract;

        public FieldCertificationStarterLoadout(
            MissionDataCache missionDataCache,
            FieldCertificationMissionContract contract)
        {
            _missionDataCache = missionDataCache ?? throw new ArgumentNullException(nameof(missionDataCache));
            _contract = contract ?? throw new ArgumentNullException(nameof(contract));
        }

        public bool EnsureMissionCargoCapacity(Character character, Robot robot)
        {
            if (character == null || character == Character.None || !character.IsInTraining() || robot == null)
                return false;

            if (!TryGetValidatedMission(out Mission mission))
                return false;

            double requiredMissionVolume = mission.Targets
                .Where(target => target.Type == MissionTargetType.use_itemsupply && target.ValidItemInfo)
                .Sum(target => target.PrimaryEntityDefault.CalculateVolume(false, target.Quantity));
            if (requiredMissionVolume <= 0)
                return false;

            RobotInventory inventory = robot.GetContainer();
            if (inventory == null)
                return false;

            double minimumCapacity = CalculateRequiredCapacity(
                inventory.Load,
                requiredMissionVolume,
                inventory.Capacity);
            if (minimumCapacity <= inventory.Capacity)
                return true;

            using (var scope = Db.CreateTransaction())
            {
                inventory.EnlistTransaction();
                inventory.DynamicProperties.Update(k.capacityOverride, minimumCapacity);
                inventory.Save();

                Transaction.Current.OnCommited(() =>
                {
                    inventory.SendUpdateToOwner();
                    Logger.Info(
                        $"onboarding character_id={character.Id} event=starter_cargo_reserved " +
                        $"robot_eid={robot.Eid} capacity={minimumCapacity} " +
                        $"load={inventory.Load} mission_volume={requiredMissionVolume}");
                });
                scope.Complete();
            }

            return true;
        }

        public static double CalculateRequiredCapacity(
            double currentLoad,
            double requiredMissionVolume,
            double configuredCapacity)
        {
            if (currentLoad < 0 || requiredMissionVolume < 0 || configuredCapacity < 0)
                throw new ArgumentOutOfRangeException();

            return Math.Max(configuredCapacity, Math.Ceiling(currentLoad + requiredMissionVolume));
        }

        private bool TryGetValidatedMission(out Mission mission)
        {
            if (!_missionDataCache.GetMissionByName(
                    FieldCertificationMissionContract.MissionName,
                    out mission))
            {
                return false;
            }

            if (!_missionDataCache.GetLocationById(mission.LocationId, out MissionLocation location))
                return false;

            return _contract.TryValidate(
                mission.Name,
                location.zoneId,
                mission.rewardFee,
                mission.Targets
                    .OrderBy(target => target.targetOrder)
                    .ThenBy(target => target.displayOrder)
                    .Select(target => target.Type)
                    .ToArray(),
                out _);
        }
    }
}
