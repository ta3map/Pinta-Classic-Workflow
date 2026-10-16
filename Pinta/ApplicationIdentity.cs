using System;
using System.Runtime.InteropServices;
using Pinta.Core;

namespace Pinta;

/// <summary>
/// Must run before Gsk/Gtk/GApplication init so the platform shell (macOS menu bar,
/// Linux WM_CLASS / app switcher, Windows task grouping) picks up the correct name.
/// </summary>
internal static class ApplicationIdentity
{
	[UnmanagedFunctionPointer (CallingConvention.Cdecl)]
	private delegate void SetPrgnameFunc (IntPtr prgnameUtf8);

	public static void ApplyBeforeGtkInit ()
	{
		GLib.Functions.SetApplicationName (PintaCore.ApplicationDisplayName);
		TrySetPrgname ("PintaClassicWorkflow");
	}

	private static void TrySetPrgname (string prgname)
	{
		ReadOnlySpan<string> candidates = OperatingSystem.IsWindows ()
			? ["libglib-2.0-0.dll", "glib-2.0-0.dll"]
			: OperatingSystem.IsMacOS ()
				? ["libglib-2.0.0.dylib"]
				: ["libglib-2.0.so.0", "libglib-2.0.so"];

		foreach (string lib in candidates) {
			try {
				nint handle = NativeLibrary.Load (lib);
				if (!NativeLibrary.TryGetExport (handle, "g_set_prgname", out nint sym))
					continue;

				var setPrgname = Marshal.GetDelegateForFunctionPointer<SetPrgnameFunc> (sym);
				IntPtr utf8 = Marshal.StringToCoTaskMemUTF8 (prgname);
				try {
					setPrgname (utf8);
				} finally {
					Marshal.FreeCoTaskMem (utf8);
				}

				return;
			} catch (DllNotFoundException) {
				// Try next candidate.
			}
		}
	}
}
