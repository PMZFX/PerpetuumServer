using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Newtonsoft.Json;
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
                }

                if (string.Equals(actor.Behavior?.Trim(), "mining", StringComparison.OrdinalIgnoreCase))
                {
                    if (actor.Mining == null)
                        throw new InvalidOperationException($"Autonomous mining options for character {actor.CharacterId} cannot be null.");
                    actor.Mining.Validate(actor.CharacterId);
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
            int furthestSurveyRing = AutonomousMiningSurveyPolicy.GetRingCount(MaxSurveySites);
            double furthestSurveyDistance = SurveyStepDistance * furthestSurveyRing * Math.Sqrt(2);
            if (furthestSurveyDistance > AutonomousNavigationService.MaximumStartDistance)
                throw new InvalidOperationException($"Autonomous mining survey extent for character {characterId} must remain within {AutonomousNavigationService.MaximumStartDistance} terrain units of its origin.");
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

        public void Validate(int characterId)
        {
            if (double.IsNaN(ReloadBelowRatio) || double.IsInfinity(ReloadBelowRatio) ||
                ReloadBelowRatio <= 0 || ReloadBelowRatio > 1.0)
                throw new InvalidOperationException($"Autonomous mining reload ratio for character {characterId} must be greater than zero and at most 1.0.");
            if (RetrySeconds < 5 || RetrySeconds > 3600)
                throw new InvalidOperationException($"Autonomous mining resupply retry for character {characterId} must be between 5 and 3600 seconds.");
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
