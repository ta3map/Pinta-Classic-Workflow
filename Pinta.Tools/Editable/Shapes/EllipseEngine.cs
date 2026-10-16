//
// EllipseEngine.cs
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

using System.Collections.Generic;
using System.Linq;
using Cairo;
using Pinta.Core;

namespace Pinta.Tools;

public sealed class EllipseEngine : ShapeEngine
{
	/// <summary>
	/// Create a new EllipseEngine.
	/// </summary>
	/// <param name="parentLayer">The parent UserLayer for the re-editable DrawingLayer.</param>
	/// <param name="drawingLayer">An existing ReEditableLayer to reuse. This is for cloning only. If not cloning, pass in null.</param>
	/// <param name="antialiasing">Whether or not antialiasing is enabled.</param>
	/// <param name="outlineColor">The outline color for the shape.</param>
	/// <param name="fillColor">The fill color for the shape.</param>
	/// <param name="brushWidth">The width of the outline of the shape.</param>
	/// <param name="lineCap">Defines the edge of the line drawn.</param>
	public EllipseEngine (
		UserLayer parentLayer,
		ReEditableLayer? drawingLayer,
		bool antialiasing,
		Color outlineColor,
		Color fillColor,
		int brushWidth,
		LineCap lineCap)
	: base (
		parentLayer,
		drawingLayer,
		BaseEditEngine.ShapeTypes.Ellipse,
		antialiasing,
		true,
		outlineColor,
		fillColor,
		brushWidth,
		lineCap)
	{ }

	private EllipseEngine (EllipseEngine src)
		: base (src)
	{
	}

	public override ShapeEngine Clone ()
	{
		return new EllipseEngine (this);
	}

	/// <summary>
	/// Generate each point in an elliptic shape and store the result in GeneratedPoints.
	/// <param name="brush_width">The width of the brush that will be used to draw the shape.</param>
	/// </summary>
	public override void GeneratePoints (int brush_width)
	{
		GeneratedPoints = [.. CreatePoints ()];
	}

	private IEnumerable<GeneratedPoint> CreatePoints ()
	{
		//The ellipse fills the bounding rectangle of the control points.
		PointD topLeft = new (ControlPoints.Min (p => p.Position.X), ControlPoints.Min (p => p.Position.Y));
		PointD bottomRight = new (ControlPoints.Max (p => p.Position.X), ControlPoints.Max (p => p.Position.Y));

		double width = bottomRight.X - topLeft.X;
		double height = bottomRight.Y - topLeft.Y;

		//Some elliptic math code taken from Cairo Extensions, and some from DocumentSelection code written for GSoC 2013.

		//Calculate an appropriate interval at which to increment t based on
		//the bounding rectangle's width and height properties. The increment
		//for t determines how many intermediate Points to calculate for the
		//ellipse. For each curve, t will go from tInterval to 1. The lower
		//the value of tInterval, the higher number of intermediate Points
		//that will be calculated and stored into the Polygon collection.
		double tInterval = .02d;

		double r_x = width / 2d; //1/2 of the bounding Rectangle Width.
		double r_y = height / 2d; //1/2 of the bounding Rectangle Height.

		//The middle of the bounding Rectangle...
		PointD c = new (
			X: topLeft.X + r_x, // ...Horizontally speaking
			Y: topLeft.Y + r_y); // ...Vertically speaking

		const double c_1 = 0.5522847498307933984022516322796d; //tan(pi / 8d) * 4d / 3d ~= 0.5522847498307933984022516322796d

		// Save first quadrant to later close the ellipse
		var first_quadrant = CalculateCurvePoints (
			tInterval,
			c.X + r_x, c.Y,
			c.X + r_x, c.Y - c_1 * r_y,
			c.X + c_1 * r_x, c.Y - r_y,
			c.X, c.Y - r_y,
			3);

		foreach (var p in first_quadrant)
			yield return p;

		foreach (
			var p in
			CalculateCurvePoints (
				tInterval,
				c.X, c.Y - r_y,
				c.X - c_1 * r_x, c.Y - r_y,
				c.X - r_x, c.Y - c_1 * r_y,
				c.X - r_x, c.Y,
				0
			)
		) yield return p;

		foreach (
			var p in
			CalculateCurvePoints (
				tInterval,
				c.X - r_x, c.Y,
				c.X - r_x, c.Y + c_1 * r_y,
				c.X - c_1 * r_x, c.Y + r_y,
				c.X, c.Y + r_y,
				1
			)
		) yield return p;

		foreach (
			var p in
			CalculateCurvePoints (
				tInterval,
				c.X, c.Y + r_y,
				c.X + c_1 * r_x, c.Y + r_y,
				c.X + r_x, c.Y + c_1 * r_y,
				c.X + r_x, c.Y,
				2
			)
		) yield return p;

		// Close the curve.
		// Do not close the curve if no dash pattern used, or else dash pattern wraps past the end of the ellipse
		if (!CairoExtensions.IsValidDashPattern (DashPattern)) {
			yield return first_quadrant.Take (2).Last ();
			// Closes the curve in more extreme, near-flat circle (width >>> height) cases.
			yield return first_quadrant.Take (3).Last ();
		}
	}

	/// <summary>
	/// Calculate each intermediate Point in the specified curve, returning Math.Round(1d / tInterval - 1d) number of Points.
	/// </summary>
	/// <param name="tInterval">The increment value for t (should be between 0-1).</param>
	/// <param name="x0">Starting point X (not included in the returned Point(s)).</param>
	/// <param name="y0">Starting point Y (not included in the returned Point(s)).</param>
	/// <param name="x1">Control point 1 X.</param>
	/// <param name="y1">Control point 1 Y.</param>
	/// <param name="x2">Control point 2 X.</param>
	/// <param name="y2">Control point 2 Y.</param>
	/// <param name="x3">Ending point X (included in the returned Point(s)).</param>
	/// <param name="y3">Ending point Y (included in the returned Point(s)).</param>
	/// <param name="cPIndex">The index of the previous ControlPoint to the generated points.</param>
	/// <returns></returns>
	private static IEnumerable<GeneratedPoint> CalculateCurvePoints (
		double tInterval,
		double x0, double y0,
		double x1, double y1,
		double x2, double y2,
		double x3, double y3,
		int cPIndex)
	{
		//Generates points of partial Polygon containing the calculated Points in the curve.
		for (double t = 0; t < 1d; t += tInterval) {
			//There are 3 "layers" in a cubic Bezier curve's calculation. These "layers"
			//must be calculated for each intermediate Point (for each value of t from
			//tInterval to 1d). The Points in each "layer" store [the distance between
			//two consecutive Points from the previous "layer" multiplied by the value
			//of t (which is between 0d-1d)] plus [the position of the first Point of
			//the two consecutive Points from the previous "layer"]. This must be
			//calculated for the X and Y of every consecutive Point in every layer
			//until the last Point possible is reached, which is the Point on the curve.

			//Note: the code below is an optimized version of the commented explanation above.

			double oneMinusT = 1d - t;
			double oneMinusTSquared = oneMinusT * oneMinusT;
			double oneMinusTCubed = oneMinusTSquared * oneMinusT;

			double tSquared = t * t;
			double tCubed = tSquared * t;

			double oneMinusTSquaredTimesTTimesThree = oneMinusTSquared * t * 3d;
			double oneMinusTTimesTSquaredTimesThree = oneMinusT * tSquared * 3d;

			yield return new (
				new PointD (
					X: oneMinusTCubed * x0 + oneMinusTSquaredTimesTTimesThree * x1 + oneMinusTTimesTSquaredTimesThree * x2 + tCubed * x3,
					Y: oneMinusTCubed * y0 + oneMinusTSquaredTimesTTimesThree * y1 + oneMinusTTimesTSquaredTimesThree * y2 + tCubed * y3),
				cPIndex
			);
		}
	}
}
