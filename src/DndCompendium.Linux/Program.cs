using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using DndCompendium.Core.Abstractions;
using DndCompendium.Infrastructure.DependencyInjection;
using DndCompendium.Infrastructure.Sources;
using DndCompendium.Linux;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.StaticFiles;

var noBrowser = args.Contains("--no-browser", StringComparer.Ordinal);
var root = AppFiles.DataDirectory();
Directory.CreateDirectory(root);
FileStream processLock;
try
{
    processLock = new FileStream(Path.Combine(root, "desktop.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
}
catch (IOException)
{
    Console.Error.WriteLine("Drivebrew ya está abierto o no puede acceder a su carpeta de datos.");
    return 1;
}

using (processLock)
{
    var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    var tokenBytes = Encoding.ASCII.GetBytes(token);
    var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
    {
        Args = [],
        ContentRootPath = AppContext.BaseDirectory,
        WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot")
    });
    builder.Logging.SetMinimumLevel(LogLevel.Warning);
    builder.WebHost.ConfigureKestrel(server =>
    {
        server.Listen(IPAddress.Loopback, 0);
        server.Limits.MaxRequestBodySize = 32L * 1024 * 1024;
        server.AddServerHeader = false;
    });
    builder.Services.ConfigureHttpJsonOptions(options =>
        options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
    builder.Services.AddDndCompendiumInfrastructure(root);
    builder.Services.AddSingleton(new AppFiles(root));
    var session = new DesktopSession();
    if (!noBrowser)
    {
        builder.Services.AddSingleton(session);
        builder.Services.AddHostedService<SessionTimeout>();
    }

    await using var app = builder.Build();
    var library = app.Services.GetRequiredService<ILocalDocumentRepository>();
    await library.InitializeAsync();
    if ((await library.GetDocumentsAsync()).Count == 0)
        await library.UpsertCatalogAsync(GoogleDriveDocumentSource.GetOfflineCatalog());

    app.Use(async (context, next) =>
    {
        if (context.Request.Host.Host != "127.0.0.1" || context.Request.Host.Port != context.Connection.LocalPort)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            var supplied = context.Request.Headers["X-Drivebrew-Session"].ToString();
            if (supplied.Length != token.Length || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(supplied), tokenBytes))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            context.Response.Headers.CacheControl = "no-store";
            session.Touch();
        }
        await next();
    });
    app.UseDefaultFiles();
    var types = new FileExtensionContentTypeProvider();
    types.Mappings[".webp"] = "image/webp";
    types.Mappings[".woff2"] = "font/woff2";
    app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = types });
    DesktopApi.Map(app);

    await app.StartAsync();
    var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    var url = $"{address}/#{token}";
    Console.WriteLine($"Drivebrew: {url}");
    if (!noBrowser)
    {
        try
        {
            using var browser = Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { url } });
            if (browser is null) throw new InvalidOperationException();
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Console.Error.WriteLine("Abrí la dirección anterior en tu navegador.");
        }
    }
    await app.WaitForShutdownAsync();
}
return 0;

internal sealed class DesktopSession
{
    private long _lastSeen = Stopwatch.GetTimestamp();
    public void Touch() => Interlocked.Exchange(ref _lastSeen, Stopwatch.GetTimestamp());
    public TimeSpan Idle => Stopwatch.GetElapsedTime(Interlocked.Read(ref _lastSeen));
}

internal sealed class SessionTimeout(DesktopSession session, IHostApplicationLifetime lifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (session.Idle < TimeSpan.FromMinutes(4)) continue;
            lifetime.StopApplication();
            return;
        }
    }
}
