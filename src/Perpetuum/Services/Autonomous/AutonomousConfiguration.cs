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
        }
    }

    public sealed class AutonomousActorDefinition
    {
        public int CharacterId { get; set; }

        [DefaultValue(true), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool Enabled { get; set; } = true;

        [DefaultValue("idle"), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public string Behavior { get; set; } = "idle";
    }
}
