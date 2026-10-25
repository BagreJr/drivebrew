using Android.Content;
using Android.Content.PM;
using DndCompendium.App.Services;
using Microsoft.Maui.ApplicationModel;

namespace DndCompendium.App;

public sealed class AndroidCharacterSheetEditor : ICharacterSheetEditor
{
    public Task OpenAsync(string pdfPath, string title, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(pdfPath)) throw new FileNotFoundException("La hoja del personaje no existe.", pdfPath);

        var context = Platform.CurrentActivity ?? Android.App.Application.Context;
        var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(
            context,
            $"{context.PackageName}.charactersheetprovider",
            new Java.IO.File(pdfPath));
        var editIntent = new Intent(Intent.ActionEdit)
            .SetDataAndType(uri, "application/pdf")
            .AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
        editIntent.ClipData = ClipData.NewRawUri(title, uri);

        var packageManager = context.PackageManager;
        Intent launchIntent;
#pragma warning disable CA1416
        if (packageManager is not null
            && editIntent.ResolveActivity(packageManager) is not null)
        {
            launchIntent = Intent.CreateChooser(editIntent, $"Completar {title}")
                ?? throw new InvalidOperationException("Android no pudo abrir el selector de editores PDF.");
        }
        else
        {
            var viewIntent = new Intent(Intent.ActionView)
                .SetDataAndType(uri, "application/pdf")
                .AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
            viewIntent.ClipData = ClipData.NewRawUri(title, uri);
            if (packageManager is null || viewIntent.ResolveActivity(packageManager) is null)
            {
                throw new InvalidOperationException("Instalá un editor de PDF para completar la hoja. También podés usar ‘Guardar copia’ e importarla después.");
            }
            launchIntent = Intent.CreateChooser(viewIntent, $"Abrir {title}")
                ?? throw new InvalidOperationException("Android no pudo abrir el selector de visores PDF.");
        }
#pragma warning restore CA1416

        if (Platform.CurrentActivity is null) launchIntent.AddFlags(ActivityFlags.NewTask);
        context.StartActivity(launchIntent);
        return Task.CompletedTask;
    }
}
