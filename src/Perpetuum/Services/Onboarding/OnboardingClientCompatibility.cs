using System;
using System.Collections.Generic;
using Perpetuum.Accounting.Characters;
using Perpetuum.Data;
using Perpetuum.GenXY;

namespace Perpetuum.Services.Onboarding
{
    /// <summary>
    /// Adapts the closed stock client to the server-owned onboarding flow. The client persists its
    /// Rookie Checklist trigger map through character settings and also uses that map to reveal
    /// ordinary UI controls. Completing the legacy map before first login makes those controls
    /// available without asking the player to perform the obsolete checklist.
    /// </summary>
    public sealed class OnboardingClientCompatibility
    {
        public Dictionary<string, object> LoadSettings(Character character)
        {
            if (character == null || character == Character.None)
                throw new ArgumentNullException(nameof(character));

            string serialized = Db.Query()
                .CommandText("select settingsstring from charactersettings where characterid=@characterID")
                .SetParameter("@characterID", character.Id)
                .ExecuteScalar<string>();
            Dictionary<string, object> settings = GenxyConverter.Deserialize(serialized);

            if (character.IsInTraining() && ApplyReplacementOnboardingState(settings))
                Save(character, settings);

            return settings;
        }

        public void Initialize(Character character)
        {
            if (character == null || character == Character.None)
                throw new ArgumentNullException(nameof(character));

            var settings = new Dictionary<string, object>();
            ApplyReplacementOnboardingState(settings);
            Save(character, settings);
        }

        public void PrepareClientProfile(
            Character character,
            IDictionary<string, object> profile)
        {
            if (character == null || character == Character.None)
                throw new ArgumentNullException(nameof(character));
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            // This is deliberately client-facing only. Race/school and all server-side training
            // restrictions remain unchanged until the replacement graduation flow runs.
            if (character.IsInTraining())
                profile[k.isInTraining] = false;
        }

        public static bool ApplyReplacementOnboardingState(IDictionary<string, object> settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            bool changed = Set(settings, "welcomeScreen", 1);
            changed |= Set(settings, "hasBeenOnHelp", 1);
            changed |= Set(settings, "hasBeenOnRecruitment", 1);

            if (!settings.TryGetValue("tutorialTriggers", out object rawTriggers) ||
                !(rawTriggers is IDictionary<string, object> triggers))
            {
                triggers = new Dictionary<string, object>(StringComparer.Ordinal);
                settings["tutorialTriggers"] = triggers;
                changed = true;
            }

            foreach (string trigger in LegacyTutorialTriggers)
                changed |= Set(triggers, trigger, 1);

            return changed;
        }

        private static bool Set(IDictionary<string, object> dictionary, string key, int value)
        {
            if (dictionary.TryGetValue(key, out object current) &&
                Convert.ToInt32(current) == value)
            {
                return false;
            }

            dictionary[key] = value;
            return true;
        }

        private static void Save(Character character, Dictionary<string, object> settings)
        {
            Db.Query()
                .CommandText("characterSettingsSetString")
                .SetParameter("@characterid", character.Id)
                .SetParameter("@data", GenxyConverter.Serialize(settings))
                .ExecuteNonQuery();
        }

        internal static IReadOnlyList<string> LegacyTutorialTriggers { get; } =
            LegacyTutorialTriggerNames.Split(
                new[] { ' ', '\r', '\n', '\t' },
                StringSplitOptions.RemoveEmptyEntries);

        private const string LegacyTutorialTriggerNames = @"
pseudotrigger_tutorialchecklist_introduction_usingthischecklist
pseudotrigger_tutorialchecklist_introduction_uibasics
pseudotrigger_tutorialchecklist_introduction_options
pseudotrigger_tutorialchecklist_introduction_helpsupport
trigger_joingeneralchat
pseudotrigger_tutorialchecklist_essentials_terminals
trigger_openprivatestorage
trigger_activaterobot
trigger_deploy
trigger_moverobot
trigger_camerarotate
trigger_camerazoom
trigger_autorun
trigger_moverobot_doubleclick
trigger_toggle_slope
trigger_openlandmarks
trigger_selecttrainingterminal
trigger_openlandmarkinfo
trigger_openradar
trigger_openrobotstatus
trigger_suicideswitch
pseudotrigger_tutorialchecklist_essentials_robotstatus
trigger_openrobotcargo
pseudotrigger_tutorialchecklist_essentials_itemsincargo
trigger_openequipwindow
trigger_moduleremove
trigger_moduleequip
trigger_ammoload
trigger_putammoincargo
trigger_deploy2
trigger_openzoneselector
pseudotrigger_tutorialchecklist_essentials_map
trigger_teleportshootingrange
trigger_opentargeting
trigger_primarylocknpc
trigger_openmodules
trigger_activatemodule_weapon
trigger_destroynpc
trigger_reload
trigger_lootitem
trigger_enterterminal
trigger_opencharacterprofile
trigger_openextensions
trigger_extensionupgrade
trigger_opensparks
trigger_installspark
trigger_openrepairshop
pseudotrigger_tutorialchecklist_advanced_repairshop
trigger_openmarket
trigger_sequerbuy
trigger_sequerunpack
trigger_sequeractivate
trigger_openmissions
trigger_requesttransportmission
trigger_deploy3
trigger_openterrainmissions
trigger_completetransportmission
trigger_enterterminal2
trigger_arganoactivate
trigger_deploy4
trigger_teleportindustrialarea
trigger_reload_directionalscan
trigger_activatemodule_miningprobe
trigger_miningprobe_findx
trigger_reload_tilescan
trigger_activatemodule_miningprobe2
trigger_lockterrain
trigger_activatemodule_minermodule
pseudotrigger_tutorialchecklist_advanced_scanningmining
trigger_lockterrainhelioptris
trigger_activatemodule_harvester
pseudotrigger_tutorialchecklist_advanced_harvesting
trigger_reload_artifact
trigger_activatemodule_miningprobe3
trigger_findartifact
pseudotrigger_tutorialchecklist_advanced_artifacts
trigger_openrefinery
trigger_refinetitanium
trigger_openrecycle
trigger_recycle
trigger_openresearch
trigger_researchkernels
trigger_openresearch_common1
trigger_researchautocannon
pseudotrigger_tutorialchecklist_complex_research
trigger_openprototype
trigger_prototyping_start
pseudotrigger_tutorialchecklist_complex_prototyping
trigger_openreverseengineering
trigger_reverseengineering_addautocannon
trigger_reverseengineering_adddecoder
trigger_reverseengineering_start
pseudotrigger_tutorialchecklist_complex_reverseengineering
trigger_openfactory
trigger_factory_addct
trigger_factory_start
pseudotrigger_tutorialchecklist_complex_massproduction
trigger_artemisactivate
trigger_deploy5
trigger_activatemodule_seismicarmor
trigger_activatemodule_sensoramplifier
trigger_teleportshootingrange2
trigger_destroynpc2
pseudotrigger_tutorialchecklist_complex_thelodicarobots
trigger_tyrannosactivate
trigger_deploy6
trigger_teleportshootingrange3
trigger_primarylocknpc2
trigger_activatemodule_shield
trigger_destroynpc_scarab
pseudotrigger_tutorialchecklist_complex_pelistalrobots
trigger_kainactivate
trigger_deploy7
trigger_teleportshootingrange4
trigger_destroynpc3
pseudotrigger_tutorialchecklist_complex_finaltask";
    }
}
