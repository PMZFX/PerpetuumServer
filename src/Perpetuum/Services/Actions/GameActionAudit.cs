using System;
using System.Diagnostics;
using Perpetuum.Log;

namespace Perpetuum.Services.Actions
{
    public interface IGameActionAudit
    {
        void Execute(GameActionContext context, string actionName, Action action);
        T Execute<T>(GameActionContext context, string actionName, Func<T> action);
    }

    public sealed class GameActionAudit : IGameActionAudit
    {
        public void Execute(GameActionContext context, string actionName, Action action)
        {
            Execute(context, actionName, () =>
            {
                action();
                return true;
            });
        }

        public T Execute<T>(GameActionContext context, string actionName, Func<T> action)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (string.IsNullOrWhiteSpace(actionName))
                throw new ArgumentException("An action name is required.", nameof(actionName));
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            var timer = Stopwatch.StartNew();
            try
            {
                T result = action();
                Write(context, actionName, "success", null, timer.ElapsedMilliseconds);
                return result;
            }
            catch (Exception ex)
            {
                string error = ex is PerpetuumException perpetuumException
                    ? perpetuumException.error.ToString()
                    : ex.GetType().Name;
                Write(context, actionName, "failure", error, timer.ElapsedMilliseconds);
                throw;
            }
        }

        private static void Write(GameActionContext context, string actionName, string outcome, string error, long elapsedMilliseconds)
        {
            string message = $"[ACTION] name={actionName} actor={context.Actor.Id} source={context.Source} outcome={outcome} elapsedMs={elapsedMilliseconds}";
            if (!string.IsNullOrEmpty(error))
            {
                message += $" error={error}";
            }

            Logger.Info(message);
        }
    }
}
