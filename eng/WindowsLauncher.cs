using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

internal static class WindowsLauncher
{
    [STAThread]
    private static int Main()
    {
        var appDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App");
        var executable = Path.Combine(appDirectory, "DndCompendium.exe");

        if (!File.Exists(executable))
        {
            return ShowError("Extraé todo el ZIP y dejá la carpeta App al lado de DndCompendium.exe.");
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = appDirectory,
                UseShellExecute = false
            };
            using (Process.Start(startInfo)) { }
            return 0;
        }
        catch (Exception error)
        {
            return ShowError("No se pudo abrir la aplicación. " + error.Message);
        }
    }

    private static int ShowError(string message)
    {
        MessageBox.Show(message, "D&D Compendium", MessageBoxButtons.OK, MessageBoxIcon.Error);
        return 1;
    }
}
