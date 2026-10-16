//
// SelectTool.cs
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
using Pinta.Core;

namespace Pinta.Tools;

/// <summary>
/// Draws a new selection shape on every drag. Resizing an existing selection is done with the Move Selection tool.
/// </summary>
public abstract class SelectTool : BaseTool
{
	private readonly IWorkspaceService workspace;

	private SelectionHistoryItem? hist = default;
	private CombineMode combine_mode = default;

	public override Gdk.Key ShortcutKey => new (Gdk.Constants.KEY_S);
	public override bool IsSelectionTool => true;
	protected override bool ShowAntialiasingButton => false;
	private readonly RectangleHandle handle;

	public SelectTool (IServiceProvider services) : base (services)
	{
		workspace = services.GetService<IWorkspaceService> ();

		handle = new (workspace) { InvertIfNegative = true };
	}

	protected abstract void DrawShape (Document document, RectangleD r, Layer l);

	protected override void OnBuildToolBar (Gtk.Box tb)
	{
		base.OnBuildToolBar (tb);
		workspace.SelectionHandler.BuildToolbar (tb, Settings);
	}

	protected override void OnMouseDown (Document document, ToolMouseEventArgs e)
	{
		// Ignore extra button clicks while drawing
		if (handle.IsDragging)
			return;

		hist = new SelectionHistoryItem (workspace, Icon, Name);
		hist.TakeSnapshot ();

		combine_mode = PintaCore.Workspace.SelectionHandler.DetermineCombineMode (e);

		double x = Math.Round (Math.Clamp (e.PointDouble.X, 0, document.ImageSize.Width));
		double y = Math.Round (Math.Clamp (e.PointDouble.Y, 0, document.ImageSize.Height));
		handle.Rectangle = new (x, y, 0.0, 0.0);

		document.PreviousSelection = document.Selection.Clone ();
		document.Selection.SelectionPolygons.Clear ();

		if (!handle.BeginDrag (new PointD (x, y), document.ImageSize))
			throw new InvalidOperationException ("Should be able to start drawing a new rectangle!");
	}

	protected override void OnMouseMove (Document document, ToolMouseEventArgs e)
	{
		if (!handle.IsDragging)
			return;

		handle.UpdateDrag (e.PointDouble, e.IsShiftPressed);

		ReDraw (document);

		SelectionModeHandler.PerformSelectionMode (document, combine_mode, document.Selection.SelectionPolygons);
	}

	protected override void OnMouseUp (Document document, ToolMouseEventArgs e)
	{
		if (handle.HasDragged (e.PointDouble)) {
			ReDraw (document);

			SelectionModeHandler.PerformSelectionMode (document, combine_mode, document.Selection.SelectionPolygons);

			document.Selection.HandleBounds = handle.Rectangle;

			if (hist != null) {
				document.History.PushNewItem (hist);
				hist = null;
			}

			handle.EndDrag ();
		} else {
			// If the user didn't move the mouse, they want to deselect
			handle.EndDrag ();

			if (hist != null) {
				// Roll back any changes made to the selection, e.g. in OnMouseDown().
				hist.Undo ();

				hist = null;
			}

			PintaCore.Actions.Edit.Deselect.Activate ();
		}
	}

	protected override void OnSaveSettings (ISettingsService settings)
	{
		base.OnSaveSettings (settings);

		workspace.SelectionHandler.OnSaveSettings (settings);
	}

	private void ReDraw (Document document)
	{
		document.Selection.Visible = true;

		DrawShape (document, handle.Rectangle, document.Layers.SelectionLayer);
	}
}
