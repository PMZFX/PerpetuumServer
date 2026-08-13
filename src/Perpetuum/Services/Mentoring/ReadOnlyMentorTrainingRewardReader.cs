using System;
using System.Linq;
using Perpetuum.EntityFramework;
using Perpetuum.Items.Templates;
using Perpetuum.Zones.Training.Reward;

namespace Perpetuum.Services.Mentoring
{
    /// <summary>
    /// Projects the existing training-exit reward table into immutable mentor facts. It uses the
    /// normal cached repository and cannot mutate rewards, characters, or checklist progress.
    /// </summary>
    public sealed class ReadOnlyMentorTrainingRewardReader : IMentorTrainingRewardReader
    {
        private const int MaximumRewardLevel = 4;
        private readonly ITrainingRewardRepository _rewardRepository;
        private readonly GlobalConfiguration _configuration;
        private readonly IMentorTextCatalog _textCatalog;

        public ReadOnlyMentorTrainingRewardReader(
            ITrainingRewardRepository rewardRepository,
            GlobalConfiguration configuration,
            IMentorTextCatalog textCatalog)
        {
            _rewardRepository = rewardRepository ?? throw new ArgumentNullException(nameof(rewardRepository));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _textCatalog = textCatalog ?? throw new ArgumentNullException(nameof(textCatalog));
        }

        public MentorTrainingRewardCatalog Read()
        {
            MentorTrainingRewardSnapshot[] rewards = _rewardRepository.GetAllRewards()
                .Where(reward => reward != null &&
                                 reward.Level >= 0 &&
                                 reward.Level <= MaximumRewardLevel)
                .Select(ToSnapshot)
                .Where(reward => reward != null)
                .OrderBy(reward => reward.RewardTrackId)
                .ThenBy(reward => reward.Level)
                .ThenBy(reward => reward.ItemName ?? reward.RobotName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return new MentorTrainingRewardCatalog(
                _configuration.StartCredit,
                _configuration.LevelCredit,
                MaximumRewardLevel,
                rewards);
        }

        private MentorTrainingRewardSnapshot ToSnapshot(TrainingReward reward)
        {
            int definition = reward.Item.Definition;
            EntityDefault item = definition > 0 ? reward.Item.EntityDefault : EntityDefault.None;
            RobotTemplate template = reward.RobotTemplate;
            EntityDefault robot = template?.EntityDefault ?? EntityDefault.None;

            if (item == EntityDefault.None && robot == EntityDefault.None)
                return null;

            return new MentorTrainingRewardSnapshot(
                reward.Level,
                reward.RaceId,
                item == EntityDefault.None ? 0 : item.Definition,
                item == EntityDefault.None ? null : _textCatalog.DisplayName(item.Name),
                item == EntityDefault.None ? 0 : reward.Item.Quantity,
                robot == EntityDefault.None ? 0 : robot.Definition,
                robot == EntityDefault.None ? null : _textCatalog.DisplayName(robot.Name),
                template?.Name);
        }
    }
}
