using DndCompendium.Core.Abstractions;
using DndCompendium.Core.Services;
using DndCompendium.Infrastructure.Persistence;
using DndCompendium.Infrastructure.Sources;
using DndCompendium.Infrastructure.Updates;
using Microsoft.Extensions.DependencyInjection;

namespace DndCompendium.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the first-stage offline library. In MAUI, pass
    /// <c>FileSystem.Current.AppDataDirectory</c> as <paramref name="rootPath"/>.
    /// </summary>
    public static IServiceCollection AddDndCompendiumInfrastructure(
        this IServiceCollection services,
        string rootPath)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        services.AddSingleton<ILocalDocumentRepository>(_ => new LocalDocumentRepository(rootPath));
        services.AddSingleton<IDocumentResourceStore>(_ => new SqliteDocumentResourceStore(rootPath));
        services.AddSingleton<ICharacterProfileStore>(_ => new JsonCharacterProfileStore(rootPath));
        services.AddSingleton<IExternalResourceExtractor, HomebrewResourceExtractor>();
        services.AddSingleton<IExternalResourceDownloader>(_ =>
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                UseDefaultCredentials = false
            };
            return new SecureExternalResourceDownloader(new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            });
        });
        services.AddSingleton<IDocumentResourceManager, DocumentResourceManager>();
        services.AddSingleton<EmbeddedDocumentSource>();
        services.AddSingleton<IDocumentSource>(provider =>
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                UseCookies = false,
                UseDefaultCredentials = false
            };
            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
            return new GoogleDriveDocumentSource(
                client,
                provider.GetRequiredService<EmbeddedDocumentSource>());
        });
        services.AddSingleton<IDownloadManager, DownloadManager>();
        services.AddSingleton<ISpellRepository>(provider =>
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                UseCookies = false,
                UseDefaultCredentials = false
            };
            return new GoogleDriveSpellRepository(rootPath, new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(45)
            }, provider.GetRequiredService<ICharacterProfileStore>());
        });
        services.AddSingleton<IExploitRepository>(provider =>
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                UseCookies = false,
                UseDefaultCredentials = false
            };
            return new GoogleDriveExploitRepository(rootPath, new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(45)
            }, provider.GetRequiredService<ICharacterProfileStore>());
        });
        services.AddSingleton<IUniversalSearchService>(provider => new UniversalSearchService(
            rootPath,
            provider.GetRequiredService<ILocalDocumentRepository>(),
            provider.GetRequiredService<ISpellRepository>(),
            provider.GetRequiredService<IExploitRepository>(),
            provider.GetRequiredService<ICharacterProfileStore>()));
        services.AddSingleton<IAppUpdateService>(_ => new HttpAppUpdateService(rootPath));
        return services;
    }
}
