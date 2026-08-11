using Perpetuum.Log;

namespace Perpetuum.Services.Autonomous
{
    public interface IAutonomousActorAudit
    {
        void Write(int characterId, string eventName, AutonomousActorStatus status, string reason = null);
    }

    public sealed class AutonomousActorAudit : IAutonomousActorAudit
    {
        public void Write(int characterId, string eventName, AutonomousActorStatus status, string reason = null)
        {
            string message = $"[AUTONOMOUS] event={eventName} actor={characterId} status={status}";
            if (!string.IsNullOrEmpty(reason))
            {
                message += $" reason={Sanitize(reason)}";
            }

            Logger.Info(message);
        }

        private static string Sanitize(string value)
        {
            return value.Replace(' ', '_').Replace('\r', '_').Replace('\n', '_');
        }
    }
}
