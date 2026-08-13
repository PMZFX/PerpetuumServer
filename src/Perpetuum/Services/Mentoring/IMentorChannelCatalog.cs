using Perpetuum.Accounting.Characters;
using Perpetuum.Services.Channels;

namespace Perpetuum.Services.Mentoring
{
    /// <summary>
    /// Supplies the non-persistent channel representation consumed by the stock
    /// client's channel list and character channel list requests.
    /// </summary>
    public interface IMentorChannelCatalog
    {
        Channel GetChannel(Character character);
    }
}
