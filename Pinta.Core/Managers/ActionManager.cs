//
// ActionManager.cs
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

namespace Pinta.Core;

public sealed class ActionManager
{
	public AppActions App { get; }
	public FileActions File { get; }
	public EditActions Edit { get; }
	public ViewActions View { get; }
	public ImageActions Image { get; }
	public LayerActions Layers { get; }
	public AdjustmentsActions Adjustments { get; }
	public EffectsActions Effects { get; }
	public WindowActions Window { get; }
	public HelpActions Help { get; }
	public AddinActions Addins { get; }

	private readonly ChromeManager chrome;
	public ActionManager (
		ChromeManager chrome,
		ImageConverterManager imageFormats,
		PaletteFormatManager paletteFormats,
		PaletteManager palette,
		RecentFileManager recentFiles,
		SystemManager system,
		ToolManager tools,
		WorkspaceManager workspace)
	{
		// --- Action handlers that don't depend on other handlers

		AddinActions addins = new ();
		AdjustmentsActions adjustments = new ();
		AppActions app = new ();
		EditActions edit = new (chrome, paletteFormats, palette, tools, workspace);
		EffectsActions effects = new (chrome);
		ViewActions view = new (chrome, workspace);
		WindowActions window = new (workspace);

		// --- Action handlers that depend on other handlers

		FileActions file = new (system, app, recentFiles);
		HelpActions help = new (system, app);
		ImageActions image = new (tools, workspace, view);
		LayerActions layers = new (chrome, imageFormats, recentFiles, tools, workspace, image);

		// --- References to keep

		App = app;
		File = file;
		Edit = edit;
		View = view;
		Image = image;
		Layers = layers;
		Adjustments = adjustments;
		Effects = effects;
		Window = window;
		Help = help;
		Addins = addins;

		this.chrome = chrome;
	}

	public void CreateToolBar (Gtk.Box toolbar)
	{
		toolbar.Append (File.New.CreateToolBarItem ());
		toolbar.Append (File.Open.CreateToolBarItem ());
		toolbar.Append (File.Save.CreateToolBarItem ());
		// Printing is disabled for now until it is fully functional.
#if false
		toolbar.AppendItem (File.Print.CreateToolBarItem ());
#endif
		toolbar.Append (GtkExtensions.CreateToolBarSeparator ());

		toolbar.Append (Edit.Cut.CreateToolBarItem ());
		toolbar.Append (Edit.Copy.CreateToolBarItem ());
		toolbar.Append (Edit.Paste.CreateToolBarItem ());

		toolbar.Append (GtkExtensions.CreateToolBarSeparator ());
		toolbar.Append (Image.CropToSelection.CreateToolBarItem ());
		toolbar.Append (Edit.Deselect.CreateToolBarItem ());

		toolbar.Append (GtkExtensions.CreateToolBarSeparator ());
		toolbar.Append (Edit.Undo.CreateToolBarItem ());
		toolbar.Append (Edit.Redo.CreateToolBarItem ());
	}

	public void CreateStatusBar (Gtk.Box statusbar, WorkspaceManager workspaceManager)
	{
		statusbar.Append (new Gtk.Box { Hexpand = true });

		Gtk.Label selection_location = CreateStatusBarLabel (statusbar, 30);
		Gtk.Label selection_size = CreateStatusBarLabel (statusbar, 34);
		statusbar.Append (Gtk.Image.NewFromIconName (Resources.Icons.ImageResizeCanvas));
		Gtk.Label canvas_size = CreateStatusBarLabel (statusbar, 13);
		statusbar.Append (Gtk.Image.NewFromIconName (Resources.Icons.CursorPosition));
		Gtk.Label cursor = CreateStatusBarLabel (statusbar, 11);

		void UpdateSelection ()
		{
			RectangleD bounds = workspaceManager.HasOpenDocuments ? workspaceManager.ActiveDocument.Selection.GetBounds () : new RectangleD ();
			selection_location.SetText (Translations.GetString ("Selection top left: ({0}, {1})", bounds.X, bounds.Y));
			selection_size.SetText (Translations.GetString ("Bounding rectangle size: ({0}, {1})", bounds.Width, bounds.Height));
		}

		void UpdateCanvasSize ()
		{
			Size size = workspaceManager.HasOpenDocuments ? workspaceManager.ImageSize : Size.Empty;
			canvas_size.SetText ($"{size.Width} × {size.Height}");
		}

		UpdateSelection ();
		UpdateCanvasSize ();
		cursor.SetText ("0, 0");

		workspaceManager.SelectionChanged += (_, _) => UpdateSelection ();
		workspaceManager.ActiveDocumentChanged += (_, _) => UpdateCanvasSize ();
		workspaceManager.DocumentActivated += (_, e) => e.Document.Workspace.ViewSizeChanged += (_, _) => UpdateCanvasSize ();

		chrome.LastCanvasCursorPointChanged += delegate {
			var pt = chrome.LastCanvasCursorPoint;
			cursor.SetText ($"{pt.X}, {pt.Y}");
		};

		View.CreateStatusBar (statusbar);
	}

	private static Gtk.Label CreateStatusBarLabel (Gtk.Box statusbar, int widthChars)
	{
		Gtk.Label label = Gtk.Label.New (null);
		label.Xalign = 0.0f;
		label.WidthChars = widthChars;
		statusbar.Append (label);
		return label;
	}

	public void RegisterHandlers ()
	{
		File.RegisterHandlers ();
		Edit.RegisterHandlers ();
		Image.RegisterHandlers ();
		Layers.RegisterHandlers ();
		View.RegisterHandlers ();
		Help.RegisterHandlers ();
	}
}
