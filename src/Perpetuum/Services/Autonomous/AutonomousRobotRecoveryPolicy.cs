namespace Perpetuum.Services.Autonomous
{
    public enum AutonomousRobotRecoveryReason
    {
        None,
        NoActiveRobot,
        RobotDestroyed,
        RobotReplaced
    }

    /// <summary>
    /// Detects loss of the robot a behavior started with. The normal player
    /// death flow remains responsible for docking, loot, insurance, trash,
    /// starter-robot creation, and active-robot selection. Autonomous behavior
    /// pauses until a later behavior start accepts the player's chosen robot.
    /// </summary>
    public static class AutonomousRobotRecoveryPolicy
    {
        public static AutonomousRobotRecoveryReason Assess(
            long expectedRobotEid,
            long activeRobotEid,
            bool liveRobotDead)
        {
            if (liveRobotDead)
                return AutonomousRobotRecoveryReason.RobotDestroyed;
            if (activeRobotEid <= 0)
                return AutonomousRobotRecoveryReason.NoActiveRobot;
            if (expectedRobotEid <= 0 || activeRobotEid != expectedRobotEid)
                return AutonomousRobotRecoveryReason.RobotReplaced;
            return AutonomousRobotRecoveryReason.None;
        }
    }
}
