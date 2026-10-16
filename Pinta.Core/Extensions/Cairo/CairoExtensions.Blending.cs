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

// Some functions are from Paint.NET:

/////////////////////////////////////////////////////////////////////////////////
// Paint.NET                                                                   //
// Copyright (C) dotPDN LLC, Rick Brewster, Tom Jackson, and contributors.     //
// Portions Copyright (C) Microsoft Corporation. All Rights Reserved.          //
// See license-pdn.txt for full licensing and attribution details.             //
/////////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.Linq;
using Cairo;

namespace Pinta.Core;

partial class CairoExtensions
{
	public static void BlendSurface (
		this Context g,
		Surface src,
		BlendMode mode = BlendMode.Normal,
		double opacity = 1.0)
	{
		g.Save ();

		g.SetSourceSurface (src, 0, 0);
		g.PaintWithBlendMode (mode, opacity);

		g.Restore ();
	}

	public static void BlendSurface (
		this Context g,
		Surface src,
		RectangleD roi,
		BlendMode mode = BlendMode.Normal,
		double opacity = 1.0)
	{
		g.Save ();

		g.Rectangle (roi);
		g.Clip ();
		g.SetSourceSurface (src, 0, 0);
		g.PaintWithBlendMode (mode, opacity);

		g.Restore ();
	}
	public static void BlendSurface (
		this Context g,
		Surface src,
		PointD offset,
		BlendMode mode = BlendMode.Normal,
		double opacity = 1.0)
	{
		g.Save ();

		g.Translate (offset.X, offset.Y);
		g.SetSourceSurface (src, 0, 0);
		g.PaintWithBlendMode (mode, opacity);

		g.Restore ();
	}

	/// <summary>
	/// Paints the current source with the given blend mode and opacity.
	/// Blend modes that Cairo has no operator for are computed per pixel, so the target must be an image surface.
	/// </summary>
	public static void PaintWithBlendMode (
		this Context g,
		BlendMode mode,
		double opacity)
	{
		if (pixel_blend_ops.TryGetValue (mode, out Func<int, int, int>? op)) {
			PaintWithPixelBlend (g, op, opacity);
			return;
		}

		g.SetBlendMode (mode);
		g.PaintWithAlpha (opacity);
	}

	// Paint.NET blend functions of the straight bottom (b) and top (a) channel values.
	private static readonly Dictionary<BlendMode, Func<int, int, int>> pixel_blend_ops = new () {
		[BlendMode.Additive] = (b, a) => Math.Min (255, b + a),
		[BlendMode.Reflect] = (b, a) => a == 255 ? 255 : Math.Min (255, b * b / (255 - a)),
		[BlendMode.Glow] = (b, a) => b == 255 ? 255 : Math.Min (255, a * a / (255 - b)),
		[BlendMode.Negation] = (b, a) => 255 - Math.Abs (255 - b - a),
	};

	private static unsafe void PaintWithPixelBlend (Context g, Func<int, int, int> op, double opacity)
	{
		Surface target = g.GetTarget ();
		target.Flush ();

		int width = ImageSurfaceGetWidth (target.Handle);
		int height = ImageSurfaceGetHeight (target.Handle);
		int stride = ImageSurfaceGetStride (target.Handle) / 4;
		Span<ColorBgra> dst = new ((void*) ImageSurfaceGetData (target.Handle), stride * height);

		RectangleI bounds = GetDeviceClipBounds (g).Intersect (new RectangleI (0, 0, width, height));

		using ImageSurface top = CreateImageSurface (Format.Argb32, bounds.Width, bounds.Height);
		using (Context t = new (top)) {
			Matrix matrix = CreateIdentityMatrix ();
			g.GetMatrix (matrix);
			t.Translate (-bounds.X, -bounds.Y);
			t.Transform (matrix);
			t.SetSource (g.GetSource ());
			t.PaintWithAlpha (opacity);
		}

		ReadOnlySpan<ColorBgra> src = top.GetReadOnlyPixelData ();
		for (int y = 0; y < bounds.Height; y++) {
			for (int x = 0; x < bounds.Width; x++) {
				ref ColorBgra d = ref dst[(bounds.Y + y) * stride + bounds.X + x];
				d = PixelBlend (d, src[y * bounds.Width + x], op);
			}
		}

		target.MarkDirty ();
	}

	private static RectangleI GetDeviceClipBounds (Context g)
	{
		g.ClipExtents (out double x1, out double y1, out double x2, out double y2);
		PointD[] corners = [new (x1, y1), new (x2, y1), new (x1, y2), new (x2, y2)];
		for (int i = 0; i < corners.Length; i++) {
			double x = corners[i].X;
			double y = corners[i].Y;
			g.UserToDevice (ref x, ref y);
			corners[i] = new (x, y);
		}

		int left = (int) Math.Floor (corners.Min (p => p.X));
		int top = (int) Math.Floor (corners.Min (p => p.Y));
		int right = (int) Math.Ceiling (corners.Max (p => p.X));
		int bottom = (int) Math.Ceiling (corners.Max (p => p.Y));
		return new (left, top, right - left, bottom - top);
	}

	/// <summary>
	/// Composites premultiplied top over bottom, using the blend function where both are opaque.
	/// </summary>
	private static ColorBgra PixelBlend (ColorBgra bottom, ColorBgra top, Func<int, int, int> op)
	{
		int sa = top.A;
		int ba = bottom.A;

		return sa == 0
			? bottom
			: ColorBgra.FromBgra (
				BlendChannel (bottom.B, top.B, ba, sa, op),
				BlendChannel (bottom.G, top.G, ba, sa, op),
				BlendChannel (bottom.R, top.R, ba, sa, op),
				(byte) (sa + ba - sa * ba / 255));
	}

	private static byte BlendChannel (int bc, int sc, int ba, int sa, Func<int, int, int> op)
	{
		int f = op (Math.Min (255, bc * 255 / Math.Max (ba, 1)), Math.Min (255, sc * 255 / sa));
		return (byte) ((sc * (255 - ba) * 255 + bc * (255 - sa) * 255 + sa * ba * f) / 65025);
	}

	public static void SetBlendMode (
		this Context g,
		BlendMode mode)
	{
		g.Operator = GetBlendModeOperator (mode);
	}

	private static Operator GetBlendModeOperator (BlendMode mode)
		=> mode switch {
			BlendMode.Normal => Operator.Over,
			BlendMode.Multiply => (Operator) ExtendedOperators.Multiply,
			BlendMode.ColorBurn => (Operator) ExtendedOperators.ColorBurn,
			BlendMode.ColorDodge => (Operator) ExtendedOperators.ColorDodge,
			BlendMode.HardLight => (Operator) ExtendedOperators.HardLight,
			BlendMode.SoftLight => (Operator) ExtendedOperators.SoftLight,
			BlendMode.Overlay => (Operator) ExtendedOperators.Overlay,
			BlendMode.Difference => (Operator) ExtendedOperators.Difference,
			BlendMode.Color => (Operator) ExtendedOperators.HslColor,
			BlendMode.Luminosity => (Operator) ExtendedOperators.HslLuminosity,
			BlendMode.Hue => (Operator) ExtendedOperators.HslHue,
			BlendMode.Saturation => (Operator) ExtendedOperators.HslSaturation,
			BlendMode.Lighten => (Operator) ExtendedOperators.Lighten,
			BlendMode.Darken => (Operator) ExtendedOperators.Darken,
			BlendMode.Screen => (Operator) ExtendedOperators.Screen,
			BlendMode.Xor => Operator.Xor,
			_ => throw new ArgumentOutOfRangeException (nameof (mode)),
		};

	private static Status Xor (this Region region, Region other)
		=> RegionXor (region.Handle, other.Handle);

	public enum ExtendedOperators
	{
		Clear = 0,

		Source = 1,
		SourceOver = 2,
		SourceIn = 3,
		SourceOut = 4,
		SourceAtop = 5,

		Destination = 6,
		DestinationOver = 7,
		DestinationIn = 8,
		DestinationOut = 9,
		DestinationAtop = 10,

		Xor = 11,
		Add = 12,
		Saturate = 13,

		Multiply = 14,
		Screen = 15,
		Overlay = 16,
		Darken = 17,
		Lighten = 18,
		ColorDodge = 19,
		ColorBurn = 20,
		HardLight = 21,
		SoftLight = 22,
		Difference = 23,
		Exclusion = 24,
		HslHue = 25,
		HslSaturation = 26,
		HslColor = 27,
		HslLuminosity = 28,
	}
}
