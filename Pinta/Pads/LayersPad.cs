using Pinta.Core;
using Pinta.Docking;
using Pinta.Gui.Widgets;

namespace Pinta;

internal static class LayersPad
{
	public static FloatingPad Create (LayerActions layerActions)
	{
		FloatingPad pad = new (
			uniqueName: "Layers",
			title: Translations.GetString ("Layers"),
			child: new LayersListView (),
			halign: Gtk.Align.End,
			valign: Gtk.Align.End);
		pad.SetSizeRequest (160, 200);

		pad.AddToolBar ().AppendMultiple ([
			layerActions.AddNewLayer.CreateDockToolBarItem (),
			layerActions.DeleteLayer.CreateDockToolBarItem (),
			layerActions.DuplicateLayer.CreateDockToolBarItem (),
			layerActions.MergeLayerDown.CreateDockToolBarItem (),
			layerActions.MoveLayerUp.CreateDockToolBarItem (),
			layerActions.MoveLayerDown.CreateDockToolBarItem (),
			layerActions.Properties.CreateDockToolBarItem (),
		]);

		return pad;
	}
}
