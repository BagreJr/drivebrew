using Microsoft.Extensions.Logging;

using DndCompendium.App.ViewModels;
using DndCompendium.App.Views;
using DndCompendium.Infrastructure.DependencyInjection;
using DndCompendium.Core.Abstractions;
using DndCompendium.App.Services;
using Microsoft.Maui.Storage;

namespace DndCompendium.App;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		builder.Services.AddDndCompendiumInfrastructure(FileSystem.Current.AppDataDirectory);
#if ANDROID
		builder.Services.AddSingleton<IAppPackageInstaller, AndroidAppPackageInstaller>();
		builder.Services.AddSingleton<IAppUpdateScheduler, AndroidAppUpdateScheduler>();
#else
		builder.Services.AddSingleton<IAppPackageInstaller, UnsupportedAppPackageInstaller>();
		builder.Services.AddSingleton<IAppUpdateScheduler, NoopAppUpdateScheduler>();
#endif
		builder.Services.AddSingleton<AppUpdateCoordinator>();

		builder.Services.AddSingleton<AppShell>();
		builder.Services.AddSingleton<LibraryViewModel>();
		builder.Services.AddSingleton<SearchViewModel>();
		builder.Services.AddSingleton<DownloadsViewModel>();
		builder.Services.AddSingleton<CharacterProfilesViewModel>();
		builder.Services.AddSingleton<ResourceManagerViewModel>();
		builder.Services.AddSingleton<FavoritesViewModel>();
		builder.Services.AddSingleton<SettingsViewModel>();
		builder.Services.AddSingleton<SpellsViewModel>();
		builder.Services.AddSingleton<PreparedSpellsViewModel>();
		builder.Services.AddSingleton<ExploitsViewModel>();
		builder.Services.AddTransient<ReaderViewModel>();
		builder.Services.AddTransient<SpellDetailViewModel>();
		builder.Services.AddTransient<ExploitDetailViewModel>();
		builder.Services.AddTransient<ResourceEditorViewModel>();

		builder.Services.AddSingleton<LibraryPage>();
		builder.Services.AddSingleton<SearchPage>();
		builder.Services.AddSingleton<DownloadsPage>();
		builder.Services.AddSingleton<CharacterProfilesPage>();
		builder.Services.AddSingleton<ResourceManagerPage>();
		builder.Services.AddSingleton<RulesAssistantPage>();
		builder.Services.AddSingleton<FavoritesPage>();
		builder.Services.AddSingleton<SettingsPage>();
		builder.Services.AddSingleton<SpellsPage>();
		builder.Services.AddSingleton<PreparedSpellsPage>();
		builder.Services.AddSingleton<ExploitsPage>();
		builder.Services.AddSingleton<MorePage>();
		builder.Services.AddTransient<ReaderPage>();
		builder.Services.AddTransient<SpellDetailPage>();
		builder.Services.AddTransient<ExploitDetailPage>();
		builder.Services.AddTransient<ResourceEditorPage>();

#if DEBUG
		builder.Services.AddHybridWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
