using System.Collections.Generic;
using Perpetuum.Host.Requests;
using Perpetuum.Services.Onboarding;

namespace Perpetuum.RequestHandlers.Characters
{
    public class CharacterSettingsGet : IRequestHandler
    {
        private readonly OnboardingClientCompatibility _onboardingCompatibility;

        public CharacterSettingsGet(OnboardingClientCompatibility onboardingCompatibility)
        {
            _onboardingCompatibility = onboardingCompatibility;
        }

        public void HandleRequest(IRequest request)
        {
            var character = request.Session.Character;
            var result = _onboardingCompatibility.LoadSettings(character);

            if (result.Count == 0)
            {
                Message.Builder.FromRequest(request).WithData(new Dictionary<string, object>(1) { { k.state, k.empty } }).Send();
            }
            else
            {
                Message.Builder.FromRequest(request).WithData(new Dictionary<string, object>(1) { { k.result, result } }).Send();
            }
        }
    }
}
