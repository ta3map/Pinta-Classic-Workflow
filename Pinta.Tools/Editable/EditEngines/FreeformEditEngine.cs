using System;
using System.Collections.Generic;
using System.Linq;
using Cairo;
using Pinta.Core;

namespace Pinta.Tools;

public sealed class FreeformEditEngine : BaseEditEngine
{
	private const double PointSpacing = 3d;
	private const double SimplifyTolerance = 2d;

	protected override string ShapeName => Translations.GetString ("Freeform Shape");

	private bool drawing_new;

	private readonly IWorkspaceService workspace;
	public FreeformEditEngine (IServiceProvider services, ShapeTool owner) : base (services, owner)
	{
		workspace = services.GetService<IWorkspaceService> ();
	}

	protected override ShapeEngine CreateShape (bool ctrlKey, bool clickedOnControlPoint, PointD prevSelPoint)
	{
		LineCurveSeriesEngine newEngine = new (
			workspace.ActiveDocument.Layers.CurrentUserLayer,
			null,
			ShapeTypes.Freeform,
			owner.UseAntialiasing,
			true,
			BaseEditEngine.OutlineColor,
			BaseEditEngine.FillColor,
			owner.EditEngine.BrushWidth,
			LineCap.Round);

		PointD startingPoint = (ctrlKey && clickedOnControlPoint) ? prevSelPoint : shape_origin;
		newEngine.ControlPoints.Add (new ControlPoint (startingPoint, DefaultEndPointTension));
		SelectedPointIndex = 0;

		newEngine.DashPattern = dash_pattern_box.ComboBox!.ComboBox.GetActiveText ()!; // NRT - Code assumes this is not-null

		drawing_new = true;

		return newEngine;
	}

	protected override void MovePoint (List<ControlPoint> controlPoints)
	{
		if (!drawing_new) {
			base.MovePoint (controlPoints);
			return;
		}

		// While the shape is being drawn, the mouse path is recorded as control points.
		if (current_point.Distance (controlPoints[^1].Position) < PointSpacing)
			return;

		controlPoints.Add (new ControlPoint (current_point, DefaultEndPointTension));
		SelectedPointIndex = controlPoints.Count - 1;
	}

	public override void HandleMouseUp (Document document, ToolMouseEventArgs e)
	{
		if (drawing_new && ActiveShapeEngine is ShapeEngine engine) {
			List<PointD> points = engine.ControlPoints.Select (p => p.Position).ToList ();
			List<PointD> simplified = [.. Simplify (points, 0, points.Count - 1), points[^1]];

			engine.ControlPoints.Clear ();
			engine.ControlPoints.AddRange (simplified.Select (p => new ControlPoint (p, DefaultMidPointTension)));
			SelectedPointIndex = engine.ControlPoints.Count - 1;
		}

		drawing_new = false;

		base.HandleMouseUp (document, e);
	}

	/// <summary>
	/// Ramer-Douglas-Peucker simplification. Returns the kept points from first up to, but not including, last.
	/// </summary>
	private static IEnumerable<PointD> Simplify (List<PointD> points, int first, int last)
	{
		int farthest = -1;
		double maxDistance = SimplifyTolerance;

		for (int i = first + 1; i < last; i++) {
			double distance = DistanceToSegment (points[i], points[first], points[last]);

			if (distance > maxDistance) {
				maxDistance = distance;
				farthest = i;
			}
		}

		return farthest < 0
			? [points[first]]
			: Simplify (points, first, farthest).Concat (Simplify (points, farthest, last));
	}

	private static double DistanceToSegment (PointD p, PointD a, PointD b)
	{
		double dx = b.X - a.X;
		double dy = b.Y - a.Y;
		double lengthSquared = dx * dx + dy * dy;
		double t = lengthSquared == 0 ? 0 : Math.Clamp (((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared, 0, 1);

		return p.Distance (new PointD (a.X + t * dx, a.Y + t * dy));
	}
}
