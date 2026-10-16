namespace Pinta.Core;

public sealed class MoveLayerHistoryItem : BaseHistoryItem
{
	private readonly int from;
	private readonly int to;

	public MoveLayerHistoryItem (string icon, string text, int from, int to) : base (icon, text)
	{
		this.from = from;
		this.to = to;
	}

	public override void Undo ()
	{
		PintaCore.Workspace.ActiveDocument.Layers.MoveLayer (to, from);
	}

	public override void Redo ()
	{
		PintaCore.Workspace.ActiveDocument.Layers.MoveLayer (from, to);
	}
}
