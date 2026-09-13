#nullable enable
using System.Collections.Generic;

namespace WorldGen.App.Localization
{
    /// <summary>
    /// A Foundation által hivatkozott UI-kulcsok angol alapszövegei. A Unity-kötés
    /// később fájlból is tölthet; ez biztosítja, hogy a Foundation saját kulcsai
    /// soha ne jelenjenek meg "[kulcs]" formában. Tesztelt: minden menü- és
    /// dialóguskulcs szerepel.
    /// </summary>
    public static class EnglishStrings
    {
        public const string Language = "en";

        public static IReadOnlyDictionary<string, string> Entries { get; } = new Dictionary<string, string>
        {
            ["app.title"] = "WORLDGEN",

            ["menu.main.newWorld"] = "New World",
            ["menu.main.continue"] = "Continue",
            ["menu.main.loadWorld"] = "Load World",
            ["menu.main.settings"] = "Settings",
            ["menu.main.help"] = "Help",
            ["menu.main.credits"] = "Credits",
            ["menu.main.quit"] = "Quit",

            ["menu.pause.resume"] = "Resume",
            ["menu.pause.saveWorld"] = "Save World",
            ["menu.pause.saveWorldAs"] = "Save As",
            ["menu.pause.loadWorld"] = "Load World",
            ["menu.pause.settings"] = "Settings",
            ["menu.pause.help"] = "Help",
            ["menu.pause.returnToMainMenu"] = "Return to Main Menu",
            ["menu.pause.quitToDesktop"] = "Quit to Desktop",

            ["dialog.button.ok"] = "OK",
            ["dialog.button.cancel"] = "Cancel",
            ["dialog.button.continue"] = "Continue",
            ["dialog.button.delete"] = "Delete",
            ["dialog.button.quit"] = "Quit",
            ["dialog.button.keep"] = "Keep Changes",
            ["dialog.button.revert"] = "Revert",

            ["dialog.unsaved.title"] = "Unsaved Progress",
            ["dialog.unsaved.message"] = "Unsaved simulation progress will be lost. Continue?",
            ["dialog.quit.title"] = "Quit WorldGen",
            ["dialog.quit.message"] = "Do you want to quit to desktop?",
            ["dialog.deleteSave.title"] = "Delete Save",
            ["dialog.deleteSave.message"] = "Delete \"{0}\" permanently? This cannot be undone.",
            ["dialog.saveFailed.title"] = "Save Failed",
            ["dialog.saveFailed.message"] = "The world could not be saved. {0}",
            ["dialog.loadFailed.title"] = "Load Failed",
            ["dialog.loadFailed.message"] = "The save could not be loaded. {0}",
            ["dialog.videoConfirm.title"] = "Keep Display Settings?",
            ["dialog.videoConfirm.message"] = "Previous settings will be restored in {0} seconds.",

            ["toast.worldSaved"] = "World saved",
            ["toast.screenshotSaved"] = "Screenshot saved",
            ["toast.settingsApplied"] = "Settings applied",
            ["toast.simulationPaused"] = "Simulation paused",
            ["toast.autosaveCompleted"] = "Autosave completed",
            ["toast.seedCopied"] = "Seed copied",
            ["toast.parametersCopied"] = "World parameters copied",

            ["save.error.diskFull"] = "There is not enough free disk space.",
            ["save.error.accessDenied"] = "Access to the save folder was denied.",
            ["save.error.pathTooLong"] = "The save path is too long.",
            ["save.error.corrupted"] = "The save file is damaged.",
            ["save.error.notFound"] = "The save file no longer exists.",
            ["save.error.unknown"] = "An unexpected error occurred. Details were written to the log.",

            ["save.compat.newerFormat"] = "This save was created by a newer version of WorldGen.",
            ["save.compat.noMigration"] = "This save format is too old to be loaded.",
            ["save.compat.generatorChanged"] = "The world generator has changed since this save. The world can be recreated from its seed and parameters.",
            ["save.compat.corrupted"] = "The save file is damaged.",

            ["worldCreation.error.nameEmpty"] = "Enter a world name.",
            ["worldCreation.error.nameTooLong"] = "The world name is too long.",
            ["worldCreation.error.seedInvalid"] = "Enter a seed.",
            ["worldCreation.error.parameterMissing"] = "A required parameter is missing.",
            ["worldCreation.error.parameterOutOfRange"] = "The value is outside the allowed range.",
            ["worldCreation.error.parameterInvalid"] = "The value is not valid.",
            ["worldCreation.error.unknownPreset"] = "Unknown preset.",
            ["worldCreation.error.nameInvalid"] = "The world name contains invalid characters.",

            ["preset.earthLike"] = "Earth-like",
            ["preset.oceanWorld"] = "Ocean World",
            ["preset.dryWorld"] = "Dry World",
            ["preset.highGravity"] = "High Gravity",
            ["preset.lowGravity"] = "Low Gravity",
            ["preset.geologicallyActive"] = "Geologically Active",
            ["preset.random"] = "Random",
            ["preset.custom"] = "Custom",

            ["loading.crust"] = "Generating planetary crust...",
            ["loading.plates"] = "Building tectonic plates...",
            ["loading.climate"] = "Calculating climate...",
            ["loading.oceans"] = "Generating oceans...",
            ["loading.renderer"] = "Preparing renderer...",

            ["profile.planet"] = "Planet",
            ["profile.seed"] = "Seed",
            ["profile.age"] = "Age",
            ["profile.radius"] = "Radius",
            ["profile.mass"] = "Mass",
            ["profile.gravity"] = "Surface Gravity",
            ["profile.oceanCoverage"] = "Ocean Coverage",
            ["profile.meanTemperature"] = "Mean Temperature",
            ["profile.pressure"] = "Atmospheric Pressure",
            ["profile.continents"] = "Continents",
            ["profile.plates"] = "Tectonic Plates",
            ["profile.tectonicActivity"] = "Tectonic Activity",
            ["unit.earthMasses"] = "Earth",
        };

        public static LocalizationTable CreateTable()
        {
            var table = new LocalizationTable(Language, Language);
            table.SetRange(Language, Entries);
            return table;
        }
    }
}
