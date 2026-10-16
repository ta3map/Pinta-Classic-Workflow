using Pinta.Core;

namespace Pinta;

/// <summary>
/// Application settings: theme, ruler units, defaults for new images and brushes, and the look of the canvas and pads.
/// </summary>
public sealed class SettingsDialog : Gtk.Dialog
{
	private const int SPACING = 6;

	private readonly Gtk.DropDown theme;
	private readonly Gtk.DropDown ruler_units;
	private readonly Gtk.SpinButton image_width;
	private readonly Gtk.SpinButton image_height;
	private readonly Gtk.DropDown image_background;
	private readonly Gtk.SpinButton brush_width;
	private readonly Gtk.SpinButton brush_hardness;
	private readonly Gtk.SpinButton pixel_grid_zoom;
	private readonly Gtk.SpinButton pad_opacity;
	private readonly Gtk.CheckButton reset_layout;

	internal SettingsDialog (ChromeManager chrome, Values initial)
	{
		theme = CreateDropDown (initial.Theme,
			Translations.GetString ("Default"),
			Translations.GetString ("Light"),
			Translations.GetString ("Dark"));
		ruler_units = CreateDropDown (initial.RulerUnits,
			Translations.GetString ("Pixels"),
			Translations.GetString ("Inches"),
			Translations.GetString ("Centimeters"));
		image_width = CreateSpinButton (1, int.MaxValue, 1, initial.ImageWidth);
		image_height = CreateSpinButton (1, int.MaxValue, 1, initial.ImageHeight);
		image_background = CreateDropDown (initial.ImageBackground,
			Translations.GetString ("White"),
			Translations.GetString ("Transparent"),
			Translations.GetString ("Secondary Color"));
		brush_width = CreateSpinButton (1, 1e5, 1, initial.BrushWidth);
		brush_hardness = CreateSpinButton (0, 100, 1, initial.BrushHardness);
		pixel_grid_zoom = CreateSpinButton (100, 3600, 100, initial.PixelGridZoom);
		pad_opacity = CreateSpinButton (10, 100, 5, initial.PadOpacity);
		reset_layout = Gtk.CheckButton.NewWithLabel (Translations.GetString ("Reset the position and size of the Tools, History, Layers and Colors windows"));

		Gtk.Grid grid = new () {
			RowSpacing = SPACING,
			ColumnSpacing = SPACING * 2,
		};

		int row = 0;
		AddHeader (grid, ref row, Translations.GetString ("Appearance"));
		AddRow (grid, ref row, Translations.GetString ("Theme:"), theme, null);
		AddRow (grid, ref row, Translations.GetString ("Ruler units:"), ruler_units, null);
		AddRow (grid, ref row, Translations.GetString ("Show pixel grid from zoom:"), pixel_grid_zoom, "%");
		AddRow (grid, ref row, Translations.GetString ("Inactive window opacity:"), pad_opacity, "%");
		grid.Attach (reset_layout, 0, row++, 3, 1);

		AddHeader (grid, ref row, Translations.GetString ("New Image"));
		AddRow (grid, ref row, Translations.GetString ("Width:"), image_width, Translations.GetString ("pixels"));
		AddRow (grid, ref row, Translations.GetString ("Height:"), image_height, Translations.GetString ("pixels"));
		AddRow (grid, ref row, Translations.GetString ("Background:"), image_background, null);

		AddHeader (grid, ref row, Translations.GetString ("Brush"));
		AddRow (grid, ref row, Translations.GetString ("Brush width:"), brush_width, Translations.GetString ("pixels"));
		AddRow (grid, ref row, Translations.GetString ("Hardness:"), brush_hardness, "%");

		Gtk.Box contentArea = this.GetContentAreaBox ();
		contentArea.SetAllMargins (12);
		contentArea.Append (grid);

		Title = Translations.GetString ("Settings");
		TransientFor = chrome.MainWindow;
		Modal = true;
		IconName = Resources.StandardIcons.Preferences;

		this.AddCancelOkButtons ();
		this.SetDefaultResponse (Gtk.ResponseType.Ok);
	}

	internal Values Result => new (
		Theme: (int) theme.Selected,
		RulerUnits: (int) ruler_units.Selected,
		ImageWidth: image_width.GetValueAsInt (),
		ImageHeight: image_height.GetValueAsInt (),
		ImageBackground: (int) image_background.Selected,
		BrushWidth: brush_width.GetValueAsInt (),
		BrushHardness: brush_hardness.GetValueAsInt (),
		PixelGridZoom: pixel_grid_zoom.GetValueAsInt (),
		PadOpacity: pad_opacity.GetValueAsInt (),
		ResetLayout: reset_layout.Active);

	private static Gtk.DropDown CreateDropDown (int selected, params string[] items)
	{
		Gtk.DropDown dropDown = Gtk.DropDown.NewFromStrings (items);
		dropDown.Selected = (uint) selected;
		return dropDown;
	}

	private static Gtk.SpinButton CreateSpinButton (double min, double max, double step, int value)
	{
		Gtk.SpinButton spin = Gtk.SpinButton.NewWithRange (min, max, step);
		spin.Value = value;
		spin.SetActivatesDefaultImmediate (true);
		return spin;
	}

	private static void AddHeader (Gtk.Grid grid, ref int row, string text)
	{
		Gtk.Label label = Gtk.Label.New (text);
		label.Halign = Gtk.Align.Start;
		label.AddCssClass (AdwaitaStyles.Title4);
		label.MarginTop = row == 0 ? 0 : SPACING * 2;
		grid.Attach (label, 0, row++, 3, 1);
	}

	private static void AddRow (Gtk.Grid grid, ref int row, string text, Gtk.Widget widget, string? units)
	{
		Gtk.Label label = Gtk.Label.New (text);
		label.Halign = Gtk.Align.End;
		grid.Attach (label, 0, row, 1, 1);
		grid.Attach (widget, 1, row, 1, 1);
		grid.Attach (Gtk.Label.New (units), 2, row, 1, 1);
		row++;
	}

	internal readonly record struct Values (
		int Theme,
		int RulerUnits,
		int ImageWidth,
		int ImageHeight,
		int ImageBackground,
		int BrushWidth,
		int BrushHardness,
		int PixelGridZoom,
		int PadOpacity,
		bool ResetLayout);
}
