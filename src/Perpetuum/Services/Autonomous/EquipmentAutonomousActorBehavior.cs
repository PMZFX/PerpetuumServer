using System;
using Perpetuum.Services.Actions;

namespace Perpetuum.Services.Autonomous
{
    public sealed class EquipmentAutonomousActorBehavior : IAutonomousActorBehavior
    {
        private readonly AutonomousActorDefinition _definition;
        private readonly IAutonomousEquipmentController _controller;
        private TimeSpan _elapsed;

        public EquipmentAutonomousActorBehavior(
            AutonomousActorDefinition definition,
            IAutonomousEquipmentController controller)
        {
            _definition = definition ?? throw new ArgumentNullException(nameof(definition));
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        }

        public string Name => "equipment";

        public void Start(GameActionContext context)
        {
            _definition.Equipment.Validate(_definition.CharacterId);
            _elapsed = TimeSpan.FromSeconds(_definition.Equipment.RetrySeconds);
        }

        public void Update(GameActionContext context, TimeSpan elapsed)
        {
            _elapsed += elapsed;
            if (_elapsed < TimeSpan.FromSeconds(_definition.Equipment.RetrySeconds))
                return;

            _elapsed = TimeSpan.Zero;
            _controller.Update(context, _definition.Equipment);
        }

        public void Stop(GameActionContext context)
        {
            _elapsed = TimeSpan.Zero;
        }
    }
}
