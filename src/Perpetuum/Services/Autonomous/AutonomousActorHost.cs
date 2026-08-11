using System;
using System.Collections.Generic;
using Perpetuum.Log;
using Perpetuum.Threading.Process;

namespace Perpetuum.Services.Autonomous
{
    public sealed class AutonomousActorHost : Process
    {
        private readonly AutonomousConfiguration _configuration;
        private readonly AutonomousActorFactory _actorFactory;
        private readonly IAutonomousActorRegistry _registry;
        private readonly IAutonomousActorAudit _audit;
        private readonly Dictionary<int, int> _failureCounts = new Dictionary<int, int>();
        private bool _running;

        public AutonomousActorHost(
            AutonomousConfiguration configuration,
            AutonomousActorFactory actorFactory,
            IAutonomousActorRegistry registry,
            IAutonomousActorAudit audit)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _actorFactory = actorFactory ?? throw new ArgumentNullException(nameof(actorFactory));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        }

        public override void Start()
        {
            _configuration.Validate();
            if (!_configuration.Enabled)
            {
                Logger.Info("[AUTONOMOUS] host=disabled");
                return;
            }

            _running = true;
            foreach (AutonomousActorDefinition definition in _configuration.Actors)
            {
                if (!definition.Enabled)
                    continue;

                try
                {
                    IAutonomousActor actor = _actorFactory(definition);
                    _registry.Add(actor);
                    _failureCounts.Add(actor.CharacterId, 0);

                    try
                    {
                        actor.Start();
                    }
                    catch (Exception ex)
                    {
                        actor.Fault(ex.GetType().Name);
                        _audit.Write(actor.CharacterId, "start_failed", actor.Status, ex.GetType().Name);
                    }
                }
                catch (Exception ex)
                {
                    _audit.Write(definition.CharacterId, "registration_failed", AutonomousActorStatus.Faulted, ex.GetType().Name);
                }
            }

            Logger.Info($"[AUTONOMOUS] host=started actors={_registry.Actors.Count}");
        }

        public override void Stop()
        {
            if (!_running)
                return;

            foreach (IAutonomousActor actor in _registry.Actors)
            {
                try
                {
                    actor.Stop();
                }
                catch (Exception ex)
                {
                    _audit.Write(actor.CharacterId, "stop_failed", actor.Status, ex.GetType().Name);
                }
            }

            _registry.Clear();
            _failureCounts.Clear();
            _running = false;
            Logger.Info("[AUTONOMOUS] host=stopped");
        }

        public override void Update(TimeSpan time)
        {
            if (!_running)
                return;

            foreach (IAutonomousActor actor in _registry.Actors)
            {
                if (actor.Status == AutonomousActorStatus.Faulted)
                    continue;

                try
                {
                    actor.Update(time);
                    _failureCounts[actor.CharacterId] = 0;
                }
                catch (Exception ex)
                {
                    int failures = _failureCounts[actor.CharacterId] + 1;
                    _failureCounts[actor.CharacterId] = failures;
                    _audit.Write(actor.CharacterId, "update_failed", actor.Status, ex.GetType().Name);

                    if (failures >= _configuration.MaxConsecutiveFailures)
                    {
                        actor.Fault(ex.GetType().Name);
                    }
                }
            }
        }
    }
}
