using System;
using System.Threading;
using System.Threading.Tasks;

namespace Perpetuum.Services.Mentoring
{
    public sealed class DeterministicMentorProvider : IMentorProvider
    {
        public Task<string> GetResponseAsync(
            MentorProviderRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult($"Mentor test received: {request.Request.Message}");
        }
    }
}
