using System;
using System.Collections.Generic;

namespace Perpetuum.Services.Onboarding
{
    /// <summary>
    /// Supplies server-owned English fallback text for onboarding content. Custom dictionaries are
    /// transmitted during sign-in, so these tokens work with the stock client without editing its
    /// packaged resources or the mounted runtime configuration.
    /// </summary>
    public sealed class OnboardingLocalization
    {
        private static readonly IReadOnlyDictionary<string, string> Entries =
            new Dictionary<string, string>
            {
                {
                    "mission_sfc_target_acquisition_title",
                    "Target Acquisition"
                },
                {
                    "mission_sfc_target_acquisition_description",
                    "Range control needs one training Scarab removed. Travel to the marked " +
                    "shooting range, establish a primary target lock, and destroy it with your " +
                    "Arkhe's autocannon. The Mentor channel can help if any control is unclear."
                },
                {
                    "mission_sfc_target_acquisition_success",
                    "Target confirmed destroyed. Range-control certification complete."
                },
                {
                    "mission_sfc_target_acquisition_reach",
                    "Travel to the marked shooting range."
                },
                {
                    "mission_sfc_target_acquisition_lock",
                    "Select a training Scarab and establish a primary target lock (default: R)."
                },
                {
                    "mission_sfc_target_acquisition_destroy",
                    "Activate your fitted autocannon and destroy the locked training Scarab."
                }
            };

        public OnboardingLocalization(ICustomDictionary customDictionary)
        {
            if (customDictionary == null)
                throw new ArgumentNullException(nameof(customDictionary));

            // Existing language files are distinct dictionaries. Until translations are authored,
            // add the English fallback to every loaded language so a token never leaks into UI.
            var updated = new HashSet<Dictionary<string, object>>();
            for (int language = 0; language <= 17; language++)
            {
                Dictionary<string, object> dictionary = customDictionary.GetDictionary(language);
                if (dictionary == null || !updated.Add(dictionary))
                    continue;

                foreach (KeyValuePair<string, string> entry in Entries)
                {
                    dictionary[entry.Key] = entry.Value;
                }
            }
        }
    }
}
