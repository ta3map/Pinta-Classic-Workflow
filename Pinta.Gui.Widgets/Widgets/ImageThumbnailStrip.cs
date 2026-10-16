using System.Collections.Generic;
using Cairo;
using Pinta.Core;

namespace Pinta.Gui.Widgets;

/// <summary>
/// A strip of thumbnails of the open images, used to switch between them.
/// </summary>
public sealed class ImageThumbnailStrip : Gtk.Box
{
	private const int THUMBNAIL_WIDTH = 48;
	private const int THUMBNAIL_HEIGHT = 28;

	private sealed record Thumbnail (Gtk.Overlay Root, Gtk.ToggleButton Button, Gtk.Picture Picture, Gtk.Label DirtyMark);

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
			CanShrink = false,
			Halign = Gtk.Align.Center,
			Valign = Gtk.Align.Center,
		};

		Gtk.ToggleButton button = new () { Child = picture };
		button.AddCssClass (AdwaitaStyles.Flat);
		button.AddCssClass ("thumbnail-button");
		button.OnClicked += (_, _) => {
			workspace.SetActiveDocument (workspace.OpenDocuments.IndexOf (doc));
			UpdateActive ();
		};

		Gtk.Label dirtyMark = Gtk.Label.New ("*");
		dirtyMark.Halign = Gtk.Align.Start;
		dirtyMark.Valign = Gtk.Align.Start;
		dirtyMark.CanTarget = false;
		dirtyMark.AddCssClass ("thumbnail-dirty-mark");

		Gtk.Button closeButton = Gtk.Button.NewFromIconName (Resources.StandardIcons.WindowClose);
		closeButton.Halign = Gtk.Align.End;
		closeButton.Valign = Gtk.Align.Start;
		closeButton.TooltipText = Translations.GetString ("Close");
		closeButton.AddCssClass ("thumbnail-close");
		closeButton.OnClicked += (_, _) => {
			workspace.SetActiveDocument (workspace.OpenDocuments.IndexOf (doc));
			PintaCore.Actions.File.Close.Activate ();
		};

		Gtk.Overlay overlay = new () { Child = button };
		overlay.AddOverlay (dirtyMark);
		overlay.AddOverlay (closeButton);

		doc.History.HistoryItemAdded += (_, _) => UpdateThumbnail (doc);
		doc.History.ActionUndone += (_, _) => UpdateThumbnail (doc);
		doc.History.ActionRedone += (_, _) => UpdateThumbnail (doc);
		doc.IsDirtyChanged += (_, _) => UpdateDirtyState (doc);
		doc.Renamed += (_, _) => UpdateDirtyState (doc);

		thumbnails[doc] = new Thumbnail (overlay, button, picture, dirtyMark);
		Append (overlay);

		UpdateThumbnail (doc);
		UpdateDirtyState (doc);
		UpdateActive ();
	}

	private void RemoveDocument (Document doc)
	{
		Remove (thumbnails[doc].Root);
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

		double scale = System.Math.Min ((double) THUMBNAIL_WIDTH / flattened.Width, (double) THUMBNAIL_HEIGHT / flattened.Height);
		int width = System.Math.Max (1, (int) (flattened.Width * scale));
		int height = System.Math.Max (1, (int) (flattened.Height * scale));

		// The texture always has the cell size, with the image centered in it, so the image proportions never affect the layout.
		ImageSurface thumbnail = CairoExtensions.CreateImageSurface (Format.Argb32, THUMBNAIL_WIDTH, THUMBNAIL_HEIGHT);
		using (Context g = new (thumbnail)) {
			g.Translate ((THUMBNAIL_WIDTH - width) / 2, (THUMBNAIL_HEIGHT - height) / 2);
			g.Rectangle (0, 0, width, height);
			g.Clip ();
			g.SetSource (CairoExtensions.CreateTransparentBackgroundPattern (4));
			g.Paint ();
			g.Scale (scale, scale);
			g.SetSourceSurface (flattened, 0, 0);
			g.Paint ();
		}
		thumbnail.Flush ();

		Gdk.MemoryTexture texture = Gdk.MemoryTexture.New (
			THUMBNAIL_WIDTH,
			THUMBNAIL_HEIGHT,
			Gdk.MemoryFormat.B8g8r8a8Premultiplied,
			GLib.Bytes.New (thumbnail.GetData ()),
			(nuint) thumbnail.Stride);

		thumbnails[doc].Picture.SetPaintable (texture);
	}
}
