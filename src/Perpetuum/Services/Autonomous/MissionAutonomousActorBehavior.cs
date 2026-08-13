using System;
using Perpetuum.Services.Actions;

namespace Perpetuum.Services.Autonomous
{
    public sealed class MissionAutonomousActorBehavior : IAutonomousActorBehavior
    {
        private readonly AutonomousActorDefinition _definition;
        private readonly IAutonomousMissionController _controller;

        public MissionAutonomousActorBehavior(
            AutonomousActorDefinition definition,
            IAutonomousMissionController controller)
        {
            _definition = definition ?? throw new ArgumentNullException(nameof(definition));
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        }

        public string Name => "mission";

        public void Start(GameActionContext context)
        {
            _controller.Start(context, _definition.Mission);
        }

        public void Update(GameActionContext context, TimeSpan elapsed)
        {
            _controller.Update(context, _definition.Mission, elapsed);
        }

        public void Stop(GameActionContext context)
        {
            _controller.Stop(context);
        }
    }
}
