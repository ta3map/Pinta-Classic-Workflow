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
	private const int RESIZE_BORDER = 6;

	private readonly HoverOpacity hover_opacity;
	private int default_width;
	private int default_height;
	private int offset_x = DEFAULT_OFFSET;
	private int offset_y = DEFAULT_OFFSET;
	private int floating_height;

	private readonly string unique_name;
	private readonly Gtk.Box content;
	private readonly Gtk.ToggleButton dock_button;
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
	public event EventHandler? DockedChanged;

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

		Gtk.ToggleButton dockButton = new () {
			IconName = StandardIcons.ViewPin,
			TooltipText = Translations.GetString ("Dock to the window edge"),
		};
		dockButton.AddCssClass (AdwaitaStyles.Flat);
		dockButton.OnToggled += (_, _) => ApplyDocked ();

		Gtk.Box header = GtkExtensions.BoxHorizontal ([label, dockButton, closeButton]);
		header.AddCssClass ("floating-pad-header");

		Gtk.GestureDrag drag = CreateDrag (header);
		drag.OnDragBegin += (_, _) => {
			if (Docked)
				drag.SetState (Gtk.EventSequenceState.Denied);
		};
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
		motion.OnEnter += (_, _) => {
			// Raise above the other floating pads; a docked pad's order is fixed by its dock.
			if (!Docked)
				InsertBefore (GetParent ()!, null);
		};
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
		FloatingHalign = halign;
		FloatingValign = valign;
		AddCssClass ("floating-pad");
		AddController (motion);

		// --- Initialization (Gtk.Box)

		Append (contentBox);

		// --- References to keep

		unique_name = uniqueName;
		content = contentBox;
		dock_button = dockButton;
		hover_opacity = new HoverOpacity (this) { InactiveOpacity = 0.8 };
		ApplyMargins ();
	}

	public bool Resizable { get; init; } = true;

	/// <summary>
	/// The corner the pad floats in. It also gives the dock: the left or right edge, at the top or bottom.
	/// </summary>
	public Gtk.Align FloatingHalign { get; }
	public Gtk.Align FloatingValign { get; }

	/// <summary>
	/// Whether the pad is docked to the window edge instead of floating over the canvas.
	/// </summary>
	public bool Docked {
		get => dock_button.Active;
		set => dock_button.Active = value;
	}

	// A docked pad fills its part of the dock column, keeping the floating width as its minimum width.
	private void ApplyDocked ()
	{
		if (Docked) {
			floating_height = HeightRequest;
			AddCssClass ("docked");
		} else {
			RemoveCssClass ("docked");
		}

		Halign = Docked ? Gtk.Align.Fill : FloatingHalign;
		Valign = Docked ? Gtk.Align.Fill : FloatingValign;
		Vexpand = Docked;
		SetSizeRequest (WidthRequest, Docked ? -1 : floating_height);
		dock_button.IconName = Docked ? StandardIcons.WindowRestore : StandardIcons.ViewPin;
		dock_button.TooltipText = Docked ? Translations.GetString ("Undock") : Translations.GetString ("Dock to the window edge");
		ApplyMargins ();
		DockedChanged?.Invoke (this, EventArgs.Empty);
	}

	/// <summary>
	/// The opacity of the pad while the pointer is outside of it.
	/// </summary>
	public double InactiveOpacity {
		get => hover_opacity.InactiveOpacity;
		set => hover_opacity.InactiveOpacity = value;
	}

	/// <summary>
	/// Panels lying over the top and bottom of the pad area: the pad is kept between them.
	/// </summary>
	public Gtk.Widget? TopPanel { get; set; }
	public Gtk.Widget? BottomPanel { get; set; }

	private int InsetTop => TopPanel?.GetVisibleHeight () ?? 0;
	private int InsetBottom => BottomPanel?.GetVisibleHeight () ?? 0;

	/// <summary>
	/// Moves the pad back to its corner and restores its original size.
	/// </summary>
	public void ResetLayout ()
	{
		OffsetX = DEFAULT_OFFSET;
		OffsetY = DEFAULT_OFFSET;
		SetSizeRequest (default_width, default_height);
	}

	private int DirectionX => FloatingHalign == Gtk.Align.End ? -1 : 1;
	private int DirectionY => FloatingValign == Gtk.Align.End ? -1 : 1;

	private int OffsetX {
		get => offset_x;
		set {
			offset_x = value;
			ApplyMargins ();
		}
	}

	private int OffsetY {
		get => offset_y;
		set {
			offset_y = value;
			ApplyMargins ();
		}
	}

	// A docked pad is laid out by its dock, so the offsets only apply while floating.
	private void ApplyMargins ()
	{
		int x = Docked ? 0 : offset_x;
		int top = Docked ? 0 : offset_y + InsetTop;
		int bottom = Docked ? 0 : offset_y + InsetBottom;
		bool right = FloatingHalign == Gtk.Align.End;
		bool down = FloatingValign == Gtk.Align.End;

		MarginStart = right ? 0 : x;
		MarginEnd = right ? x : 0;
		MarginTop = down ? 0 : top;
		MarginBottom = down ? bottom : 0;
	}

	/// <summary>
	/// -1 / 1 when the point is near the left / right (top / bottom) edge, otherwise 0.
	/// A docked pad is only resized by its inner edge, facing the canvas.
	/// </summary>
	private void GetEdges (double x, double y, out int edgeX, out int edgeY)
	{
		int border = Resizable ? RESIZE_BORDER : 0;
		int innerEdge = FloatingHalign == Gtk.Align.End ? -1 : 1;
		edgeX = Convert.ToInt32 (x > GetWidth () - border) - Convert.ToInt32 (x < border);
		edgeY = Convert.ToInt32 (y > GetHeight () - border) - Convert.ToInt32 (y < border);
		edgeX = Docked && edgeX != innerEdge ? 0 : edgeX;
		edgeY = Docked ? 0 : edgeY;
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
		OffsetY = Math.Clamp (y, 0, Math.Max (0, area.GetHeight () - InsetTop - InsetBottom - GetHeight ()));
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
		SetSizeRequest (width, Docked ? -1 : height);
		if (Docked)
			return;

		OffsetX = FloatingHalign == Gtk.Align.End ? area.GetWidth () - left - width : left;
		OffsetY = FloatingValign == Gtk.Align.End ? area.GetHeight () - InsetBottom - top - height : top - InsetTop;
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
		default_width = WidthRequest;
		default_height = HeightRequest;
		OffsetX = settings.GetSetting ($"pad-{unique_name}-x", DEFAULT_OFFSET);
		OffsetY = settings.GetSetting ($"pad-{unique_name}-y", DEFAULT_OFFSET);
		SetSizeRequest (
			Resizable ? settings.GetSetting ($"pad-{unique_name}-width", WidthRequest) : WidthRequest,
			Resizable ? settings.GetSetting ($"pad-{unique_name}-height", HeightRequest) : HeightRequest);
		Docked = settings.GetSetting ($"pad-{unique_name}-docked", false);
	}

	public void SaveSettings (ISettingsService settings)
	{
		settings.PutSetting ($"pad-{unique_name}-docked", Docked);
		settings.PutSetting ($"pad-{unique_name}-x", OffsetX);
		settings.PutSetting ($"pad-{unique_name}-y", OffsetY);
		settings.PutSetting ($"pad-{unique_name}-width", WidthRequest);
		settings.PutSetting ($"pad-{unique_name}-height", Docked ? floating_height : HeightRequest);
	}
}
