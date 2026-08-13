using System;

namespace Perpetuum.Services.MissionEngine.MissionProcessorObjects
{
    /// <summary>
    /// Immutable, post-commit notification for consumers that present mission progress without
    /// becoming part of the mission transaction itself.
    /// </summary>
    public sealed class MissionProgressEvent
    {
        public MissionProgressEvent(
            int characterId,
            int missionId,
            Guid missionGuid,
            string missionName,
            MissionTargetType targetType,
            bool targetCompleted,
            bool missionCompleted)
        {
            CharacterId = characterId;
            MissionId = missionId;
            MissionGuid = missionGuid;
            MissionName = missionName;
            TargetType = targetType;
            TargetCompleted = targetCompleted;
            MissionCompleted = missionCompleted;
        }

        public int CharacterId { get; }
        public int MissionId { get; }
        public Guid MissionGuid { get; }
        public string MissionName { get; }
        public MissionTargetType TargetType { get; }
        public bool TargetCompleted { get; }
        public bool MissionCompleted { get; }
    }
}
