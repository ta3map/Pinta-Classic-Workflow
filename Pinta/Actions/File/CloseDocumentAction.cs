//
// CloseDocumentAction.cs
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

namespace Pinta.Actions;

internal sealed class CloseDocumentAction : IActionHandler
{
	private readonly ActionManager actions;
	private readonly ChromeManager chrome;
	private readonly WorkspaceManager workspace;
	private readonly ToolManager tools;
	internal CloseDocumentAction (
		ActionManager actions,
		ChromeManager chrome,
		WorkspaceManager workspace,
		ToolManager tools)
	{
		this.actions = actions;
		this.chrome = chrome;
		this.workspace = workspace;
		this.tools = tools;
	}

	void IActionHandler.Initialize ()
	{
		actions.File.Close.Activated += Activated;
	}

	void IActionHandler.Uninitialize ()
	{
		actions.File.Close.Activated -= Activated;
	}

	private async void Activated (object sender, EventArgs e)
	{
		// Commit any pending changes
		tools.Commit ();

		// If it's not dirty, just close it
		if (!workspace.ActiveDocument.IsDirty) {
			workspace.CloseActiveDocument ();
			return;
		}

		string heading = Translations.GetString (
			"Save changes to image \"{0}\" before closing?",
			workspace.ActiveDocument.DisplayName);

		string body = Translations.GetString ("If you don't save, all changes will be permanently lost.");

		Adw.MessageDialog dialog = Adw.MessageDialog.New (chrome.MainWindow, heading, body);
		dialog.ExtraChild = CreatePreview (workspace.ActiveDocument);

		const string cancel_response = "cancel";
		const string discard_response = "discard";
		const string save_response = "save";

		dialog.AddResponse (cancel_response, Translations.GetString ("_Cancel"));
		dialog.AddResponse (discard_response, Translations.GetString ("_Discard"));
		dialog.AddResponse (save_response, Translations.GetString ("_Save"));

		// Configure the styling for the save / discard buttons.
		dialog.SetResponseAppearance (discard_response, Adw.ResponseAppearance.Destructive);
		dialog.SetResponseAppearance (save_response, Adw.ResponseAppearance.Suggested);

		dialog.CloseResponse = cancel_response;
		dialog.DefaultResponse = save_response;

		string response = dialog.RunBlocking ();
		if (response == save_response) {

			bool saved = await workspace.ActiveDocument.Save (false);

			// If saved is false, then the user
			// must have cancelled the Save dialog
			if (saved)
				workspace.CloseActiveDocument ();

		} else if (response == discard_response) {
			workspace.CloseActiveDocument ();
		}

	}

	private static Gtk.Picture CreatePreview (Document document)
	{
		using Cairo.ImageSurface flattened = document.GetFlattenedImage ();
		using Cairo.ImageSurface preview = CairoExtensions.CreateImageSurface (Cairo.Format.Argb32, flattened.Width, flattened.Height);

		// The checkerboard cells should look about 8 px large after the preview is scaled down.
		int cellSize = Math.Max (8, Math.Max (flattened.Width, flattened.Height) * 8 / PREVIEW_SIZE);

		using (Cairo.Context g = new (preview)) {
			g.SetSource (CairoExtensions.CreateTransparentBackgroundPattern (cellSize));
			g.Paint ();
			g.SetSourceSurface (flattened, 0, 0);
			g.Paint ();
		}

		preview.Flush ();

		Gdk.MemoryTexture texture = Gdk.MemoryTexture.New (
			preview.Width,
			preview.Height,
			Gdk.MemoryFormat.B8g8r8a8Premultiplied,
			GLib.Bytes.New (preview.GetData ()),
			(nuint) preview.Stride);

		Gtk.Picture picture = Gtk.Picture.NewForPaintable (texture);
		picture.ContentFit = Gtk.ContentFit.Contain;
		picture.SetSizeRequest (PREVIEW_SIZE, PREVIEW_SIZE);
		return picture;
	}

	private const int PREVIEW_SIZE = 256;
}
