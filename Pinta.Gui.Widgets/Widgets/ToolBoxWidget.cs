using System.Linq;
using Pinta.Core;

namespace Pinta.Gui.Widgets;

public sealed class ToolBoxWidget : Gtk.FlowBox
{
	private readonly ToolManager tools;

	public ToolBoxWidget (ToolManager tools)
	{
		// --- Initialization (Gtk.FlowBox)

		// Two fixed columns filled row by row, like Paint.NET; the order comes from the tools' priorities.
		SetOrientation (Gtk.Orientation.Horizontal);
		MinChildrenPerLine = 2;
		MaxChildrenPerLine = 2;
		Homogeneous = true;
		AddCssClass ("tool-box");

		// --- References to keep

		this.tools = tools;
	}

	public void AddItem (BaseTool tool)
	{
		var index = tools.ToList ().IndexOf (tool);
		Insert (tool.ToolItem, index);
	}

	public void RemoveItem (BaseTool tool)
	{
		Remove (tool.ToolItem);
	}
}
