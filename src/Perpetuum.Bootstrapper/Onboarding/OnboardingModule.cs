using Autofac;
using Perpetuum.Services.Onboarding;

namespace Perpetuum.Bootstrapper.Onboarding
{
    internal static class OnboardingModule
    {
        public static void RegisterOnboarding(this ContainerBuilder builder)
        {
            builder.RegisterType<FieldCertificationMissionContract>().SingleInstance();
            builder.RegisterType<CombatCertificationMissionContract>().SingleInstance();
            builder.RegisterType<OnboardingClientCompatibility>().SingleInstance();
            builder.RegisterType<OnboardingLocalization>()
                .SingleInstance()
                .AutoActivate();
            builder.RegisterType<FieldCertificationEnrollment>()
                .As<IFieldCertificationEnrollment>()
                .SingleInstance();
            builder.RegisterType<FieldCertificationStarterLoadout>()
                .As<IFieldCertificationStarterLoadout>()
                .SingleInstance();
            builder.RegisterType<FieldCertificationPresentation>()
                .SingleInstance()
                .AutoActivate();
            builder.RegisterType<FieldCertificationProgressGuidance>()
                .SingleInstance()
                .AutoActivate();
        }
    }
}
