using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Perpetuum.Services.Autonomous
{
    public sealed class AutonomousActorRegistry : IAutonomousActorRegistry
    {
        private readonly ConcurrentDictionary<int, IAutonomousActor> _actors =
            new ConcurrentDictionary<int, IAutonomousActor>();

        public IReadOnlyCollection<IAutonomousActor> Actors =>
            _actors.Values.OrderBy(actor => actor.CharacterId).ToArray();

        public IReadOnlyCollection<AutonomousActorSnapshot> Snapshots =>
            Actors.Select(actor => actor.Snapshot).ToArray();

        public void Add(IAutonomousActor actor)
        {
            if (actor == null)
                throw new ArgumentNullException(nameof(actor));

            if (!_actors.TryAdd(actor.CharacterId, actor))
                throw new InvalidOperationException($"Autonomous character {actor.CharacterId} is already registered.");
        }

        public bool TryGet(int characterId, out IAutonomousActor actor)
        {
            return _actors.TryGetValue(characterId, out actor);
        }

        public void Clear()
        {
            _actors.Clear();
        }
    }
}
