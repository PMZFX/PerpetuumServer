using Perpetuum.Accounting.Characters;

namespace Perpetuum.Services.Onboarding
{
    public interface IFieldCertificationEnrollment
    {
        void Schedule(Character character, long trainingDockingBaseEid);
    }
}
