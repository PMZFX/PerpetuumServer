using System;

namespace Perpetuum.Services.Actions
{
    public readonly struct MovementInput
    {
        public MovementInput(double direction, double throttle)
        {
            Direction = direction;
            Throttle = throttle;
        }

        public double Direction { get; }
        public double Throttle { get; }
    }

    /// <summary>
    /// Applies normalized steering input to the actor's live player. Position,
    /// speed, collision, slope, and terrain remain server-authoritative.
    /// </summary>
    public interface IMovementInputService
    {
        void Apply(GameActionContext context, MovementInput input);
    }

    public sealed class MovementInputService : IMovementInputService
    {
        public void Apply(GameActionContext context, MovementInput input)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            ValidateNormalized(input.Direction);
            ValidateNormalized(input.Throttle);

            var player = context.Actor.GetPlayerRobotFromZone()
                .ThrowIfNull(ErrorCodes.PlayerNotFound);
            player.Direction = input.Direction;
            player.CurrentSpeed = input.Throttle;
        }

        private static void ValidateNormalized(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0 || value > 1)
                throw new PerpetuumException(ErrorCodes.InvalidMovement);
        }
    }
}
