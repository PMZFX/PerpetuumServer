using Perpetuum.Host.Requests;
using Perpetuum.Services.Onboarding;

namespace Perpetuum.RequestHandlers.Characters
{
    public class CharacterGetMyProfile : IRequestHandler
    {
        private readonly OnboardingClientCompatibility _onboardingCompatibility;

        public CharacterGetMyProfile(OnboardingClientCompatibility onboardingCompatibility)
        {
            _onboardingCompatibility = onboardingCompatibility;
        }

        public void HandleRequest(IRequest request)
        {
            var character = request.Session.Character;
            var profile = character.GetFullProfile();
            _onboardingCompatibility.PrepareClientProfile(character, profile);
            Message.Builder.FromRequest(request).WithData(profile).Send();
        }
    }
}
