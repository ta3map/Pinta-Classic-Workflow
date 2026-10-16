using Pinta.Core;

namespace Pinta.Gui.Widgets;

/// <summary>
/// Status bar indicator showing that an operation is running, with its progress.
/// </summary>
public sealed class StatusBarProgress : Gtk.Box, IProgressIndicator
{
	private const uint PULSE_MILLISECONDS = 100;

	private readonly Gtk.Label text_label;
	private readonly Gtk.ProgressBar progress_bar;
	private int pulse_generation;

	public StatusBarProgress ()
	{
		text_label = Gtk.Label.New (null);

		progress_bar = new Gtk.ProgressBar { Valign = Gtk.Align.Center };
		progress_bar.SetSizeRequest (150, -1);

		SetOrientation (Gtk.Orientation.Horizontal);
		Spacing = 6;
		Visible = false;
		Append (progress_bar);
		Append (text_label);
	}

	public string Text {
		get => text_label.GetText ();
		set => text_label.SetText (value);
	}

	public double Progress {
		get => progress_bar.Fraction;
		set {
			pulse_generation++;
			progress_bar.Fraction = value;
			Visible = value < 1;
		}
	}

	public void Pulse ()
	{
		Visible = true;

		int generation = ++pulse_generation;
		GLib.Functions.TimeoutAdd (0, PULSE_MILLISECONDS, () => {
			if (generation != pulse_generation)
				return false;

			progress_bar.Pulse ();
			return true;
		});
	}

	public new void Show ()
		=> Visible = true;

	public new void Hide ()
	{
		pulse_generation++;
		Visible = false;
	}
}
