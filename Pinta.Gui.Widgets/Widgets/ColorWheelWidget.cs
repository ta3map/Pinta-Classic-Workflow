using System;
using Cairo;
using Pinta.Core;

namespace Pinta.Gui.Widgets;

/// <summary>
/// A hue / saturation color wheel, like the one in Paint.NET's Colors window.
/// Picking a color from the wheel sets its hue and saturation at full value.
/// </summary>
public sealed class ColorWheelWidget : Gtk.DrawingArea
{
	private const int RADIUS = 75;
	private const int PADDING = 6;
	private const int CURSOR_SIZE = 10;

	private Color color;

	/// <summary>
	/// Raised continuously while the user drags over the wheel.
	/// </summary>
	public event EventHandler? ColorChanged;

	/// <summary>
	/// Raised when the user releases the mouse button.
	/// </summary>
	public event EventHandler? ColorCommitted;

	public ColorWheelWidget ()
	{
		int size = (RADIUS + PADDING) * 2;
		SetSizeRequest (size, size);
		Halign = Gtk.Align.Start;
		Valign = Gtk.Align.Start;
		SetDrawFunc ((_, g, _, _) => Draw (g));

		Gtk.GestureDrag drag = Gtk.GestureDrag.New ();
		drag.OnDragBegin += (_, args) => SetColorFromPoint (args.StartX, args.StartY);
		drag.OnDragUpdate += (_, args) => {
			drag.GetStartPoint (out double x, out double y);
			SetColorFromPoint (x + args.OffsetX, y + args.OffsetY);
		};
		drag.OnDragEnd += (_, _) => ColorCommitted?.Invoke (this, EventArgs.Empty);
		AddController (drag);
	}

	public Color Color {
		get => color;
		set {
			color = value;
			QueueDraw ();
		}
	}

	private void SetColorFromPoint (double x, double y)
	{
		double dx = x - PADDING - RADIUS;
		double dy = y - PADDING - RADIUS;

		double hue = (Math.Atan2 (dy, -dx) + Math.PI) / (2 * Math.PI) * 360;
		double sat = Math.Min (Math.Sqrt (dx * dx + dy * dy) / RADIUS, 1);

		Color = color.CopyHsv (hue: hue, sat: sat, value: 1);
		ColorChanged?.Invoke (this, EventArgs.Empty);
	}

	private void Draw (Context g)
	{
		int diameter = RADIUS * 2;

		using ImageSurface surface = CairoExtensions.CreateImageSurface (Format.Argb32, diameter, diameter);
		Span<ColorBgra> data = surface.GetPixelData ();

		for (int y = 0; y < diameter; y++) {
			for (int x = 0; x < diameter; x++) {
				int dx = x - RADIUS;
				int dy = y - RADIUS;
				int magnitudeSquared = dx * dx + dy * dy;

				if (magnitudeSquared > RADIUS * RADIUS)
					continue;

				double magnitude = Math.Sqrt (magnitudeSquared);
				double hue = (Math.Atan2 (dy, -dx) + Math.PI) / (2 * Math.PI) * 360;
				double sat = Math.Min (magnitude / RADIUS, 1);
				double alpha = Math.Min (RADIUS - magnitude, 1);

				data[diameter * y + x] = Color.FromHsv (hue, sat, 1, alpha).ToColorBgra ();
			}
		}

		surface.MarkDirty ();
		g.SetSourceSurface (surface, PADDING, PADDING);
		g.Paint ();

		HsvColor hsv = color.ToHsv ();
		double angle = hsv.Hue * Math.PI / 180;
		double cursorX = PADDING + RADIUS + Math.Cos (angle) * hsv.Sat * RADIUS;
		double cursorY = PADDING + RADIUS - Math.Sin (angle) * hsv.Sat * RADIUS;
		RectangleD cursor = new (cursorX - CURSOR_SIZE / 2, cursorY - CURSOR_SIZE / 2, CURSOR_SIZE, CURSOR_SIZE);

		g.Antialias = Antialias.None;
		g.FillRectangle (cursor, color);
		g.DrawRectangle (cursor, new Color (0, 0, 0), 4);
		g.DrawRectangle (cursor, new Color (1, 1, 1), 1);
	}
}
