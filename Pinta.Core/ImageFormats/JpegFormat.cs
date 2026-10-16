//
// JpegFormat.cs
//
// Author:
//       Maia Kozheva <sikon@ubuntu.com>
//
// Copyright (c) 2010 Maia Kozheva <sikon@ubuntu.com>
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using GdkPixbuf;

namespace Pinta.Core;

public sealed class JpegFormat : GdkPixbufFormat
{
	//The default JPG compression quality to use when no saved setting is loaded. This will usually
	//occur when Pinta is first run on a machine, although there are other possible cases as well.
	internal const int DefaultQuality = 85;

	public JpegFormat ()
		: base ("jpeg", supportsAlpha: false)
	{
	}

	protected override void DoSave (Pixbuf pb, Gio.File file, string fileType, Gtk.Window parent)
	{
		//Load the JPG compression quality, but use the default value if there is no saved value.
		int level = PintaCore.Settings.GetSetting<int> (SettingNames.JPG_QUALITY, DefaultQuality);

		using var stream = file.Replace ();
		try {
			pb.SaveToStreamv (stream, fileType, ["quality"], [level.ToString ()], null);
		} finally {
			stream.Close (null);
		}
	}
}
