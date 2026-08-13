using Perpetuum.Accounting.Characters;
using Perpetuum.Robots;

namespace Perpetuum.Services.Onboarding
{
    public interface IFieldCertificationStarterLoadout
    {
        bool EnsureMissionCargoCapacity(Character character, Robot robot);
    }
}
