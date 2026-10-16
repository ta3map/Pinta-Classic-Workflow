//
// DocumentWorkspace.cs
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

using System;

namespace Pinta.Core;

public sealed class DocumentWorkspace
{
	private readonly Document document;

	private readonly ActionManager actions;
	private readonly ToolManager tools;

	private enum ZoomType
	{
		ZoomIn,
		ZoomOut,
		ZoomManually,
	}

	internal DocumentWorkspace (
		ActionManager actions,
		ToolManager tools,
		Document document)
	{
		this.actions = actions;
		this.tools = tools;

		this.document = document;

		History = new DocumentHistory (actions.Edit, document);
	}

	#region Public Events
	public event EventHandler<CanvasInvalidatedEventArgs>? CanvasInvalidated;
	public event EventHandler? ViewSizeChanged;
	#endregion

	#region Public Properties
	public Gtk.Widget Canvas { get; set; } = null!; // NRT - This is set soon after creation
	public Gtk.Widget CanvasWindow { get; set; } = null!; // NRT - This is set soon after creation

	/// <summary>
	/// Returns whether the zoomed image fits in the window without requiring scrolling.
	/// </summary>
	public bool ImageViewFitsInWindow {
		get {
			Gtk.Viewport view = (Gtk.Viewport) Canvas.Parent!;
			int window_x = view.GetAllocatedWidth ();
			int window_y = view.GetAllocatedHeight ();
			return ViewSize.Width <= window_x && ViewSize.Height <= window_y;
		}
	}

	/// <summary>
	/// Size of the zoomed image.
	/// </summary>
	public Size ViewSize {
		get => view_size;
		set {
			if (view_size == value) return;
			view_size = value;
			OnViewSizeChanged ();
		}
	}
	private Size view_size;

	public DocumentHistory History { get; }

	/// <summary>
	/// Returns whether the image (at 100% zoom) would fit in the window without requiring scrolling.
	/// </summary>
	public bool ImageFitsInWindow {
		get {
			Gtk.Viewport view = (Gtk.Viewport) Canvas.Parent!;
			int window_x = view.GetAllocatedWidth ();
			int window_y = view.GetAllocatedHeight ();
			return document.ImageSize.Width <= window_x && document.ImageSize.Height <= window_y;
		}
	}

	/// <summary>
	/// Scale factor for the zoomed image.
	/// </summary>
	public double Scale {
		get => ViewSize.Width / (double) document.ImageSize.Width;
		set {
			if (value == ViewSize.Width / (double) document.ImageSize.Width && value == ViewSize.Height / (double) document.ImageSize.Height)
				return;

			document.ImageSize = CoercedToPositive (document.ImageSize);
			ViewSize = GetNewViewSize (document.ImageSize, value);

			Invalidate ();

			if (tools.CurrentTool?.CursorChangesOnZoom == true) {
				//The current tool's cursor changes when the zoom changes.
				tools.CurrentTool.SetCursor (tools.CurrentTool.DefaultCursor);
			}
		}
	}

	/// <summary>
	/// Ensures that the size has a width and a height of at least 1
	/// </summary>
	private static Size CoercedToPositive (Size baseSize)
		=> new (
			Width: Math.Max (baseSize.Width, 1),
			Height: Math.Max (baseSize.Height, 1));

	private static Size GetNewViewSize (Size imageSize, double scale)
	{
		int new_x = Math.Max ((int) (imageSize.Width * scale), 1);
		int new_y = Math.Max ((int) ((long) new_x * imageSize.Height / imageSize.Width), 1);
		return new (new_x, new_y);
	}

	#endregion

	#region Public Methods
	public void Invalidate ()
	{
		OnCanvasInvalidated (new CanvasInvalidatedEventArgs ());
	}

	/// <summary>
	/// Repaints a rectangle region on the canvas.
	/// </summary>
	/// <param name='canvasRect'>
	/// The rectangle region of the canvas requiring repainting
	/// </param>
	public void Invalidate (RectangleI canvasRect)
	{
		OnCanvasInvalidated (new CanvasInvalidatedEventArgs (canvasRect));
	}

	/// <summary>
	/// Repaints a rectangle region in the window.
	/// Note that this overload uses window coordinates, whereas Invalidate() uses canvas coordinates.
	/// </summary>
	public void InvalidateWindowRect (RectangleI windowRect)
	{
		PointD windowTopLeft = new (windowRect.Left, windowRect.Top);
		PointD windowBtmRight = new (windowRect.Right + 1, windowRect.Bottom + 1);

		PointD canvasTopLeft = ViewPointToCanvas (windowTopLeft);
		PointD canvasBtmRight = ViewPointToCanvas (windowBtmRight);

		RectangleI canvasRect = RectangleD.FromPoints (canvasTopLeft, canvasBtmRight).ToInt ();
		OnCanvasInvalidated (new CanvasInvalidatedEventArgs (canvasRect));
	}

	/// <summary>
	/// Grabs focus to the canvas widget. This can be used to avoid leaving focus in
	/// toolbar widgets, for example.
	/// </summary>
	public void GrabFocusToCanvas ()
	{
		bool gained_focus = CanvasWindow.GrabFocus ();
		// Log a warning if something went wrong, e.g. there is a non-focusable widget
		// in the hierarchy.
		if (!gained_focus)
			Console.Error.WriteLine ("Failed to gain focus on the canvas widget!");
	}

	/// <summary>
	/// Determines whether the rectangle lies (at least partially) outside the canvas area.
	/// </summary>
	public bool IsPartiallyOffscreen (RectangleI rect)
		=> rect.IsEmpty || rect.Left < 0 || rect.Top < 0;

	public bool PointInCanvas (PointD point)
	{
		if (point.X < 0 || point.Y < 0)
			return false;

		if (point.X >= document.ImageSize.Width || point.Y >= document.ImageSize.Height)
			return false;

		return true;
	}

	public void RecenterView (PointD point)
	{
		Gtk.Viewport view = (Gtk.Viewport) Canvas.Parent!;

		ScrollCanvasPointTo (point, new PointD (view.Hadjustment!.PageSize / 2, view.Vadjustment!.PageSize / 2));
	}

	// The image is surrounded by free space of the visible area's size (see PintaCanvas.UpdateMargins),
	// so the image edge is at PageSize inside the scrollable area.

	/// <summary>
	/// Converts a point in the coordinates of the visible area (the canvas viewport) to canvas coordinates.
	/// </summary>
	public PointD VisiblePointToCanvas (PointD visible_point)
	{
		Gtk.Viewport view = (Gtk.Viewport) Canvas.Parent!;
		return new (
			(view.Hadjustment!.Value + visible_point.X - view.Hadjustment.PageSize) / Scale,
			(view.Vadjustment!.Value + visible_point.Y - view.Vadjustment.PageSize) / Scale);
	}

	private PointD CanvasPointToVisible (PointD canvas_point)
	{
		Gtk.Viewport view = (Gtk.Viewport) Canvas.Parent!;
		return new (
			canvas_point.X * Scale + view.Hadjustment!.PageSize - view.Hadjustment.Value,
			canvas_point.Y * Scale + view.Vadjustment!.PageSize - view.Vadjustment.Value);
	}

	/// <summary>
	/// Scrolls so that 'canvas_point' is shown at 'visible_point' of the visible area.
	/// The viewport updates the scroll range only on the next layout, so it is set here as well.
	/// </summary>
	private void ScrollCanvasPointTo (PointD canvas_point, PointD visible_point)
	{
		Gtk.Viewport view = (Gtk.Viewport) Canvas.Parent!;
		Gtk.Adjustment h_adjust = view.Hadjustment!;
		Gtk.Adjustment v_adjust = view.Vadjustment!;

		h_adjust.Upper = ViewSize.Width + 2 * h_adjust.PageSize;
		v_adjust.Upper = ViewSize.Height + 2 * v_adjust.PageSize;

		h_adjust.Value = canvas_point.X * Scale + h_adjust.PageSize - visible_point.X;
		v_adjust.Value = canvas_point.Y * Scale + v_adjust.PageSize - visible_point.Y;
	}

	public void ScrollCanvas (PointI delta)
	{
		Gtk.Viewport view = (Gtk.Viewport) Canvas.Parent!;

		var h_adjust = view.GetHadjustment ()!;
		h_adjust.Value = Math.Clamp (delta.X + h_adjust.Value, h_adjust.Lower, h_adjust.Upper - h_adjust.PageSize);

		var v_adjust = view.GetVadjustment ()!;
		v_adjust.Value = Math.Clamp (delta.Y + v_adjust.Value, v_adjust.Lower, v_adjust.Upper - v_adjust.PageSize);
	}

	/// <summary>
	/// Converts a point from image view coordinates to canvas coordinates
	/// </summary>
	/// <param name='x'>
	/// The X coordinate of the view point
	/// </param>
	/// <param name='y'>
	/// The Y coordinate of the view point
	/// </param>
	public PointD ViewPointToCanvas (PointD viewPoint)
	{
		Fraction<int> sf = ScaleFactor.CreateClamped (document.ImageSize.Width, ViewSize.Width);
		PointD pt = sf.ScalePoint (viewPoint);
		return new (pt.X, pt.Y);
	}

	/// <summary>
	/// Converts a point from canvas coordinates to view coordinates
	/// </summary>
	public PointD CanvasPointToView (PointD canvasPoint)
	{
		Fraction<int> sf = ScaleFactor.CreateClamped (document.ImageSize.Width, ViewSize.Width);
		return sf.UnscalePoint (canvasPoint);
	}

	public void ZoomIn ()
	{
		ZoomAroundCenter (ZoomType.ZoomIn);
	}

	public void ZoomOut ()
	{
		ZoomAroundCenter (ZoomType.ZoomOut);
	}

	/// <param name="visible_point">Point in the coordinates of the visible area (the canvas viewport)</param>
	public void ZoomInAroundVisiblePoint (in PointD visible_point)
	{
		ZoomAndRecenterView (ZoomType.ZoomIn, visible_point);
	}

	public void ZoomInAroundCanvasPoint (in PointD canvas_point)
	{
		ZoomInAroundVisiblePoint (CanvasPointToVisible (canvas_point));
	}

	/// <param name="visible_point">Point in the coordinates of the visible area (the canvas viewport)</param>
	public void ZoomOutAroundVisiblePoint (in PointD visible_point)
	{
		ZoomAndRecenterView (ZoomType.ZoomOut, visible_point);
	}

	public void ZoomOutAroundCanvasPoint (in PointD canvas_point)
	{
		ZoomOutAroundVisiblePoint (CanvasPointToVisible (canvas_point));
	}

	public void ZoomManually ()
	{
		ZoomAroundCenter (ZoomType.ZoomManually);
	}

	public void ZoomToCanvasRectangle (RectangleD rect)
	{
		double ratio =
			(document.ImageSize.Width / rect.Width <= document.ImageSize.Height / rect.Height)
			? document.ImageSize.Width / rect.Width
			: document.ImageSize.Height / rect.Height;

		actions.View.ZoomComboBox.ComboBox.GetEntry ().SetText (ViewActions.ToPercent (ratio));
		GLib.MainContext.Default ().Iteration (false); //Force update of scrollbar upper before recenter

		PointD newPoint = new (
			X: rect.X + rect.Width / 2,
			Y: rect.Y + rect.Height / 2);

		RecenterView (newPoint);
	}
	#endregion

	#region Private Methods
	private void OnCanvasInvalidated (CanvasInvalidatedEventArgs e)
	{
		CanvasInvalidated?.Invoke (this, e);
	}

	public void OnViewSizeChanged ()
	{
		ViewSizeChanged?.Invoke (this, EventArgs.Empty);
	}

	/// <summary>
	/// Zoom in/out around the center of the screen.
	/// </summary>
	private void ZoomAroundCenter (ZoomType zoomType)
	{
		Gtk.Viewport view = (Gtk.Viewport) Canvas.Parent!;
		ZoomAndRecenterView (zoomType, new PointD (view.Hadjustment!.PageSize / 2, view.Vadjustment!.PageSize / 2));
	}

	/// <summary>
	/// Zoom in/out keeping the image point under 'visible_point' at the same place on screen.
	/// Computed only from the scroll state and the scale, since the canvas widget allocation
	/// is not updated until the next layout (several scroll events can arrive before it).
	/// </summary>
	/// <param name="visible_point">Point to zoom around, in the coordinates of the visible area</param>
	private void ZoomAndRecenterView (ZoomType zoomType, PointD visible_point)
	{
		if (zoomType == ZoomType.ZoomOut && (ViewSize.Width == 1 || ViewSize.Height == 1))
			return; //Can't zoom in past a 1x1 px canvas

		if (!ViewActions.TryParsePercent (actions.View.ZoomComboBox.ComboBox.GetActiveText ()!, out var zoom))
			zoom = Scale * 100;

		zoom = Math.Min (zoom, 3600);

		actions.View.SuspendZoomUpdate ();

		PointD image_point = VisiblePointToCanvas (visible_point);

		if (zoomType == ZoomType.ZoomIn || zoomType == ZoomType.ZoomOut) {

			int i = 0;

			bool UpdateZoomLevel (string zoomInList)
			{
				if (!ViewActions.TryParsePercent (zoomInList, out var zoom_level))
					return false;

				switch (zoomType) {
					case ZoomType.ZoomIn:

						if (zoomInList == Translations.GetString ("Window") || zoom_level <= zoom) {
							actions.View.ZoomComboBox.ComboBox.Active = i - 1;
							return true;
						}

						break;

					case ZoomType.ZoomOut:

						if (zoomInList == Translations.GetString ("Window"))
							return true;

						if (zoom_level < zoom) {
							actions.View.ZoomComboBox.ComboBox.Active = i;
							return true;
						}

						break;
				}
				return false;
			}

			foreach (string item in actions.View.ZoomCollection) {

				if (UpdateZoomLevel (item))
					break;

				i++;
			}
		}

		actions.View.UpdateCanvasScale ();

		ScrollCanvasPointTo (image_point, visible_point);

		actions.View.ResumeZoomUpdate ();
	}

	#endregion
}
