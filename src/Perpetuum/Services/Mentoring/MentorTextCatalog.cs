using System;
using System.Collections.Generic;

namespace Perpetuum.Services.Mentoring
{
    /// <summary>
    /// Resolves the same English display strings the client receives. Internal definition names
    /// remain the fallback so mentor context is still useful when localization data is unavailable.
    /// </summary>
    public sealed class MentorTextCatalog : IMentorTextCatalog
    {
        private readonly ICustomDictionary _customDictionary;

        public MentorTextCatalog(ICustomDictionary customDictionary)
        {
            _customDictionary = customDictionary ?? throw new ArgumentNullException(nameof(customDictionary));
        }

        public string DisplayName(string definitionName)
        {
            return Lookup(definitionName) ?? MentorNameFormatter.ToDisplayName(definitionName);
        }

        public string Description(string descriptionToken)
        {
            return Lookup(descriptionToken);
        }

        private string Lookup(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return null;

            Dictionary<string, object> dictionary = _customDictionary.GetDictionary(0);
            if (dictionary == null || !dictionary.TryGetValue(key, out object value))
                return null;

            string text = value?.ToString()?.Trim();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
    }

    internal sealed class FallbackMentorTextCatalog : IMentorTextCatalog
    {
        public static readonly FallbackMentorTextCatalog Instance = new FallbackMentorTextCatalog();

        private FallbackMentorTextCatalog()
        {
        }

        public string DisplayName(string definitionName)
        {
            return MentorNameFormatter.ToDisplayName(definitionName);
        }

        public string Description(string descriptionToken)
        {
            return null;
        }
    }
}
