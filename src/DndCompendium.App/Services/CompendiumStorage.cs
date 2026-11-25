namespace DndCompendium.App.Services;

internal static class CompendiumStorage
{
    public static string DataDirectory =>
#if WINDOWS
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Drivebrew", "DndCompendium");
#else
        FileSystem.Current.AppDataDirectory;
#endif
}
