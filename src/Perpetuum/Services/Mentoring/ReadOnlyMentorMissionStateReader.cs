using System;
using System.Collections.Generic;
using System.Linq;
using Perpetuum.Accounting.Characters;
using Perpetuum.EntityFramework;
using Perpetuum.Services.MissionEngine.AdministratorObjects;
using Perpetuum.Services.MissionEngine.Missions;
using Perpetuum.Services.MissionEngine.MissionProcessorObjects;
using Perpetuum.Services.MissionEngine.MissionTargets;
using Perpetuum.Services.Sessions;

namespace Perpetuum.Services.Mentoring
{
    public sealed class ReadOnlyMentorMissionStateReader : IMentorMissionStateReader
    {
        private readonly ISessionManager _sessionManager;
        private readonly Lazy<MissionProcessor> _missionProcessor;
        private readonly Lazy<IEntityDefaultReader> _entityDefaultReader;
        private readonly IMentorTextCatalog _textCatalog;

        public ReadOnlyMentorMissionStateReader(
            ISessionManager sessionManager,
            Lazy<MissionProcessor> missionProcessor,
            Lazy<IEntityDefaultReader> entityDefaultReader,
            IMentorTextCatalog textCatalog)
        {
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            _missionProcessor = missionProcessor ?? throw new ArgumentNullException(nameof(missionProcessor));
            _entityDefaultReader = entityDefaultReader ??
                throw new ArgumentNullException(nameof(entityDefaultReader));
            _textCatalog = textCatalog ?? throw new ArgumentNullException(nameof(textCatalog));
        }

        public MentorMissionState Read(MentorRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            ISession session = _sessionManager.GetByCharacter(request.CharacterId);
            if (session == null || session.Character == null || session.Character.Id != request.CharacterId)
                throw new InvalidOperationException("The requesting character is no longer online.");
            if (session.AccountId != request.AccountId)
                throw new InvalidOperationException("The request is not scoped to the active character session.");

            Character character = session.Character;
            if (!_missionProcessor.Value.MissionAdministrator.GetMissionInProgressCollector(
                    character,
                    out MissionInProgressCollector collector))
            {
                return new MentorMissionState(Array.Empty<MentorMissionSnapshot>());
            }

            MentorMissionSnapshot[] missions = collector.GetMissionsInProgress()
                .OrderByDescending(mission => IsTrainingMission(mission.myMission.missionCategory.ToString()))
                .ThenByDescending(mission => mission.started)
                .ThenBy(mission => mission.MissionId)
                .Select(CreateSnapshot)
                .ToArray();

            return new MentorMissionState(missions);
        }

        private MentorMissionSnapshot CreateSnapshot(MissionInProgress mission)
        {
            lock (mission.lockObject)
            {
                var objectives = new List<MentorObjectiveSnapshot>();
                foreach (MissionTarget target in mission.myMission.Targets.OrderBy(item => item.displayOrder))
                {
                    if (!mission.GetTargetInProgress(target.id, out MissionTargetInProgress progress))
                        continue;

                    EntityDefault definition = target.ValidDefinitionSet
                        ? _entityDefaultReader.Value.Get(target.Definition)
                        : EntityDefault.None;
                    string definitionName = definition == EntityDefault.None ? null : definition.Name;

                    objectives.Add(new MentorObjectiveSnapshot(
                        target.id,
                        target.Type.ToString(),
                        progress.IsMyTurn && !progress.completed,
                        progress.completed,
                        target.isOptional,
                        progress.progressCount,
                        target.ValidQuantitySet ? target.Quantity : 0,
                        target.ValidDefinitionSet ? target.Definition : 0,
                        definitionName,
                        target.ValidZoneSet ? target.ZoneId : (int?)null,
                        target.ValidPositionSet ? target.targetPosition : (Position?)null,
                        target.ValidRangeSet ? target.TargetPositionRange : 0,
                        ResolveInstruction(target)));
                }

                return new MentorMissionSnapshot(
                    mission.MissionId,
                    mission.missionGuid,
                    mission.myMission.title,
                    mission.myMission.missionCategory.ToString(),
                    mission.MissionLevel,
                    mission.started,
                    mission.expire,
                    objectives);
            }
        }

        private string ResolveInstruction(MissionTarget target)
        {
            string description = _textCatalog.Description(target.DescriptionToken);
            if (!string.IsNullOrWhiteSpace(description))
                return description;

            string activated = _textCatalog.Description(target.activatedMessage);
            return string.IsNullOrWhiteSpace(activated) ? target.activatedMessage : activated;
        }

        private static bool IsTrainingMission(string category)
        {
            return category?.IndexOf("training", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
