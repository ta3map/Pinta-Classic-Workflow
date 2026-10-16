// 
// BaseBrushTool.cs
//  
// Author:
//       Joseph Hillenbrand <joehillen@gmail.com>
// 
// Copyright (c) 2010 Joseph Hillenbrand
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
using Cairo;
using Gtk;
using Pinta.Core;

namespace Pinta.Tools;

// This is a base class for brush type tools (paintbrush, eraser, etc)
public abstract class BaseBrushTool : BaseTool
{
	protected IPaletteService Palette { get; }

	protected ImageSurface? undo_surface;
	protected bool surface_modified;
	protected MouseButton mouse_button;

	protected BaseBrushTool (IServiceProvider services) : base (services)
	{
		Palette = services.GetService<IPaletteService> ();

		BrushWidthSpinButton.TooltipText = Translations.GetString ("Change brush width. Shortcut keys: [ ]");
		BrushWidthSpinButton.OnValueChanged += (_, _) => {
			Settings.PutSetting (SettingNames.BRUSH_WIDTH, BrushWidth);
			OnBrushWidthChanged ();
		};

		HardnessSlider.OnValueChanged += (_, _) => Settings.PutSetting (SettingNames.BRUSH_HARDNESS, Hardness);
	}

	protected int Hardness => (int) HardnessSlider.GetValue ();

	protected override bool ShowAntialiasingButton => true;

	protected int BrushWidth {
		get => brush_width?.GetValueAsInt () ?? DEFAULT_BRUSH_WIDTH;
		set {
			if (brush_width is not null)
				brush_width.Value = value;
		}
	}

	protected override void OnBuildToolBar (Box tb)
	{
		base.OnBuildToolBar (tb);

		tb.Append (BrushWidthLabel);
		tb.Append (BrushWidthPresets);
		tb.Append (BrushWidthSpinButton);
		tb.Append (HardnessLabel);
		tb.Append (HardnessSlider);
	}

	protected override void OnActivated (Document? document)
	{
		base.OnActivated (document);

		BrushWidth = Settings.GetSetting (SettingNames.BRUSH_WIDTH, DEFAULT_BRUSH_WIDTH);
		HardnessSlider.SetValue (Settings.GetSetting (SettingNames.BRUSH_HARDNESS, DEFAULT_BRUSH_HARDNESS));
	}

	/// <summary>
	/// Stamps soft round dabs from the previous dab towards the given point,
	/// using the current source and operator of the context.
	/// </summary>
	protected RectangleI StampSoftLine (Context g, PointD to)
	{
		if (last_dab is null)
			StampDab (g, to);

		PointD from = last_dab ?? to;
		PointD dab = from;
		double spacing = Math.Max (1, BrushWidth * (1 + 4 * Hardness / 100.0) / 20.0);

		while (dab.Distance (to) >= spacing) {
			dab = Utility.Lerp (dab, to, (float) (spacing / dab.Distance (to)));
			StampDab (g, dab);
		}

		last_dab = dab;

		int padding = BrushWidth / 2 + 2;
		return RectangleI.FromPoints (from.ToInt (), to.ToInt ()).Inflated (padding, padding);
	}

	private void StampDab (Context g, PointD center)
	{
		RadialGradient mask = new (center.X, center.Y, 0, center.X, center.Y, BrushWidth / 2.0) { Extend = Extend.None };
		double hardness = Hardness / 100.0;
		mask.AddColorStop (0, new Color (0, 0, 0, 1));
		for (int i = 0; i <= FALLOFF_STOPS; i++) {
			double s = (double) i / FALLOFF_STOPS;
			mask.AddColorStop (hardness + (1 - hardness) * s, new Color (0, 0, 0, Math.Pow (1 - s, 3)));
		}
		g.Mask (mask);
	}

	protected override void OnMouseDown (Document document, ToolMouseEventArgs e)
	{
		// If we are already drawing, ignore any additional mouse down events
		if (mouse_button != MouseButton.None)
			return;

		surface_modified = false;
		undo_surface = document.Layers.CurrentUserLayer.Surface.Clone ();
		mouse_button = e.MouseButton;

		OnMouseMove (document, e);
	}

	protected override void OnMouseUp (Document document, ToolMouseEventArgs e)
	{
		if (undo_surface != null && surface_modified) {
			document.History.PushNewItem (new SimpleHistoryItem (Icon, Name, undo_surface, document.Layers.CurrentUserLayerIndex));
		}

		surface_modified = false;
		undo_surface = null;
		mouse_button = MouseButton.None;
		last_dab = null;
	}

	protected override bool OnKeyDown (Document document, ToolKeyEventArgs e)
	{
		switch (e.Key.Value) {
			case Gdk.Constants.KEY_bracketleft:
				BrushWidth--;
				return true;
			case Gdk.Constants.KEY_bracketright:
				BrushWidth++;
				return true;
		}

		return base.OnKeyDown (document, e);
	}

	protected virtual void OnBrushWidthChanged ()
	{
		// Change the cursor when the BrushWidth is changed.
		SetCursor (DefaultCursor);
	}


	private const int FALLOFF_STOPS = 8;

	private PointD? last_dab;
	private SpinButton? brush_width;
	private Label? brush_width_label;
	private MenuButton? brush_width_presets;
	private Scale? hardness_slider;
	private Label? hardness_label;

	protected SpinButton BrushWidthSpinButton => brush_width ??= GtkExtensions.CreateToolBarSpinButton (1, 1e5, 1, Settings.GetSetting (SettingNames.BRUSH_WIDTH, DEFAULT_BRUSH_WIDTH));
	private Scale HardnessSlider => hardness_slider ??= GtkExtensions.CreateToolBarSlider (0, 100, 1, Settings.GetSetting (SettingNames.BRUSH_HARDNESS, DEFAULT_BRUSH_HARDNESS));
	private Label HardnessLabel => hardness_label ??= Label.New (string.Format (" {0}: ", Translations.GetString ("Hardness")));
	private MenuButton BrushWidthPresets => brush_width_presets ??= GtkExtensions.CreateToolBarPresetsButton (BrushWidthSpinButton, GtkExtensions.SizePresets);
	protected Label BrushWidthLabel => brush_width_label ??= Label.New (string.Format (" {0}: ", Translations.GetString ("Brush width")));
}
