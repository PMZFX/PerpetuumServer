using System;
using System.Collections.Generic;
using System.Linq;
using Perpetuum.Accounting;
using Perpetuum.Common.Loggers.Transaction;
using Perpetuum.Data;
using Perpetuum.Services.ExtensionService;

namespace Perpetuum.Services.Actions
{
    public sealed class ExtensionTrainingAction
    {
        public ExtensionTrainingAction(int extensionId)
        {
            ExtensionId = extensionId;
        }

        public int ExtensionId { get; }
    }

    public sealed class ExtensionTrainingQuote
    {
        public ExtensionTrainingQuote(
            int extensionId,
            int currentLevel,
            int nextLevel,
            int extensionPointCost,
            int availableExtensionPoints,
            double creditCost,
            double walletBalance,
            bool trainingCharacter)
        {
            ExtensionId = extensionId;
            CurrentLevel = currentLevel;
            NextLevel = nextLevel;
            ExtensionPointCost = extensionPointCost;
            AvailableExtensionPoints = availableExtensionPoints;
            CreditCost = creditCost;
            WalletBalance = walletBalance;
            TrainingCharacter = trainingCharacter;
        }

        public int ExtensionId { get; }
        public int CurrentLevel { get; }
        public int NextLevel { get; }
        public int ExtensionPointCost { get; }
        public int AvailableExtensionPoints { get; }
        public double CreditCost { get; }
        public double WalletBalance { get; }
        public bool TrainingCharacter { get; }
        public bool CanAffordPoints => TrainingCharacter || AvailableExtensionPoints >= ExtensionPointCost;
        public bool CanAffordCredits => WalletBalance >= CreditCost;
    }

    public sealed class ExtensionTrainingResult
    {
        public ExtensionTrainingResult(ExtensionTrainingQuote quote, IDictionary<string, object> payload)
        {
            Quote = quote ?? throw new ArgumentNullException(nameof(quote));
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        }

        public ExtensionTrainingQuote Quote { get; }
        public IDictionary<string, object> Payload { get; }
    }

    public interface IExtensionTrainingActionService
    {
        ExtensionTrainingQuote Quote(GameActionContext context, ExtensionTrainingAction action);
        ExtensionTrainingResult Execute(GameActionContext context, ExtensionTrainingAction action);
    }

    /// <summary>
    /// Character-bound extension training shared by client and autonomous callers.
    /// Administrative exceptions are derived from the actor's account access;
    /// GameActionSource remains audit metadata only.
    /// </summary>
    public sealed class ExtensionTrainingActionService : IExtensionTrainingActionService
    {
        private readonly IAccountManager _accountManager;
        private readonly ExtensionPoints _extensionPoints;
        private readonly IExtensionReader _extensionReader;
        private readonly IGameActionAudit _audit;

        public ExtensionTrainingActionService(
            IAccountManager accountManager,
            ExtensionPoints extensionPoints,
            IExtensionReader extensionReader,
            IGameActionAudit audit)
        {
            _accountManager = accountManager ?? throw new ArgumentNullException(nameof(accountManager));
            _extensionPoints = extensionPoints ?? throw new ArgumentNullException(nameof(extensionPoints));
            _extensionReader = extensionReader ?? throw new ArgumentNullException(nameof(extensionReader));
            _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        }

        public ExtensionTrainingQuote Quote(GameActionContext context, ExtensionTrainingAction action)
        {
            return _audit.Execute(context, "extensionTrainingQuote", () => CreateQuote(context, action));
        }

        public ExtensionTrainingResult Execute(GameActionContext context, ExtensionTrainingAction action)
        {
            return _audit.Execute(context, "extensionTrainingExecute", () => ExecuteCore(context, action));
        }

        private ExtensionTrainingResult ExecuteCore(GameActionContext context, ExtensionTrainingAction action)
        {
            using (var scope = Db.CreateTransaction())
            {
                ExtensionTrainingQuote quote = CreateQuote(context, action);
                var character = context.Actor;
                var account = _accountManager.Repository.Get(character.AccountId)
                    .ThrowIfNull(ErrorCodes.AccountNotFound);
                bool isAdmin = character.AccessLevel.IsAdminOrGm();

                if (quote.CreditCost > 0)
                    character.SubtractFromWallet(TransactionType.extensionLearn, quote.CreditCost);

                if (!quote.TrainingCharacter)
                {
                    if (!quote.CanAffordPoints)
                        isAdmin.ThrowIfFalse(ErrorCodes.NotEnoughExtensionPoints);
                    _accountManager.AddExtensionPointsSpent(
                        account,
                        character,
                        quote.ExtensionPointCost,
                        quote.ExtensionId,
                        quote.NextLevel);
                }

                character.IncreaseExtensionLevel(quote.ExtensionId, quote.NextLevel);
                IDictionary<string, object> payload = _accountManager.GetEPData(account, character);
                payload.Add(k.extensionID, quote.ExtensionId);
                payload.Add(k.extensionLevel, quote.NextLevel);
                scope.Complete();
                return new ExtensionTrainingResult(quote, payload);
            }
        }

        private ExtensionTrainingQuote CreateQuote(GameActionContext context, ExtensionTrainingAction action)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            var character = context.Actor;
            var account = _accountManager.Repository.Get(character.AccountId)
                .ThrowIfNull(ErrorCodes.AccountNotFound);
            character.IsDocked.ThrowIfFalse(ErrorCodes.CharacterHasToBeDocked);
            ExtensionInfo extensionInfo = _extensionReader.GetExtensionByID(action.ExtensionId)
                .ThrowIfNull(ErrorCodes.ExtensionNotFound);
            bool isAdmin = character.AccessLevel.IsAdminOrGm();
            if (!extensionInfo.RequiredExtensions.All(character.CheckLearnedExtension))
                isAdmin.ThrowIfFalse(ErrorCodes.PrerequireExtensionError);

            int currentLevel = character.GetExtensionLevel(action.ExtensionId);
            int nextLevel = currentLevel + 1;
            nextLevel.ThrowIfGreater(10, ErrorCodes.ExtensionFullyLearnt);
            int pointCost = _extensionPoints.GetNominalExtensionPoints(nextLevel, extensionInfo.rank);
            double creditCost = nextLevel == 1 ? extensionInfo.price : 0;
            double walletBalance = character.GetWallet(TransactionType.extensionLearn).Balance;
            return new ExtensionTrainingQuote(
                action.ExtensionId,
                currentLevel,
                nextLevel,
                pointCost,
                _accountManager.CalculateCurrentEp(account),
                creditCost,
                walletBalance,
                character.IsInTraining());
        }
    }
}
