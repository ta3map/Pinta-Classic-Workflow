//
// StatusBarColorPaletteWidget.cs
//
// Author:
//       Jonathan Pobst <monkey@jpobst.com>
//
// Copyright (c) 2020 Jonathan Pobst
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
using System.Threading.Tasks;
using Cairo;
using Pinta.Core;

namespace Pinta.Gui.Widgets;

/// <summary>
/// The recently used colors and the current palette.
/// </summary>
public sealed class StatusBarColorPaletteWidget : Gtk.DrawingArea
{
	// The palette is folded into this many bands, one below the other, to keep the widget narrow.
	private const int BANDS = 2;

	private readonly IChromeService chrome;
	private readonly IPaletteService palette;
	private readonly ISystemService system;

	private RectangleD palette_rect;
	private RectangleD recent_palette_rect;

	public StatusBarColorPaletteWidget (IChromeService chrome, IPaletteService palette, ISystemService system)
	{
		this.chrome = chrome;
		this.palette = palette;
		this.system = system;

		HasTooltip = true;
		OnQueryTooltip += HandleQueryTooltip;

		palette.RecentColorsChanged += Palette_ColorChanged;
		palette.CurrentPalette.PaletteChanged += Palette_ColorChanged;

		int recent_cols = palette.MaxRecentlyUsedColor / PaletteWidget.PALETTE_ROWS;
		recent_palette_rect = new RectangleD (0, 1, PaletteWidget.SWATCH_SIZE * recent_cols, PaletteWidget.SWATCH_SIZE * PaletteWidget.PALETTE_ROWS);

		HeightRequest = PaletteWidget.SWATCH_SIZE * PaletteWidget.PALETTE_ROWS * BANDS + 2;
		UpdateLayout ();

		SetDrawFunc ((area, context, width, height) => Draw (context));

		// Handle mouse clicks.
		Gtk.GestureClick click_gesture = Gtk.GestureClick.New ();
		click_gesture.SetButton (0); // Listen for all mouse buttons.
		click_gesture.OnReleased += (_, e) => {
			HandleClick (new PointD (e.X, e.Y), click_gesture.GetCurrentButton (), click_gesture.GetCurrentEventState ());
			click_gesture.SetState (Gtk.EventSequenceState.Claimed);
		};
		AddController (click_gesture);
	}

	private async void HandleClick (PointD point, uint button, Gdk.ModifierType state)
	{
		switch (GetElementAtPoint (point)) {

			case WidgetElement.Palette:

				int index = PaletteWidget.GetSwatchAtLocation (palette, point, palette_rect, bands: BANDS);

				if (index < 0)
					break;

				bool isCtrlPressed = state.IsControlPressed ();
				if (button == GtkExtensions.MOUSE_RIGHT_BUTTON) {
					palette.SecondaryColor = palette.CurrentPalette.Colors[index];
				} else if (button == GtkExtensions.MOUSE_LEFT_BUTTON && !isCtrlPressed) {
					palette.PrimaryColor = palette.CurrentPalette.Colors[index];
				} else if (button == GtkExtensions.MOUSE_MIDDLE_BUTTON ||
					   (button == GtkExtensions.MOUSE_LEFT_BUTTON && isCtrlPressed)) {
					SingleColor pick = new (palette.CurrentPalette.Colors[index]);
					var colors = await GetUserChosenColor (
						pick,
						Translations.GetString ("Choose Palette Color"));

					if (colors != null)
						palette.CurrentPalette.SetColor (index, colors.Color);
				}

				break;

			case WidgetElement.RecentColorsPalette:

				int recent_index = PaletteWidget.GetSwatchAtLocation (palette, point, recent_palette_rect, true);

				if (recent_index < 0)
					break;

				Color recentColor = palette.RecentlyUsedColors.ElementAt (recent_index);

				if (button == GtkExtensions.MOUSE_RIGHT_BUTTON) {
					palette.SetColor (false, recentColor, false);
				} else if (button == GtkExtensions.MOUSE_LEFT_BUTTON) {
					palette.SetColor (true, recentColor, false);
				}

				break;
		}
	}

	private void Draw (Context g)
	{
		const int TILE_SIZE = 8;
		using Pattern checkeredPattern =
			CairoExtensions.CreateTransparentBackgroundPattern (TILE_SIZE);

		// Draw recently used color swatches
		var recent = palette.RecentlyUsedColors;

		for (int i = 0; i < recent.Count; i++) {

			RectangleD swatchBounds = PaletteWidget.GetSwatchBounds (palette, i, recent_palette_rect, true);
			Color recentColor = recent.ElementAt (i);

			if (recentColor.A < 1) // Only draw checkered pattern if there is transparency
				g.FillRectangle (swatchBounds, checkeredPattern);

			g.FillRectangle (swatchBounds, recentColor);
		}

		// Draw color swatches
		var currentPalette = palette.CurrentPalette;

		for (int i = 0; i < currentPalette.Colors.Count; i++) {

			RectangleD swatchBounds = PaletteWidget.GetSwatchBounds (palette, i, palette_rect, bands: BANDS);
			Color paletteColor = currentPalette.Colors[i];

			if (paletteColor.A < 1) // Only draw checkered pattern if there is transparency
				g.FillRectangle (swatchBounds, checkeredPattern);

			g.FillRectangle (swatchBounds, paletteColor);
		}

		g.Dispose ();
	}

	/// <summary>
	/// Size the palette area and the widget to fit all the palette colors.
	/// </summary>
	private void UpdateLayout ()
	{
		palette_rect = new RectangleD (
			recent_palette_rect.Right + PaletteWidget.PALETTE_MARGIN,
			1,
			PaletteWidget.SWATCH_SIZE * PaletteWidget.GetPaletteColumns (palette, BANDS),
			PaletteWidget.SWATCH_SIZE * PaletteWidget.PALETTE_ROWS * BANDS);

		WidthRequest = (int) palette_rect.Right;
	}

	/// <summary>
	/// Provide a custom tooltip based on the cursor location.
	/// </summary>
	private bool HandleQueryTooltip (object o, Gtk.Widget.QueryTooltipSignalArgs args)
	{
		string? text = null;
		PointD point = new (args.X, args.Y);

		switch (GetElementAtPoint (point)) {
			case WidgetElement.Palette:
				if (PaletteWidget.GetSwatchAtLocation (palette, point, palette_rect, bands: BANDS) >= 0) {
					// Translators: {0} is 'Ctrl', or a platform-specific key such as 'Command' on macOS.
					text = Translations.GetString ("Left click to set primary color. Right click to set secondary color. Middle click or press {0} and left click to choose palette color.",
						system.CtrlLabel ());
				}

				break;
			case WidgetElement.RecentColorsPalette:
				if (PaletteWidget.GetSwatchAtLocation (palette, point, recent_palette_rect, true) >= 0)
					text = Translations.GetString ("Left click to set primary color. Right click to set secondary color.");
				break;
		}

		args.Tooltip.SetText (text);
		return text != null;
	}

	private void Palette_ColorChanged (object? sender, EventArgs e)
	{
		UpdateLayout ();

		// Color change events may be received while the widget is minimized,
		// so we only call Invalidate() if the widget is shown.
		if (GetRealized ())
			QueueDraw ();
	}

	private async Task<SingleColor?> GetUserChosenColor (
		SingleColor colors,
		string title)
	{
		using ColorPickerDialog dialog = new (
			chrome.MainWindow,
			palette,
			colors,
			primarySelected: true,
			false,
			title);

		try {
			Gtk.ResponseType response = await dialog.RunAsync ();

			if (response != Gtk.ResponseType.Ok)
				return null;

			return (SingleColor) dialog.Colors;

		} finally {
			dialog.Destroy ();
		}
	}

	private WidgetElement GetElementAtPoint (PointD point)
	{
		if (palette_rect.ContainsPoint (point))
			return WidgetElement.Palette;

		if (recent_palette_rect.ContainsPoint (point))
			return WidgetElement.RecentColorsPalette;

		return WidgetElement.Nothing;
	}

	private enum WidgetElement
	{
		Nothing,
		Palette,
		RecentColorsPalette,
	}
}
