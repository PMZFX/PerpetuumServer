using System;
using System.Collections.Generic;
using System.Linq;

namespace Perpetuum.Services.Mentoring
{
    public enum MentorQuestionCategory
    {
        General,
        Location,
        Travel,
        Tutorial,
        Mission,
        Combat,
        Controls,
        Entity,
        Fitting,
        Mining,
        Industry,
        Market,
        Extensions,
        Security
    }

    public sealed class MentorEvidencePlan
    {
        public MentorEvidencePlan(
            MentorQuestionCategory category,
            bool needsMissionState,
            bool needsZoneCombat,
            bool needsTrainingRewards,
            bool needsEntityResolution,
            bool needsKnowledge)
        {
            Category = category;
            NeedsMissionState = needsMissionState;
            NeedsZoneCombat = needsZoneCombat;
            NeedsTrainingRewards = needsTrainingRewards;
            NeedsEntityResolution = needsEntityResolution;
            NeedsKnowledge = needsKnowledge;
        }

        public MentorQuestionCategory Category { get; }
        public bool NeedsPlayerContext => true;
        public bool NeedsMissionState { get; }
        public bool NeedsZoneCombat { get; }
        public bool NeedsTrainingRewards { get; }
        public bool NeedsEntityResolution { get; }
        public bool NeedsKnowledge { get; }
    }

    /// <summary>
    /// Classifies broad player intent and selects bounded evidence domains. It never supplies an
    /// answer phrase; authoritative readers and the model remain responsible for facts and wording.
    /// </summary>
    public static class MentorQuestionEvidencePlanner
    {
        private static readonly string[] TrainingSubjects =
        {
            "tutorial", "training", "rookie", "checklist", "onboarding", "certification"
        };

        private static readonly string[] RewardSubjects =
        {
            "reward", "worth", "finish", "complete", "leave", "exit", "skip", "graduate"
        };

        public static MentorEvidencePlan Plan(
            string question,
            bool recentConversationWasAboutTraining = false)
        {
            string normalized = MentorNameFormatter.Normalize(question);
            var tokens = new HashSet<string>(
                normalized.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries),
                StringComparer.Ordinal);

            MentorQuestionCategory category = Classify(normalized, tokens);
            bool trainingRewards = NeedsTrainingRewards(
                question,
                recentConversationWasAboutTraining);
            return new MentorEvidencePlan(
                category,
                category == MentorQuestionCategory.Mission ||
                category == MentorQuestionCategory.Tutorial ||
                category == MentorQuestionCategory.General,
                category == MentorQuestionCategory.Combat,
                trainingRewards,
                category == MentorQuestionCategory.Entity ||
                category == MentorQuestionCategory.Fitting ||
                category == MentorQuestionCategory.Industry ||
                category == MentorQuestionCategory.Market,
                category != MentorQuestionCategory.Location &&
                category != MentorQuestionCategory.Security);
        }

        public static bool NeedsTrainingRewards(
            string question,
            bool recentConversationWasAboutTraining = false)
        {
            string normalized = MentorNameFormatter.Normalize(question);
            string[] tokens = normalized.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            bool mentionsTraining = ContainsAny(tokens, TrainingSubjects);
            bool mentionsOutcome = ContainsAny(tokens, RewardSubjects);

            // Keep mission/industry reward questions separate, but preserve short tutorial
            // follow-ups such as "What are the potential rewards, though?".
            return mentionsOutcome && (mentionsTraining || recentConversationWasAboutTraining);
        }

        public static bool MentionsTrainingSubject(string question)
        {
            string normalized = MentorNameFormatter.Normalize(question);
            string[] tokens = normalized.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return ContainsAny(tokens, TrainingSubjects);
        }

        private static MentorQuestionCategory Classify(
            string normalized,
            IReadOnlyCollection<string> tokens)
        {
            bool privateSubject = ContainsAny(tokens, new[]
            {
                "inventory", "account", "character", "state", "data", "location"
            });
            bool anotherSubject = tokens.Contains("someone") || tokens.Contains("another");
            if (ContainsAny(tokens, new[]
                {
                    "password", "credential", "email", "admin", "database", "sql", "prompt", "secret"
                }) || (privateSubject && anotherSubject) || normalized.Contains("ignore your rules"))
            {
                return MentorQuestionCategory.Security;
            }

            bool extensionSubject = ContainsAny(tokens, new[] { "extension", "extensions", "ep", "skill" });
            bool explicitTutorialSubject = ContainsAny(tokens, new[]
            {
                "tutorial", "rookie", "checklist", "onboarding", "certification"
            });
            if (explicitTutorialSubject || (tokens.Contains("training") && !extensionSubject))
                return MentorQuestionCategory.Tutorial;

            if (IsExactLocationQuestion(normalized) ||
                normalized.StartsWith("where am i ", StringComparison.Ordinal) ||
                ((tokens.Contains("location") || tokens.Contains("coordinates")) &&
                 (tokens.Contains("my") || tokens.Contains("current"))))
                return MentorQuestionCategory.Location;

            if (ContainsAny(tokens, new[] { "mission", "objective", "contract", "assignment", "task" }))
                return MentorQuestionCategory.Mission;

            if (extensionSubject || tokens.Contains("train"))
                return MentorQuestionCategory.Extensions;

            if (ContainsAny(tokens, new[] { "market", "buy", "sell", "price", "order", "offer", "cheap" }))
                return MentorQuestionCategory.Market;

            if (ContainsAny(tokens, new[]
                {
                    "mine", "mining", "ore", "deposit", "harvest", "harvesting", "mineral", "scanner"
                }))
            {
                return MentorQuestionCategory.Mining;
            }

            if (ContainsAny(tokens, new[] { "fit", "fitting", "equip", "slot", "chassis", "head", "leg" }))
                return MentorQuestionCategory.Fitting;

            if (ContainsAny(tokens, new[]
                {
                    "manufacture", "manufacturing", "production", "produce", "recipe", "component",
                    "factory", "facility", "material"
                }) || (tokens.Contains("build") && !tokens.Contains("building")))
            {
                return MentorQuestionCategory.Industry;
            }

            if (ContainsAny(tokens, new[] { "hotkey", "keybind", "shortcut", "binding", "keyboard", "mouse" }) ||
                normalized.Contains("what key") || normalized.Contains("which key"))
            {
                return MentorQuestionCategory.Controls;
            }

            if (ContainsAny(tokens, new[]
                {
                    "fight", "attack", "weapon", "gun", "enemy", "enemies", "drone", "target",
                    "lock", "ammo", "damage", "range", "armor", "shield", "death", "kill"
                }))
            {
                return MentorQuestionCategory.Combat;
            }

            if (ContainsAny(tokens, new[]
                {
                    "route", "teleport", "travel", "destination", "nearest", "terminal", "base",
                    "island", "zone", "refinery"
                }) || normalized.StartsWith("how do i get ", StringComparison.Ordinal) ||
                normalized.StartsWith("where can i find ", StringComparison.Ordinal))
            {
                return MentorQuestionCategory.Travel;
            }

            if (normalized.StartsWith("what is ", StringComparison.Ordinal) ||
                normalized.StartsWith("what s ", StringComparison.Ordinal) ||
                normalized.StartsWith("tell me about ", StringComparison.Ordinal) ||
                normalized.StartsWith("identify ", StringComparison.Ordinal))
            {
                return MentorQuestionCategory.Entity;
            }

            return MentorQuestionCategory.General;
        }

        private static bool IsExactLocationQuestion(string normalized)
        {
            return normalized == "where am i" ||
                   normalized == "where am i right now" ||
                   normalized == "what is my location" ||
                   normalized == "what s my location";
        }

        private static bool ContainsAny(IEnumerable<string> tokens, IEnumerable<string> roots)
        {
            return tokens.Any(token => roots.Any(root =>
                token == root ||
                (root.Length >= 4 && token.StartsWith(root, StringComparison.Ordinal))));
        }
    }
}
