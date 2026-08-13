using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Perpetuum.Log;

namespace Perpetuum.Services.Mentoring
{
    public sealed class DiagnosticMentorProvider : IMentorProvider
    {
        private readonly IMentorMissionStateReader _missionStateReader;
        private readonly IMentorEntityResolver _entityResolver;
        private readonly IMentorKnowledgeBase _knowledgeBase;

        public DiagnosticMentorProvider(
            IMentorMissionStateReader missionStateReader,
            IMentorEntityResolver entityResolver,
            IMentorKnowledgeBase knowledgeBase)
        {
            _missionStateReader = missionStateReader ??
                throw new ArgumentNullException(nameof(missionStateReader));
            _entityResolver = entityResolver ?? throw new ArgumentNullException(nameof(entityResolver));
            _knowledgeBase = knowledgeBase ?? throw new ArgumentNullException(nameof(knowledgeBase));
        }

        public Task<string> GetResponseAsync(
            MentorProviderRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            cancellationToken.ThrowIfCancellationRequested();
            string normalized = MentorNameFormatter.Normalize(request.Request.Message);

            if (IsHelpRequest(normalized))
                return Answer(request.Request, "help", "none", "none", Help());
            if (IsLocationRequest(normalized))
                return Answer(
                    request.Request,
                    "player_location",
                    "get_player_context",
                    "live_player_state",
                    DescribeLocation(request.PlayerContext));
            if (IsRobotRequest(normalized))
                return Answer(
                    request.Request,
                    "active_robot",
                    "get_player_context",
                    "live_player_state",
                    DescribeRobot(request.PlayerContext));
            if (IsObjectiveRequest(normalized))
                return Answer(
                    request.Request,
                    "current_objective",
                    "get_mission_state",
                    "live_mission_state",
                    DescribeCurrentObjective(_missionStateReader.Read(request.Request)));
            if (IsMissionRequest(normalized))
                return Answer(
                    request.Request,
                    "active_mission",
                    "get_mission_state",
                    "live_mission_state",
                    DescribeMissions(_missionStateReader.Read(request.Request)));

            string purchaseQuery = ExtractPurchaseEntityQuery(normalized);
            if (purchaseQuery != null)
                return Answer(
                    request.Request,
                    "entity_purchase",
                    "resolve_entity",
                    "live_entity_definitions",
                    DescribePurchase(purchaseQuery));

            IReadOnlyList<MentorKnowledgeMatch> knowledge = _knowledgeBase.Search(normalized);
            if (knowledge.Count > 0)
            {
                MentorKnowledgeDocument document = knowledge[0].Document;
                return Answer(
                    request.Request,
                    "knowledge",
                    "knowledge_search",
                    $"{document.Id}@{document.Version}",
                    DescribeKnowledge(knowledge[0]));
            }

            string entityQuery = ExtractEntityQuery(normalized);
            if (entityQuery != null)
                return Answer(
                    request.Request,
                    "entity",
                    "resolve_entity",
                    "live_entity_definitions",
                    DescribeEntity(entityQuery));

            return Answer(
                request.Request,
                "unknown",
                "none",
                "none",
                "I couldn't verify an answer from the current server tools. I can check your location, " +
                "active robot, mission or objective, explain common game concepts, and resolve named items or robots.");
        }

        private static Task<string> Answer(
            MentorRequest request,
            string category,
            string toolsCalled,
            string sources,
            string text)
        {
            Logger.Info(
                $"mentor request_id={request.RequestId} character_id={request.CharacterId} " +
                $"category={category} tools_called={toolsCalled} retrieval_sources={sources}");
            return Task.FromResult(text);
        }

        private static string DescribeKnowledge(MentorKnowledgeMatch match)
        {
            MentorKnowledgeDocument document = match.Document;
            string provenance;
            switch (document.Source)
            {
                case MentorKnowledgeSource.CuratedCurrent:
                    provenance = "Current mentor guide";
                    break;
                case MentorKnowledgeSource.GeneratedCurrent:
                    provenance = "Current generated server guide";
                    break;
                default:
                    provenance = "Historical reference; verify against current server data";
                    break;
            }

            return $"{document.Content} [{provenance}: {document.Title}, v{document.Version}]";
        }

        private string DescribeEntity(string query)
        {
            IReadOnlyList<MentorEntityMatch> matches = _entityResolver.Resolve(query);
            if (matches.Count == 0)
                return $"I couldn't find a current server definition matching \"{query}\".";

            MentorEntityMatch best = matches[0];
            string answer = $"The best current-server match is {best.DisplayName} " +
                $"({best.DefinitionName}, definition {best.Definition}).";

            if (matches.Count > 1)
            {
                string related = string.Join(", ", matches.Skip(1).Take(3).Select(match => match.DisplayName));
                answer += $" Related matches: {related}.";
            }

            return answer;
        }

        private string DescribePurchase(string query)
        {
            IReadOnlyList<MentorEntityMatch> matches = _entityResolver.Resolve(query);
            if (matches.Count == 0)
                return $"I couldn't find a current server item matching \"{query}\", so I can't verify a place to buy it.";

            MentorEntityMatch best = matches[0];
            return $"I found {best.DisplayName} ({best.DefinitionName}, definition {best.Definition}), " +
                   "but I don't yet have a live market-offer query, so I can't verify where it is currently for sale.";
        }

        private static string DescribeLocation(MentorPlayerContext context)
        {
            string zone = string.IsNullOrWhiteSpace(context.ZoneName)
                ? context.ZoneId.HasValue ? $"zone {context.ZoneId.Value}" : "an unknown zone"
                : context.ZoneName;
            string position = context.Position.HasValue
                ? $" near ({Math.Round(context.Position.Value.X)}, {Math.Round(context.Position.Value.Y)})"
                : string.Empty;

            if (context.IsDocked)
            {
                string dockingBase = string.IsNullOrWhiteSpace(context.DockingBaseName)
                    ? context.DockingBaseEid > 0 ? $"base {context.DockingBaseEid}" : "a base"
                    : MentorNameFormatter.ToDisplayName(context.DockingBaseName);
                return $"You're docked at {dockingBase} in {zone}{position}.";
            }

            return $"You're undocked in {zone}{position}.";
        }

        private static string DescribeRobot(MentorPlayerContext context)
        {
            if (context.ActiveRobotEid <= 0)
                return "You do not currently have an active robot selected.";
            if (string.IsNullOrWhiteSpace(context.ActiveRobotName))
                return $"Your active robot is entity {context.ActiveRobotEid}, but I couldn't verify its definition.";

            return $"Your active robot is {MentorNameFormatter.ToDisplayName(context.ActiveRobotName)} " +
                $"({context.ActiveRobotName}, definition {context.ActiveRobotDefinition}).";
        }

        private static string DescribeMissions(MentorMissionState state)
        {
            if (state.Missions.Count == 0)
                return "You do not currently have an active mission.";

            if (state.Missions.Count == 1)
            {
                MentorMissionSnapshot mission = state.Missions[0];
                return $"Your active mission is {MissionTitle(mission)}. {ObjectiveSummary(mission)}";
            }

            string titles = string.Join(", ", state.Missions.Take(3).Select(MissionTitle));
            string suffix = state.Missions.Count > 3 ? $", plus {state.Missions.Count - 3} more" : string.Empty;
            return $"You have {state.Missions.Count} active missions: {titles}{suffix}.";
        }

        private static string DescribeCurrentObjective(MentorMissionState state)
        {
            if (state.Missions.Count == 0)
                return "You do not currently have an active mission or objective.";

            MentorMissionSnapshot mission = state.Missions
                .FirstOrDefault(candidate => candidate.Objectives.Any(objective => objective.IsActive)) ??
                state.Missions[0];
            return $"For {MissionTitle(mission)}, {ObjectiveSummary(mission)}";
        }

        private static string ObjectiveSummary(MentorMissionSnapshot mission)
        {
            MentorObjectiveSnapshot[] active = mission.Objectives
                .Where(objective => objective.IsActive)
                .ToArray();
            if (active.Length == 0)
                return "I couldn't identify an unfinished current objective from the server state.";

            string summary = string.Join(" Also, ", active.Take(2).Select(DescribeObjective));
            if (active.Length > 2)
                summary += $" There are {active.Length - 2} additional active objectives.";
            return summary;
        }

        private static string DescribeObjective(MentorObjectiveSnapshot objective)
        {
            string item = string.IsNullOrWhiteSpace(objective.DefinitionName)
                ? null
                : MentorNameFormatter.ToDisplayName(objective.DefinitionName);
            string quantity = objective.Required > 1 ? objective.Required + " " : string.Empty;
            string action;

            switch (objective.Type)
            {
                case "kill_definition":
                case "rnd_kill_definition":
                    action = $"destroy {quantity}{item ?? "the required targets"}";
                    break;
                case "lock_unit":
                case "rnd_lock_unit":
                    action = $"lock {item ?? "the required unit"}";
                    break;
                case "reach_position":
                case "rnd_point":
                    action = "reach the marked objective location";
                    break;
                case "dock_in":
                    action = "dock at the required base";
                    break;
                case "teleport":
                    action = "use the required teleport";
                    break;
                case "scan_mineral":
                case "rnd_scan_mineral":
                    action = $"scan for {item ?? "the required mineral"}";
                    break;
                case "drill_mineral":
                case "rnd_drill_mineral":
                    action = $"mine {quantity}{item ?? "the required mineral"}";
                    break;
                case "harvest_plant":
                case "rnd_harvest_plant":
                    action = $"harvest {quantity}{item ?? "the required material"}";
                    break;
                case "fetch_item":
                case "loot_item":
                case "rnd_fetch_item":
                case "rnd_loot_definition":
                    action = $"collect {quantity}{item ?? "the required item"}";
                    break;
                case "use_itemsupply":
                case "rnd_use_itemsupply":
                    action = $"activate the marked Item Supply and remain still until " +
                             $"{item ?? "the required item"} is loaded into cargo";
                    break;
                case "submit_item":
                case "rnd_submit_item":
                    action = $"open the marked submission point and move " +
                             $"{quantity}{item ?? "the required item"} from cargo into Submit items";
                    break;
                case "massproduce":
                case "rnd_massproduce":
                    action = $"manufacture {quantity}{item ?? "the required item"}";
                    break;
                case "research":
                case "rnd_research":
                    action = $"research {item ?? "the required item"}";
                    break;
                case "find_artifact":
                case "rnd_find_artifact":
                    action = "find the required artifact";
                    break;
                case "use_switch":
                case "rnd_use_switch":
                    action = "activate the required switch";
                    break;
                default:
                    action = MentorNameFormatter.ToDisplayName(objective.Type);
                    break;
            }

            if (objective.Required > 1 && objective.Progress > 0)
                action += $" ({objective.Progress}/{objective.Required} complete)";
            if (objective.Position.HasValue)
            {
                string zone = objective.ZoneId.HasValue ? $" in zone {objective.ZoneId.Value}" : string.Empty;
                action += $"{zone} near ({Math.Round(objective.Position.Value.X)}, {Math.Round(objective.Position.Value.Y)})";
            }

            return char.ToUpperInvariant(action[0]) + action.Substring(1) + ".";
        }

        private static string MissionTitle(MentorMissionSnapshot mission)
        {
            return string.IsNullOrWhiteSpace(mission.Title)
                ? $"mission {mission.MissionId}"
                : $"{mission.Title} (mission {mission.MissionId})";
        }

        private static bool IsHelpRequest(string message)
        {
            return message == "mentor help" || message == "help mentor" || message == "help";
        }

        private static bool IsLocationRequest(string message)
        {
            return message.Contains("where am i") ||
                   message.Contains("my location") ||
                   message.Contains("current location") ||
                   message.Contains("what zone am i");
        }

        private static bool IsRobotRequest(string message)
        {
            return message.Contains("what robot") ||
                   message.Contains("which robot") ||
                   message.Contains("active robot") ||
                   message.Contains("robot am i using");
        }

        private static bool IsObjectiveRequest(string message)
        {
            return message.Contains("current objective") ||
                   message.Contains("what am i supposed to do") ||
                   message.Contains("what should i do") ||
                   message.Contains("what do i do next") ||
                   message == "objective";
        }

        private static bool IsMissionRequest(string message)
        {
            return message.Contains("what mission") ||
                   message.Contains("which mission") ||
                   message.Contains("active mission") ||
                   message == "mission";
        }

        private static string ExtractEntityQuery(string message)
        {
            string[] prefixes = { "what is ", "what s ", "tell me about ", "identify " };
            string prefix = prefixes.FirstOrDefault(message.StartsWith);
            if (prefix == null)
                return null;

            string query = message.Substring(prefix.Length).Trim();
            if (query.StartsWith("a ", StringComparison.Ordinal))
                query = query.Substring(2);
            else if (query.StartsWith("an ", StringComparison.Ordinal))
                query = query.Substring(3);
            else if (query.StartsWith("the ", StringComparison.Ordinal))
                query = query.Substring(4);

            string[] ambiguous = { "this", "that", "this item", "that item", "this robot", "that robot" };
            return query.Length == 0 || ambiguous.Contains(query) ? null : query;
        }

        private static string ExtractPurchaseEntityQuery(string message)
        {
            string[] prefixes = { "where can i buy ", "where do i buy ", "how do i buy " };
            string prefix = prefixes.FirstOrDefault(message.StartsWith);
            if (prefix == null)
                return null;

            string query = message.Substring(prefix.Length).Trim();
            if (query.StartsWith("a ", StringComparison.Ordinal))
                query = query.Substring(2);
            else if (query.StartsWith("an ", StringComparison.Ordinal))
                query = query.Substring(3);
            else if (query.StartsWith("the ", StringComparison.Ordinal))
                query = query.Substring(4);

            return query.Length == 0 ? null : query;
        }

        private static string Help()
        {
            return "Ask where you are, which robot is active, what mission or objective you have, " +
                   "why a basic action may not work, or about a named item or robot. Examples: " +
                   "What should I do next? Why won't my weapon fire? What is Mesmer?";
        }
    }
}
