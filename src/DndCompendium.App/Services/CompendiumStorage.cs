namespace DndCompendium.App.Services;

internal static class CompendiumStorage
{
    public static string DataDirectory =>
#if WINDOWS && !DRIVEBREW_APPCONTAINER
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Drivebrew", "DndCompendium");
#else
        FileSystem.Current.AppDataDirectory;
#endif
}
