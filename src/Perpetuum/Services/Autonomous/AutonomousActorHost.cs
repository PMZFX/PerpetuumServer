using System;
using System.Collections.Generic;
using System.Linq;
using Perpetuum.Log;
using Perpetuum.Threading.Process;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Perpetuum.Services.Autonomous
{
    public sealed class AutonomousActorHost : Process
    {
        private readonly AutonomousConfiguration _configuration;
        private readonly AutonomousActorFactory _actorFactory;
        private readonly IAutonomousActorRegistry _registry;
        private readonly IAutonomousActorAudit _audit;
        private readonly IAutonomousPopulationTelemetry _telemetry;
        private readonly Dictionary<int, int> _failureCounts = new Dictionary<int, int>();
        private readonly Dictionary<int, TimeSpan> _actorElapsed = new Dictionary<int, TimeSpan>();
        private readonly List<IAutonomousActor> _scheduledActors = new List<IAutonomousActor>();
        private readonly HashSet<int> _controlEligibleActorIds = new HashSet<int>();
        private int _nextActorIndex;
        private bool _running;

        public AutonomousActorHost(
            AutonomousConfiguration configuration,
            AutonomousActorFactory actorFactory,
            IAutonomousActorRegistry registry,
            IAutonomousActorAudit audit,
            IAutonomousPopulationTelemetry telemetry)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _actorFactory = actorFactory ?? throw new ArgumentNullException(nameof(actorFactory));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _audit = audit ?? throw new ArgumentNullException(nameof(audit));
            _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
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
            IReadOnlyList<AutonomousActorDefinition> definitions = _configuration
                .ResolveActorDefinitions()
                .Where(definition => definition.Enabled)
                .OrderBy(definition => definition.CharacterId)
                .ToArray();
            int registrationFailures = 0;
            foreach (AutonomousActorDefinition definition in definitions)
            {
                try
                {
                    IAutonomousActor actor = _actorFactory(definition);
                    _registry.Add(actor);
                    _failureCounts.Add(actor.CharacterId, 0);
                    _actorElapsed.Add(actor.CharacterId, TimeSpan.Zero);
                    _scheduledActors.Add(actor);

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
                    registrationFailures++;
                    _audit.Write(definition.CharacterId, "registration_failed", AutonomousActorStatus.Faulted, ex.GetType().Name);
                }
            }

            _nextActorIndex = 0;
            _telemetry.Start(definitions.Count, _registry.Snapshots);
            for (int i = 0; i < registrationFailures; i++)
                _telemetry.RecordRegistrationFailure();
            Logger.Info($"[AUTONOMOUS] host=started actors={_registry.Actors.Count}");
        }

        public override void Stop()
        {
            if (!_running)
                return;

            _telemetry.Stop(_registry.Snapshots);
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
            _actorElapsed.Clear();
            _scheduledActors.Clear();
            _controlEligibleActorIds.Clear();
            _nextActorIndex = 0;
            _running = false;
            Logger.Info("[AUTONOMOUS] host=stopped");
        }

        public override void Update(TimeSpan time)
        {
            if (!_running)
                return;

            _controlEligibleActorIds.Clear();
            foreach (IAutonomousActor actor in _scheduledActors)
            {
                _actorElapsed[actor.CharacterId] += time;
                if (actor.CheckControlOwnership())
                    _controlEligibleActorIds.Add(actor.CharacterId);
                else
                    _actorElapsed[actor.CharacterId] = TimeSpan.Zero;
            }

            int availableActors = _controlEligibleActorIds.Count;
            int budget = _configuration.PopulationLab.Enabled
                ? Math.Min(_configuration.PopulationLab.MaximumActorUpdatesPerTick, availableActors)
                : availableActors;
            int visited = 0;
            int scheduled = 0;
            while (visited < _scheduledActors.Count && scheduled < budget)
            {
                if (_nextActorIndex >= _scheduledActors.Count)
                    _nextActorIndex = 0;
                IAutonomousActor actor = _scheduledActors[_nextActorIndex++];
                visited++;
                if (!_controlEligibleActorIds.Contains(actor.CharacterId))
                    continue;

                TimeSpan actorElapsed = _actorElapsed[actor.CharacterId];
                _actorElapsed[actor.CharacterId] = TimeSpan.Zero;
                var stopwatch = Stopwatch.StartNew();
                bool succeeded = false;
                try
                {
                    actor.Update(actorElapsed);
                    _failureCounts[actor.CharacterId] = 0;
                    succeeded = true;
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
                finally
                {
                    stopwatch.Stop();
                    _telemetry.RecordUpdate(actorElapsed, stopwatch.Elapsed, succeeded);
                    scheduled++;
                }
            }

            _telemetry.Tick(
                time,
                scheduled,
                Math.Max(0, availableActors - scheduled),
                _registry);
        }
    }
}
