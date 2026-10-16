using Pinta.Core;
using Pinta.Docking;
using Pinta.Gui.Widgets;

namespace Pinta;

internal static class HistoryPad
{
	public static FloatingPad Create (EditActions edit)
	{
		FloatingPad pad = new (
			uniqueName: "History",
			title: Translations.GetString ("History"),
			child: new HistoryListView (),
			halign: Gtk.Align.End,
			valign: Gtk.Align.Start);
		pad.SetSizeRequest (160, 173);

		pad.AddToolBar ().AppendMultiple ([
			edit.Undo.CreateDockToolBarItem (),
			edit.Redo.CreateDockToolBarItem ()]);

		return pad;
	}
}
