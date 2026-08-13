using System;
using System.Collections.Generic;

namespace Perpetuum.Services.Mentoring
{
    public sealed class MentorRateLimiter
    {
        private sealed class Window
        {
            public DateTime StartedAtUtc;
            public int Count;
        }

        private readonly object _sync = new object();
        private readonly MentorOptions _options;
        private readonly IMentorClock _clock;
        private readonly Dictionary<int, Window> _characters = new Dictionary<int, Window>();
        private readonly Dictionary<int, Window> _accounts = new Dictionary<int, Window>();

        public MentorRateLimiter(MentorOptions options, IMentorClock clock)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public bool TryAcquire(int characterId, int accountId)
        {
            DateTime now = _clock.UtcNow;

            lock (_sync)
            {
                Window character = GetCurrentWindow(_characters, characterId, now);
                Window account = GetCurrentWindow(_accounts, accountId, now);

                if (character.Count >= _options.RequestsPerCharacter ||
                    account.Count >= _options.RequestsPerAccount)
                {
                    return false;
                }

                character.Count++;
                account.Count++;
                return true;
            }
        }

        private Window GetCurrentWindow(Dictionary<int, Window> windows, int key, DateTime now)
        {
            if (!windows.TryGetValue(key, out Window window) ||
                now - window.StartedAtUtc >= _options.RateLimitWindow)
            {
                window = new Window { StartedAtUtc = now };
                windows[key] = window;
            }

            return window;
        }
    }
}
