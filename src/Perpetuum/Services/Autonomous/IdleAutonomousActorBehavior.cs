using System;
using Perpetuum.Services.Actions;

namespace Perpetuum.Services.Autonomous
{
    /// <summary>
    /// Safe lifecycle probe. It deliberately performs no game actions.
    /// </summary>
    public sealed class IdleAutonomousActorBehavior : IAutonomousActorBehavior
    {
        public string Name => "idle";

        public void Start(GameActionContext context) { }
        public void Update(GameActionContext context, TimeSpan elapsed) { }
        public void Stop(GameActionContext context) { }
    }
}
