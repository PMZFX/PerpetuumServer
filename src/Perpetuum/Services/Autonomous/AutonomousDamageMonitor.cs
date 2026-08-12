using System;
using System.Threading;
using Perpetuum.Players;
using Perpetuum.Units;
using Perpetuum.Zones.DamageProcessors;

namespace Perpetuum.Services.Autonomous
{
    public interface IAutonomousDamageMonitor
    {
        void Bind(Player player);
        bool TryTakeAttacker(out long attackerEid);
        void Reset();
    }

    /// <summary>
    /// Captures only actual positive-damage sources reported by the controlled
    /// player's normal damage processor. It does not enumerate or resolve
    /// targets; visibility and combat legality remain action-time rules.
    /// </summary>
    public sealed class AutonomousDamageMonitor : IAutonomousDamageMonitor
    {
        private Player _player;
        private long _latestAttackerEid;

        public void Bind(Player player)
        {
            if (ReferenceEquals(_player, player))
                return;

            Unbind();
            _player = player;
            if (_player != null)
                _player.DamageTaken += OnDamageTaken;
        }

        public bool TryTakeAttacker(out long attackerEid)
        {
            attackerEid = Interlocked.Exchange(ref _latestAttackerEid, 0);
            return attackerEid > 0;
        }

        public void Reset()
        {
            Unbind();
            Interlocked.Exchange(ref _latestAttackerEid, 0);
        }

        private void Unbind()
        {
            Player player = _player;
            _player = null;
            if (player != null)
                player.DamageTaken -= OnDamageTaken;
        }

        private void OnDamageTaken(Unit victim, Unit attacker, DamageTakenEventArgs args)
        {
            if (attacker == null || args == null || args.TotalDamage <= 0.0 || attacker.Eid == victim.Eid)
                return;

            Interlocked.Exchange(ref _latestAttackerEid, attacker.Eid);
        }
    }
}
