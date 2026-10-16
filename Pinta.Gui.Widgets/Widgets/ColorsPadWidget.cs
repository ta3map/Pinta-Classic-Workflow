using Cairo;
using Pinta.Core;

namespace Pinta.Gui.Widgets;

/// <summary>
/// Contents of the Colors window, modelled after Paint.NET: a color wheel for the
/// selected (primary or secondary) color, an expandable panel with hex, HSV, RGB
/// and alpha controls, and the palette below.
/// </summary>
public sealed class ColorsPadWidget : Gtk.Box
{
	private const string EXPANDED_SETTING = "colors-pad-expanded";
	private const int SLIDER_WIDTH = 150;

	private readonly IPaletteService palette;
	private readonly Gtk.DropDown target;
	private readonly Gtk.ToggleButton more_button;
	private readonly Gtk.Box sliders_box;
	private readonly ColorWheelWidget wheel;
	private readonly Gtk.Entry hex_entry;
	private readonly ColorPickerSlider[] sliders;

	// Set while the controls are refreshed from the palette, so their change events are not written back.
	private bool updating;

	public ColorsPadWidget (IChromeService chrome, IPaletteService palette, ISystemService system)
	{
		this.palette = palette;

		target = Gtk.DropDown.New (
			Gtk.StringList.New ([Translations.GetString ("Primary"), Translations.GetString ("Secondary")]),
			expression: null);
		Gtk.DropDown.SelectedPropertyDefinition.Notify (target, (_, _) => UpdateView ());

		more_button = new Gtk.ToggleButton ();
		more_button.OnToggled += (_, _) => SetExpanded (more_button.Active);

		wheel = new ColorWheelWidget ();
		wheel.ColorChanged += (_, _) => ApplyColor (wheel.Color, addToRecent: false);
		wheel.ColorCommitted += (_, _) => ApplyColor (wheel.Color, addToRecent: true);

		hex_entry = new Gtk.Entry { MaxWidthChars = 10, Hexpand = true };
		hex_entry.OnChanged += (_, _) => ApplyColor (Color.FromHex (hex_entry.GetText ()) ?? CurrentColor, addToRecent: false);

		Gtk.Label hexLabel = new () { Label_ = Translations.GetString ("Hex"), WidthRequest = 50 };

		sliders = [
			CreateSlider (ColorPickerSlider.Component.Hue),
			CreateSlider (ColorPickerSlider.Component.Saturation),
			CreateSlider (ColorPickerSlider.Component.Value),
			CreateSlider (ColorPickerSlider.Component.Red),
			CreateSlider (ColorPickerSlider.Component.Green),
			CreateSlider (ColorPickerSlider.Component.Blue),
			CreateSlider (ColorPickerSlider.Component.Alpha),
		];

		sliders_box = new Gtk.Box { Spacing = 2 };
		sliders_box.SetOrientation (Gtk.Orientation.Vertical);
		sliders_box.Append (GtkExtensions.BoxHorizontal ([hexLabel, hex_entry]));
		sliders_box.Append (sliders[0]);
		sliders_box.Append (sliders[1]);
		sliders_box.Append (sliders[2]);
		sliders_box.Append (new Gtk.Separator ());
		sliders_box.Append (sliders[3]);
		sliders_box.Append (sliders[4]);
		sliders_box.Append (sliders[5]);
		sliders_box.Append (new Gtk.Separator ());
		sliders_box.Append (Gtk.Label.New (Translations.GetString ("Transparency")));
		sliders_box.Append (sliders[6]);

		ColorSwatchesWidget swatches = new (palette);
		swatches.ColorTargetClicked += (_, primary) => target.Selected = primary ? 0u : 1u;

		StatusBarColorPaletteWidget paletteWidget = new (chrome, palette, system);

		palette.PrimaryColorChanged += (_, _) => UpdateView ();
		palette.SecondaryColorChanged += (_, _) => UpdateView ();

		swatches.Halign = Gtk.Align.Start;
		more_button.Valign = Gtk.Align.Start;
		Gtk.Box targetColumn = GtkExtensions.BoxVertical ([target, swatches]);
		targetColumn.Spacing = 2;
		Gtk.Box topRow = GtkExtensions.BoxHorizontal ([targetColumn, new Gtk.Box { Hexpand = true }, more_button]);
		Gtk.Box middleRow = GtkExtensions.BoxHorizontal ([wheel, sliders_box]);
		middleRow.Spacing = 6;

		// --- Initialization (Gtk.Box)

		SetOrientation (Gtk.Orientation.Vertical);
		Spacing = 4;
		Append (topRow);
		Append (middleRow);
		Append (paletteWidget);

		SetExpanded (false);
		UpdateView ();
	}

	private bool IsPrimary => target.Selected == 0;

	private Color CurrentColor => IsPrimary ? palette.PrimaryColor : palette.SecondaryColor;

	private ColorPickerSlider CreateSlider (ColorPickerSlider.Component component)
	{
		ColorPickerSlider slider = new (component, CurrentColor, SLIDER_WIDTH);
		slider.OnColorChanged += (_, _) => ApplyColor (slider.Color, addToRecent: false);
		return slider;
	}

	private void ApplyColor (Color color, bool addToRecent)
	{
		if (updating)
			return;

		palette.SetColor (IsPrimary, color, addToRecent);
	}

	private void UpdateView ()
	{
		updating = true;

		Color color = CurrentColor;
		wheel.Color = color;

		foreach (ColorPickerSlider slider in sliders)
			slider.Color = color;

		if (!hex_entry.IsEditingText ())
			hex_entry.SetText (color.ToHex ());

		updating = false;
	}

	private void SetExpanded (bool expanded)
	{
		sliders_box.Visible = expanded;
		more_button.Label = expanded
			? Translations.GetString ("<< Less")
			: Translations.GetString ("More >>");
	}

	public void LoadSettings (ISettingsService settings)
	{
		more_button.Active = settings.GetSetting (EXPANDED_SETTING, false);
	}

	public void SaveSettings (ISettingsService settings)
	{
		settings.PutSetting (EXPANDED_SETTING, more_button.Active);
	}
}
