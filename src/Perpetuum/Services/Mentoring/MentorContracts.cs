using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Perpetuum.Services.Mentoring
{
    public sealed class MentorOptions
    {
        public static MentorOptions PhaseOneDefaults => new MentorOptions(
            "Mentor",
            32,
            TimeSpan.FromSeconds(60),
            TimeSpan.FromMinutes(1),
            20,
            40,
            1000,
            2000);

        public MentorOptions(
            string channelName,
            int queueCapacity,
            TimeSpan providerTimeout,
            TimeSpan rateLimitWindow,
            int requestsPerCharacter,
            int requestsPerAccount,
            int maximumRequestLength,
            int maximumResponseLength)
        {
            if (string.IsNullOrWhiteSpace(channelName))
                throw new ArgumentException("A mentor channel name is required.", nameof(channelName));
            if (queueCapacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(queueCapacity));
            if (providerTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(providerTimeout));
            if (rateLimitWindow <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(rateLimitWindow));
            if (requestsPerCharacter <= 0)
                throw new ArgumentOutOfRangeException(nameof(requestsPerCharacter));
            if (requestsPerAccount <= 0)
                throw new ArgumentOutOfRangeException(nameof(requestsPerAccount));
            if (maximumRequestLength <= 0)
                throw new ArgumentOutOfRangeException(nameof(maximumRequestLength));
            if (maximumResponseLength <= 0)
                throw new ArgumentOutOfRangeException(nameof(maximumResponseLength));

            ChannelName = channelName;
            QueueCapacity = queueCapacity;
            ProviderTimeout = providerTimeout;
            RateLimitWindow = rateLimitWindow;
            RequestsPerCharacter = requestsPerCharacter;
            RequestsPerAccount = requestsPerAccount;
            MaximumRequestLength = maximumRequestLength;
            MaximumResponseLength = maximumResponseLength;
        }

        public string ChannelName { get; }
        public int QueueCapacity { get; }
        public TimeSpan ProviderTimeout { get; }
        public TimeSpan RateLimitWindow { get; }
        public int RequestsPerCharacter { get; }
        public int RequestsPerAccount { get; }
        public int MaximumRequestLength { get; }
        public int MaximumResponseLength { get; }
    }

    public sealed class MentorRequest
    {
        public MentorRequest(
            Guid requestId,
            int characterId,
            int accountId,
            string channel,
            string message,
            DateTime createdAtUtc)
        {
            RequestId = requestId;
            CharacterId = characterId;
            AccountId = accountId;
            Channel = channel;
            Message = message;
            CreatedAtUtc = createdAtUtc;
        }

        public Guid RequestId { get; }
        public int CharacterId { get; }
        public int AccountId { get; }
        public string Channel { get; }
        public string Message { get; }
        public DateTime CreatedAtUtc { get; }
    }

    public sealed class MentorPlayerContext
    {
        public MentorPlayerContext(
            int characterId,
            int accountId,
            string characterName,
            bool isDocked,
            int? zoneId,
            string zoneName,
            Position? position,
            long activeRobotEid)
            : this(
                characterId,
                accountId,
                characterName,
                isDocked,
                zoneId,
                zoneName,
                position,
                activeRobotEid,
                0,
                null,
                0,
                null,
                false)
        {
        }

        public MentorPlayerContext(
            int characterId,
            int accountId,
            string characterName,
            bool isDocked,
            int? zoneId,
            string zoneName,
            Position? position,
            long activeRobotEid,
            int activeRobotDefinition,
            string activeRobotName,
            long dockingBaseEid,
            string dockingBaseName,
            bool isTrainingCharacter,
            IReadOnlyList<MentorNearbyUnitSnapshot> visibleNpcs = null,
            IReadOnlyList<MentorNearbyUnitSnapshot> zoneCombatTargets = null,
            bool zoneCombatScanAvailable = false)
        {
            CharacterId = characterId;
            AccountId = accountId;
            CharacterName = characterName;
            IsDocked = isDocked;
            ZoneId = zoneId;
            ZoneName = zoneName;
            Position = position;
            ActiveRobotEid = activeRobotEid;
            ActiveRobotDefinition = activeRobotDefinition;
            ActiveRobotName = activeRobotName;
            DockingBaseEid = dockingBaseEid;
            DockingBaseName = dockingBaseName;
            IsTrainingCharacter = isTrainingCharacter;
            VisibleNpcs = visibleNpcs ?? Array.Empty<MentorNearbyUnitSnapshot>();
            ZoneCombatTargets = zoneCombatTargets ?? Array.Empty<MentorNearbyUnitSnapshot>();
            ZoneCombatScanAvailable = zoneCombatScanAvailable;
        }

        public int CharacterId { get; }
        public int AccountId { get; }
        public string CharacterName { get; }
        public bool IsDocked { get; }
        public int? ZoneId { get; }
        public string ZoneName { get; }
        public Position? Position { get; }
        public long ActiveRobotEid { get; }
        public int ActiveRobotDefinition { get; }
        public string ActiveRobotName { get; }
        public long DockingBaseEid { get; }
        public string DockingBaseName { get; }
        /// <summary>
        /// True while character creation/onboarding is incomplete. This is not a statement about
        /// the character's current zone and does not imply that combat is prohibited.
        /// </summary>
        public bool IsTrainingCharacter { get; }
        public IReadOnlyList<MentorNearbyUnitSnapshot> VisibleNpcs { get; }
        /// <summary>
        /// Nearest server-attackable NPC or tutorial target units in the current zone. Targets may
        /// be outside the player's current detection or locking range, so this is an opportunity
        /// snapshot rather than a claim that the client can currently see or engage them.
        /// </summary>
        public IReadOnlyList<MentorNearbyUnitSnapshot> ZoneCombatTargets { get; }
        public bool ZoneCombatScanAvailable { get; }
    }

    public sealed class MentorNearbyUnitSnapshot
    {
        public MentorNearbyUnitSnapshot(
            long eid,
            int definition,
            string definitionName,
            double distance,
            bool isLockable,
            bool isWithinLockingRange,
            bool isValidAttackTarget,
            string lockState,
            bool isPrimaryLock,
            bool isVisible = true,
            Position? position = null)
        {
            Eid = eid;
            Definition = definition;
            DefinitionName = definitionName;
            Distance = distance;
            IsLockable = isLockable;
            IsWithinLockingRange = isWithinLockingRange;
            IsValidAttackTarget = isValidAttackTarget;
            LockState = lockState;
            IsPrimaryLock = isPrimaryLock;
            IsVisible = isVisible;
            Position = position;
        }

        public long Eid { get; }
        public int Definition { get; }
        public string DefinitionName { get; }
        public double Distance { get; }
        public bool IsLockable { get; }
        public bool IsWithinLockingRange { get; }
        public bool IsValidAttackTarget { get; }
        public string LockState { get; }
        public bool IsPrimaryLock { get; }
        public bool IsVisible { get; }
        public Position? Position { get; }
    }

    public sealed class MentorTrainingRewardCatalog
    {
        public MentorTrainingRewardCatalog(
            int baseNic,
            int nicPerCompletedLevel,
            int maximumRewardLevel,
            IReadOnlyList<MentorTrainingRewardSnapshot> rewards)
        {
            BaseNic = baseNic;
            NicPerCompletedLevel = nicPerCompletedLevel;
            MaximumRewardLevel = maximumRewardLevel;
            Rewards = rewards ?? throw new ArgumentNullException(nameof(rewards));
        }

        public int BaseNic { get; }
        public int NicPerCompletedLevel { get; }
        public int MaximumRewardLevel { get; }
        public IReadOnlyList<MentorTrainingRewardSnapshot> Rewards { get; }
    }

    public sealed class MentorTrainingRewardSnapshot
    {
        public MentorTrainingRewardSnapshot(
            int level,
            int rewardTrackId,
            int definition,
            string itemName,
            int quantity,
            int robotDefinition,
            string robotName,
            string robotTemplateName)
        {
            Level = level;
            RewardTrackId = rewardTrackId;
            Definition = definition;
            ItemName = itemName;
            Quantity = quantity;
            RobotDefinition = robotDefinition;
            RobotName = robotName;
            RobotTemplateName = robotTemplateName;
        }

        public int Level { get; }
        public int RewardTrackId { get; }
        public int Definition { get; }
        public string ItemName { get; }
        public int Quantity { get; }
        public int RobotDefinition { get; }
        public string RobotName { get; }
        public string RobotTemplateName { get; }
    }

    public sealed class MentorMissionState
    {
        public MentorMissionState(IReadOnlyList<MentorMissionSnapshot> missions)
        {
            Missions = missions ?? throw new ArgumentNullException(nameof(missions));
        }

        public IReadOnlyList<MentorMissionSnapshot> Missions { get; }
    }

    public sealed class MentorMissionSnapshot
    {
        public MentorMissionSnapshot(
            int missionId,
            Guid missionGuid,
            string title,
            string category,
            int level,
            DateTime startedAtUtc,
            DateTime expiresAtUtc,
            IReadOnlyList<MentorObjectiveSnapshot> objectives)
        {
            MissionId = missionId;
            MissionGuid = missionGuid;
            Title = title;
            Category = category;
            Level = level;
            StartedAtUtc = startedAtUtc;
            ExpiresAtUtc = expiresAtUtc;
            Objectives = objectives ?? throw new ArgumentNullException(nameof(objectives));
        }

        public int MissionId { get; }
        public Guid MissionGuid { get; }
        public string Title { get; }
        public string Category { get; }
        public int Level { get; }
        public DateTime StartedAtUtc { get; }
        public DateTime ExpiresAtUtc { get; }
        public IReadOnlyList<MentorObjectiveSnapshot> Objectives { get; }
    }

    public sealed class MentorObjectiveSnapshot
    {
        public MentorObjectiveSnapshot(
            int targetId,
            string type,
            bool isActive,
            bool isCompleted,
            bool isOptional,
            int progress,
            int required,
            int definition,
            string definitionName,
            int? zoneId,
            Position? position,
            int range,
            string instruction)
        {
            TargetId = targetId;
            Type = type;
            IsActive = isActive;
            IsCompleted = isCompleted;
            IsOptional = isOptional;
            Progress = progress;
            Required = required;
            Definition = definition;
            DefinitionName = definitionName;
            ZoneId = zoneId;
            Position = position;
            Range = range;
            Instruction = instruction;
        }

        public int TargetId { get; }
        public string Type { get; }
        public bool IsActive { get; }
        public bool IsCompleted { get; }
        public bool IsOptional { get; }
        public int Progress { get; }
        public int Required { get; }
        public int Definition { get; }
        public string DefinitionName { get; }
        public int? ZoneId { get; }
        public Position? Position { get; }
        public int Range { get; }
        public string Instruction { get; }
    }

    public sealed class MentorEntityMatch
    {
        public MentorEntityMatch(
            int definition,
            string definitionName,
            string displayName,
            string descriptionToken,
            long categoryFlags,
            string description = null)
        {
            Definition = definition;
            DefinitionName = definitionName;
            DisplayName = displayName;
            DescriptionToken = descriptionToken;
            CategoryFlags = categoryFlags;
            Description = description;
        }

        public int Definition { get; }
        public string DefinitionName { get; }
        public string DisplayName { get; }
        public string DescriptionToken { get; }
        public long CategoryFlags { get; }
        public string Description { get; }
    }

    public sealed class MentorProviderRequest
    {
        public MentorProviderRequest(MentorRequest request, MentorPlayerContext playerContext)
        {
            Request = request ?? throw new ArgumentNullException(nameof(request));
            PlayerContext = playerContext ?? throw new ArgumentNullException(nameof(playerContext));
        }

        public MentorRequest Request { get; }
        public MentorPlayerContext PlayerContext { get; }
    }

    public sealed class MentorResponse
    {
        public MentorResponse(Guid requestId, int characterId, string channel, string text)
        {
            RequestId = requestId;
            CharacterId = characterId;
            Channel = channel;
            Text = text;
        }

        public Guid RequestId { get; }
        public int CharacterId { get; }
        public string Channel { get; }
        public string Text { get; }
    }

    public enum MentorSubmissionStatus
    {
        Accepted,
        Invalid,
        RateLimited,
        QueueFull,
        Stopping
    }

    public interface IMentorChatIngress
    {
        bool TryHandle(string channelName, int characterId, int accountId, string message);
    }

    public interface IMentorRequestDispatcher
    {
        MentorSubmissionStatus Submit(MentorRequest request);
    }

    public interface IMentorProvider
    {
        Task<string> GetResponseAsync(MentorProviderRequest request, CancellationToken cancellationToken);
    }

    public interface IMentorPlayerContextReader
    {
        MentorPlayerContext Read(MentorRequest request);
    }

    public interface IMentorMissionStateReader
    {
        MentorMissionState Read(MentorRequest request);
    }

    public interface IMentorTrainingRewardReader
    {
        MentorTrainingRewardCatalog Read();
    }

    public interface IMentorTextCatalog
    {
        string DisplayName(string definitionName);
        string Description(string descriptionToken);
    }

    public interface IMentorEntityResolver
    {
        IReadOnlyList<MentorEntityMatch> Resolve(string query, int maximumResults = 5);
    }

    public interface IMentorResponseSink
    {
        void Send(MentorResponse response);
    }

    public interface IMentorConversationSink
    {
        void EchoQuestion(int characterId, string channel, string message);
    }

    public interface IMentorClock
    {
        DateTime UtcNow { get; }
    }

    public static class MentorMessages
    {
        public const string EmptyQuestion = "Please enter a question for the mentor.";
        public const string QuestionTooLong = "That question is too long. Please shorten it and try again.";
        public const string RateLimited = "You're asking a little too quickly. Try again in a moment.";
        public const string QueueFull = "Mentor is busy right now. Try again in a moment.";
        public const string TimedOut = "Mentor didn't respond in time. Try again in a moment.";
        public const string ContextUnavailable = "I couldn't verify your current player context against the server.";
        public const string Unavailable = "Mentor service is currently unavailable.";
    }
}
