//
// MainWindow.cs
//
// Author:
//       Jonathan Pobst <monkey@jpobst.com>
//
// Copyright (c) 2010 Jonathan Pobst
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Linq;
using Mono.Addins;
using Pinta.Core;
using Pinta.Docking;
using Pinta.Gui.Widgets;
using Pinta.Resources;

namespace Pinta;

internal sealed class MainWindow
{
	private const int DEFAULT_PAD_OPACITY = 80;
	private const int DEFAULT_PANEL_OPACITY = 100;
	private const string DEFAULT_WORKSPACE_COLOR = "#9A9996";

	// The background around the image, the same for the light and dark themes.
	private readonly Gtk.CssProvider workspace_style = Gtk.CssProvider.New ();

	private void ApplyWorkspaceColor (string color)
		=> workspace_style.LoadFromString ($".workspace {{ background-color: {color}; }}");

	readonly Adw.Application app;
	// NRT - Created in OnActivated
	WindowShell window_shell = null!;
	DockNotebook notebook = null!;
	FloatingPad[] pads = null!;
	ColorsPadWidget colors_widget = null!;
	ImageThumbnailStrip thumbnail_strip = null!;
	Gio.Menu help_menu = null!;

	private int main_thread_id = -1;
	private Gtk.DropTarget drop_target = null!;

	public MainWindow (Adw.Application app)
	{
		this.app = app;
	}

	/// <summary>
	/// Performs any initialization that is not related to showing a new
	/// window (i.e. the Activate () method).
	/// In particular, the application menu must be initialized here in order to
	/// appear in the application window later (Gtk.Application.Menubar).
	/// </summary>
	public void Startup ()
	{
		CreateMainMenu ();
	}

	public void Activate ()
	{
		// Build our window
		CreateWindow ();

		// Initialize interface things
		_ = new ActionHandlers ();

		PintaCore.Chrome.InitializeErrorDialogHandler (ErrorDialog.ShowError);
		PintaCore.Chrome.InitializeMessageDialog (ErrorDialog.ShowMessage);
		PintaCore.Chrome.InitializeSimpleEffectDialog (SimpleEffectDialog.Launch);

		PintaCore.Initialize ();

		// Initialize extensions
		string addins_dir = System.IO.Path.Combine (PintaCore.Settings.GetUserSettingsDirectory (), "addins");
		AddinManager.Initialize (addins_dir);

		AddinManager.Registry.Update ();
		var setupService = new AddinSetupService (AddinManager.Registry);
		if (!setupService.AreRepositoriesRegistered ())
			setupService.RegisterRepositories (true);

		// Look out for any changes in extensions
		main_thread_id = Environment.CurrentManagedThreadId;
		AddinManager.AddExtensionNodeHandler (typeof (IExtension), OnExtensionChanged);

		// Load the user's previous settings
		LoadUserSettings ();
		PintaCore.Actions.App.BeforeQuit += delegate { SaveUserSettings (); };

		// We support drag and drop for URIs, which are converted into a Gdk.FileList.
		drop_target = Gtk.DropTarget.New (Gdk.FileList.GetGType (), Gdk.DragAction.Copy);
		drop_target.OnDrop += HandleDrop;
		window_shell.Window.AddController (drop_target);

		// Handle a few main window specific actions
		window_shell.Window.OnCloseRequest += HandleCloseRequest;

		// Add custom key handling during the capture phase. For example, this ensures that the "-" key
		// can be typed into the dash pattern box instead of being handled as a shortcut
		var key_controller = Gtk.EventControllerKey.New ();
		key_controller.SetPropagationPhase (Gtk.PropagationPhase.Capture);
		key_controller.OnKeyPressed += HandleGlobalKeyPress;
		key_controller.OnKeyReleased += HandleGlobalKeyRelease;
		window_shell.Window.AddController (key_controller);

		// TODO: These need to be [re]moved when we redo zoom support
		PintaCore.Actions.View.ZoomToWindow.Activated += ZoomToWindow_Activated;
		PintaCore.Actions.View.ZoomToSelection.Activated += ZoomToSelection_Activated;

		PintaCore.Workspace.ActiveDocumentChanged += ActiveDocumentChanged;

		PintaCore.Workspace.DocumentActivated += Workspace_DocumentCreated;
		PintaCore.Workspace.DocumentClosed += Workspace_DocumentClosed;

		notebook.ActiveTabChanged += DockNotebook_ActiveTabChanged;
	}

	private void Workspace_DocumentClosed (object? sender, DocumentEventArgs e)
	{
		var tab = FindTabWithCanvas ((CanvasWindow) e.Document.Workspace.CanvasWindow);

		if (tab != null)
			notebook.RemoveTab (tab);
	}

	private void DockNotebook_ActiveTabChanged (object? sender, EventArgs e)
	{
		var item = notebook.ActiveItem;

		if (item == null)
			return;

		var view = (DocumentViewContent) item;

		int index = PintaCore.Workspace.OpenDocuments.IndexOf (view.Document);
		PintaCore.Workspace.SetActiveDocument (index);
		((CanvasWindow) view.Widget).Canvas.Cursor = PintaCore.Tools.CurrentTool?.CurrentCursor;
	}

	private void Workspace_DocumentCreated (object? sender, DocumentEventArgs e)
	{
		var doc = e.Document;

		int selected_index = notebook.ActiveItemIndex;

		CanvasWindow canvas = new (
			PintaCore.Chrome,
			PintaCore.Tools,
			doc, PintaCore.CanvasGrid) {
			RulersVisible = PintaCore.Actions.View.Rulers.Value,
			RulerMetric = GetCurrentRulerMetric ()
		};
		doc.Workspace.CanvasWindow = canvas;
		doc.Workspace.Canvas = canvas.Canvas;

		DocumentViewContent my_content = new (doc, canvas);

		// Insert our tab to the right of the currently selected tab
		notebook.InsertTab (my_content, selected_index + 1);

		// Zoom to window only on first show (if we do it always, it will be called on every resize)
		// Note: this does seem to allow a small flicker where large images are shown at 100% zoom before
		// zooming out (Bug 1959673)
		// If the canvas is turned into a custom Gtk.Widget subclass in the future, this could
		// perhaps be done on the first measurement request

		bool canvasHasBeenShown = false;

		Gtk.Viewport view = (Gtk.Viewport) doc.Workspace.Canvas.Parent!;
		view.Hadjustment!.OnChanged += (o, e2) => {
			if (canvasHasBeenShown)
				return;

			GLib.Functions.IdleAdd (
				0,
				() => {
					ZoomToWindow_Activated (o, e);
					PintaCore.Workspace.Invalidate ();
					return false;
				}
			);

			canvasHasBeenShown = true;
		};

		PintaCore.Actions.View.Rulers.Toggled += (active, _) => { canvas.RulersVisible = active; };
		PintaCore.Actions.View.RulerMetric.OnActivate += (o, args) => {
			PintaCore.Actions.View.RulerMetric.ChangeState (args.Parameter!);
			canvas.RulerMetric = GetCurrentRulerMetric ();
		};
	}

	private static MetricType GetCurrentRulerMetric ()
	{
		GLib.Variant state = PintaCore.Actions.View.RulerMetric.GetState () ??
			throw new InvalidOperationException ("action should not be stateless");

		return (MetricType) state.GetInt32 ();
	}

	private bool HandleGlobalKeyPress (
		Gtk.EventControllerKey controller,
		Gtk.EventControllerKey.KeyPressedSignalArgs args)
	{
		// Give the widget that has focus a first shot at handling the event.
		// Otherwise, key presses may be intercepted by shortcuts for menu items.
		if (SendToFocusWidget (controller))
			return true;

		// Give the Canvas (and by extension the tools)
		// first shot at handling the event if
		// the mouse pointer is on the canvas
		if (PintaCore.Workspace.HasOpenDocuments) {
			var canvas_window = (CanvasWindow) PintaCore.Workspace.ActiveWorkspace.CanvasWindow;

			if ((canvas_window.HasFocus || canvas_window.IsMouseOnCanvas) &&
				 canvas_window.DoKeyPressEvent (controller, args)) {
				return true;
			}
		}

		if (args.GetKey ().Value == Gdk.Constants.KEY_Escape)
			PintaCore.Tools.Commit ();

		// If the canvas/tool didn't consume it, see if its a toolbox shortcut
		if (!args.State.HasModifierKey () && PintaCore.Tools.SetCurrentTool (args.GetKey ()))
			return true;

		// Finally, see if the palette widget wants it.
		bool shouldSwapColors = !args.State.HasModifierKey () && args.GetKey ().ToUpper ().Value == Gdk.Constants.KEY_X;

		if (shouldSwapColors) {
			PintaCore.Palette.SwapColors ();
			return true;
		}

		// We return 'false' to indicate that nobody consumed it
		return false;
	}

	private void HandleGlobalKeyRelease (
		Gtk.EventControllerKey controller,
		Gtk.EventControllerKey.KeyReleasedSignalArgs args)
	{
		if (SendToFocusWidget (controller) || !PintaCore.Workspace.HasOpenDocuments)
			return;

		// Give the Canvas (and by extension the tools)
		// first shot at handling the event if
		// the mouse pointer is on the canvas
		var canvas_window = (CanvasWindow) PintaCore.Workspace.ActiveWorkspace.CanvasWindow;

		if (canvas_window.HasFocus || canvas_window.IsMouseOnCanvas)
			canvas_window.DoKeyReleaseEvent (controller, args);
	}

	private bool SendToFocusWidget (Gtk.EventControllerKey key_controller)
	{
		var widget = window_shell.Window.FocusWidget;
		if (widget != null && key_controller.Forward (widget)) return true;
		return false;
	}

	// Called when an extension node is added or removed
	// Note this may be called from any thread, not just the main UI thread!
	private void OnExtensionChanged (object s, ExtensionNodeEventArgs args)
	{
		// Run synchronously if invoked from the main thread, e.g. when loading
		// addins at startup we require them to be immediately loaded.
		if (Environment.CurrentManagedThreadId == main_thread_id)
			UpdateExtension (args);
		else {
			// Otherwise, schedule the addin to be loaded/unloaded from the main thread
			// in case it touches the UI, e.g. to update menu items.
			GLib.Functions.IdleAdd (GLib.Constants.PRIORITY_DEFAULT_IDLE, () => {
				UpdateExtension (args);
				return false;
			});
		}
	}

	private static void UpdateExtension (ExtensionNodeEventArgs args)
	{
		if (args.Change == ExtensionChange.Add) {
			try {
				IExtension extension = (IExtension) args.ExtensionObject;
				extension.Initialize ();
			} catch (Exception e) {
				// Translators: {0} is the name of an add-in.
				string body = Translations.GetString ("The '{0}' add-in may not be compatible with this version of Pinta", args.ExtensionNode.Addin.Id);
				_ = PintaCore.Chrome.ShowErrorDialog (
					PintaCore.Chrome.MainWindow,
					Translations.GetString ("Failed to initialize add-in"),
					body, e.ToString ());
			}
		} else {
			IExtension extension = (IExtension) args.ExtensionObject;
			extension.Uninitialize ();
		}
	}

	private void CreateWindow ()
	{
		// Check for stored window settings
		int width = PintaCore.Settings.GetSetting (SettingNames.WINDOW_SIZE_WIDTH, 1100);
		int height = PintaCore.Settings.GetSetting (SettingNames.WINDOW_SIZE_HEIGHT, 750);
		bool maximize = PintaCore.Settings.GetSetting (SettingNames.WINDOW_MAXIMIZED, false);

		ResourceLoader.LoadCssStyles ();

		window_shell = new WindowShell (
			app,
			"Pinta.GenericWindow",
			PintaCore.ApplicationDisplayName,
			width,
			height,
			maximize);

		Adw.StyleManager style_manager = Adw.StyleManager.GetDefault ();
		style_manager.OnNotify += (_, _) => UpdateDarkClass (style_manager);
		UpdateDarkClass (style_manager);

		CreateMainToolBar ();
		CreateToolToolBar ();

		// The status bar is created before the pads, so that the pads float over it.
		CreateStatusBar ();
		CreateDockAndPads ();

		app.AddWindow (window_shell.Window);

		PintaCore.Chrome.InitializeApplication (app);
		PintaCore.Chrome.InitializeWindowShell (window_shell.Window);
	}

	private void UpdateDarkClass (Adw.StyleManager style_manager)
	{
		if (style_manager.Dark)
			window_shell.Window.AddCssClass ("dark");
		else
			window_shell.Window.RemoveCssClass ("dark");
	}

	private void CreateMainMenu ()
	{
		bool isMac = PintaCore.System.OperatingSystem == OS.Mac;

		Gio.Menu fileMenu = Gio.Menu.New ();
		Gio.Menu editMenu = Gio.Menu.New ();
		Gio.Menu imageMenu = Gio.Menu.New ();
		Gio.Menu layersMenu = Gio.Menu.New ();
		Gio.Menu adjustmentsMenu = Gio.Menu.New ();
		Gio.Menu effectsMenu = Gio.Menu.New ();
		Gio.Menu addinsMenu = Gio.Menu.New ();
		Gio.Menu windowMenu = Gio.Menu.New ();
		Gio.Menu padSection = Gio.Menu.New ();
		help_menu = Gio.Menu.New ();

		Gio.Menu viewMenu = Gio.Menu.New ();
		viewMenu.AppendSection (null, padSection);

		Gio.Menu menuBar = Gio.Menu.New ();
		menuBar.AppendSubmenu (Translations.GetString ("_File"), fileMenu);
		menuBar.AppendSubmenu (Translations.GetString ("_Edit"), editMenu);
		menuBar.AppendSubmenu (Translations.GetString ("_View"), viewMenu);
		menuBar.AppendSubmenu (Translations.GetString ("_Image"), imageMenu);
		menuBar.AppendSubmenu (Translations.GetString ("_Layers"), layersMenu);
		menuBar.AppendSubmenu (Translations.GetString ("_Adjustments"), adjustmentsMenu);
		menuBar.AppendSubmenu (Translations.GetString ("Effe_cts"), effectsMenu);

		// --- Global initializations

		app.Menubar = menuBar;

		if (isMac) {
			// Since GTK 4.14 there is an autogenerated Application menu, so we just need
			// to register actions with matching names for About, Quit, etc
			// https://gitlab.gnome.org/GNOME/gtk/-/issues/6762
			PintaCore.Actions.App.RegisterActions (app);
		}
		PintaCore.Actions.File.RegisterActions (app, fileMenu);
		PintaCore.Actions.Edit.RegisterActions (app, editMenu);
		PintaCore.Actions.View.RegisterActions (app, viewMenu);
		PintaCore.Actions.Image.RegisterActions (app, imageMenu);
		PintaCore.Actions.Layers.RegisterActions (app, layersMenu);
		PintaCore.Actions.Addins.RegisterActions (app, addinsMenu);
		PintaCore.Actions.Window.RegisterActions (app, windowMenu);
		PintaCore.Actions.Help.RegisterActions (app, help_menu);

		Gio.Menu effectCategoriesSection = Gio.Menu.New ();
		effectsMenu.AppendSection (null, PintaCore.Actions.Effects.RepeatMenu);
		effectsMenu.AppendSection (null, effectCategoriesSection);
		app.AddCommand (PintaCore.Actions.Effects.RepeatLastEffect);

		PintaCore.Chrome.InitializeMainMenu (adjustmentsMenu, effectCategoriesSection);
	}

	private void CreateMainToolBar ()
	{
		var main_toolbar = window_shell.CreateToolBar ("main_toolbar");
		PintaCore.Actions.CreateToolBar (main_toolbar);

		PintaCore.Actions.View.PixelGrid.Toggled += (visible, _) => PintaCore.CanvasGrid.ShowPixelGrid = visible;
		PintaCore.Actions.View.Settings.Activated += (_, _) => ShowSettingsDialog ();

		main_toolbar.Append (GtkExtensions.CreateToolBarSeparator ());
		main_toolbar.Append (CreatePadToggle (PintaCore.Actions.View.Rulers));
		main_toolbar.Append (CreatePadToggle (PintaCore.Actions.View.PixelGrid));
		main_toolbar.Append (GtkExtensions.CreateToolBarSeparator ());

		thumbnail_strip = new ImageThumbnailStrip (PintaCore.Workspace) {
			Hexpand = true,
			Halign = Gtk.Align.Start,
		};
		main_toolbar.Append (thumbnail_strip);
		PintaCore.Actions.View.ImageTabs.Toggled += (visible, _) => thumbnail_strip.Visible = visible;

		main_toolbar.Append (CreatePadToggle (PintaCore.Actions.View.ToolBox));
		main_toolbar.Append (CreatePadToggle (PintaCore.Actions.View.History));
		main_toolbar.Append (CreatePadToggle (PintaCore.Actions.View.Layers));
		main_toolbar.Append (CreatePadToggle (PintaCore.Actions.View.Colors));
		main_toolbar.Append (new Gtk.Button {
			IconName = PintaCore.Actions.View.Settings.IconName,
			ActionName = PintaCore.Actions.View.Settings.FullName,
			TooltipText = PintaCore.Actions.View.Settings.Label,
		});

		main_toolbar.Append (new Gtk.MenuButton {
			IconName = Pinta.Resources.Icons.MenuHelp,
			MenuModel = help_menu,
			TooltipText = Translations.GetString ("Help"),
		});
	}

	private async void ShowSettingsDialog ()
	{
		SettingsDialog.Values initial = new (
			Theme: PintaCore.Actions.View.ColorScheme.GetState ()!.GetInt32 (),
			RulerUnits: (int) GetCurrentRulerMetric (),
			ImageWidth: PintaCore.Settings.GetSetting (SettingNames.NEW_IMAGE_WIDTH, 800),
			ImageHeight: PintaCore.Settings.GetSetting (SettingNames.NEW_IMAGE_HEIGHT, 600),
			ImageBackground: (int) PintaCore.Settings.GetSetting (SettingNames.NEW_IMAGE_BACKGROUND, BackgroundType.White),
			BrushWidth: PintaCore.Settings.GetSetting (BaseTool.BRUSH_WIDTH_SETTING, BaseTool.DEFAULT_BRUSH_WIDTH),
			BrushHardness: PintaCore.Settings.GetSetting (BaseTool.BRUSH_HARDNESS_SETTING, BaseTool.DEFAULT_BRUSH_HARDNESS),
			PixelGridZoom: PintaCore.CanvasGrid.PixelGridMinZoom,
			PadOpacity: PintaCore.Settings.GetSetting (SettingNames.PAD_INACTIVE_OPACITY, DEFAULT_PAD_OPACITY),
			TopPanelsOpacity: PintaCore.Settings.GetSetting (SettingNames.TOP_PANELS_INACTIVE_OPACITY, DEFAULT_PANEL_OPACITY),
			StatusBarOpacity: PintaCore.Settings.GetSetting (SettingNames.STATUSBAR_INACTIVE_OPACITY, DEFAULT_PANEL_OPACITY),
			WorkspaceColor: PintaCore.Settings.GetSetting (SettingNames.WORKSPACE_COLOR, DEFAULT_WORKSPACE_COLOR),
			ResetLayout: false);

		using SettingsDialog dialog = new (PintaCore.Chrome, initial);
		Gtk.ResponseType response = await dialog.RunAsync ();
		SettingsDialog.Values result = dialog.Result;
		dialog.Destroy ();

		if (response != Gtk.ResponseType.Ok)
			return;

		PintaCore.Actions.View.ColorScheme.Activate (GLib.Variant.NewInt32 (result.Theme));
		PintaCore.Actions.View.RulerMetric.Activate (GLib.Variant.NewInt32 (result.RulerUnits));

		PintaCore.Settings.PutSetting (SettingNames.NEW_IMAGE_WIDTH, result.ImageWidth);
		PintaCore.Settings.PutSetting (SettingNames.NEW_IMAGE_HEIGHT, result.ImageHeight);
		PintaCore.Settings.PutSetting (SettingNames.NEW_IMAGE_BACKGROUND, (BackgroundType) result.ImageBackground);

		PintaCore.Settings.PutSetting (BaseTool.BRUSH_WIDTH_SETTING, result.BrushWidth);
		PintaCore.Settings.PutSetting (BaseTool.BRUSH_HARDNESS_SETTING, result.BrushHardness);
		PintaCore.Tools.ReloadCurrentTool ();

		PintaCore.CanvasGrid.PixelGridMinZoom = result.PixelGridZoom;
		PintaCore.CanvasGrid.SaveGridSettings ();

		PintaCore.Settings.PutSetting (SettingNames.WORKSPACE_COLOR, result.WorkspaceColor);
		ApplyWorkspaceColor (result.WorkspaceColor);

		PintaCore.Settings.PutSetting (SettingNames.TOP_PANELS_INACTIVE_OPACITY, result.TopPanelsOpacity);
		PintaCore.Settings.PutSetting (SettingNames.STATUSBAR_INACTIVE_OPACITY, result.StatusBarOpacity);
		window_shell.TopPanelsOpacity.InactiveOpacity = result.TopPanelsOpacity / 100.0;
		window_shell.StatusBarOpacity.InactiveOpacity = result.StatusBarOpacity / 100.0;

		PintaCore.Settings.PutSetting (SettingNames.PAD_INACTIVE_OPACITY, result.PadOpacity);
		foreach (FloatingPad pad in pads) {
			pad.InactiveOpacity = result.PadOpacity / 100.0;
			if (result.ResetLayout)
				pad.ResetLayout ();
		}
	}

	private static Gtk.ToggleButton CreatePadToggle (ToggleCommand command)
	{
		return new Gtk.ToggleButton {
			IconName = command.IconName,
			ActionName = command.FullName,
			TooltipText = string.Join (" ", command.Shortcuts.Take (1).Select (s => $"({s})").Prepend (command.Label)),
		};
	}

	private void CreateToolToolBar ()
	{
		Gtk.Box tool_toolbar = window_shell.CreateToolBar ("tool_toolbar");
		tool_toolbar.HeightRequest = 48;

		PintaCore.Chrome.InitializeToolToolBar (tool_toolbar);
	}

	private void CreateStatusBar ()
	{
		Gtk.Box statusbar = window_shell.CreateStatusBar ("statusbar");

		StatusBarProgress progress = new ();
		statusbar.Append (progress);
		PintaCore.Chrome.InitializeProgressIndicator (progress);

		PintaCore.Actions.CreateStatusBar (statusbar, PintaCore.Workspace);

		PintaCore.Chrome.InitializeStatusBar (statusbar);
	}

	private void CreateDockAndPads ()
	{
		ToolBoxWidget toolbox = new (PintaCore.Tools);

		PintaCore.Tools.ToolAdded += (_, e) => toolbox.AddItem (e.Tool);
		PintaCore.Tools.ToolRemoved += (_, e) => toolbox.RemoveItem (e.Tool);
		PintaCore.Chrome.InitializeToolBox (toolbox);

		FloatingPad tools_pad = new (
			uniqueName: "Tools",
			title: Translations.GetString ("Tools"),
			child: toolbox,
			halign: Gtk.Align.Start,
			valign: Gtk.Align.Start);

		colors_widget = new ColorsPadWidget (PintaCore.Chrome, PintaCore.Palette, PintaCore.System);

		FloatingPad colors_pad = new (
			uniqueName: "Colors",
			title: Translations.GetString ("Colors"),
			child: colors_widget,
			halign: Gtk.Align.Start,
			valign: Gtk.Align.End) {
			Resizable = false,
		};

		FloatingPad history_pad = HistoryPad.Create (PintaCore.Actions.Edit);
		FloatingPad layers_pad = LayersPad.Create (PintaCore.Actions.Layers);

		BindPad (tools_pad, PintaCore.Actions.View.ToolBox);
		BindPad (history_pad, PintaCore.Actions.View.History);
		BindPad (layers_pad, PintaCore.Actions.View.Layers);
		BindPad (colors_pad, PintaCore.Actions.View.Colors);

		notebook = new DockNotebook {
			Hexpand = true,
			Vexpand = true,
		};
		window_shell.CreateWorkspace (notebook);

		pads = [tools_pad, history_pad, layers_pad, colors_pad];
		foreach (FloatingPad pad in pads)
			window_shell.AddPad (pad);
	}

	private static void BindPad (FloatingPad pad, ToggleCommand command)
	{
		command.Toggled += (visible, _) => pad.Visible = visible;
		pad.CloseClicked += (_, _) => command.Value = false;
	}

	private void LoadUserSettings ()
	{
		double padOpacity = PintaCore.Settings.GetSetting (SettingNames.PAD_INACTIVE_OPACITY, DEFAULT_PAD_OPACITY) / 100.0;
		foreach (FloatingPad pad in pads) {
			pad.LoadSettings (PintaCore.Settings);
			pad.InactiveOpacity = padOpacity;
		}

		Gtk.StyleContext.AddProviderForDisplay (window_shell.Window.GetDisplay (), workspace_style, Gtk.Constants.STYLE_PROVIDER_PRIORITY_APPLICATION + 1);
		ApplyWorkspaceColor (PintaCore.Settings.GetSetting (SettingNames.WORKSPACE_COLOR, DEFAULT_WORKSPACE_COLOR));

		window_shell.TopPanelsOpacity.InactiveOpacity = PintaCore.Settings.GetSetting (SettingNames.TOP_PANELS_INACTIVE_OPACITY, DEFAULT_PANEL_OPACITY) / 100.0;
		window_shell.StatusBarOpacity.InactiveOpacity = PintaCore.Settings.GetSetting (SettingNames.STATUSBAR_INACTIVE_OPACITY, DEFAULT_PANEL_OPACITY) / 100.0;

		colors_widget.LoadSettings (PintaCore.Settings);

		// Set selected tool to last selected or default to the PaintBrush
		PintaCore.Tools.SetCurrentTool (PintaCore.Settings.GetSetting (SettingNames.LAST_SELECTED_TOOL, "PaintBrushTool"));

		PintaCore.Actions.View.Rulers.Value = PintaCore.Settings.GetSetting (SettingNames.RULER_SHOWN, false);
		PintaCore.Actions.View.PixelGrid.Value = PintaCore.Settings.GetSetting (SettingNames.PIXEL_GRID_SHOWN, false);
		PintaCore.Actions.View.ToolBar.Value = PintaCore.Settings.GetSetting (SettingNames.TOOLBAR_SHOWN, true);
		PintaCore.Actions.View.StatusBar.Value = PintaCore.Settings.GetSetting (SettingNames.STATUSBAR_SHOWN, true);
		PintaCore.Actions.View.ToolBox.Value = PintaCore.Settings.GetSetting (SettingNames.TOOLBOX_SHOWN, true);
		PintaCore.Actions.View.History.Value = PintaCore.Settings.GetSetting (SettingNames.HISTORY_SHOWN, true);
		PintaCore.Actions.View.Layers.Value = PintaCore.Settings.GetSetting (SettingNames.LAYERS_SHOWN, true);
		PintaCore.Actions.View.Colors.Value = PintaCore.Settings.GetSetting (SettingNames.COLORS_SHOWN, true);
		PintaCore.Actions.View.ImageTabs.Value = PintaCore.Settings.GetSetting (SettingNames.IMAGE_TABS_SHOWN, true);

		string dialog_uri = PintaCore.Settings.GetSetting (SettingNames.LAST_DIALOG_DIRECTORY, PintaCore.RecentFiles.DefaultDialogDirectory?.GetUri () ?? "");
		PintaCore.RecentFiles.LastDialogDirectory = Gio.FileHelper.NewForUri (dialog_uri);

		MetricType ruler_metric = (MetricType) PintaCore.Settings.GetSetting (SettingNames.RULER_METRIC, (int) MetricType.Pixels);
		PintaCore.Actions.View.RulerMetric.Activate (GLib.Variant.NewInt32 ((int) ruler_metric));

		int color_scheme = PintaCore.Settings.GetSetting (SettingNames.COLOR_SCHEME, 0);
		PintaCore.Actions.View.ColorScheme.Activate (GLib.Variant.NewInt32 (color_scheme));
	}

	private void SaveUserSettings ()
	{
		foreach (FloatingPad pad in pads)
			pad.SaveSettings (PintaCore.Settings);

		colors_widget.SaveSettings (PintaCore.Settings);

		// Don't store the maximized height if the window is maximized
		if (!window_shell.Window.IsMaximized ()) {
			PintaCore.Settings.PutSetting (SettingNames.WINDOW_SIZE_WIDTH, window_shell.Window.GetWidth ());
			PintaCore.Settings.PutSetting (SettingNames.WINDOW_SIZE_HEIGHT, window_shell.Window.GetHeight ());
		}

		PintaCore.Settings.PutSetting (SettingNames.RULER_METRIC, (int) GetCurrentRulerMetric ());
		PintaCore.Settings.PutSetting (SettingNames.COLOR_SCHEME, PintaCore.Actions.View.ColorScheme.GetState ()!.GetInt32 ());
		PintaCore.Settings.PutSetting (SettingNames.WINDOW_MAXIMIZED, window_shell.Window.IsMaximized ());
		PintaCore.Settings.PutSetting (SettingNames.RULER_SHOWN, PintaCore.Actions.View.Rulers.Value);
		PintaCore.Settings.PutSetting (SettingNames.PIXEL_GRID_SHOWN, PintaCore.Actions.View.PixelGrid.Value);
		PintaCore.Settings.PutSetting (SettingNames.IMAGE_TABS_SHOWN, PintaCore.Actions.View.ImageTabs.Value);
		PintaCore.Settings.PutSetting (SettingNames.TOOLBAR_SHOWN, PintaCore.Actions.View.ToolBar.Value);
		PintaCore.Settings.PutSetting (SettingNames.STATUSBAR_SHOWN, PintaCore.Actions.View.StatusBar.Value);
		PintaCore.Settings.PutSetting (SettingNames.TOOLBOX_SHOWN, PintaCore.Actions.View.ToolBox.Value);
		PintaCore.Settings.PutSetting (SettingNames.HISTORY_SHOWN, PintaCore.Actions.View.History.Value);
		PintaCore.Settings.PutSetting (SettingNames.LAYERS_SHOWN, PintaCore.Actions.View.Layers.Value);
		PintaCore.Settings.PutSetting (SettingNames.COLORS_SHOWN, PintaCore.Actions.View.Colors.Value);
		PintaCore.Settings.PutSetting (SettingNames.LAST_DIALOG_DIRECTORY, PintaCore.RecentFiles.LastDialogDirectory?.GetUri () ?? "");

		if (PintaCore.Tools.CurrentTool is BaseTool tool)
			PintaCore.Settings.PutSetting (SettingNames.LAST_SELECTED_TOOL, tool.GetType ().Name);

		PintaCore.Settings.DoSaveSettingsBeforeQuit ();
	}

	#region Action Handlers
	private bool HandleCloseRequest (object o, EventArgs args)
	{
		PintaCore.Actions.App.Exit.Activate ();

		// Stop the default handler from running so the user can cancel quitting.
		return true;
	}

	private bool HandleDrop (Gtk.DropTarget sender, Gtk.DropTarget.DropSignalArgs args)
	{
		if (args.Value.GetBoxed (Gdk.FileList.GetGType ()) is not Gdk.FileList file_list)
			return false;

		OpenDroppedFiles (file_list);
		return true;
	}

	private static async void OpenDroppedFiles (Gdk.FileList file_list)
	{
		foreach (Gio.File file in file_list.GetFilesHelper ()) {
			if (await PintaCore.Workspace.OpenFile (file) &&
			   file.GetUriScheme () is string scheme &&
			   (scheme.StartsWith ("http") || scheme.StartsWith ("ftp"))) {
				// If the file was likely dragged from a browser, mark as not having a file
				// so that the user must choose a new file to save to instead of hitting a permission error.
				PintaCore.Workspace.ActiveDocument.ClearFileReference ();
			}
		}
	}

	private void ZoomToSelection_Activated (object sender, EventArgs e)
	{
		PintaCore.Workspace.ActiveWorkspace.ZoomToCanvasRectangle (PintaCore.Workspace.ActiveDocument.Selection.GetBounds ());
	}

	private void ZoomToWindow_Activated (object sender, EventArgs e)
	{
		// The image is small enough to fit in the window
		if (PintaCore.Workspace.ImageFitsInWindow) {
			PintaCore.Actions.View.ActualSize.Activate ();
		} else {
			int image_x = PintaCore.Workspace.ImageSize.Width;
			int image_y = PintaCore.Workspace.ImageSize.Height;

			var canvas_viewport = PintaCore.Workspace.ActiveWorkspace.Canvas.Parent!;

			int window_x = canvas_viewport.GetAllocatedWidth ();
			int window_y = canvas_viewport.GetAllocatedHeight ();

			double ratio =
				(image_x / (double) window_x >= image_y / (double) window_y)
				? (window_x - 20) / (double) image_x
				: (window_y - 20) / (double) image_y;

			// The image is more constrained by width than height

			PintaCore.Workspace.Scale = ratio;
			PintaCore.Actions.View.SuspendZoomUpdate ();
			PintaCore.Actions.View.ZoomComboBox.ComboBox.GetEntry ().SetText (ViewActions.ToPercent (PintaCore.Workspace.Scale));
			PintaCore.Actions.View.ResumeZoomUpdate ();
		}

		Size image_size = PintaCore.Workspace.ImageSize;
		PintaCore.Workspace.ActiveWorkspace.RecenterView (new PointD (image_size.Width / 2.0, image_size.Height / 2.0));
		PintaCore.Actions.View.ZoomToWindowActivated = true;
	}
	#endregion


	private void ActiveDocumentChanged (object? sender, EventArgs e)
	{
		if (!PintaCore.Workspace.HasOpenDocuments)
			return;

		PintaCore.Actions.View.SuspendZoomUpdate ();
		PintaCore.Actions.View.ZoomComboBox.ComboBox.GetEntry ().SetText (ViewActions.ToPercent (PintaCore.Workspace.Scale));
		PintaCore.Actions.View.ResumeZoomUpdate ();

		var doc = PintaCore.Workspace.ActiveDocument;
		var tab = FindTabWithCanvas ((CanvasWindow) doc.Workspace.CanvasWindow);

		if (tab != null)
			notebook.ActiveItem = tab;

		doc.Workspace.GrabFocusToCanvas ();
	}

	private IDockNotebookItem? FindTabWithCanvas (CanvasWindow canvas_window) =>
		notebook.Items
		.Where (i => ((CanvasWindow) i.Widget) == canvas_window)
		.FirstOrDefault ();
}
