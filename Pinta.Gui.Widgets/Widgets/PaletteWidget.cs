using System;
using Pinta.Core;

namespace Pinta.Gui.Widgets;

internal static class PaletteWidget
{
	internal const int PALETTE_ROWS = 2;
	internal const int SWATCH_SIZE = 13;
	internal const int PALETTE_MARGIN = 6;

	public static int GetSwatchAtLocation (
		IPaletteService palette,
		PointD point,
		RectangleD palette_bounds,
		bool recentColorPalette = false,
		int bands = 1)
	{
		int max =
			recentColorPalette
			? palette.RecentlyUsedColors.Count
			: palette.CurrentPalette.Colors.Count;

		// This could be more efficient, but is good enough for now
		for (int i = 0; i < max; i++)
			if (GetSwatchBounds (palette, i, palette_bounds, recentColorPalette, bands).ContainsPoint (point))
				return i;

		return -1;
	}

	/// <summary>
	/// Number of columns of the normal palette when it is folded into the given number of bands.
	/// </summary>
	public static int GetPaletteColumns (IPaletteService palette, int bands)
		=> Math.Max (1, (palette.CurrentPalette.Colors.Count + PALETTE_ROWS * bands - 1) / (PALETTE_ROWS * bands));

	public static RectangleD GetSwatchBounds (
		IPaletteService palette,
		int index,
		RectangleD palette_bounds,
		bool recentColorPalette = false,
		int bands = 1)
	{
		// Normal swatches are laid out like this:
		// 0 | 2 | 4 | 6
		// 1 | 3 | 5 | 7
		// When folded into several bands, each band continues below the previous one.
		// Recent swatches are laid out like this (it's less visually jarring as they change):
		// 0 | 1 | 2 | 3
		// 4 | 5 | 6 | 7

		// First we need to figure out what row and column the color is
		int recent_cols = palette.MaxRecentlyUsedColor / PALETTE_ROWS;
		int row = recentColorPalette ? index / recent_cols : index % PALETTE_ROWS;
		int col = recentColorPalette ? index % recent_cols : index / PALETTE_ROWS;

		int band_cols = recentColorPalette ? recent_cols : GetPaletteColumns (palette, bands);
		row += col / band_cols * PALETTE_ROWS;
		col %= band_cols;

		// Now we need to construct the bounds of that row/column
		double x = palette_bounds.X + (col * SWATCH_SIZE);
		double y = palette_bounds.Y + (row * SWATCH_SIZE);

		return new (x, y, SWATCH_SIZE, SWATCH_SIZE);
	}
}
