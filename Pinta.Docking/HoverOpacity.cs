namespace Pinta.Docking;

/// <summary>
/// Makes a widget translucent while the pointer is outside of it.
/// </summary>
public sealed class HoverOpacity
{
	private readonly Gtk.Widget widget;
	private readonly Gtk.EventControllerMotion motion = Gtk.EventControllerMotion.New ();
	private double inactive_opacity = 1;

	public HoverOpacity (Gtk.Widget widget)
	{
		motion.OnEnter += (_, _) => widget.Opacity = 1;
		motion.OnLeave += (_, _) => widget.Opacity = inactive_opacity;
		widget.AddController (motion);

		this.widget = widget;
	}

	public double InactiveOpacity {
		get => inactive_opacity;
		set {
			inactive_opacity = value;
			widget.Opacity = motion.ContainsPointer ? 1 : value;
		}
	}
}
