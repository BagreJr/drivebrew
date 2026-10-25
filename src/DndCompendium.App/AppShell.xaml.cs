namespace DndCompendium.App;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();
		Routing.RegisterRoute(AppRoutes.Reader, typeof(Views.ReaderPage));
		Routing.RegisterRoute(AppRoutes.SpellDetail, typeof(Views.SpellDetailPage));
		Routing.RegisterRoute(AppRoutes.ExploitDetail, typeof(Views.ExploitDetailPage));
		Routing.RegisterRoute(AppRoutes.ResourceEditor, typeof(Views.ResourceEditorPage));
		Routing.RegisterRoute(AppRoutes.CharacterSheet, typeof(Views.CharacterSheetPage));
	}
}
