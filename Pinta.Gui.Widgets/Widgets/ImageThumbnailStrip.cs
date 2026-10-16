using System.Collections.Generic;
using Cairo;
using Pinta.Core;

namespace Pinta.Gui.Widgets;

/// <summary>
/// A strip of thumbnails of the open images, used to switch between them.
/// </summary>
public sealed class ImageThumbnailStrip : Gtk.Box
{
	private const int THUMBNAIL_SIZE = 28;

	private sealed record Thumbnail (Gtk.ToggleButton Button, Gtk.Picture Picture, Gtk.Label DirtyMark);

	private readonly WorkspaceManager workspace;
	private readonly Dictionary<Document, Thumbnail> thumbnails = [];

	public ImageThumbnailStrip (WorkspaceManager workspace)
	{
		this.workspace = workspace;

		SetOrientation (Gtk.Orientation.Horizontal);
		Spacing = 2;

		workspace.DocumentActivated += (_, e) => AddDocument (e.Document);
		workspace.DocumentClosed += (_, e) => RemoveDocument (e.Document);
		workspace.ActiveDocumentChanged += (_, _) => UpdateActive ();
	}

	private void AddDocument (Document doc)
	{
		Gtk.Picture picture = new () {
			CanShrink = true,
			ContentFit = Gtk.ContentFit.Contain,
		};
		picture.SetSizeRequest (THUMBNAIL_SIZE, THUMBNAIL_SIZE);

		Gtk.Label dirtyMark = Gtk.Label.New ("*");
		dirtyMark.Halign = Gtk.Align.End;
		dirtyMark.Valign = Gtk.Align.Start;
		dirtyMark.AddCssClass ("thumbnail-dirty-mark");

		Gtk.Overlay overlay = new () { Child = picture };
		overlay.AddOverlay (dirtyMark);

		Gtk.ToggleButton button = new () { Child = overlay };
		button.AddCssClass (AdwaitaStyles.Flat);
		button.AddCssClass ("thumbnail-button");
		button.OnClicked += (_, _) => {
			workspace.SetActiveDocument (workspace.OpenDocuments.IndexOf (doc));
			UpdateActive ();
		};

		doc.History.HistoryItemAdded += (_, _) => UpdateThumbnail (doc);
		doc.History.ActionUndone += (_, _) => UpdateThumbnail (doc);
		doc.History.ActionRedone += (_, _) => UpdateThumbnail (doc);
		doc.IsDirtyChanged += (_, _) => UpdateDirtyState (doc);
		doc.Renamed += (_, _) => UpdateDirtyState (doc);

		thumbnails[doc] = new Thumbnail (button, picture, dirtyMark);
		Append (button);

		UpdateThumbnail (doc);
		UpdateDirtyState (doc);
		UpdateActive ();
	}

	private void RemoveDocument (Document doc)
	{
		Remove (thumbnails[doc].Button);
		thumbnails.Remove (doc);
	}

	private void UpdateActive ()
	{
		foreach (var (doc, thumbnail) in thumbnails)
			thumbnail.Button.Active = doc == workspace.ActiveDocumentOrDefault;
	}

	private void UpdateDirtyState (Document doc)
	{
		thumbnails[doc].Button.TooltipText = doc.DisplayName + (doc.IsDirty ? "*" : "");
		thumbnails[doc].DirtyMark.Visible = doc.IsDirty;
	}

	private void UpdateThumbnail (Document doc)
	{
		using ImageSurface flattened = doc.GetFlattenedImage ();

		double scale = (double) THUMBNAIL_SIZE / System.Math.Max (flattened.Width, flattened.Height);
		int width = System.Math.Max (1, (int) (flattened.Width * scale));
		int height = System.Math.Max (1, (int) (flattened.Height * scale));

		ImageSurface thumbnail = CairoExtensions.CreateImageSurface (Format.Argb32, width, height);
		using (Context g = new (thumbnail)) {
			g.Scale (scale, scale);
			g.SetSourceSurface (flattened, 0, 0);
			g.Paint ();
		}
		thumbnail.Flush ();

		Gdk.MemoryTexture texture = Gdk.MemoryTexture.New (
			width,
			height,
			Gdk.MemoryFormat.B8g8r8a8Premultiplied,
			GLib.Bytes.New (thumbnail.GetData ()),
			(nuint) thumbnail.Stride);

		thumbnails[doc].Picture.SetPaintable (texture);
	}
}
