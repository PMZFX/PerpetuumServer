using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Newtonsoft.Json;
using Perpetuum.Robots;
using Perpetuum.Services.MissionEngine;
using Perpetuum.Zones.Terrains.Materials;

namespace Perpetuum.Services.Autonomous
{
    public sealed class AutonomousConfiguration
    {
        [DefaultValue(false), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool Enabled { get; set; }

        [DefaultValue(500), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public int TickIntervalMilliseconds { get; set; } = 500;

        [DefaultValue(3), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public int MaxConsecutiveFailures { get; set; } = 3;

        public List<AutonomousActorDefinition> Actors { get; set; } = new List<AutonomousActorDefinition>();

        public void Validate()
        {
            if (TickIntervalMilliseconds < 100 || TickIntervalMilliseconds > 60000)
                throw new InvalidOperationException("Autonomous.TickIntervalMilliseconds must be between 100 and 60000.");

            if (MaxConsecutiveFailures < 1 || MaxConsecutiveFailures > 100)
                throw new InvalidOperationException("Autonomous.MaxConsecutiveFailures must be between 1 and 100.");

            if (Actors == null)
                throw new InvalidOperationException("Autonomous.Actors cannot be null.");

            if (Actors.Any(actor => actor == null || actor.CharacterId <= 0))
                throw new InvalidOperationException("Every autonomous actor must have a positive character ID.");

            var duplicate = Actors.GroupBy(actor => actor.CharacterId).FirstOrDefault(group => group.Count() > 1);
            if (duplicate != null)
                throw new InvalidOperationException($"Autonomous character {duplicate.Key} is configured more than once.");

            foreach (AutonomousActorDefinition actor in Actors)
            {
                if (actor.RecoveryRevision < 0)
                    throw new InvalidOperationException($"Autonomous recovery revision for character {actor.CharacterId} cannot be negative.");

                if (string.Equals(actor.Behavior?.Trim(), "patrol", StringComparison.OrdinalIgnoreCase))
                {
                    if (actor.Patrol == null)
                        throw new InvalidOperationException($"Autonomous patrol options for character {actor.CharacterId} cannot be null.");
                    actor.Patrol.Validate(actor.CharacterId);
                    if (actor.Patrol.Pve.Enabled)
                    {
                        if (actor.Equipment?.Enabled != true)
                            throw new InvalidOperationException($"Autonomous PvE for character {actor.CharacterId} requires enabled equipment preparation.");
                        if (actor.Equipment.Slots == null || actor.Equipment.Slots.Count == 0)
                            throw new InvalidOperationException($"Autonomous PvE for character {actor.CharacterId} requires a configured combat fitting.");
                        if (actor.Equipment.RepairBelowRatio <= actor.Patrol.Pve.RetreatArmorRatio)
                            throw new InvalidOperationException($"Autonomous PvE repair threshold for character {actor.CharacterId} must exceed its armor retreat threshold.");
                    }
                }

                if (string.Equals(actor.Behavior?.Trim(), "mining", StringComparison.OrdinalIgnoreCase))
                {
                    if (actor.Mining == null)
                        throw new InvalidOperationException($"Autonomous mining options for character {actor.CharacterId} cannot be null.");
                    actor.Mining.Validate(actor.CharacterId);
                }

                if (string.Equals(actor.Behavior?.Trim(), "trader", StringComparison.OrdinalIgnoreCase))
                {
                    if (actor.Trader == null)
                        throw new InvalidOperationException($"Autonomous trader options for character {actor.CharacterId} cannot be null.");
                    actor.Trader.Validate(actor.CharacterId);
                }

                if (string.Equals(actor.Behavior?.Trim(), "manufacturer", StringComparison.OrdinalIgnoreCase))
                {
                    if (actor.Manufacturer == null)
                        throw new InvalidOperationException($"Autonomous manufacturer options for character {actor.CharacterId} cannot be null.");
                    actor.Manufacturer.Validate(actor.CharacterId);
                }

                if (string.Equals(actor.Behavior?.Trim(), "equipment", StringComparison.OrdinalIgnoreCase))
                {
                    if (actor.Equipment == null)
                        throw new InvalidOperationException($"Autonomous equipment options for character {actor.CharacterId} cannot be null.");
                    if (!actor.Equipment.Enabled)
                        throw new InvalidOperationException($"Autonomous equipment behavior for character {actor.CharacterId} must be enabled.");
                    actor.Equipment.Validate(actor.CharacterId);
                }
                else if (actor.Equipment?.Enabled == true)
                {
                    actor.Equipment.Validate(actor.CharacterId);
                }

                if (string.Equals(actor.Behavior?.Trim(), "mission", StringComparison.OrdinalIgnoreCase))
                {
                    if (actor.Mission == null)
                        throw new InvalidOperationException($"Autonomous mission options for character {actor.CharacterId} cannot be null.");
                    if (!actor.Mission.Enabled)
                        throw new InvalidOperationException($"Autonomous mission behavior for character {actor.CharacterId} must be enabled.");
                    actor.Mission.Validate(actor.CharacterId);
                    if (actor.Mission.GetCategory() == MissionCategory.Combat)
                    {
                        if (!actor.Mission.Pve.Enabled)
                            throw new InvalidOperationException($"Autonomous combat missions for character {actor.CharacterId} require enabled mission PvE.");
                        if (actor.Equipment?.Enabled != true)
                            throw new InvalidOperationException($"Autonomous combat missions for character {actor.CharacterId} require enabled equipment preparation.");
                        if (actor.Equipment.Slots == null || actor.Equipment.Slots.Count == 0)
                            throw new InvalidOperationException($"Autonomous combat missions for character {actor.CharacterId} require a configured combat fitting.");
                        if (actor.Equipment.RepairBelowRatio <= actor.Mission.Pve.RetreatArmorRatio)
                            throw new InvalidOperationException($"Autonomous combat mission repair threshold for character {actor.CharacterId} must exceed its armor retreat threshold.");
                    }
                }
                else if (actor.Mission?.Enabled == true)
                {
                    actor.Mission.Validate(actor.CharacterId);
                }
            }
        }
    }

    public sealed class AutonomousActorDefinition
    {
        public int CharacterId { get; set; }

        [DefaultValue(true), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool Enabled { get; set; } = true;

        [DefaultValue("idle"), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public string Behavior { get; set; } = "idle";

        [DefaultValue(0), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public int RecoveryRevision { get; set; }

        public AutonomousPatrolOptions Patrol { get; set; } = new AutonomousPatrolOptions();

        public AutonomousMiningOptions Mining { get; set; } = new AutonomousMiningOptions();

        public AutonomousTraderOptions Trader { get; set; } = new AutonomousTraderOptions();

        public AutonomousManufacturerOptions Manufacturer { get; set; } = new AutonomousManufacturerOptions();

        public AutonomousEquipmentOptions Equipment { get; set; } = new AutonomousEquipmentOptions();

        public AutonomousMissionOptions Mission { get; set; } = new AutonomousMissionOptions();
    }

    public sealed class AutonomousMissionOptions
    {
        [DefaultValue(false), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool Enabled { get; set; }

        [DefaultValue("Transport"), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public string Category { get; set; } = "Transport";

        [DefaultValue(0), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public int Level { get; set; }

        public long SourceBaseEid { get; set; }

        [DefaultValue(1), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public int TargetCount { get; set; } = 1;

        [DefaultValue(0.45), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public double Throttle { get; set; } = 0.45;

        [DefaultValue(5), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public int DockedDwellSeconds { get; set; } = 5;

        [DefaultValue(30), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public int RetrySeconds { get; set; } = 30;

        [DefaultValue(false), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool AllowRandom { get; set; }

        [DefaultValue(0), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public int ProgressionExtensionId { get; set; }

        [DefaultValue(0), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public int ProgressionExtensionLevel { get; set; }

        public AutonomousPveOptions Pve { get; set; } = new AutonomousPveOptions();

        public MissionCategory GetCategory()
        {
            return Enum.Parse<MissionCategory>(Category, true);
        }

        public void Validate(int characterId)
        {
            if (!Enabled)
                return;
            if (!Enum.TryParse(Category, true, out MissionCategory category) ||
                !Enum.IsDefined(typeof(MissionCategory), category))
                throw new InvalidOperationException($"Autonomous mission category for character {characterId} is invalid.");
            if (Level < -1 || Level > 9)
                throw new InvalidOperationException($"Autonomous mission level for character {characterId} must be between -1 and 9.");
            if (SourceBaseEid <= 0)
                throw new InvalidOperationException($"Autonomous mission for character {characterId} requires a positive source base EID.");
            if (TargetCount < 1 || TargetCount > 1000)
                throw new InvalidOperationException($"Autonomous mission target count for character {characterId} must be between 1 and 1000.");
            if (double.IsNaN(Throttle) || double.IsInfinity(Throttle) || Throttle <= 0 || Throttle > 1)
                throw new InvalidOperationException($"Autonomous mission throttle for character {characterId} must be greater than 0 and at most 1.");
            if (DockedDwellSeconds < 1 || DockedDwellSeconds > 3600)
                throw new InvalidOperationException($"Autonomous mission docked dwell for character {characterId} must be between 1 and 3600 seconds.");
            if (RetrySeconds < 5 || RetrySeconds > 3600)
                throw new InvalidOperationException($"Autonomous mission retry for character {characterId} must be between 5 and 3600 seconds.");
            if (ProgressionExtensionId < 0 || ProgressionExtensionLevel < 0 || ProgressionExtensionLevel > 10)
                throw new InvalidOperationException($"Autonomous mission progression extension for character {characterId} is invalid.");
            if ((ProgressionExtensionId == 0) != (ProgressionExtensionLevel == 0))
                throw new InvalidOperationException($"Autonomous mission progression for character {characterId} requires both an extension ID and target level.");
            if (Pve == null)
                throw new InvalidOperationException($"Autonomous mission PvE options for character {characterId} cannot be null.");
            Pve.Validate(characterId);
        }
    }

    public sealed class AutonomousEquipmentOptions
    {
        [DefaultValue(false), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool Enabled { get; set; }

        public string Robot { get; set; }

        public long RepairFacilityEid { get; set; }

        [DefaultValue(0.95), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public double RepairBelowRatio { get; set; } = 0.95;

        [DefaultValue(false), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool UseCorporationWallet { get; set; }

        [DefaultValue(30), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public int RetrySeconds { get; set; } = 30;

        public List<AutonomousEquipmentSlotOptions> Slots { get; set; } =
            new List<AutonomousEquipmentSlotOptions>();

        public AutonomousEquipmentProcurementOptions Procurement { get; set; } =
            new AutonomousEquipmentProcurementOptions();

        public void Validate(int characterId)
        {
            if (!Enabled)
                return;
            if (string.IsNullOrWhiteSpace(Robot))
                throw new InvalidOperationException($"Autonomous equipment for character {characterId} requires a robot definition name.");
            if (RepairFacilityEid <= 0)
                throw new InvalidOperationException($"Autonomous equipment for character {characterId} requires a positive repair facility EID.");
            if (double.IsNaN(RepairBelowRatio) || double.IsInfinity(RepairBelowRatio) ||
                RepairBelowRatio < 0 || RepairBelowRatio > 1)
                throw new InvalidOperationException($"Autonomous equipment repair ratio for character {characterId} must be between 0 and 1.");
            if (RetrySeconds < 5 || RetrySeconds > 3600)
                throw new InvalidOperationException($"Autonomous equipment retry for character {characterId} must be between 5 and 3600 seconds.");
            if (Slots == null)
                throw new InvalidOperationException($"Autonomous equipment slots for character {characterId} cannot be null.");
            if (Procurement == null)
                throw new InvalidOperationException($"Autonomous equipment procurement for character {characterId} cannot be null.");

            foreach (AutonomousEquipmentSlotOptions slot in Slots)
                slot?.Validate(characterId);
            if (Slots.Any(slot => slot == null))
                throw new InvalidOperationException($"Autonomous equipment slots for character {characterId} cannot contain null entries.");
            if (Slots.GroupBy(slot => new {slot.Component, slot.Slot}).Any(group => group.Count() > 1))
                throw new InvalidOperationException($"Autonomous equipment for character {characterId} cannot configure the same slot twice.");
            Procurement.Validate(characterId);
        }
    }

    public sealed class AutonomousEquipmentProcurementOptions
    {
        [DefaultValue(false), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool Enabled { get; set; }

        [DefaultValue(0.0), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public double MaximumUnitPrice { get; set; }

        [DefaultValue(10000.0), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public double WalletReserve { get; set; } = 10000.0;

        public void Validate(int characterId)
        {
            if (double.IsNaN(WalletReserve) || double.IsInfinity(WalletReserve) || WalletReserve < 0)
                throw new InvalidOperationException($"Autonomous equipment wallet reserve for character {characterId} must be finite and non-negative.");
            if (Enabled && (double.IsNaN(MaximumUnitPrice) ||
                            double.IsInfinity(MaximumUnitPrice) ||
                            MaximumUnitPrice <= 0))
                throw new InvalidOperationException($"Autonomous equipment maximum purchase price for character {characterId} must be positive when procurement is enabled.");
        }
    }

    public sealed class AutonomousEquipmentSlotOptions
    {
        public string Module { get; set; }
        public string Ammo { get; set; }
        public string Component { get; set; }
        public int Slot { get; set; }

        public RobotComponentType GetComponentType()
        {
            return Enum.Parse<RobotComponentType>(Component, true);
        }

        public void Validate(int characterId)
        {
            if (string.IsNullOrWhiteSpace(Module))
                throw new InvalidOperationException($"Autonomous equipment slot for character {characterId} requires a module definition name.");
            if (!Enum.TryParse(Component, true, out RobotComponentType component) ||
                !Enum.IsDefined(typeof(RobotComponentType), component))
                throw new InvalidOperationException($"Autonomous equipment slot for character {characterId} has an invalid robot component.");
            if (Slot < 0)
                throw new InvalidOperationException($"Autonomous equipment slot for character {characterId} cannot be negative.");
        }
    }

    public sealed class AutonomousManufacturerOptions
    {
        public int TargetDefinition { get; set; }

        [DefaultValue(1), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public long Quantity { get; set; } = 1;

        public long MillFacilityEid { get; set; }

        [DefaultValue(0), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public long ResearchFacilityEid { get; set; }

        [DefaultValue(0), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public long PrototypeFacilityEid { get; set; }

        [DefaultValue(0), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public long RefineryFacilityEid { get; set; }

        [DefaultValue(false), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool UseCorporationWallet { get; set; }

        [DefaultValue(30), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public int RetrySeconds { get; set; } = 30;

        public AutonomousManufacturerProcurementOptions Procurement { get; set; } =
            new AutonomousManufacturerProcurementOptions();

        public AutonomousManufacturerSalesOptions Sales { get; set; } =
            new AutonomousManufacturerSalesOptions();

        public void Validate(int characterId)
        {
            if (TargetDefinition <= 0)
                throw new InvalidOperationException($"Autonomous manufacturer for character {characterId} requires a positive target definition.");
            if (Quantity <= 0 || Quantity > 1000000000)
                throw new InvalidOperationException($"Autonomous manufacturer quantity for character {characterId} must be between 1 and 1000000000.");
            if (MillFacilityEid <= 0)
                throw new InvalidOperationException($"Autonomous manufacturer for character {characterId} requires a positive mill facility EID.");
            if (ResearchFacilityEid < 0)
                throw new InvalidOperationException($"Autonomous manufacturer research facility EID for character {characterId} cannot be negative.");
            if (PrototypeFacilityEid < 0)
                throw new InvalidOperationException($"Autonomous manufacturer prototype facility EID for character {characterId} cannot be negative.");
            if (RefineryFacilityEid < 0)
                throw new InvalidOperationException($"Autonomous manufacturer refinery facility EID for character {characterId} cannot be negative.");
            if (RetrySeconds < 5 || RetrySeconds > 3600)
                throw new InvalidOperationException($"Autonomous manufacturer retry for character {characterId} must be between 5 and 3600 seconds.");
            if (Procurement == null)
                throw new InvalidOperationException($"Autonomous manufacturer procurement options for character {characterId} cannot be null.");
            if (Sales == null)
                throw new InvalidOperationException($"Autonomous manufacturer sales options for character {characterId} cannot be null.");
            Procurement.Validate(characterId);
            Sales.Validate(characterId);
        }
    }

    public sealed class AutonomousManufacturerSalesOptions
    {
        [DefaultValue(false), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool Enabled { get; set; }

        [DefaultValue(1.0), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public double MinimumUnitPrice { get; set; } = 1.0;

        [DefaultValue(24), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public int OrderDurationHours { get; set; } = 24;

        public void Validate(int characterId)
        {
            if (double.IsNaN(MinimumUnitPrice) || double.IsInfinity(MinimumUnitPrice) ||
                MinimumUnitPrice <= 0)
                throw new InvalidOperationException($"Autonomous manufacturer minimum sale price for character {characterId} must be finite and positive.");
            if (OrderDurationHours < 1 || OrderDurationHours > 720)
                throw new InvalidOperationException($"Autonomous manufacturer order duration for character {characterId} must be between 1 and 720 hours.");
        }
    }

    public sealed class AutonomousManufacturerProcurementOptions
    {
        [DefaultValue(false), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool Enabled { get; set; }

        [DefaultValue(100), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public int MaximumPurchaseQuantity { get; set; } = 100;

        [DefaultValue(0.0), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public double MaximumUnitPrice { get; set; }

        [DefaultValue(10000.0), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public double WalletReserve { get; set; } = 10000.0;

        public void Validate(int characterId)
        {
            if (MaximumPurchaseQuantity < 1 || MaximumPurchaseQuantity > 10000)
                throw new InvalidOperationException($"Autonomous manufacturer purchase batch for character {characterId} must be between 1 and 10000.");
            if (double.IsNaN(WalletReserve) || double.IsInfinity(WalletReserve) || WalletReserve < 0)
                throw new InvalidOperationException($"Autonomous manufacturer wallet reserve for character {characterId} must be finite and non-negative.");
            if (Enabled && (double.IsNaN(MaximumUnitPrice) || double.IsInfinity(MaximumUnitPrice) || MaximumUnitPrice <= 0))
                throw new InvalidOperationException($"Autonomous manufacturer maximum purchase price for character {characterId} must be greater than zero when procurement is enabled.");
        }
    }

    public sealed class AutonomousTraderOptions
    {
        [DefaultValue(0.45), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public double Throttle { get; set; } = 0.45;

        [DefaultValue(10), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public int DockedDwellSeconds { get; set; } = 10;

        [DefaultValue(120), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public int MaximumObservationAgeMinutes { get; set; } = 120;

        [DefaultValue(1.0), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public double MinimumUnitProfit { get; set; } = 1.0;

        [DefaultValue(0.05), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public double MinimumMargin { get; set; } = 0.05;

        [DefaultValue(100), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public int MaximumQuantity { get; set; } = 100;

        [DefaultValue(10000.0), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public double WalletReserve { get; set; } = 10000.0;

        [DefaultValue(24), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public int OrderDurationHours { get; set; } = 24;

        public List<long> MarketBaseEids { get; set; } = new List<long>();

        public List<string> Commodities { get; set; } = new List<string>();

        public void Validate(int characterId)
        {
            if (double.IsNaN(Throttle) || double.IsInfinity(Throttle) || Throttle < 0.1 || Throttle > 1.0)
                throw new InvalidOperationException($"Autonomous trader throttle for character {characterId} must be between 0.1 and 1.0.");
            if (DockedDwellSeconds < 0 || DockedDwellSeconds > 3600)
                throw new InvalidOperationException($"Autonomous trader docked dwell for character {characterId} must be between 0 and 3600 seconds.");
            if (MaximumObservationAgeMinutes < 1 || MaximumObservationAgeMinutes > 10080)
                throw new InvalidOperationException($"Autonomous trader observation age for character {characterId} must be between 1 and 10080 minutes.");
            if (double.IsNaN(MinimumUnitProfit) || double.IsInfinity(MinimumUnitProfit) || MinimumUnitProfit < 0)
                throw new InvalidOperationException($"Autonomous trader minimum unit profit for character {characterId} cannot be negative.");
            if (double.IsNaN(MinimumMargin) || double.IsInfinity(MinimumMargin) || MinimumMargin < 0 || MinimumMargin > 10)
                throw new InvalidOperationException($"Autonomous trader minimum margin for character {characterId} must be between 0 and 10.");
            if (MaximumQuantity < 1 || MaximumQuantity > 100000)
                throw new InvalidOperationException($"Autonomous trader maximum quantity for character {characterId} must be between 1 and 100000.");
            if (double.IsNaN(WalletReserve) || double.IsInfinity(WalletReserve) || WalletReserve < 0)
                throw new InvalidOperationException($"Autonomous trader wallet reserve for character {characterId} cannot be negative.");
            if (OrderDurationHours < 1 || OrderDurationHours > 720)
                throw new InvalidOperationException($"Autonomous trader order duration for character {characterId} must be between 1 and 720 hours.");
            if (MarketBaseEids == null || MarketBaseEids.Count < 2 ||
                MarketBaseEids.Any(eid => eid <= 0) || MarketBaseEids.Distinct().Count() != MarketBaseEids.Count)
                throw new InvalidOperationException($"Autonomous trader for character {characterId} requires at least two distinct positive market base EIDs.");
            if (Commodities == null || Commodities.Count == 0 ||
                Commodities.Any(name => string.IsNullOrWhiteSpace(name)) ||
                Commodities.Select(name => name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Commodities.Count)
                throw new InvalidOperationException($"Autonomous trader for character {characterId} requires distinct commodity definition names.");
        }
    }

    public sealed class AutonomousMiningOptions
    {
        [DefaultValue("Titan"), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public string Material { get; set; } = "Titan";

        [DefaultValue(0.45), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public double Throttle { get; set; } = 0.45;

        [DefaultValue(0.75), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public double CargoFillRatio { get; set; } = 0.75;

        [DefaultValue(15), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public int DockedDwellSeconds { get; set; } = 15;

        [DefaultValue(30), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public int ScanTimeoutSeconds { get; set; } = 30;

        [DefaultValue(8), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public int LockTimeoutSeconds { get; set; } = 8;

        [DefaultValue(300), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public int MaxMiningSeconds { get; set; } = 300;

        [DefaultValue(3), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public int MaxScanAttempts { get; set; } = 3;

        [DefaultValue(11), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public int SurveyStepDistance { get; set; } = 11;

        [DefaultValue(48), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public int MaxSurveySites { get; set; } = 48;

        public AutonomousThreatOptions Threat { get; set; } = new AutonomousThreatOptions();

        public AutonomousMarketOptions Market { get; set; } = new AutonomousMarketOptions();

        public AutonomousMiningResupplyOptions Resupply { get; set; } = new AutonomousMiningResupplyOptions();

        public MaterialType GetMaterialType()
        {
            return Enum.TryParse(Material, true, out MaterialType materialType)
                ? materialType
                : MaterialType.Undefined;
        }

        public void Validate(int characterId)
        {
            MaterialType materialType = GetMaterialType();
            if (materialType == MaterialType.Undefined || !Enum.IsDefined(typeof(MaterialType), materialType))
                throw new InvalidOperationException($"Autonomous mining material for character {characterId} is invalid.");
            if (double.IsNaN(Throttle) || double.IsInfinity(Throttle) || Throttle < 0.1 || Throttle > 1.0)
                throw new InvalidOperationException($"Autonomous mining throttle for character {characterId} must be between 0.1 and 1.0.");
            if (double.IsNaN(CargoFillRatio) || double.IsInfinity(CargoFillRatio) || CargoFillRatio <= 0 || CargoFillRatio > 1.0)
                throw new InvalidOperationException($"Autonomous mining cargo ratio for character {characterId} must be greater than 0 and at most 1.");
            if (DockedDwellSeconds < 0 || DockedDwellSeconds > 3600)
                throw new InvalidOperationException($"Autonomous mining docked dwell for character {characterId} must be between 0 and 3600 seconds.");
            if (ScanTimeoutSeconds < 1 || ScanTimeoutSeconds > 120)
                throw new InvalidOperationException($"Autonomous mining scan timeout for character {characterId} must be between 1 and 120 seconds.");
            if (LockTimeoutSeconds < 1 || LockTimeoutSeconds > 60)
                throw new InvalidOperationException($"Autonomous mining lock timeout for character {characterId} must be between 1 and 60 seconds.");
            if (MaxMiningSeconds < 1 || MaxMiningSeconds > 3600)
                throw new InvalidOperationException($"Autonomous mining duration for character {characterId} must be between 1 and 3600 seconds.");
            if (MaxScanAttempts < 1 || MaxScanAttempts > 20)
                throw new InvalidOperationException($"Autonomous mining scan attempts for character {characterId} must be between 1 and 20.");
            if (SurveyStepDistance < 2 || SurveyStepDistance > 24)
                throw new InvalidOperationException($"Autonomous mining survey step for character {characterId} must be between 2 and 24 terrain units.");
            if (MaxSurveySites < 0 || MaxSurveySites > 128)
                throw new InvalidOperationException($"Autonomous mining survey sites for character {characterId} must be between 0 and 128.");
            double maximumSurveyLeg = AutonomousMiningSurveyPolicy.GetMaximumLegDistance(SurveyStepDistance);
            if (maximumSurveyLeg > AutonomousNavigationService.MaximumStartDistance)
                throw new InvalidOperationException($"Autonomous mining survey legs for character {characterId} must remain within {AutonomousNavigationService.MaximumStartDistance} terrain units.");
            if (Threat == null)
                throw new InvalidOperationException($"Autonomous mining threat options for character {characterId} cannot be null.");
            Threat.Validate(characterId);
            if (Market == null)
                throw new InvalidOperationException($"Autonomous mining market options for character {characterId} cannot be null.");
            Market.Validate(characterId);
            if (Resupply == null)
                throw new InvalidOperationException($"Autonomous mining resupply options for character {characterId} cannot be null.");
            Resupply.Validate(characterId);
        }
    }

    public sealed class AutonomousMiningResupplyOptions
    {
        [DefaultValue(true), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool Enabled { get; set; } = true;

        [DefaultValue(0.5), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public double ReloadBelowRatio { get; set; } = 0.5;

        [DefaultValue(60), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public int RetrySeconds { get; set; } = 60;

        [DefaultValue(false), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool BuyFromMarket { get; set; }

        [DefaultValue(64), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public int TileProbeReserve { get; set; } = 64;

        [DefaultValue(500), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public int MiningChargeReserve { get; set; } = 500;

        [DefaultValue(500), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public int MaximumPurchaseQuantity { get; set; } = 500;

        [DefaultValue(0.0), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public double MaximumUnitPrice { get; set; }

        public void Validate(int characterId)
        {
            if (double.IsNaN(ReloadBelowRatio) || double.IsInfinity(ReloadBelowRatio) ||
                ReloadBelowRatio <= 0 || ReloadBelowRatio > 1.0)
                throw new InvalidOperationException($"Autonomous mining reload ratio for character {characterId} must be greater than zero and at most 1.0.");
            if (RetrySeconds < 5 || RetrySeconds > 3600)
                throw new InvalidOperationException($"Autonomous mining resupply retry for character {characterId} must be between 5 and 3600 seconds.");
            if (TileProbeReserve < 0 || TileProbeReserve > 100000)
                throw new InvalidOperationException($"Autonomous tile-probe reserve for character {characterId} must be between 0 and 100000.");
            if (MiningChargeReserve < 0 || MiningChargeReserve > 100000)
                throw new InvalidOperationException($"Autonomous mining-charge reserve for character {characterId} must be between 0 and 100000.");
            if (MaximumPurchaseQuantity < 1 || MaximumPurchaseQuantity > 10000)
                throw new InvalidOperationException($"Autonomous mining purchase batch for character {characterId} must be between 1 and 10000.");
            if (BuyFromMarket && (double.IsNaN(MaximumUnitPrice) || double.IsInfinity(MaximumUnitPrice) || MaximumUnitPrice <= 0))
                throw new InvalidOperationException($"Autonomous mining maximum purchase price for character {characterId} must be greater than zero when market buying is enabled.");
            if (BuyFromMarket && TileProbeReserve == 0 && MiningChargeReserve == 0)
                throw new InvalidOperationException($"Autonomous mining market buying for character {characterId} requires a positive probe or charge reserve.");
        }
    }

    public sealed class AutonomousMarketOptions
    {
        [DefaultValue(false), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool Enabled { get; set; }

        [DefaultValue(true), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool SellAllRawMaterials { get; set; } = true;

        [DefaultValue(1.0), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public double MinimumUnitPrice { get; set; } = 1.0;

        [DefaultValue(0.98), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public double ListPriceFactor { get; set; } = 0.98;

        [DefaultValue(24), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public int OrderDurationHours { get; set; } = 24;

        [DefaultValue(60), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public int RetrySeconds { get; set; } = 60;

        public void Validate(int characterId)
        {
            if (double.IsNaN(MinimumUnitPrice) || double.IsInfinity(MinimumUnitPrice) || MinimumUnitPrice <= 0)
                throw new InvalidOperationException($"Autonomous market minimum unit price for character {characterId} must be greater than zero.");
            if (double.IsNaN(ListPriceFactor) || double.IsInfinity(ListPriceFactor) || ListPriceFactor < 0.1 || ListPriceFactor > 2.0)
                throw new InvalidOperationException($"Autonomous market list price factor for character {characterId} must be between 0.1 and 2.0.");
            if (OrderDurationHours < 1 || OrderDurationHours > 720)
                throw new InvalidOperationException($"Autonomous market order duration for character {characterId} must be between 1 and 720 hours.");
            if (RetrySeconds < 5 || RetrySeconds > 3600)
                throw new InvalidOperationException($"Autonomous market retry for character {characterId} must be between 5 and 3600 seconds.");
        }
    }

    public sealed class AutonomousPatrolOptions
    {
        [DefaultValue(14), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public int Radius { get; set; } = 14;

        [DefaultValue(0.45), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public double Throttle { get; set; } = 0.45;

        [DefaultValue(15), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public int DockedDwellSeconds { get; set; } = 15;

        [DefaultValue(2), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public int FieldDwellSeconds { get; set; } = 2;

        public AutonomousThreatOptions Threat { get; set; } = new AutonomousThreatOptions();

        public AutonomousDefenseOptions Defense { get; set; } = new AutonomousDefenseOptions();

        public AutonomousPveOptions Pve { get; set; } = new AutonomousPveOptions();

        public void Validate(int characterId)
        {
            if (Radius < 4 || Radius > 48)
                throw new InvalidOperationException($"Autonomous patrol radius for character {characterId} must be between 4 and 48.");

            if (double.IsNaN(Throttle) || double.IsInfinity(Throttle) || Throttle < 0.1 || Throttle > 1.0)
                throw new InvalidOperationException($"Autonomous patrol throttle for character {characterId} must be between 0.1 and 1.0.");

            if (DockedDwellSeconds < 0 || DockedDwellSeconds > 3600)
                throw new InvalidOperationException($"Autonomous docked dwell for character {characterId} must be between 0 and 3600 seconds.");

            if (FieldDwellSeconds < 0 || FieldDwellSeconds > 300)
                throw new InvalidOperationException($"Autonomous field dwell for character {characterId} must be between 0 and 300 seconds.");

            if (Threat == null)
                throw new InvalidOperationException($"Autonomous threat options for character {characterId} cannot be null.");

            Threat.Validate(characterId);

            if (Defense == null)
                throw new InvalidOperationException($"Autonomous defense options for character {characterId} cannot be null.");

            Defense.Validate(characterId);

            if (Pve == null)
                throw new InvalidOperationException($"Autonomous PvE options for character {characterId} cannot be null.");

            Pve.Validate(characterId);
        }
    }

    public sealed class AutonomousPveOptions
    {
        [DefaultValue(false), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool Enabled { get; set; }

        [DefaultValue(75.0), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public double AcquisitionRange { get; set; } = 75.0;

        [DefaultValue(45.0), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public double EngagementRange { get; set; } = 45.0;

        [DefaultValue(30), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public int SearchSeconds { get; set; } = 30;

        [DefaultValue(8), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public int LockTimeoutSeconds { get; set; } = 8;

        [DefaultValue(90), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public int MaxEngagementSeconds { get; set; } = 90;

        [DefaultValue(0.45), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public double RetreatArmorRatio { get; set; } = 0.45;

        [DefaultValue(0.15), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public double RetreatCoreRatio { get; set; } = 0.15;

        [DefaultValue(1), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public int TargetCount { get; set; } = 1;

        [DefaultValue(1), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public int MaxLosses { get; set; } = 1;

        public void Validate(int characterId)
        {
            if (double.IsNaN(AcquisitionRange) || double.IsInfinity(AcquisitionRange) ||
                AcquisitionRange < 1 || AcquisitionRange > 200)
                throw new InvalidOperationException($"Autonomous PvE acquisition range for character {characterId} must be between 1 and 200.");
            if (double.IsNaN(EngagementRange) || double.IsInfinity(EngagementRange) ||
                EngagementRange < 1 || EngagementRange > AcquisitionRange)
                throw new InvalidOperationException($"Autonomous PvE engagement range for character {characterId} must be positive and no greater than acquisition range.");
            if (SearchSeconds < 1 || SearchSeconds > 3600)
                throw new InvalidOperationException($"Autonomous PvE search time for character {characterId} must be between 1 and 3600 seconds.");
            if (LockTimeoutSeconds < 1 || LockTimeoutSeconds > 60)
                throw new InvalidOperationException($"Autonomous PvE lock timeout for character {characterId} must be between 1 and 60 seconds.");
            if (MaxEngagementSeconds <= LockTimeoutSeconds || MaxEngagementSeconds > 1800)
                throw new InvalidOperationException($"Autonomous PvE engagement limit for character {characterId} must exceed its lock timeout and be at most 1800 seconds.");
            if (double.IsNaN(RetreatArmorRatio) || double.IsInfinity(RetreatArmorRatio) ||
                RetreatArmorRatio <= 0 || RetreatArmorRatio >= 1)
                throw new InvalidOperationException($"Autonomous PvE armor retreat ratio for character {characterId} must be between zero and one.");
            if (double.IsNaN(RetreatCoreRatio) || double.IsInfinity(RetreatCoreRatio) ||
                RetreatCoreRatio <= 0 || RetreatCoreRatio >= 1)
                throw new InvalidOperationException($"Autonomous PvE core retreat ratio for character {characterId} must be between zero and one.");
            if (TargetCount < 1 || TargetCount > 1000)
                throw new InvalidOperationException($"Autonomous PvE target count for character {characterId} must be between 1 and 1000.");
            if (MaxLosses < 1 || MaxLosses > 100)
                throw new InvalidOperationException($"Autonomous PvE loss budget for character {characterId} must be between 1 and 100.");
        }
    }

    public sealed class AutonomousThreatOptions
    {
        [DefaultValue(true), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool Enabled { get; set; } = true;

        [DefaultValue(35.0), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public double ResponseRange { get; set; } = 35.0;

        [DefaultValue(60), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public int DockedDwellSeconds { get; set; } = 60;

        public void Validate(int characterId)
        {
            if (double.IsNaN(ResponseRange) || double.IsInfinity(ResponseRange) || ResponseRange < 1 || ResponseRange > 200)
                throw new InvalidOperationException($"Autonomous threat response range for character {characterId} must be between 1 and 200.");

            if (DockedDwellSeconds < 0 || DockedDwellSeconds > 3600)
                throw new InvalidOperationException($"Autonomous threat dwell for character {characterId} must be between 0 and 3600 seconds.");
        }
    }

    public sealed class AutonomousDefenseOptions
    {
        [DefaultValue(false), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool Enabled { get; set; }

        [DefaultValue(60.0), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public double ResponseRange { get; set; } = 60.0;

        [DefaultValue(8), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public int LockTimeoutSeconds { get; set; } = 8;

        [DefaultValue(20), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public int MaxEngagementSeconds { get; set; } = 20;

        public void Validate(int characterId)
        {
            if (double.IsNaN(ResponseRange) || double.IsInfinity(ResponseRange) || ResponseRange < 1 || ResponseRange > 200)
                throw new InvalidOperationException($"Autonomous defense response range for character {characterId} must be between 1 and 200.");

            if (LockTimeoutSeconds < 1 || LockTimeoutSeconds > 60)
                throw new InvalidOperationException($"Autonomous defense lock timeout for character {characterId} must be between 1 and 60 seconds.");

            if (MaxEngagementSeconds <= LockTimeoutSeconds || MaxEngagementSeconds > 300)
                throw new InvalidOperationException($"Autonomous defense engagement limit for character {characterId} must be greater than its lock timeout and at most 300 seconds.");
        }
    }
}
