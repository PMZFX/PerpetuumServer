using System;
using Perpetuum.Services.Actions;

namespace Perpetuum.Services.Autonomous
{
    public sealed class MissionAutonomousActorBehavior : IAutonomousActorBehavior
    {
        private readonly AutonomousActorDefinition _definition;
        private readonly IAutonomousMissionController _controller;
        private readonly IAutonomousEquipmentController _equipment;

        public MissionAutonomousActorBehavior(
            AutonomousActorDefinition definition,
            IAutonomousMissionController controller,
            IAutonomousEquipmentController equipment)
        {
            _definition = definition ?? throw new ArgumentNullException(nameof(definition));
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
            _equipment = equipment ?? throw new ArgumentNullException(nameof(equipment));
        }

        public string Name => "mission";

        public void Start(GameActionContext context)
        {
            _controller.Start(context, _definition.Mission);
        }

        public void Update(GameActionContext context, TimeSpan elapsed)
        {
            if (context.Actor.IsDocked &&
                _definition.Mission.GetCategory() == Perpetuum.Services.MissionEngine.MissionCategory.Combat &&
                _equipment.Update(context, _definition.Equipment) != AutonomousEquipmentUpdateResult.Ready)
                return;
            _controller.Update(context, _definition.Mission, elapsed);
        }

        public void Stop(GameActionContext context)
        {
            _controller.Stop(context);
        }
    }
}
