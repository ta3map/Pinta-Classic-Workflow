using System;
using Pinta.Core;
using Pinta.Resources;

namespace Pinta.Docking;

/// <summary>
/// A semi-transparent tool window floating over the canvas, which can be moved by dragging its title
/// and resized by dragging its edges.
/// Its position is the offset from the corner given by its Halign / Valign.
/// </summary>
public sealed class FloatingPad : Gtk.Box
{
	private const int DEFAULT_OFFSET = 8;
	private const double INACTIVE_OPACITY = 0.8;
	private const int RESIZE_BORDER = 6;

	private readonly string unique_name;
	private readonly Gtk.Box content;
	private double drag_start_x;
	private double drag_start_y;
	private int drag_start_offset_x;
	private int drag_start_offset_y;
	private int drag_start_left;
	private int drag_start_top;
	private int drag_start_width;
	private int drag_start_height;
	private int resize_edge_x;
	private int resize_edge_y;

	public event EventHandler? CloseClicked;

	public FloatingPad (
		string uniqueName,
		string title,
		Gtk.Widget child,
		Gtk.Align halign,
		Gtk.Align valign)
	{
		Gtk.Label label = Gtk.Label.New (title);
		label.Hexpand = true;
		label.Halign = Gtk.Align.Start;
		label.MarginStart = 8;

		Gtk.Button closeButton = Gtk.Button.NewFromIconName (StandardIcons.WindowClose);
		closeButton.AddCssClass (AdwaitaStyles.Flat);
		closeButton.OnClicked += (_, _) => CloseClicked?.Invoke (this, EventArgs.Empty);

		Gtk.Box header = GtkExtensions.BoxHorizontal ([label, closeButton]);
		header.AddCssClass ("floating-pad-header");

		Gtk.GestureDrag drag = CreateDrag (header);
		drag.OnDragUpdate += (_, args) => {
			GetDragDelta (header, drag, args.OffsetX, args.OffsetY, out int dx, out int dy);
			MoveTo (drag_start_offset_x + dx * DirectionX, drag_start_offset_y + dy * DirectionY);
		};
		header.AddController (drag);

		// Runs before the children, and only takes the press when it starts on an edge.
		Gtk.GestureDrag resize = CreateDrag (this);
		resize.SetPropagationPhase (Gtk.PropagationPhase.Capture);
		resize.OnDragBegin += (_, args) => {
			GetEdges (args.StartX, args.StartY, out resize_edge_x, out resize_edge_y);
			resize.SetState (resize_edge_x == 0 && resize_edge_y == 0 ? Gtk.EventSequenceState.Denied : Gtk.EventSequenceState.Claimed);
		};
		resize.OnDragUpdate += (_, args) => {
			GetDragDelta (this, resize, args.OffsetX, args.OffsetY, out int dx, out int dy);
			ResizeBy (dx, dy);
		};
		AddController (resize);

		Gtk.EventControllerMotion motion = Gtk.EventControllerMotion.New ();
		motion.OnEnter += (_, _) => Opacity = 1;
		motion.OnLeave += (_, _) => Opacity = INACTIVE_OPACITY;
		motion.OnMotion += (_, args) => {
			GetEdges (args.X, args.Y, out int edgeX, out int edgeY);
			SetCursorFromName (GetResizeCursor (edgeX, edgeY));
		};

		child.Vexpand = true;

		Gtk.Box contentBox = Gtk.Box.New (Gtk.Orientation.Vertical, 0);
		contentBox.Hexpand = true;
		contentBox.Append (header);
		contentBox.Append (child);

		// --- Initialization (Gtk.Widget)

		Halign = halign;
		Valign = valign;
		Opacity = INACTIVE_OPACITY;
		AddCssClass ("floating-pad");
		AddController (motion);

		// --- Initialization (Gtk.Box)

		Append (contentBox);

		// --- References to keep

		unique_name = uniqueName;
		content = contentBox;
		OffsetX = DEFAULT_OFFSET;
		OffsetY = DEFAULT_OFFSET;
	}

	public bool Resizable { get; init; } = true;

	private int DirectionX => Halign == Gtk.Align.End ? -1 : 1;
	private int DirectionY => Valign == Gtk.Align.End ? -1 : 1;

	private int OffsetX {
		get => Halign == Gtk.Align.End ? MarginEnd : MarginStart;
		set {
			MarginStart = Halign == Gtk.Align.End ? 0 : value;
			MarginEnd = Halign == Gtk.Align.End ? value : 0;
		}
	}

	private int OffsetY {
		get => Valign == Gtk.Align.End ? MarginBottom : MarginTop;
		set {
			MarginTop = Valign == Gtk.Align.End ? 0 : value;
			MarginBottom = Valign == Gtk.Align.End ? value : 0;
		}
	}

	/// <summary>
	/// -1 / 1 when the point is near the left / right (top / bottom) edge, otherwise 0.
	/// </summary>
	private void GetEdges (double x, double y, out int edgeX, out int edgeY)
	{
		int border = Resizable ? RESIZE_BORDER : 0;
		edgeX = Convert.ToInt32 (x > GetWidth () - border) - Convert.ToInt32 (x < border);
		edgeY = Convert.ToInt32 (y > GetHeight () - border) - Convert.ToInt32 (y < border);
	}

	private static string? GetResizeCursor (int edgeX, int edgeY) => (edgeX, edgeY) switch {
		(0, 0) => null,
		(0, _) => "ns-resize",
		(_, 0) => "ew-resize",
		_ when edgeX == edgeY => "nwse-resize",
		_ => "nesw-resize",
	};

	private Gtk.GestureDrag CreateDrag (Gtk.Widget handle)
	{
		Gtk.GestureDrag drag = Gtk.GestureDrag.New ();
		drag.OnDragBegin += (_, args) => {
			Gtk.Widget area = GetParent ()!;
			handle.TranslateCoordinates (area, args.StartX, args.StartY, out drag_start_x, out drag_start_y);
			TranslateCoordinates (area, 0, 0, out double left, out double top);
			drag_start_left = (int) left;
			drag_start_top = (int) top;
			drag_start_offset_x = OffsetX;
			drag_start_offset_y = OffsetY;
			drag_start_width = GetWidth ();
			drag_start_height = GetHeight ();
		};
		return drag;
	}

	// The handle moves along with the pad, so pointer positions are converted to the parent's coordinates.
	private void GetDragDelta (Gtk.Widget handle, Gtk.GestureDrag drag, double offsetX, double offsetY, out int dx, out int dy)
	{
		drag.GetStartPoint (out double startX, out double startY);
		handle.TranslateCoordinates (GetParent ()!, startX + offsetX, startY + offsetY, out double x, out double y);
		dx = (int) (x - drag_start_x);
		dy = (int) (y - drag_start_y);
	}

	private void MoveTo (int x, int y)
	{
		Gtk.Widget area = GetParent ()!;
		OffsetX = Math.Clamp (x, 0, Math.Max (0, area.GetWidth () - GetWidth ()));
		OffsetY = Math.Clamp (y, 0, Math.Max (0, area.GetHeight () - GetHeight ()));
	}

	private void ResizeBy (int dx, int dy)
	{
		content.Measure (Gtk.Orientation.Horizontal, -1, out _, out int naturalWidth, out _, out _);
		int width = Math.Max (drag_start_width + dx * resize_edge_x, naturalWidth);
		content.Measure (Gtk.Orientation.Vertical, width, out _, out int naturalHeight, out _, out _);
		int height = Math.Max (drag_start_height + dy * resize_edge_y, naturalHeight);

		// Dragging the left / top edge moves the pad, so that the opposite edge stays in place.
		int left = drag_start_left + (resize_edge_x < 0 ? drag_start_width - width : 0);
		int top = drag_start_top + (resize_edge_y < 0 ? drag_start_height - height : 0);

		Gtk.Widget area = GetParent ()!;
		SetSizeRequest (width, height);
		OffsetX = Halign == Gtk.Align.End ? area.GetWidth () - left - width : left;
		OffsetY = Valign == Gtk.Align.End ? area.GetHeight () - top - height : top;
	}

	/// <summary>
	/// Create a toolbar and add it to the bottom of the pad.
	/// </summary>
	public Gtk.Box AddToolBar ()
	{
		Gtk.Box toolbar = GtkExtensions.CreateToolBar ();
		content.Append (toolbar);
		return toolbar;
	}

	public void LoadSettings (ISettingsService settings)
	{
		OffsetX = settings.GetSetting ($"pad-{unique_name}-x", DEFAULT_OFFSET);
		OffsetY = settings.GetSetting ($"pad-{unique_name}-y", DEFAULT_OFFSET);
		SetSizeRequest (
			Resizable ? settings.GetSetting ($"pad-{unique_name}-width", WidthRequest) : WidthRequest,
			Resizable ? settings.GetSetting ($"pad-{unique_name}-height", HeightRequest) : HeightRequest);
	}

	public void SaveSettings (ISettingsService settings)
	{
		settings.PutSetting ($"pad-{unique_name}-x", OffsetX);
		settings.PutSetting ($"pad-{unique_name}-y", OffsetY);
		settings.PutSetting ($"pad-{unique_name}-width", WidthRequest);
		settings.PutSetting ($"pad-{unique_name}-height", HeightRequest);
	}
}
