using System;
using System.Collections.Generic;
using Perpetuum.Services.Actions;

namespace Perpetuum.Services.Autonomous
{
    public enum AutonomousActorStatus
    {
        Stopped,
        Standby,
        Active,
        Suspended,
        Faulted
    }

    public sealed class AutonomousActorSnapshot
    {
        public AutonomousActorSnapshot(int characterId, string behavior, AutonomousActorStatus status, string reason)
        {
            CharacterId = characterId;
            Behavior = behavior;
            Status = status;
            Reason = reason;
        }

        public int CharacterId { get; }
        public string Behavior { get; }
        public AutonomousActorStatus Status { get; }
        public string Reason { get; }
    }

    public interface IAutonomousActor
    {
        int CharacterId { get; }
        AutonomousActorStatus Status { get; }
        AutonomousActorSnapshot Snapshot { get; }

        void Start();
        void Stop();
        void Update(TimeSpan elapsed);
        void Fault(string reason);
    }

    public delegate IAutonomousActor AutonomousActorFactory(AutonomousActorDefinition definition);

    public interface IAutonomousActorBehavior
    {
        string Name { get; }
        void Start(GameActionContext context);
        void Update(GameActionContext context, TimeSpan elapsed);
        void Stop(GameActionContext context);
    }

    public delegate IAutonomousActorBehavior AutonomousActorBehaviorFactory(string name);

    public interface IAutonomousActorRegistry
    {
        IReadOnlyCollection<IAutonomousActor> Actors { get; }
        IReadOnlyCollection<AutonomousActorSnapshot> Snapshots { get; }

        void Add(IAutonomousActor actor);
        bool TryGet(int characterId, out IAutonomousActor actor);
        void Clear();
    }
}
