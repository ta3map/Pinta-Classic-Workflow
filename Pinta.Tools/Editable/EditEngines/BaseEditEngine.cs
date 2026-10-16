//
// BaseEditEngine.cs
//
// Author:
//       Andrew Davis <andrew.3.1415@gmail.com>
//
// Copyright (c) 2014 Andrew Davis, GSoC 2014
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
using System.Collections.ObjectModel;
using System.Linq;
using Cairo;
using Pinta.Core;

namespace Pinta.Tools;

//The EditEngine was created for tools that wish to utilize any of the control point, line/curve, hover point (reacting to the mouse),
//and etc. code that was originally used in the LineCurveTool for editability. If a class wishes to use it, it should create and instantiate
//a protected instance of the EditEngine inside the class and then utilize it in a similar fashion to any of the editable tools.
public abstract class BaseEditEngine
{
	public enum ShapeTypes
	{
		OpenLineCurveSeries,
		ClosedLineCurveSeries,
		Ellipse,
		RoundedLineSeries,
		Freeform,
	}

	public static Dictionary<ShapeTypes, ShapeTool> CorrespondingTools { get; } = [];

	protected abstract string ShapeName { get; }

	protected readonly ShapeTool owner;

	protected bool is_drawing = false;

	protected RectangleD? last_dirty = null;

	protected PointD shape_origin;
	protected PointD current_point;

	public static Color OutlineColor {
		get => PintaCore.Palette.PrimaryColor;
		set => PintaCore.Palette.PrimaryColor = value;
	}

	public static Color FillColor {
		get => PintaCore.Palette.SecondaryColor;
		set => PintaCore.Palette.SecondaryColor = value;
	}

	// NRT - Created by HandleBuildToolBar
	protected ToolBarDropDownButton shape_type_button = null!;
	protected Gtk.Label shape_type_label = null!;

	protected Gtk.Label fill_label = null!;
	protected ToolBarDropDownButton fill_button = null!;
	protected Gtk.Separator fill_sep = null!;

	protected Gtk.SpinButton outline_width = null!;
	private Gtk.MenuButton? outline_width_presets;
	protected Gtk.Label outline_width_label = null!;
	protected Gtk.Separator outline_width_sep = null!;

	protected DashPatternBox dash_pattern_box = new ();
	private bool prev_antialiasing = true;

	public int BrushWidth {
		get => outline_width?.GetValueAsInt () ?? BaseTool.DEFAULT_BRUSH_WIDTH;
		set {
			if (outline_width is not null)
				outline_width.Value = value;
		}
	}

	private Gtk.Label? hardness_label;
	private Gtk.Scale? hardness_slider;

	protected int Hardness => (int) (hardness_slider?.GetValue () ?? BaseTool.DEFAULT_BRUSH_HARDNESS);

	private bool StrokeShape {
		get {
			if (fill_button.SelectedItem?.Tag is int value)
				return value % 2 == 0;

			return true;
		}
	}

	private bool FillShape {
		get {
			if (fill_button.SelectedItem?.Tag is int value)
				return value >= 1;

			return false;
		}
	}

	private ShapeTypes ShapeType {
		get {
			if (shape_type_button.SelectedItem?.Tag is int value)
				return (ShapeTypes) value;

			return 0;
		}
	}

	public const double ShapeClickStartingRange = 10d;
	public const double DefaultEndPointTension = 0d;
	public const double DefaultMidPointTension = 1d / 3d;

	public int SelectedPointIndex;
	public int SelectedShapeIndex;

	protected int prev_selected_shape_index;

	/// <summary>
	/// The selected ControlPoint.
	/// </summary>
	public ControlPoint? SelectedPoint {
		get {
			ShapeEngine? selEngine = SelectedShapeEngine;

			if (selEngine != null && SelectedPointIndex > -1 && selEngine.ControlPoints.Count > SelectedPointIndex)
				return selEngine.ControlPoints[SelectedPointIndex];
			else
				return null;
		}
	}

	/// <summary>
	/// The active shape's ShapeEngine. A point does not have to be selected here, only a shape. This can be null.
	/// </summary>
	public ShapeEngine? ActiveShapeEngine {
		get {
			if (SelectedShapeIndex > -1 && SEngines.Count > SelectedShapeIndex)
				return SEngines[SelectedShapeIndex];
			else
				return null;
		}
	}

	/// <summary>
	/// The selected shape's ShapeEngine. This requires that a point in the shape be selected (a frame is selected as a whole) and should be used in most cases. This can be null.
	/// </summary>
	public ShapeEngine? SelectedShapeEngine => (SelectedPointIndex > -1 || IsFrame) ? ActiveShapeEngine : null;

	/// <summary>
	/// Frame shapes always have 8 control points (corners and edge midpoints) and are edited like a selection.
	/// </summary>
	protected virtual bool IsFrame => false;

	/// <summary>
	/// Display the handles for all active shape engines' control points, along with the hover position
	/// </summary>
	public IEnumerable<IToolHandle> Handles =>
		SEngines.SelectMany (engine => engine.ControlPointHandles).Append (hover_handle);

	private readonly MoveHandle hover_handle;

	private readonly Gdk.Cursor grab_cursor = GdkExtensions.CursorFromName (Pinta.Resources.StandardCursors.Grab);

	protected bool changing_tension = false;
	protected PointD last_mouse_pos = new (0d, 0d);

	//Helps to keep track of the first modification on a shape after the mouse is clicked, to prevent unnecessary history items.
	//Stores the editable shape data.
	public static Collection<ShapeEngine> SEngines = [];

	#region ToolbarEventHandlers

	protected virtual void BrushMinusButtonClickedEvent (object? o, EventArgs args)
	{
		BrushWidth--;

		//No need to store previous settings or redraw, as this is done in the Changed event handler.
	}

	protected virtual void BrushPlusButtonClickedEvent (object? o, EventArgs args)
	{
		BrushWidth++;

		//No need to store previous settings or redraw, as this is done in the Changed event handler.
	}

	protected void Palette_PrimaryColorChanged (object? sender, EventArgs e)
	{
		ShapeEngine? activeEngine = ActiveShapeEngine;
		if (activeEngine == null) return;
		activeEngine.OutlineColor = OutlineColor;
		DrawActiveShape (false, false, true, false, false);
	}

	protected void Palette_SecondaryColorChanged (object? sender, EventArgs e)
	{
		ShapeEngine? activeEngine = ActiveShapeEngine;
		if (activeEngine == null) return;
		activeEngine.FillColor = FillColor;
		DrawActiveShape (false, false, true, false, false);
	}

	private void OnFillStyleChanged (object? sender, EventArgs e)
	{
		outline_width.Visible = outline_width_label.Visible = outline_width_sep.Visible = hardness_label!.Visible = hardness_slider!.Visible = StrokeShape;
		dash_pattern_box.SetVisible (StrokeShape);
		DrawActiveShape (false, false, true, false, false);
	}

	#endregion ToolbarEventHandlers

	private readonly IToolService tools;
	private readonly ActionManager actions;
	private readonly IPaletteService palette;
	private readonly IWorkspaceService workspace;
	private readonly ISettingsService settings;

	public BaseEditEngine (
		IServiceProvider services,
		ShapeTool passedOwner)
	{
		tools = services.GetService<IToolService> ();
		actions = services.GetService<ActionManager> ();
		palette = services.GetService<IPaletteService> ();
		workspace = services.GetService<IWorkspaceService> ();
		settings = services.GetService<ISettingsService> ();

		owner = passedOwner;

		hover_handle = new (workspace);

		ResetShapes ();
	}

	public virtual void OnSaveSettings (ISettingsService settings, string toolPrefix)
	{
		if (fill_button is not null)
			settings.PutSetting (SettingNames.FillStyle (toolPrefix), fill_button.SelectedIndex);

		if (shape_type_button is not null)
			settings.PutSetting (SettingNames.ShapeType (toolPrefix), shape_type_button.SelectedIndex);
	}

	public virtual void HandleBuildToolBar (Gtk.Box tb, ISettingsService settings, string toolPrefix)
	{
		if (shape_type_label == null) {
			string shapeTypeText = Translations.GetString ("Shape Type");
			shape_type_label = Gtk.Label.New ($" {shapeTypeText}: ");
		}

		tb.Append (shape_type_label);

		if (shape_type_button == null) {
			shape_type_button = new ToolBarDropDownButton ();

			shape_type_button.AddItem (Translations.GetString ("Open Line/Curve Series"), Resources.Icons.ToolLine, 0);
			shape_type_button.AddItem (Translations.GetString ("Closed Line/Curve Series"), Resources.Icons.ToolRectangle, 1);
			shape_type_button.AddItem (Translations.GetString ("Ellipse"), Resources.Icons.ToolEllipse, 2);
			shape_type_button.AddItem (Translations.GetString ("Rounded Line Series"), Resources.Icons.ToolRectangleRounded, 3);
			shape_type_button.AddItem (Translations.GetString ("Freeform Shape"), Resources.Icons.ToolFreeformShape, 4);

			shape_type_button.SelectedIndex = settings.GetSetting (
				SettingNames.ShapeType (toolPrefix),
				0);

			shape_type_button.SelectedItemChanged += (o, e) => {
				ShapeTypes newShapeType = ShapeType;
				ShapeEngine? selEngine = SelectedShapeEngine;

				//Verify that the tool needs to be switched.
				if (GetCorrespondingTool (newShapeType) == owner)
					return;

				if (selEngine == null) {
					ActivateCorrespondingTool (newShapeType, true);
					return;
				}

				//if shape is selected it will be converted to new shape and shape type will be changed, otherwise only shape type will be changed.

				//Clone the old shape; it should be automatically garbage-collected. newShapeType already has the updated value.
				selEngine = selEngine.Convert (newShapeType, SelectedShapeIndex);

				int previousSSI = SelectedShapeIndex;
				ActivateCorrespondingTool (selEngine.ShapeType, true);
				SelectedShapeIndex = previousSSI;
				//Draw the updated shape with organized points generation (for mouse detection).
				DrawActiveShape (true, false, true, false, true);
			};
		}

		shape_type_button.SelectedItem = shape_type_button.Items[(int) owner.ShapeType];

		tb.Append (shape_type_button);

		fill_sep ??= GtkExtensions.CreateToolBarSeparator ();

		tb.Append (fill_sep);

		if (fill_label == null) {
			string fillStyleText = Translations.GetString ("Fill Style");
			fill_label = Gtk.Label.New ($" {fillStyleText}: ");
		}

		tb.Append (fill_label);

		if (fill_button == null) {
			fill_button = new ToolBarDropDownButton ();

			fill_button.AddItem (Translations.GetString ("Outline Shape"), Resources.Icons.FillStyleOutline, 0);
			fill_button.AddItem (Translations.GetString ("Fill Shape"), Resources.Icons.FillStyleFill, 1);
			fill_button.AddItem (Translations.GetString ("Fill and Outline Shape"), Resources.Icons.FillStyleOutlineFill, 2);

			fill_button.SelectedIndex = settings.GetSetting (
				SettingNames.FillStyle (toolPrefix),
				0);
			fill_button.SelectedItemChanged += OnFillStyleChanged;
		}

		tb.Append (fill_button);

		outline_width_sep ??= GtkExtensions.CreateToolBarSeparator ();

		tb.Append (outline_width_sep);

		if (outline_width_label == null) {
			string brushWidthText = Translations.GetString ("Brush width");
			outline_width_label = Gtk.Label.New ($" {brushWidthText}: ");
		}

		tb.Append (outline_width_label);

		if (outline_width == null) {

			outline_width = GtkExtensions.CreateToolBarSpinButton (
				1,
				1e5,
				1,
				settings.GetSetting (SettingNames.BRUSH_WIDTH, BaseTool.DEFAULT_BRUSH_WIDTH)
			);
			outline_width.TooltipText = Translations.GetString ("Change brush width. Shortcut keys: [ ]");

			outline_width.OnValueChanged += (o, e) => {
				settings.PutSetting (SettingNames.BRUSH_WIDTH, BrushWidth);

				ShapeEngine? selEngine = SelectedShapeEngine;
				if (selEngine == null) return;
				selEngine.BrushWidth = BrushWidth;
				StorePreviousSettings ();
				DrawActiveShape (false, false, true, false, false);
			};
		}

		outline_width_presets ??= GtkExtensions.CreateToolBarPresetsButton (outline_width, GtkExtensions.SizePresets);
		tb.Append (outline_width_presets);

		tb.Append (outline_width);

		hardness_label ??= Gtk.Label.New ($" {Translations.GetString ("Hardness")}: ");
		tb.Append (hardness_label);

		if (hardness_slider == null) {
			hardness_slider = GtkExtensions.CreateToolBarSlider (0, 100, 1, settings.GetSetting (SettingNames.BRUSH_HARDNESS, BaseTool.DEFAULT_BRUSH_HARDNESS));
			hardness_slider.OnValueChanged += (_, _) => {
				settings.PutSetting (SettingNames.BRUSH_HARDNESS, Hardness);
				DrawActiveShape (false, false, true, false, false);
			};
		}

		tb.Append (hardness_slider);

		Gtk.ComboBoxText? dpbBox = dash_pattern_box.SetupToolbar (tb);

		outline_width.Visible = outline_width_label.Visible = outline_width_sep.Visible = hardness_label.Visible = hardness_slider.Visible = StrokeShape;
		dash_pattern_box.SetVisible (StrokeShape);

		if (dpbBox == null)
			return;

		dpbBox.GetEntry ().SetText (settings.GetSetting (SettingNames.DASH_PATTERN, "-"));

		dpbBox.OnChanged += (o, e) => {
			settings.PutSetting (SettingNames.DASH_PATTERN, dpbBox.GetActiveText ()!);

			ShapeEngine? selEngine = SelectedShapeEngine;
			if (selEngine == null) return;
			selEngine.DashPattern = dpbBox.GetActiveText ()!;
			StorePreviousSettings ();
			DrawActiveShape (false, false, true, false, false);
		};
	}

	public virtual void HandleActivated ()
	{
		RecallPreviousSettings ();

		palette.PrimaryColorChanged += Palette_PrimaryColorChanged;
		palette.SecondaryColorChanged += Palette_SecondaryColorChanged;
	}

	public virtual void HandleDeactivated (BaseTool? newTool)
	{
		SelectedPointIndex = -1;
		SelectedShapeIndex = -1;

		StorePreviousSettings ();

		//Determine if the tool being switched to will be another editable tool.
		if (workspace.HasOpenDocuments && !(newTool?.IsEditableShapeTool == true)) {
			//The tool being switched to is not editable. Finalize every editable shape not yet finalized.
			FinalizeAllShapes ();
		}

		palette.PrimaryColorChanged -= Palette_PrimaryColorChanged;
		palette.SecondaryColorChanged -= Palette_SecondaryColorChanged;
	}

	public virtual void HandleCommit ()
	{
		//Finalize every editable shape not yet finalized.
		FinalizeAllShapes ();
	}

	public virtual bool HandleBeforeUndo ()
		=> CancelShapes ();

	/// <summary>
	/// Discards the shapes that are not baked yet. Returns whether there was anything to discard.
	/// </summary>
	private bool CancelShapes ()
	{
		bool editing = SEngines.Count > 0;

		foreach (ShapeEngine engine in SEngines)
			engine.DrawingLayer.TryRemoveLayer ();

		ResetShapes ();
		actions.Edit.Undo.Sensitive = workspace.ActiveDocument.History.CanUndo;
		workspace.ActiveDocument.Workspace.Invalidate ();

		return editing;
	}

	public virtual bool HandleBeforeRedo ()
	{
		FinalizeAllShapes ();
		return false;
	}

	public virtual bool HandleKeyDown (Document document, ToolKeyEventArgs e)
	{
		Gdk.Key keyPressed = e.Key;
		switch (keyPressed.Value) {
			case Gdk.Constants.KEY_Delete:
				HandleDelete ();
				return true;
			case Gdk.Constants.KEY_Return:
			case Gdk.Constants.KEY_KP_Enter:
				FinalizeAllShapes ();
				return true;
			case Gdk.Constants.KEY_Escape:
				bool editing = SEngines.Count > 0;
				FinalizeAllShapes ();
				return editing;
			case Gdk.Constants.KEY_space:
				HandleSpace (e);
				return true;
			case Gdk.Constants.KEY_Up:
				NudgeSelectedPoint (0d, -1d);
				return true;
			case Gdk.Constants.KEY_Down:
				NudgeSelectedPoint (0d, 1d);
				return true;
			case Gdk.Constants.KEY_Left:
				HandleLeft (e);
				return true;
			case Gdk.Constants.KEY_Right:
				HandleRight (e);
				return true;
			case Gdk.Constants.KEY_bracketleft:
				BrushWidth--;
				return true;
			case Gdk.Constants.KEY_bracketright:
				BrushWidth++;
				return true;
			default:
				if (keyPressed.IsControlKey ()) {
					// Redraw since the Ctrl key affects the hover cursor, etc
					DrawActiveShape (false, false, true, e.IsShiftPressed, false, true);
					return true;
				} else {
					return false;
				}
		}
	}

	private void HandleRight (ToolKeyEventArgs e)
	{
		//Make sure a control point is selected.

		if (SelectedPointIndex < 0)
			return;

		if (e.IsControlPressed) {
			//Change the selected control point to be the following one.

			ShapeEngine? activeEngine = ActiveShapeEngine;

			if (activeEngine != null) {
				++SelectedPointIndex;

				if (SelectedPointIndex > activeEngine.ControlPoints.Count - 1)
					SelectedPointIndex = 0;

			}
		} else {
			NudgeSelectedPoint (1d, 0d);
		}

		DrawActiveShape (true, false, true, false, false);
	}

	private void HandleLeft (ToolKeyEventArgs e)
	{
		//Make sure a control point is selected.

		if (SelectedPointIndex < 0)
			return;

		if (e.IsControlPressed) {
			//Change the selected control point to be the previous one.

			--SelectedPointIndex;

			if (SelectedPointIndex < 0) {
				ShapeEngine? activeEngine = ActiveShapeEngine;

				if (activeEngine != null)
					SelectedPointIndex = activeEngine.ControlPoints.Count - 1;

			}
		} else {
			NudgeSelectedPoint (-1d, 0d);
		}

		DrawActiveShape (true, false, true, false, false);
	}

	/// <summary>
	/// Moves the selected control point by the given offset, the same way as dragging it with the mouse.
	/// </summary>
	private void NudgeSelectedPoint (double dx, double dy)
	{
		if (SelectedPoint is not ControlPoint selPoint)
			return;

		current_point = new PointD (selPoint.Position.X + dx, selPoint.Position.Y + dy);
		MovePoint (SelectedShapeEngine!.ControlPoints);

		DrawActiveShape (true, false, true, false, false);
	}

	private void HandleSpace (ToolKeyEventArgs e)
	{
		ControlPoint? selPoint = SelectedPoint;

		if (selPoint == null || IsFrame)
			return;

		//This can be assumed not to be null since selPoint was not null.
		ShapeEngine selEngine = SelectedShapeEngine!; // NRT - ^^

		bool shiftKey = e.IsShiftPressed;
		bool ctrlKey = e.IsControlPressed;

		PointD newPointPos;

		if (ctrlKey) {
			//Ctrl + space combo: same position as currently selected point.
			newPointPos = new PointD (selPoint.Position.X, selPoint.Position.Y);
		} else {
			shape_origin = new PointD (selPoint.Position.X, selPoint.Position.Y);

			if (shiftKey) {
				CalculateModifiedCurrentPoint ();
			}

			//Space only: position of mouse (after any potential shift alignment).
			newPointPos = new PointD (current_point.X, current_point.Y);
		}

		//Place the new point on the outside-most end, order-wise.
		if (SelectedPointIndex < selEngine.ControlPoints.Count / 2d) {
			selEngine.ControlPoints.Insert (SelectedPointIndex,
			    new ControlPoint (new PointD (newPointPos.X, newPointPos.Y), DefaultMidPointTension));
		} else {
			selEngine.ControlPoints.Insert (SelectedPointIndex + 1,
			    new ControlPoint (new PointD (newPointPos.X, newPointPos.Y), DefaultMidPointTension));

			++SelectedPointIndex;
		}

		DrawActiveShape (true, false, true, shiftKey, false, e.IsControlPressed);
	}

	private void HandleDelete ()
	{
		if (ActiveShapeEngine is not ShapeEngine selEngine || (SelectedPointIndex < 0 && !IsFrame))
			return;

		List<ControlPoint> controlPoints = selEngine.ControlPoints;

		//Either delete a ControlPoint or an entire shape (if there's only 1 ControlPoint left, or the shape is a frame).
		if (controlPoints.Count > 1 && !IsFrame) {
			//Delete the selected point from the shape.
			controlPoints.RemoveAt (SelectedPointIndex);

			//Set the newly selected point to be the median-most point on the shape, order-wise.
			if (SelectedPointIndex > controlPoints.Count / 2)
				--SelectedPointIndex;

		} else {
			//Since the shape itself will be deleted, remove its ReEditableLayer from the drawing loop.

			ReEditableLayer removeMe = SEngines.ElementAt (SelectedShapeIndex).DrawingLayer;

			if (removeMe.InTheLoop)
				SEngines.ElementAt (SelectedShapeIndex).DrawingLayer.TryRemoveLayer ();

			//Delete the selected shape.
			SEngines.RemoveAt (SelectedShapeIndex);

			//Redraw the workspace.
			workspace.ActiveDocument.Workspace.Invalidate ();

			SelectedPointIndex = -1;
			SelectedShapeIndex = -1;
		}

		DrawActiveShape (true, false, true, false, false);
	}

	public virtual bool HandleKeyUp (Document document, ToolKeyEventArgs e)
	{
		Gdk.Key keyReleased = e.Key;

		if (keyReleased.IsControlKey ())
			DrawActiveShape (false, false, true, e.IsShiftPressed, false, false);

		switch (keyReleased.Value) {
			case Gdk.Constants.KEY_Delete:
			case Gdk.Constants.KEY_Return:
			case Gdk.Constants.KEY_KP_Enter:
			case Gdk.Constants.KEY_Escape:
			case Gdk.Constants.KEY_space:
			case Gdk.Constants.KEY_Up:
			case Gdk.Constants.KEY_Down:
			case Gdk.Constants.KEY_Left:
			case Gdk.Constants.KEY_Right:
				return true;
			default:
				return false;
		}
	}

	public virtual void HandleMouseDown (Document document, ToolMouseEventArgs e)
	{
		PointD unclamped_point = e.PointDouble;

		//If we are already drawing, ignore any additional mouse down events.
		if (is_drawing) return;

		//Redraw the previously (and possibly currently) active shape without any control points in case another shape is made active.
		DrawActiveShape (false, false, false, false, false);

		Document doc = workspace.ActiveDocument;

		shape_origin = doc.ClampToImageSize (unclamped_point);
		current_point = shape_origin;

		bool shiftKey = e.IsShiftPressed;

		if (shiftKey)
			CalculateModifiedCurrentPoint ();

		is_drawing = true;

		//Right clicking changes tension. Frames have no tension.
		changing_tension = e.MouseButton != MouseButton.Left && !IsFrame;

		bool ctrlKey = e.IsControlPressed;

		SEngines.FindClosestControlPoint (
			unclamped_point,
			out int closestCPShapeIndex,
			out int closestCPIndex,
			out var closestControlPoint,
			out _);

		OrganizedPointCollection.FindClosestPoint (
			SEngines,
			unclamped_point,
			out int closestShapeIndex,
			out int closestPointIndex,
			out var closestPoint,
			out _);

		bool clicked_control_point = false;
		bool clicked_generated_point = false;

		PointD current_window_point = workspace.CanvasPointToView (unclamped_point);
		MoveHandle test_handle = new (workspace);

		// Check if the user is directly clicking on a control point.
		if (closestControlPoint != null) {
			test_handle.CanvasPosition = closestControlPoint.Position;
			clicked_control_point = test_handle.ContainsPoint (current_window_point);
			if (clicked_control_point) {
				SelectedPointIndex = closestCPIndex;
				SelectedShapeIndex = closestCPShapeIndex;
			}
		}

		// Otherwise, the user might have clicked on a generated point.
		if (!clicked_control_point && closestPoint.HasValue) {
			test_handle.CanvasPosition = closestPoint.Value;
			clicked_generated_point = test_handle.ContainsPoint (current_window_point);
		}

		if (!changing_tension && clicked_generated_point) {
			//Determine if the currently active tool matches the clicked on shape's corresponding tool, and if not, switch to it.
			if (ActivateCorrespondingTool (closestShapeIndex, true) != null) {
				//Pass on the event and its data to the newly activated tool.
				tools.DoMouseDown (document, e);

				//Don't do anything else here once the tool is switched and the event is passed on.
				return;
			}

			//The currently active tool matches the clicked on shape's corresponding tool.

			//Only create a new shape if the user isn't holding the control key down. Clicking a frame's outline only selects it.
			if (!ctrlKey && !IsFrame) {
				SEngines[closestShapeIndex].ControlPoints.Insert (closestPointIndex,
					new ControlPoint (new PointD (current_point.X, current_point.Y), DefaultMidPointTension));
			}

			SelectedPointIndex = IsFrame ? -1 : closestPointIndex;
			SelectedShapeIndex = closestShapeIndex;

			ShapeEngine? activeEngine = ActiveShapeEngine;

			if (activeEngine != null)
				UpdateToolbarSettings (activeEngine);
		}

		//Create a new shape if the user control + clicks on a shape or if the user simply clicks outside of any shapes.
		if (!changing_tension && (ctrlKey || (!clicked_control_point && !clicked_generated_point))) {
			PointD prevSelPoint;

			//First, store the position of the currently selected point.
			if (SelectedPoint != null && ctrlKey) {
				prevSelPoint = new PointD (SelectedPoint.Position.X, SelectedPoint.Position.Y);
			} else {
				//This doesn't matter, other than the fact that it gets set to a value in order for the code to build.
				prevSelPoint = new PointD (0d, 0d);
			}

			//Only one shape is edited at a time: the previous shapes are baked before a new one starts.
			FinalizeAllShapes ();
			is_drawing = true;

			//Create the shape, add its starting points, and add it to SEngines.
			SEngines.Add (CreateShape (ctrlKey, clicked_control_point, prevSelPoint));

			//Undo is available while a shape is not baked yet, even with an empty history: it discards the shape.
			actions.Edit.Undo.Sensitive = true;

			//Select the new shape.
			SelectedShapeIndex = SEngines.Count - 1;

			ShapeEngine? activeEngine = ActiveShapeEngine;

			if (activeEngine != null) {
				//Set the AntiAliasing.
				activeEngine.AntiAliasing = owner.UseAntialiasing;
			}

			StorePreviousSettings ();
		} else if (clicked_control_point) {
			//Since the user is not creating a new shape or control point but rather modifying an existing control point, it should be determined
			//whether the currently active tool matches the clicked on shape's corresponding tool, and if not, switch to it.
			if (ActivateCorrespondingTool (SelectedShapeIndex, true) != null) {
				//Pass on the event and its data to the newly activated tool.
				tools.DoMouseDown (document, e);

				//Don't do anything else here once the tool is switched and the event is passed on.
				return;
			}

			//The currently active tool matches the clicked on shape's corresponding tool.

			ShapeEngine? activeEngine = ActiveShapeEngine;

			if (activeEngine != null)
				UpdateToolbarSettings (activeEngine);
		}

		DrawActiveShape (false, false, true, shiftKey, false, e.IsControlPressed);
	}

	public virtual void HandleMouseUp (Document document, ToolMouseEventArgs e)
	{
		is_drawing = false;

		changing_tension = false;

		DrawActiveShape (true, false, true, e.IsShiftPressed, false, e.IsControlPressed);
	}

	public virtual void HandleMouseMove (Document document, ToolMouseEventArgs e)
	{
		current_point = e.PointDouble;
		bool shiftKey = e.IsShiftPressed;

		if (!is_drawing) {
			//Redraw the active shape to show a (temporary) highlighted control point (over any shape) when applicable.
			DrawActiveShape (false, false, true, shiftKey, false, e.IsControlPressed);
			last_mouse_pos = current_point;
			return;
		}

		Document doc = document;

		current_point = document.ClampToImageSize (current_point);

		if (shiftKey)
			CalculateModifiedCurrentPoint ();

		ControlPoint? selPoint = SelectedPoint;

		//Make sure a control point is selected.
		if (selPoint == null) {
			last_mouse_pos = current_point;
			return;
		}

		List<ControlPoint> controlPoints = SelectedShapeEngine!.ControlPoints; // NRT - Code assumes this is not-null

		if (!changing_tension) {
			//Moving a control point.

			//Make sure the control point was moved.
			if (current_point.X != selPoint.Position.X || current_point.Y != selPoint.Position.Y)
				MovePoint (controlPoints);

			DrawActiveShape (false, false, true, shiftKey, false, e.IsControlPressed);
			last_mouse_pos = current_point;
			return;
		}

		//Changing a control point's tension.

		//Unclamp the mouse position when changing tension.
		current_point = e.PointDouble;

		//Calculate the new tension based off of the movement of the mouse that's
		//perpendicular to the previous and following control points.

		PointD curPoint = selPoint.Position;
		PointD prevPoint, nextPoint;

		//Calculate the previous control point.
		if (SelectedPointIndex > 0) {
			prevPoint = controlPoints[SelectedPointIndex - 1].Position;
		} else {
			//There is none.
			prevPoint = curPoint;
		}

		//Calculate the following control point.
		if (SelectedPointIndex < controlPoints.Count - 1) {
			nextPoint = controlPoints[SelectedPointIndex + 1].Position;
		} else {
			//There is none.
			nextPoint = curPoint;
		}

		//The x and y differences are used as factors for the x and y change in the mouse position.
		double xDiff = prevPoint.X - nextPoint.X;
		double yDiff = prevPoint.Y - nextPoint.Y;
		double totalDiff = xDiff + yDiff;

		//Calculate the midpoint in between the previous and following points.
		PointD midPoint = new PointD ((prevPoint.X + nextPoint.X) / 2d, (prevPoint.Y + nextPoint.Y) / 2d);

		//Calculate the x change in the mouse position.
		double xChange =
			(curPoint.X <= midPoint.X)
			? current_point.X - last_mouse_pos.X
			: last_mouse_pos.X - current_point.X;

		//Calculate the y change in the mouse position.
		double yChange =
			(curPoint.Y <= midPoint.Y)
			? current_point.Y - last_mouse_pos.Y
			: last_mouse_pos.Y - current_point.Y;

		//Update the control point's tension.

		//Note: the difference factors are to be inverted for x and y change because this is perpendicular motion.
		controlPoints[SelectedPointIndex].Tension +=
			Math.Round (Math.Clamp ((xChange * yDiff + yChange * xDiff) / totalDiff, -1d, 1d)) / 50d;

		//Restrict the new tension to range from 0d to 1d.
		controlPoints[SelectedPointIndex].Tension = Math.Clamp (selPoint.Tension, 0d, 1d);

		DrawActiveShape (false, false, true, shiftKey, false, e.IsControlPressed);


		last_mouse_pos = current_point;
	}


	/// <summary>
	/// Draw the currently active shape.
	/// </summary>
	/// <param name="calculateOrganizedPoints">Whether to calculate the spatially organized
	/// points for mouse detection after drawing the shape.</param>
	/// <param name="finalize">Whether to finalize the drawing.</param>
	/// <param name="drawHoverSelection">Whether to draw any hover point or selected point.</param>
	/// <param name="shiftKey">Whether the shift key is being pressed. This is for width/height constraining/equalizing.</param>
	/// <param name="preventSwitchBack">Whether to prevent switching back to the old tool if a tool change is necessary.</param>
	public void DrawActiveShape (bool calculateOrganizedPoints, bool finalize, bool drawHoverSelection, bool shiftKey, bool preventSwitchBack, bool ctrl_key = false)
	{
		ShapeTool? oldTool = BaseEditEngine.ActivateCorrespondingTool (SelectedShapeIndex, calculateOrganizedPoints);

		//First, determine if the currently active tool matches the shape's corresponding tool, and if not, switch to it.
		if (oldTool != null) {
			//The tool has switched, so call DrawActiveShape again but inside that tool.
			if (tools.CurrentTool is ShapeTool tool)
				tool.EditEngine.DrawActiveShape (
				calculateOrganizedPoints, finalize, drawHoverSelection, shiftKey, preventSwitchBack);

			//Afterwards, switch back to the old tool, unless specified otherwise.
			if (!preventSwitchBack) {
				ActivateCorrespondingTool (oldTool.ShapeType, true);
			}

			return;
		}

		//The currently active tool should now match the shape's corresponding tool.

		BeforeDraw ();

		ShapeEngine? activeEngine = ActiveShapeEngine;

		if (activeEngine == null) {
			//No shape will be drawn; however, the hover point still needs to be drawn if drawHoverSelection is true.
			UpdateHoverHandle (drawHoverSelection, ctrl_key);
			return;
		}

		//Clear any temporary drawing, because something new will be drawn.
		activeEngine.DrawingLayer.Layer.Clear ();

		RectangleD dirty;

		//Determine if the drawing should be for finalizing the shape onto the image or drawing it temporarily.
		if (finalize)
			dirty = DrawFinalized (activeEngine);
		else
			dirty = DrawUnfinalized (activeEngine, drawHoverSelection, shiftKey, ctrl_key);

		//Determine if the organized (spatially hashed) points should be generated. This is for mouse interaction detection after drawing.
		if (calculateOrganizedPoints)
			OrganizePoints (activeEngine);

		InvalidateAfterDraw (dirty);
	}

	/// <summary>
	/// Do not call. Use DrawActiveShape.
	/// </summary>
	private void BeforeDraw ()
	{
		//Check to see if a new shape is selected.
		if (prev_selected_shape_index == SelectedShapeIndex)
			return;

		//A new shape is selected, so clear the previous dirty Rectangle.
		last_dirty = null;

		prev_selected_shape_index = SelectedShapeIndex;
	}

	/// <summary>
	/// Do not call. Use DrawActiveShape.
	/// </summary>
	/// <param name="engine"></param>
	private RectangleD DrawFinalized (ShapeEngine engine)
		=> DrawShape (engine, workspace.ActiveDocument.Layers.CurrentUserLayer, false, false, false);

	/// <summary>
	/// Do not call. Use DrawActiveShape.
	/// </summary>
	/// <param name="engine"></param>
	/// <param name="dirty"></param>
	/// <param name="drawHoverSelection"></param>
	/// <param name="shiftKey"></param>
	private RectangleD DrawUnfinalized (ShapeEngine engine, bool drawHoverSelection, bool shiftKey, bool ctrl_key)
	{
		//Draw the shape onto the temporary DrawingLayer.
		return DrawShape (engine, engine.DrawingLayer.Layer, true, drawHoverSelection, ctrl_key);
	}

	/// <summary>
	/// Do not call. Use DrawActiveShape.
	/// </summary>
	/// <param name="engine"></param>
	private static void OrganizePoints (ShapeEngine engine)
	{
		//Organize the generated points for quick mouse interaction detection.

		//First, clear the previously organized points, if any.
		engine.OrganizedPoints.ClearCollection ();

		foreach (GeneratedPoint gp in engine.GeneratedPoints) {
			//For each generated point on the shape, calculate the spatial hashing for it and then store this information for later usage.
			engine.OrganizedPoints.StoreAndOrganizePoint (new OrganizedPoint (new PointD (gp.Position.X, gp.Position.Y), gp.ControlPointIndex));
		}
	}

	private void InvalidateAfterDraw (RectangleD dirty)
	{
		Document doc = workspace.ActiveDocument;

		// Increase the size of the dirty rect to account for antialiasing.
		if (owner.UseAntialiasing)
			dirty = dirty.Inflated (1, 1);

		//Combine, clamp, and invalidate the dirty Rectangle.
		if (last_dirty is not null)
			dirty = dirty.Union (last_dirty.Value);

		dirty = dirty.Clamped ();
		doc.Workspace.Invalidate (dirty.ToInt ());

		last_dirty = dirty;
	}


	protected RectangleD DrawShape (ShapeEngine engine, Layer l, bool drawCP, bool drawHoverSelection, bool ctrl_key)
	{
		ShapeEngine? activeEngine = ActiveShapeEngine;

		if (activeEngine == null)
			return RectangleD.Zero;

		Document doc = workspace.ActiveDocument;

		using Context g = new (l.Surface);

		g.AppendPath (doc.Selection.SelectionPath);
		g.FillRule = FillRule.EvenOdd;
		g.Clip ();

		g.Antialias = activeEngine.AntiAliasing ? Antialias.Subpixel : Antialias.None;

		bool isDashedLine = g.SetDashFromString (activeEngine.DashPattern, activeEngine.BrushWidth, LineCap.Square);

		g.LineWidth = activeEngine.BrushWidth;

		RectangleD? totalDirty = null;

		//Draw the shape.
		if (activeEngine.ControlPoints.Count > 0) {
			//Generate the points that make up the shape.
			activeEngine.GeneratePoints (activeEngine.BrushWidth);

			var points = activeEngine.GetActualPoints ();

			//Expand the invalidation rectangle as necessary.

			if (FillShape) {
				Color fillColor = StrokeShape ? activeEngine.FillColor : activeEngine.OutlineColor;
				RectangleD dirty = g.FillPolygonal (points.AsSpan (), fillColor);
				totalDirty = totalDirty?.Union (dirty) ?? dirty;
			}

			if (StrokeShape) {

				// dashpatterns cannot work with butt, so if we are using a dashpattern we default to square.
				LineCap lineCap =
					isDashedLine
					? LineCap.Square
					: activeEngine.LineCap;

				RectangleD dirty = g.DrawPolygonal (points.AsSpan (), activeEngine.OutlineColor, lineCap, Hardness / 100.0);
				totalDirty = totalDirty?.Union (dirty) ?? dirty;
			}
		}

		g.SetDash ([], 0.0);

		//Draw anything extra (that not every shape has), like arrows.
		DrawExtras (ref totalDirty, g, engine);
		DrawControlPoints (g, activeEngine, drawCP, drawHoverSelection, ctrl_key);

		return totalDirty ?? RectangleD.Zero;
	}

	private void DrawControlPoints (Context g, ShapeEngine shape, bool draw_controls, bool draw_selection, bool ctrl_key)
	{
		RectangleI dirty = MoveHandle.UnionInvalidateRects (shape.ControlPointHandles);
		shape.ControlPointHandles.Clear ();

		if (!draw_controls) {
			workspace.InvalidateWindowRect (dirty);
			return;
		}

		UpdateHoverHandle (draw_selection, ctrl_key);

		foreach (ControlPoint point in shape.ControlPoints) {

			//Skip drawing the control point if it is being hovered over.
			if (draw_selection && hover_handle.Active && hover_handle.CanvasPosition.DistanceSquared (point.Position) < 1d)
				continue;

			shape.ControlPointHandles.Add (
				new MoveHandle (workspace) {
					Active = true,
					CanvasPosition = point.Position,
					Selected = (point == SelectedPoint) && draw_selection
				}
			);
		}

		dirty = dirty.Union (MoveHandle.UnionInvalidateRects (shape.ControlPointHandles));

		workspace.InvalidateWindowRect (dirty);
	}

	/// <summary>
	/// Update the hover handle's position and redraw it.
	/// </summary>
	protected void UpdateHoverHandle (bool draw_selection, bool ctrl_key)
	{
		RectangleI dirty =
			hover_handle.Active
			? hover_handle.InvalidateRect
			: RectangleI.Zero;

		// Don't show the hover handle while the user is changing a control point's tension.
		hover_handle.Active = hover_handle.Selected = false;

		if (!changing_tension && draw_selection) {

			PointD current_window_point = workspace.CanvasPointToView (current_point);

			SEngines.FindClosestControlPoint (
				current_point,
				out _,
				out _,
				out var closestControlPoint,
				out _);

			// Check if the user is directly hovering over a control point.
			if (closestControlPoint != null) {
				hover_handle.CanvasPosition = closestControlPoint.Position;
				hover_handle.Active = hover_handle.Selected = hover_handle.ContainsPoint (current_window_point);
			}

			// Otherwise, the user may be hovering over a generated point.
			if (!hover_handle.Active) {

				OrganizedPointCollection.FindClosestPoint (
					SEngines,
					current_point,
					out _,
					out _,
					out var closestPoint,
					out _);

				if (closestPoint.HasValue) {
					hover_handle.CanvasPosition = closestPoint.Value;
					hover_handle.Active = hover_handle.ContainsPoint (current_window_point);
				}
			}

			if (hover_handle.Active)
				dirty = dirty.Union (hover_handle.InvalidateRect);
		}

		// Update the tool's cursor if we are hovering over a control point / generated point,
		// and Ctrl is not pressed (since Ctrl+click starts a new shape).
		// Otherwise, the normal cursor is shown to indicate that a shape can be drawn.
		var tool = tools.CurrentTool!;

		if (hover_handle.Active && !is_drawing && !ctrl_key)
			tool.SetCursor (grab_cursor);
		else
			tool.SetCursor (tool.DefaultCursor);

		workspace.InvalidateWindowRect (dirty);
	}

	/// <summary>
	/// Go through every editable shape and draw it.
	/// </summary>
	public void DrawAllShapes ()
	{
		//Store the SelectedShapeIndex value for later restoration.
		int previousToolSI = SelectedShapeIndex;

		//Draw all of the shapes.
		for (SelectedShapeIndex = 0; SelectedShapeIndex < SEngines.Count; ++SelectedShapeIndex) {
			//Only draw the selected point for the selected shape.
			DrawActiveShape (true, false, previousToolSI == SelectedShapeIndex, false, true);
		}

		//Restore the previous SelectedShapeIndex value.
		SelectedShapeIndex = previousToolSI;

		//Determine if the currently active tool matches the shape's corresponding tool, and if not, switch to it.
		BaseEditEngine.ActivateCorrespondingTool (SelectedShapeIndex, false);

		//The currently active tool should now match the shape's corresponding tool.
	}

	/// <summary>
	/// Go through every editable shape not yet finalized and finalize it.
	/// </summary>
	protected void FinalizeAllShapes ()
	{
		//Finalize every editable shape not yet finalized.

		if (SEngines.Count == 0)
			return;

		Document doc = workspace.ActiveDocument;

		ImageSurface undoSurface = doc.Layers.CurrentUserLayer.Surface.Clone ();

		RectangleD? totalDirty = null;

		//Finalize all of the shapes.
		for (SelectedShapeIndex = 0; SelectedShapeIndex < SEngines.Count; ++SelectedShapeIndex) {
			//Get a reference to each shape's corresponding tool.
			ShapeTool? correspondingTool = GetCorrespondingTool (SEngines[SelectedShapeIndex].ShapeType);

			if (correspondingTool == null)
				continue;

			//Finalize the now active shape using its corresponding tool's EditEngine.

			BaseEditEngine correspondingEngine = correspondingTool.EditEngine;

			correspondingEngine.SelectedShapeIndex = SelectedShapeIndex;

			correspondingEngine.BeforeDraw ();

			//Clear any temporary drawing, because something new will be drawn.
			SEngines[SelectedShapeIndex].DrawingLayer.Layer.Clear ();

			//Draw the current shape with the corresponding tool's EditEngine.
			RectangleD dirty = correspondingEngine.DrawFinalized (SEngines[SelectedShapeIndex]);
			totalDirty = totalDirty?.Union (dirty) ?? dirty;

			SEngines[SelectedShapeIndex].DrawingLayer.TryRemoveLayer ();
		}

		doc.History.PushNewItem (new SimpleHistoryItem (owner.Icon, owner.Name, undoSurface, doc.Layers.CurrentUserLayerIndex));

		if (totalDirty.HasValue) {
			InvalidateAfterDraw (totalDirty.Value);
		}

		//Clear out all of the data.
		ResetShapes ();
	}

	/// <summary>
	/// Constrain the current point to snap to fixed angles from the previous point, or to
	/// produce a square / circle when drawing those shape types.
	/// </summary>
	protected void CalculateModifiedCurrentPoint ()
	{
		ShapeEngine? selEngine = SelectedShapeEngine;

		//Don't bother calculating a modified point if there is no selected shape.
		if (selEngine == null)
			return;

		if (IsFrame) {

			// Constrain a corner to a square / circle. Edge handles are not constrained.
			if (SelectedPointIndex % 2 != 0)
				return;

			PointD origin = selEngine.ControlPoints[(SelectedPointIndex + 4) % 8].Position;

			PointD d = current_point - origin;

			double length = Math.Max (Math.Abs (d.X), Math.Abs (d.Y));

			PointD offset = new (
				X: length * Math.Sign (d.X),
				Y: length * Math.Sign (d.Y));

			current_point = origin + offset;

		} else {
			// Calculate the modified position of currentPoint such that the angle between the adjacent point
			// (if any) and currentPoint is snapped to the closest angle out of a certain number of angles.
			ControlPoint adjacentPoint;

			if (SelectedPointIndex > 0) {
				//Previous point.
				adjacentPoint = selEngine.ControlPoints[SelectedPointIndex - 1];
			} else if (selEngine.ControlPoints.Count > 1) {
				//Previous point (looping around to the end) if there is more than 1 point.
				adjacentPoint = selEngine.ControlPoints[^1];
			} else {
				//Don't bother calculating a modified point because there is no reference point to align it with (there is only 1 point).
				return;
			}

			PointD dir = new (
				X: current_point.X - adjacentPoint.Position.X,
				Y: current_point.Y - adjacentPoint.Position.Y);

			RadiansAngle baseTheta = new (Math.Atan2 (dir.Y, dir.X));

			double length = Utility.Magnitude (dir);

			RadiansAngle theta = new (Math.Round (12 * baseTheta.Radians / Math.PI) * Math.PI / 12);

			current_point = new PointD (
				X: adjacentPoint.Position.X + length * Math.Cos (theta.Radians),
				Y: adjacentPoint.Position.Y + length * Math.Sin (theta.Radians));
		}
	}

	/// <summary>
	/// Resets the editable data.
	/// </summary>
	protected void ResetShapes ()
	{
		SEngines = [];

		//The fields are modified instead of the properties here because a redraw call is undesired (for speed/efficiency).
		SelectedPointIndex = -1;
		SelectedShapeIndex = -1;

		is_drawing = false;

		last_dirty = null;
	}

	/// <summary>
	/// Activates the corresponding tool to the given shapeIndex value if the tool is not already active, and then returns the previous tool
	/// if a tool switch has occurred or null otherwise. If a switch did occur and this was called in e.g. an event handler, it should most
	/// likely pass the event data on to the newly activated tool (accessing it using PintaCore.Tools.CurrentTool) and then return.
	/// </summary>
	/// <param name="shapeIndex">The index of the shape in SEngines to find the corresponding tool to and switch to.</param>
	/// <param name="permanentSwitch">Whether the tool switch is permanent or just temporary (for drawing).</param>
	/// <returns>The *previous* tool if a tool switch has occurred or null otherwise.</returns>
	public static ShapeTool? ActivateCorrespondingTool (int shapeIndex, bool permanentSwitch)
	{
		//First make sure that there is a validly selectable tool.
		if (shapeIndex > -1 && SEngines.Count > shapeIndex)
			return ActivateCorrespondingTool (SEngines[shapeIndex].ShapeType, permanentSwitch);

		//Let the caller know that the active tool has not been switched.
		return null;
	}

	/// <summary>
	/// Activates the corresponding tool to the given shapeType value if the tool is not already active, and then returns the previous tool
	/// if a tool switch has occurred or null otherwise. If a switch did occur and this was called in e.g. an event handler, it should most
	/// likely pass the event data on to the newly activated tool (accessing it using PintaCore.Tools.CurrentTool) and then return.
	/// </summary>
	/// <param name="shapeType">The index of the shape in SEngines to find the corresponding tool to and switch to.</param>
	/// <param name="permanentSwitch">Whether the tool switch is permanent or just temporary (for drawing).</param>
	/// <returns>The *previous* tool if a tool switch has occurred or null otherwise.</returns>
	public static ShapeTool? ActivateCorrespondingTool (ShapeTypes shapeType, bool permanentSwitch)
	{
		ShapeTool? correspondingTool = GetCorrespondingTool (shapeType);

		//Verify that the corresponding tool is valid and that it doesn't match the currently active tool.
		if (correspondingTool == null || PintaCore.Tools.CurrentTool == correspondingTool) {
			//Let the caller know that the active tool has not been switched.
			return null;
		}

		ShapeTool? oldTool = PintaCore.Tools.CurrentTool as ShapeTool;

		int oldToolSPI = -1;
		int oldToolSSI = -1;
		//SetCurrentTool sets oldTool's SelectedPointIndex and SelectedShapeIndex to -1 so their value has to be saved before this happens.
		if (oldTool != null && oldTool.IsEditableShapeTool && permanentSwitch) {
			oldToolSPI = oldTool.EditEngine.SelectedPointIndex;
			oldToolSSI = oldTool.EditEngine.SelectedShapeIndex;
		}

		//The active tool needs to be switched to the corresponding tool.
		PintaCore.Tools.SetCurrentTool (correspondingTool);
		var newTool = (ShapeTool?) PintaCore.Tools.CurrentTool;

		// This shouldn't be possible, but we need a null check.
		if (newTool is null)
			return null;

		//What happens next depends on whether the old tool was an editable ShapeTool.
		if (oldTool != null && oldTool.IsEditableShapeTool) {

			if (permanentSwitch) {
				//Set the new tool's active shape and point to the old shape and point.
				newTool.EditEngine.SelectedPointIndex = oldToolSPI;
				newTool.EditEngine.SelectedShapeIndex = oldToolSSI;

				//Make sure neither tool thinks it is drawing anything.
				newTool.EditEngine.is_drawing = false;
				oldTool.EditEngine.is_drawing = false;
			}

			ShapeEngine? activeEngine = newTool.EditEngine.ActiveShapeEngine;

			if (activeEngine != null)
				newTool.EditEngine.UpdateToolbarSettings (activeEngine);

		} else {
			if (permanentSwitch) {
				//Make sure that the new tool doesn't think it is drawing anything.
				newTool.EditEngine.is_drawing = false;
			}
		}

		//Let the caller know that the active tool has been switched.
		return oldTool;
	}

	/// <summary>
	/// Gets the corresponding tool to the given shape type and then returns that tool.
	/// </summary>
	/// <param name="ShapeType">The shape type to find the corresponding tool to.</param>
	/// <returns>The corresponding tool to the given shape type.</returns>
	public static ShapeTool? GetCorrespondingTool (ShapeTypes shapeType)
	{

		//Get the corresponding BaseTool reference to the shape type.
		CorrespondingTools.TryGetValue (shapeType, out var correspondingTool);

		return correspondingTool;
	}


	/// <summary>
	/// Copy the given shape's settings to the toolbar settings. Calls StorePreviousSettings.
	/// </summary>
	/// <param name="engine"></param>
	public virtual void UpdateToolbarSettings (ShapeEngine engine)
	{
		owner.UseAntialiasing = engine.AntiAliasing;

		//Update the DashPatternBox to represent the current shape's DashPattern.
		dash_pattern_box.ComboBox!.ComboBox.GetEntry ().SetText (engine.DashPattern); // NRT - Code assumes this is not-null

		OutlineColor = engine.OutlineColor;
		FillColor = engine.FillColor;

		BrushWidth = engine.BrushWidth;

		StorePreviousSettings ();
	}

	/// <summary>
	/// Copy the previous settings to the toolbar settings.
	/// </summary>
	protected virtual void RecallPreviousSettings ()
	{
		dash_pattern_box.ComboBox?.ComboBox.GetEntry ().SetText (settings.GetSetting (SettingNames.DASH_PATTERN, "-"));

		owner.UseAntialiasing = prev_antialiasing;
		BrushWidth = settings.GetSetting (SettingNames.BRUSH_WIDTH, BaseTool.DEFAULT_BRUSH_WIDTH);
		hardness_slider?.SetValue (settings.GetSetting (SettingNames.BRUSH_HARDNESS, BaseTool.DEFAULT_BRUSH_HARDNESS));
	}

	/// <summary>
	/// Copy the toolbar settings to the previous settings.
	/// </summary>
	protected virtual void StorePreviousSettings ()
	{
		prev_antialiasing = owner.UseAntialiasing;
	}

	/// <summary>
	/// Creates a new shape, adds its starting points, and returns it.
	/// </summary>
	/// <param name="ctrlKey"></param>
	/// <param name="clickedOnControlPoint"></param>
	/// <param name="prevSelPoint"></param>
	protected abstract ShapeEngine CreateShape (bool ctrlKey, bool clickedOnControlPoint, PointD prevSelPoint);

	protected virtual void MovePoint (List<ControlPoint> controlPoints)
	{
		//Update the control point's position.
		controlPoints.ElementAt (SelectedPointIndex).Position = new PointD (current_point.X, current_point.Y);
	}

	protected virtual void DrawExtras (ref RectangleD? totalDirty, Context g, ShapeEngine engine)
	{

	}

	protected void AddLinePoints (bool ctrlKey, bool clickedOnControlPoint, ShapeEngine selEngine, PointD prevSelPoint)
	{
		PointD startingPoint;

		//Create the initial points of the shape. The second point will follow the mouse around until released.
		if (ctrlKey && clickedOnControlPoint) {
			startingPoint = prevSelPoint;
		} else {
			startingPoint = shape_origin;
		}


		selEngine.ControlPoints.Add (new ControlPoint (new PointD (startingPoint.X, startingPoint.Y), DefaultEndPointTension));
		selEngine.ControlPoints.Add (
			new ControlPoint (new PointD (startingPoint.X + .01d, startingPoint.Y + .01d), DefaultEndPointTension));


		SelectedPointIndex = 1;
		SelectedShapeIndex = SEngines.Count - 1;
	}

	protected void AddFramePoints (bool ctrlKey, bool clickedOnControlPoint, ShapeEngine selEngine, PointD prevSelPoint)
	{
		//The frame starts collapsed at the starting point. The bottom right corner follows the mouse around until released.
		PointD startingPoint = (ctrlKey && clickedOnControlPoint) ? prevSelPoint : shape_origin;

		selEngine.ControlPoints.AddRange (Enumerable.Range (0, 8).Select (_ => new ControlPoint (startingPoint, 0.0)));

		SelectedPointIndex = 4;
		SelectedShapeIndex = SEngines.Count - 1;
	}

	/// <summary>
	/// Moves a handle of a frame shape. The 8 control points go clockwise from the top left corner:
	/// corners have even indices, edge midpoints have odd indices.
	/// </summary>
	protected void MoveFramePoint (List<ControlPoint> controlPoints)
	{
		double left = controlPoints[0].Position.X;
		double top = controlPoints[0].Position.Y;
		double right = controlPoints[4].Position.X;
		double bottom = controlPoints[4].Position.Y;

		int i = SelectedPointIndex;

		if (i is 0 or 6 or 7)
			left = current_point.X;
		if (i is 2 or 3 or 4)
			right = current_point.X;
		if (i is 0 or 1 or 2)
			top = current_point.Y;
		if (i is 4 or 5 or 6)
			bottom = current_point.Y;

		SetFramePoints (controlPoints, left, top, right, bottom);
	}

	public static void SetFramePoints (List<ControlPoint> controlPoints, double left, double top, double right, double bottom)
	{
		double midX = (left + right) / 2;
		double midY = (top + bottom) / 2;

		PointD[] positions = [
			new (left, top), new (midX, top), new (right, top), new (right, midY),
			new (right, bottom), new (midX, bottom), new (left, bottom), new (left, midY)];

		controlPoints.Clear ();
		controlPoints.AddRange (positions.Select (p => new ControlPoint (p, 0.0)));
	}
}
