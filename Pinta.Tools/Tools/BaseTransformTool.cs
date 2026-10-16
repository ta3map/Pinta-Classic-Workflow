// 
// BaseTransformTool.cs
//  
// Author:
//       Volodymyr <${AuthorEmail}>
// 
// Copyright (c) 2012 Volodymyr
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
using System.Linq;
using Cairo;
using ClipperLib;
using Pinta.Core;

namespace Pinta.Tools;

public abstract class BaseTransformTool : BaseTool
{
	private const double ROTATE_HALO_WIDTH = 32;
	private readonly int rotate_steps = 32;
	private readonly Matrix transform = CairoExtensions.CreateIdentityMatrix ();
	private RectangleD source_rect;
	private PointD original_point;
	private bool is_dragging = false;
	private bool is_rotating = false;
	private bool is_scaling = false;
	private bool is_resizing = false;
	private bool using_mouse = false;
	private Gdk.Cursor? hover_cursor;
	private readonly IWorkspaceService workspace;
	private readonly RectangleHandle resize_handle;
	private readonly Gdk.Cursor rotate_cursor;

	/// <summary>
	/// Initializes a new instance of the <see cref="BaseTransformTool"/> class.
	/// </summary>
	public BaseTransformTool (IServiceProvider services) : base (services)
	{
		workspace = services.GetService<IWorkspaceService> ();
		resize_handle = new (workspace) { InvertIfNegative = true, ClampToImage = false };
		rotate_cursor = Gdk.Cursor.NewFromTexture (Resources.GetIcon (Pinta.Resources.Icons.ImageRotate90CW), 8, 8, null);
	}

	public override IEnumerable<IToolHandle> Handles {
		get {
			if (!resize_handle.IsDragging && workspace.HasOpenDocuments) {
				DocumentSelection selection = workspace.ActiveDocument.Selection;
				resize_handle.Rectangle = selection.GetBounds ();
				resize_handle.Active = selection.Visible;
			}
			return [resize_handle];
		}
	}

	protected override void OnMouseDown (
		Document document,
		ToolMouseEventArgs e)
	{
		if (IsActive)
			return;

		original_point = e.PointDouble;

		if (e.MouseButton == MouseButton.Left && resize_handle.Active && resize_handle.BeginDrag (e.PointDouble, document.ImageSize)) {
			is_resizing = true;
			using_mouse = true;
			OnStartTransform (document);
			return;
		}

		if (e.MouseButton == MouseButton.Left && InRotateHalo (document, e.PointDouble)) {
			is_rotating = true;
			using_mouse = true;
			OnStartTransform (document);
			return;
		}

		if (!document.Workspace.PointInCanvas (e.PointDouble))
			return;

		if (e.MouseButton == MouseButton.Right)
			is_rotating = true;
		else if (e.IsControlPressed)
			is_scaling = true;
		else
			is_dragging = true;

		using_mouse = true;

		OnStartTransform (document);
	}

	protected override void OnMouseMove (
		Document document,
		ToolMouseEventArgs e)
	{
		if (!IsActive)
			UpdateHoverCursor (document, e);

		if (!IsActive || !using_mouse)
			return;

		bool constrain = e.IsShiftPressed;

		PointD center = source_rect.GetCenter ();

		// The cursor position can be a subpixel value. Round to an integer
		// so that we only translate by entire pixels.
		// (Otherwise, blurring / anti-aliasing may be introduced)

		double dx = Math.Floor (e.PointDouble.X - original_point.X);
		double dy = Math.Floor (e.PointDouble.Y - original_point.Y);

		PointD c1 = original_point - center;
		PointD c2 = e.PointDouble - center;

		RadiansAngle angle = new (Math.Atan2 (c1.Y, c1.X) - Math.Atan2 (c2.Y, c2.X));

		transform.InitIdentity ();

		if (is_resizing) {

			resize_handle.UpdateDrag (e.PointDouble, constrain);
			PointD start = resize_handle.StartPoint;
			PointD end = resize_handle.EndPoint;

			transform.Translate (start.X, start.Y);
			transform.Scale (SignedScale (end.X - start.X, source_rect.Width), SignedScale (end.Y - start.Y, source_rect.Height));
			transform.Translate (-source_rect.X, -source_rect.Y);
		} else if (is_scaling) {

			double sx = (c1.X + dx) / c1.X;
			double sy = (c1.Y + dy) / c1.Y;

			if (constrain) {

				double max_scale = Math.Max (Math.Abs (sx), Math.Abs (sy));

				sx = max_scale * Math.Sign (sx);
				sy = max_scale * Math.Sign (sy);
			}

			transform.Translate (center.X, center.Y);
			transform.Scale (sx, sy);
			transform.Translate (-center.X, -center.Y);
		} else if (is_rotating) {

			if (constrain)
				angle = Utility.GetNearestStepAngle (angle, rotate_steps);

			transform.Translate (center.X, center.Y);
			transform.Rotate (-angle.Radians);
			transform.Translate (-center.X, -center.Y);

		} else {
			transform.Translate (dx, dy);
		}

		OnUpdateTransform (document, transform);
	}

	protected override void OnMouseUp (
		Document document,
		ToolMouseEventArgs e)
	{
		if (!IsActive || !using_mouse)
			return;

		OnFinishTransform (document, transform);
	}

	protected override bool OnKeyDown (
		Document document,
		ToolKeyEventArgs e)
	{
		if (using_mouse) // Don't handle the arrow keys while already interacting via the mouse.
			return base.OnKeyDown (document, e);

		double dx = 0.0;
		double dy = 0.0;

		switch (e.Key.Value) {
			case Gdk.Constants.KEY_Left:
				dx = -1;
				break;
			case Gdk.Constants.KEY_Right:
				dx = 1;
				break;
			case Gdk.Constants.KEY_Up:
				dy = -1;
				break;
			case Gdk.Constants.KEY_Down:
				dy = 1;
				break;
			default:
				// Otherwise, let the key be handled elsewhere.
				return base.OnKeyDown (document, e);
		}

		if (!IsActive) {
			is_dragging = true;
			OnStartTransform (document);
		}

		transform.Translate (dx, dy);
		OnUpdateTransform (document, transform);

		return true;
	}

	protected override bool OnKeyUp (
		Document document,
		ToolKeyEventArgs e)
	{
		if (IsActive && !using_mouse)
			OnFinishTransform (document, transform);

		return base.OnKeyUp (document, e);
	}

	protected abstract RectangleD GetSourceRectangle (Document document);

	protected virtual void OnStartTransform (Document document)
	{
		source_rect = GetSourceRectangle (document);
		transform.InitIdentity ();
	}

	protected virtual void OnUpdateTransform (
		Document document,
		Matrix transform)
	{ }

	protected virtual void OnFinishTransform (
		Document document,
		Matrix transform)
	{
		is_dragging = false;
		is_rotating = false;
		is_scaling = false;
		using_mouse = false;

		if (is_resizing)
			resize_handle.EndDrag ();

		is_resizing = false;
	}

	private bool IsActive
		=> is_dragging || is_rotating || is_scaling || is_resizing;

	protected override void OnActivated (Document? document)
	{
		base.OnActivated (document);
		hover_cursor = null;
	}

	private void UpdateHoverCursor (Document document, ToolMouseEventArgs e)
	{
		Gdk.Cursor? cursor = (resize_handle.Active ? resize_handle.GetCursorAtPoint (e.WindowPoint) : null)
			?? (InRotateHalo (document, e.PointDouble) ? rotate_cursor : null);

		if (cursor == hover_cursor)
			return;

		hover_cursor = cursor;
		SetCursor (cursor ?? DefaultCursor);
	}

	private static double SignedScale (double size, double source)
		=> (Math.Abs (size) < 1 ? Math.Sign (size) | 1 : size) / source;

	private static bool InRotateHalo (Document document, PointD point)
	{
		List<List<IntPoint>> polygons = document.Selection.SelectionPolygons;
		IntPoint pixel = new ((long) Math.Floor (point.X), (long) Math.Floor (point.Y));

		return document.Selection.Visible
			&& polygons.Any (p => p.Count > 1)
			&& polygons.Count (p => Clipper.PointInPolygon (pixel, p) != 0) % 2 == 0
			&& polygons.Where (p => p.Count > 1).Min (p => DistanceToPolygon (p, point)) <= ROTATE_HALO_WIDTH / document.Workspace.Scale;
	}

	private static double DistanceToPolygon (List<IntPoint> polygon, PointD point)
		=> polygon.Select ((a, i) => DistanceToSegment (point, a, polygon[(i + 1) % polygon.Count])).Min ();

	private static double DistanceToSegment (PointD p, IntPoint a, IntPoint b)
	{
		PointD start = new (a.X, a.Y);
		PointD end = new (b.X, b.Y);
		PointD delta = end - start;
		double length_squared = delta.X * delta.X + delta.Y * delta.Y;
		double t = length_squared == 0 ? 0 : Math.Clamp (((p.X - start.X) * delta.X + (p.Y - start.Y) * delta.Y) / length_squared, 0, 1);
		return p.Distance (Utility.Lerp (start, end, (float) t));
	}
}

