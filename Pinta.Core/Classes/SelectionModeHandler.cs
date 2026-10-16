// 
// SelectionModeHandler.cs
//  
// Author:
//       Andrew Davis <andrew.3.1415@gmail.com>
// 
// Copyright (c) 2013 Andrew Davis, GSoC 2013
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
using System.Collections.Generic;
using ClipperLib;

namespace Pinta.Core;

public sealed class SelectionModeHandler
{
	private Gtk.Label? selection_label;
	private Gtk.ToggleButton[]? mode_buttons;

	private CombineMode selected_mode;
	private readonly IReadOnlyList<(CombineMode Mode, string Tooltip, string Icon)> combine_modes;

	public SelectionModeHandler (SystemManager system)
	{
		combine_modes = [
			(CombineMode.Replace, Translations.GetString ("Replace"), Resources.Icons.SelectionReplace),
			// Translators: {0} is 'Ctrl', or a platform-specific key such as 'Command' on macOS.
			(CombineMode.Union, Translations.GetString ("Union (+) ({0} + Left Click)", system.CtrlLabel ()), Resources.Icons.SelectionUnion),
			(CombineMode.Exclude, Translations.GetString ("Exclude (-) (Right Click)"), Resources.Icons.SelectionExclude),
			// Translators: {0} is 'Ctrl', or a platform-specific key such as 'Command' on macOS.
			(CombineMode.Xor, Translations.GetString ("Xor ({0} + Right Click)", system.CtrlLabel ()), Resources.Icons.SelectionXor),
			// Translators: {0} is 'Alt', or a platform-specific key such as 'Option' on macOS.
			(CombineMode.Intersect, Translations.GetString ("Intersect ({0} + Left Click)", GtkExtensions.AltLabel ()), Resources.Icons.SelectionIntersect),
		];
	}

	public void BuildToolbar (Gtk.Box tb, ISettingsService settings)
	{
		selection_label ??= Gtk.Label.New (Translations.GetString (" Selection Mode: "));

		tb.Append (selection_label);

		mode_buttons ??= CreateModeButtons (settings.GetSetting (SettingNames.SELECTION_COMBINE_MODE, 0));

		foreach (Gtk.ToggleButton button in mode_buttons)
			tb.Append (button);
	}

	private Gtk.ToggleButton[] CreateModeButtons (int active)
	{
		Gtk.ToggleButton[] buttons = new Gtk.ToggleButton[combine_modes.Count];

		for (int i = 0; i < buttons.Length; i++) {
			var (mode, tooltip, icon) = combine_modes[i];
			Gtk.ToggleButton button = new () {
				IconName = icon,
				TooltipText = tooltip,
				Group = i == 0 ? null : buttons[0],
				CanFocus = false,
			};
			button.OnClicked += (_, _) => selected_mode = mode;
			buttons[i] = button;
		}

		int index = Math.Clamp (active, 0, buttons.Length - 1);
		buttons[index].Active = true;
		selected_mode = combine_modes[index].Mode;
		return buttons;
	}

	/// <summary>
	/// Determine the current combine mode - various combinations of left/right click
	/// and Ctrl/Shift can override the selected mode from the toolbar.
	/// </summary>
	public CombineMode DetermineCombineMode (ToolMouseEventArgs args)
	{
		switch (args.MouseButton) {
			case MouseButton.Left:
				if (args.IsControlPressed)
					return CombineMode.Union;
				else if (args.IsAltPressed)
					return CombineMode.Intersect;
				else
					return selected_mode;
			case MouseButton.Right:
				if (args.IsControlPressed)
					return CombineMode.Xor;
				else
					return CombineMode.Exclude;
			default:
				return selected_mode;
		}
	}

	public static void PerformSelectionMode (Document doc, CombineMode mode, List<List<IntPoint>> polygons)
	{
		doc.Selection = doc.PreviousSelection.Clone ();
		doc.Selection.Visible = true;

		//Make sure time isn't wasted if the CombineMode is Replace - Replace is much simpler than the other 4 selection modes.
		switch (mode) {
			case CombineMode.Replace:
				PerformSelectionReplace (doc, polygons);
				break;
			default:
				PerformSelectionWithMode (doc, mode, polygons);
				break;
		}

		doc.Selection.MarkDirty ();
	}

	private static void PerformSelectionWithMode (Document doc, CombineMode mode, List<List<IntPoint>> polygons)
	{
		List<List<IntPoint>> resultingPolygons = new ();

		//Specify the Clipper Subject (the previous Polygons) and the Clipper Clip (the new Polygons).
		//Note: for Union, ignore the Clipper Library instructions - the new polygon(s) should be Clips, not Subjects!
		doc.Selection.SelectionClipper.AddPaths (doc.Selection.SelectionPolygons, PolyType.ptSubject, true);
		doc.Selection.SelectionClipper.AddPaths (polygons, PolyType.ptClip, true);

		switch (mode) {
			case CombineMode.Xor:
				//Xor means "Combine both Polygon sets, but leave out any areas of intersection between the two."
				doc.Selection.SelectionClipper.Execute (ClipType.ctXor, resultingPolygons);
				break;
			case CombineMode.Exclude:
				//Exclude == Difference

				//Exclude/Difference means "Subtract any overlapping areas of the new Polygon set from the old Polygon set."
				doc.Selection.SelectionClipper.Execute (ClipType.ctDifference, resultingPolygons);
				break;
			case CombineMode.Intersect:
				//Intersect means "Leave only the overlapping areas between the new and old Polygon sets."
				doc.Selection.SelectionClipper.Execute (ClipType.ctIntersection, resultingPolygons);
				break;
			default:
				//Default should only be *CombineMode.Union*, but just in case...

				//Union means "Combine both Polygon sets, and keep any overlapping areas as well."
				doc.Selection.SelectionClipper.Execute (ClipType.ctUnion, resultingPolygons);
				break;
		}

		//After using Clipper, it has to be cleared so there are no conflicts with its next usage.
		doc.Selection.SelectionClipper.Clear ();

		//Set the resulting selection path to the calculated ("clipped") selection path.
		doc.Selection.SelectionPolygons = resultingPolygons;
	}

	private static void PerformSelectionReplace (Document doc, List<List<IntPoint>> polygons)
	{
		//Clear any previously stored Polygons.
		doc.Selection.SelectionPolygons.Clear ();

		//Set the resulting selection path to the new selection path.
		doc.Selection.SelectionPolygons = polygons;
	}

	public void OnSaveSettings (ISettingsService settings)
	{
		if (mode_buttons is not null)
			settings.PutSetting (SettingNames.SELECTION_COMBINE_MODE, Array.FindIndex (mode_buttons, b => b.Active));
	}
}

public enum CombineMode
{
	Union,
	Xor,
	Exclude,
	Replace,
	Intersect,
}
