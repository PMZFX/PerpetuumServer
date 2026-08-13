using Autofac;
using Perpetuum.Services.Mentoring;
using Perpetuum.Threading.Process;

namespace Perpetuum.Bootstrapper.Mentoring
{
    internal static class MentoringModule
    {
        public static void RegisterMentoring(this ContainerBuilder builder)
        {
            builder.RegisterInstance(MentorOptions.PhaseOneDefaults).SingleInstance();
            builder.RegisterType<SystemMentorClock>().As<IMentorClock>().SingleInstance();
            builder.RegisterType<MentorRateLimiter>().SingleInstance();
            builder.RegisterType<MentorTextCatalog>()
                .As<IMentorTextCatalog>()
                .SingleInstance();
            builder.RegisterType<ReadOnlyMentorPlayerContextReader>()
                .As<IMentorPlayerContextReader>()
                .SingleInstance();
            builder.RegisterType<ReadOnlyMentorMissionStateReader>()
                .As<IMentorMissionStateReader>()
                .SingleInstance();
            builder.RegisterType<ReadOnlyMentorTrainingRewardReader>()
                .As<IMentorTrainingRewardReader>()
                .SingleInstance();
            builder.RegisterType<MentorEntityResolver>()
                .As<IMentorEntityResolver>()
                .SingleInstance();
            builder.RegisterType<EmbeddedMentorKnowledgeBase>()
                .As<IMentorKnowledgeBase>()
                .SingleInstance();
            builder.RegisterType<MentorChannelCatalog>()
                .As<IMentorChannelCatalog>()
                .SingleInstance();
            builder.RegisterType<DiagnosticMentorProvider>()
                .AsSelf()
                .SingleInstance();
            builder.RegisterType<ModelEndpointMentorProvider>()
                .As<IMentorProvider>()
                .SingleInstance();
            builder.RegisterType<MentorChannelGateway>()
                .As<IMentorResponseSink>()
                .As<IMentorConversationSink>()
                .AsSelf()
                .SingleInstance()
                .AutoActivate();
            builder.RegisterType<MentorRequestProcessor>()
                .As<IMentorRequestDispatcher>()
                .AsSelf()
                .SingleInstance()
                .AutoActivate()
                .OnActivated(activation =>
                    activation.Context.Resolve<IProcessManager>().AddProcess(activation.Instance));
            builder.RegisterType<MentorBridge>().As<IMentorChatIngress>().SingleInstance();
        }
    }
}
