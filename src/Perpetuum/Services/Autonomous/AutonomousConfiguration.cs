using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Newtonsoft.Json;

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
                if (string.Equals(actor.Behavior?.Trim(), "patrol", StringComparison.OrdinalIgnoreCase))
                {
                    if (actor.Patrol == null)
                        throw new InvalidOperationException($"Autonomous patrol options for character {actor.CharacterId} cannot be null.");
                    actor.Patrol.Validate(actor.CharacterId);
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

        public AutonomousPatrolOptions Patrol { get; set; } = new AutonomousPatrolOptions();
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
