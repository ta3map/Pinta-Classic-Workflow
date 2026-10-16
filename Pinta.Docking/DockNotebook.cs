//  
// Author:
//       Cameron White <cameronwhite91@gmail.com>
// 
// Copyright (c) 2020 Cameron White
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

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Pinta.Core;

namespace Pinta.Docking;

public sealed class TabEventArgs (IDockNotebookItem? item) : EventArgs
{
	public IDockNotebookItem? Item { get; } = item;
}

public sealed class DockNotebook : Gtk.Box
{
	private readonly Adw.TabView tab_view;
	private readonly HashSet<IDockNotebookItem> items = [];

	public DockNotebook ()
	{
		Adw.TabView tabView = new () {
			Vexpand = true,
			Valign = Gtk.Align.Fill,
		};

		// --- Initialization (Gtk.Box)

		SetOrientation (Gtk.Orientation.Vertical);

		Append (tabView);

		// --- References to keep

		tab_view = tabView;

		// --- Further initialization

		// Emit an event when the current tab is changed.
		Adw.TabView.SelectedPagePropertyDefinition.Notify (tabView, TabView_TabChanged);
	}

	private void TabView_TabChanged (GObject.Object _, NotifySignalArgs __)
	{
		Adw.TabPage? page = tab_view.SelectedPage;
		IDockNotebookItem? item = FindItemForPage (page);
		ActiveTabChanged?.Invoke (this, new TabEventArgs (item));
	}

	/// <summary>
	/// Emitted when switching to a different tab.
	/// </summary>
	public event EventHandler<TabEventArgs>? ActiveTabChanged;

	/// <summary>
	/// The items currently in the notebook.
	/// </summary>
	public IEnumerable<IDockNotebookItem> Items => items;

	/// <summary>
	/// Returns the active notebook item.
	/// </summary>
	public IDockNotebookItem? ActiveItem {
		get => FindItemForPage (tab_view.SelectedPage);
		set => tab_view.SelectedPage = tab_view.GetPage (value!.Widget);
	}

	/// <summary>
	/// Returns the index of the active item.
	/// </summary>
	public int ActiveItemIndex => tab_view.SelectedPage switch {
		null => -1,
		var page => tab_view.GetPagePosition (page)
	};

	public void InsertTab (IDockNotebookItem item, int position)
	{
		items.Add (item);

		Adw.TabPage page = tab_view.Insert (item.Widget, position);
		page.Title = item.Label;
		// Update the tab's label when the document's title changes.
		item.LabelChanged += (o, args) => { page.Title = item.Label; };
	}

	public void RemoveTab (IDockNotebookItem item)
	{
		Adw.TabPage page = tab_view.GetPage (item.Widget);
		tab_view.ClosePage (page);
		items.Remove (item);
	}

	private IDockNotebookItem? FindItemForPage (Adw.TabPage? page) => items.Where (i => i.Widget == page?.Child).FirstOrDefault ();
}
