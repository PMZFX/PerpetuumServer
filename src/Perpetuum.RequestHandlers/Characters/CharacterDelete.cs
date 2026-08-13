using Perpetuum.Accounting;
using Perpetuum.Accounting.Characters;
using Perpetuum.Data;
using Perpetuum.Groups.Corporations;
using Perpetuum.Groups.Gangs;
using Perpetuum.Host.Requests;
using Perpetuum.Services.Channels;

namespace Perpetuum.RequestHandlers.Characters
{
    public class CharacterDelete : IRequestHandler
    {
        private readonly IAccountManager _accountManager;
        private readonly IChannelManager _channelManager;
        private readonly IGangManager _gangManager;

        public CharacterDelete(IAccountManager accountManager,IChannelManager channelManager,IGangManager gangManager)
        {
            _accountManager = accountManager;
            _channelManager = channelManager;
            _gangManager = gangManager;
        }

        public void HandleRequest(IRequest request)
        {
            using (var scope = Db.CreateTransaction())
            {
                var account = _accountManager.Repository.Get(request.Session.AccountId).ThrowIfNull(ErrorCodes.AccountNotFound);
                var character = Character.Get(request.Data.GetOrDefault<int>(k.characterID));

                account.Id.ThrowIfNotEqual(character.AccountId, ErrorCodes.AccessDenied);

                Corporation.GetRoleFromSql(character).ThrowIfNotEqual(CorporationRole.NotDefined, ErrorCodes.MemberHasRolesError);

                character.IsActive = false;
                character.SetActiveRobot(null);

                var gang = _gangManager.GetGangByMember(character);
                _gangManager.RemoveMember(gang, character, false);

                // clean up channels
                _channelManager.LeaveAllChannels(character);

                //clean corp and know character data
                character.CleanGameRelatedData();

                var oldCorporation = character.GetCorporation();

                //if he was in private corp -> move the member to his default corp
                if (oldCorporation is PrivateCorporation)
                {
                    character.GetDefaultCorporation().AddMember(character, CorporationRole.NotDefined, oldCorporation);
                    oldCorporation.RemoveMember(character);
                }

                Message.Builder.FromRequest(request).WithOk().Send();
                
                scope.Complete();
            }
        }
    }
}
