using System;
using Perpetuum.Services.Actions;

namespace Perpetuum.Services.Autonomous
{
    public sealed class ManufacturerAutonomousActorBehavior : IAutonomousActorBehavior
    {
        private readonly AutonomousActorDefinition _definition;
        private readonly IAutonomousIndustryController _controller;
        private TimeSpan _elapsed;

        public ManufacturerAutonomousActorBehavior(
            AutonomousActorDefinition definition,
            IAutonomousIndustryController controller)
        {
            _definition = definition ?? throw new ArgumentNullException(nameof(definition));
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        }

        public string Name => "manufacturer";

        public void Start(GameActionContext context)
        {
            _elapsed = TimeSpan.FromSeconds(_definition.Manufacturer.RetrySeconds);
        }

        public void Update(GameActionContext context, TimeSpan elapsed)
        {
            _elapsed += elapsed;
            if (_elapsed < TimeSpan.FromSeconds(_definition.Manufacturer.RetrySeconds))
                return;
            _elapsed = TimeSpan.Zero;
            _controller.Update(context, _definition.Manufacturer);
        }

        public void Stop(GameActionContext context)
        {
            _elapsed = TimeSpan.Zero;
        }
    }
}
