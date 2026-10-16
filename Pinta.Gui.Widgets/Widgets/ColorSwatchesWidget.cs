using System;
using Cairo;
using Pinta.Core;

namespace Pinta.Gui.Widgets;

/// <summary>
/// The primary and secondary color swatches, with the swap and reset buttons.
/// </summary>
public sealed class ColorSwatchesWidget : Gtk.DrawingArea
{
	private readonly RectangleD primary_rect = new (4, 3, 24, 24);
	private readonly RectangleD secondary_rect = new (17, 16, 24, 24);
	private readonly RectangleD swap_rect = new (27, 2, 15, 15);
	private readonly RectangleD reset_rect = new (2, 27, 15, 15);

	private readonly IPaletteService palette;

	/// <summary>
	/// Raised when the primary (true) or secondary (false) color swatch is clicked.
	/// </summary>
	public event EventHandler<bool>? ColorTargetClicked;

	public ColorSwatchesWidget (IPaletteService palette)
	{
		this.palette = palette;

		SetSizeRequest (44, 44);
		Valign = Gtk.Align.Center;
		HasTooltip = true;
		OnQueryTooltip += HandleQueryTooltip;
		SetDrawFunc ((_, g, _, _) => Draw (g));

		palette.PrimaryColorChanged += (_, _) => QueueDraw ();
		palette.SecondaryColorChanged += (_, _) => QueueDraw ();

		Gtk.GestureClick click = Gtk.GestureClick.New ();
		click.OnReleased += (_, e) => HandleClick (new PointD (e.X, e.Y));
		AddController (click);
	}

	private void HandleClick (PointD point)
	{
		switch (GetElementAtPoint (point)) {
			case WidgetElement.PrimaryColor:
				ColorTargetClicked?.Invoke (this, true);
				break;
			case WidgetElement.SecondaryColor:
				ColorTargetClicked?.Invoke (this, false);
				break;
			case WidgetElement.SwapColors:
				Color temp = palette.PrimaryColor;
				// Swapping should not trigger adding colors to recently used palette
				palette.SetColor (true, palette.SecondaryColor, false);
				palette.SetColor (false, temp, false);
				break;
			case WidgetElement.ResetColors:
				palette.PrimaryColor = new Color (0, 0, 0);
				palette.SecondaryColor = new Color (1, 1, 1);
				break;
		}
	}

	private void Draw (Context g)
	{
		using Pattern checkeredPattern = CairoExtensions.CreateTransparentBackgroundPattern (16);

		DrawSwatch (g, secondary_rect, palette.SecondaryColor, checkeredPattern);
		DrawSwatch (g, primary_rect, palette.PrimaryColor, checkeredPattern);

		GetStyleContext ().GetColor (out Gdk.RGBA fg_color);
		Color cairo_fg_color = fg_color.ToCairoColor ();
		DrawSwapIcon (g, cairo_fg_color);

		double square_size = 0.6 * reset_rect.Width;
		g.DrawRectangle (new RectangleD (reset_rect.Location (), square_size, square_size), cairo_fg_color, 1);
		g.FillRectangle (new RectangleD (reset_rect.Right - square_size, reset_rect.Bottom - square_size, square_size, square_size), cairo_fg_color);
	}

	private static void DrawSwatch (Context g, RectangleD rect, Color color, Pattern checkeredPattern)
	{
		if (color.A < 1)
			g.FillRectangle (rect, checkeredPattern);

		g.FillRectangle (rect, color);
		g.DrawRectangle (new RectangleD (rect.X + 1, rect.Y + 1, rect.Width - 2, rect.Height - 2), new Color (1, 1, 1), 1);
		g.DrawRectangle (rect, new Color (0, 0, 0), 1);
	}

	private void DrawSwapIcon (Context g, Color color)
	{
		const double ARROW_SIZE = 4;
		const double RADIUS = 11;
		const double OFFSET = 1;

		g.Save ();
		g.LineWidth = 1.5;
		g.SetSourceColor (color);

		PointD p1 = new (
			X: swap_rect.Left + RADIUS,
			Y: swap_rect.Bottom - OFFSET);

		g.MoveTo (p1.X, p1.Y);

		g.CurveTo (
			p1.X,
			p1.Y - RADIUS - 2,
			p1.X,
			p1.Y - RADIUS + OFFSET,
			swap_rect.Left + OFFSET,
			swap_rect.Bottom - RADIUS);

		g.MoveTo (p1.X - ARROW_SIZE, p1.Y - ARROW_SIZE);

		g.LineTo (p1.X, p1.Y);
		g.LineTo (p1.X + ARROW_SIZE, p1.Y - ARROW_SIZE);

		PointD p2 = new (
			X: swap_rect.Left + OFFSET,
			Y: swap_rect.Bottom - RADIUS);

		g.MoveTo (p2.X + ARROW_SIZE, p2.Y - ARROW_SIZE);

		g.LineTo (p2.X, p2.Y);
		g.LineTo (p2.X + ARROW_SIZE, p2.Y + ARROW_SIZE);

		g.Stroke ();

		g.Restore ();
	}

	private bool HandleQueryTooltip (object o, QueryTooltipSignalArgs args)
	{
		string? text = GetElementAtPoint (new PointD (args.X, args.Y)) switch {
			WidgetElement.PrimaryColor => Translations.GetString ("Click to edit primary color."),
			WidgetElement.SecondaryColor => Translations.GetString ("Click to edit secondary color."),
			WidgetElement.SwapColors => $"{Translations.GetString ("Click to switch between primary and secondary color.")} {Translations.GetString ("Shortcut key")}: X",
			WidgetElement.ResetColors => Translations.GetString ("Click to reset primary and secondary color."),
			_ => null,
		};

		args.Tooltip.SetText (text);
		return text != null;
	}

	private WidgetElement GetElementAtPoint (PointD point)
	{
		if (primary_rect.ContainsPoint (point))
			return WidgetElement.PrimaryColor;

		if (secondary_rect.ContainsPoint (point))
			return WidgetElement.SecondaryColor;

		if (swap_rect.ContainsPoint (point))
			return WidgetElement.SwapColors;

		if (reset_rect.ContainsPoint (point))
			return WidgetElement.ResetColors;

		return WidgetElement.Nothing;
	}

	private enum WidgetElement
	{
		Nothing,
		PrimaryColor,
		SecondaryColor,
		SwapColors,
		ResetColors,
	}
}
