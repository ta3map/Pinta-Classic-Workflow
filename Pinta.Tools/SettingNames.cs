namespace Pinta.Tools;

internal static class SettingNames
{
	internal const string COLOR_PICKER_TOOL_SELECTION = "color-picker-tool-selection";
	internal const string COLOR_PICKER_SAMPLE_SIZE = "color-picker-sample-size";
	internal const string COLOR_PICKER_SAMPLE_TYPE = "color-picker-sample-type";

	internal const string ERASER_ERASE_TYPE = "eraser-erase-type";

	internal const string PAINT_BRUSH_BRUSH = "paint-brush-brush";

	internal const string BRUSH_WIDTH = Pinta.Core.BaseTool.BRUSH_WIDTH_SETTING;
	internal const string BRUSH_HARDNESS = Pinta.Core.BaseTool.BRUSH_HARDNESS_SETTING;

	internal const string TEXT_FONT = "text-font";
	internal const string TEXT_VARIANT = "text-variant";
	internal const string TEXT_WEIGHT = "text-weight";
	internal const string TEXT_ITALIC = "text-italic";
	internal const string TEXT_UNDERLINE = "text-underline";
	internal const string TEXT_ALIGNMENT = "text-alignment";
	internal const string TEXT_STYLE = "text-style";
	internal const string TEXT_OUTLINE_WIDTH = "text-outline-width";
	internal const string TEXT_JOIN = "text-join";

	internal const string RECOLOR_TOLERANCE = "recolor-tolerance";

	internal const string GRADIENT_TYPE = "gradient-type";
	internal const string GRADIENT_COLOR_MODE = "gradient-color-mode";

	internal const string DASH_PATTERN = "dash-pattern";

	internal const string LASSO_MODE = "lasso-mode";

	internal static string Arrow1 (string prefix)
		=> $"{prefix}-arrow1";

	internal static string Arrow2 (string prefix)
		=> $"{prefix}-arrow2";

	internal static string ArrowSize (string prefix)
		=> $"{prefix}-arrow-size";

	internal static string ArrowAngle (string prefix)
		=> $"{prefix}-arrow-angle";

	internal static string ArrowLength (string prefix)
		=> $"{prefix}-arrow-length";

	internal static string FloodToolFillMode (FloodTool tool)
		=> $"{tool.GetType ().Name.ToLowerInvariant ()}-fill-mode";

	internal static string FloodToolFillTolerance (FloodTool tool)
		=> $"{tool.GetType ().Name.ToLowerInvariant ()}-fill-tolerance";

	internal static string FillStyle (string prefix)
		=> $"{prefix}-fill-style";

	internal static string ShapeType (string prefix)
		=> $"{prefix}-shape-type";

	internal static string Radius (string prefix)
		=> $"{prefix}-radius";
}
