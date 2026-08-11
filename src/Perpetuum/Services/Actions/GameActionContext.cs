using System;
using Perpetuum.Accounting.Characters;

namespace Perpetuum.Services.Actions
{
    public enum GameActionSource
    {
        Client,
        Autonomous,
        System
    }

    /// <summary>
    /// Identifies who requested an action and where the request originated.
    /// Source is audit metadata only; authorization always comes from Actor.
    /// </summary>
    public sealed class GameActionContext
    {
        public GameActionContext(Character actor, GameActionSource source)
        {
            Actor = actor ?? throw new ArgumentNullException(nameof(actor));
            Source = source;
        }

        public Character Actor { get; }
        public GameActionSource Source { get; }
    }
}
