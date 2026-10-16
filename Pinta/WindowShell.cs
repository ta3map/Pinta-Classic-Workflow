// 
// WindowShell.cs
//  
// Author:
//       Jonathan Pobst <monkey@jpobst.com>
// 
// Copyright (c) 2011 Jonathan Pobst
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

using System.Collections.Generic;
using System.Linq;
using Pinta.Core;
using Pinta.Docking;

namespace Pinta;

public sealed class WindowShell
{
	private readonly Gtk.ApplicationWindow app_window;
	private readonly Gtk.Overlay main_overlay;
	private readonly Gtk.Box top_panels;
	private readonly Gtk.Box status_bar;
	private readonly Gtk.Paned left_dock = CreateDock (Gtk.Align.Start);
	private readonly Gtk.Paned right_dock = CreateDock (Gtk.Align.End);

	// A column of up to two pads (top and bottom) sharing its height, lying over the workspace at the window edge.
	private static Gtk.Paned CreateDock (Gtk.Align edge)
	{
		Gtk.Paned dock = Gtk.Paned.New (Gtk.Orientation.Vertical);
		dock.ShrinkStartChild = true;
		dock.ShrinkEndChild = true;
		dock.Halign = edge;
		dock.Valign = Gtk.Align.Fill;
		dock.Visible = false;
		return dock;
	}

	public WindowShell (
		Gtk.Application app,
		string name,
		string title,
		int width,
		int height,
		bool maximize)
	{
		app_window = Gtk.ApplicationWindow.New (app);
		app_window.Name = name;
		app_window.Title = title;
		app_window.DefaultWidth = width;
		app_window.DefaultHeight = height;
		app_window.Resizable = true;

		if (maximize)
			app_window.Maximize ();

		// The workspace fills the whole area below the menu, and the toolbars and the status bar
		// lie over it, so that the image can be seen through them when they are translucent.
		top_panels = Gtk.Box.New (Gtk.Orientation.Vertical, 0);
		top_panels.Valign = Gtk.Align.Start;
		top_panels.AddCssClass ("overlay-panel");
		TopPanelsOpacity = new HoverOpacity (top_panels);

		status_bar = GtkExtensions.CreateToolBar ();
		status_bar.Valign = Gtk.Align.End;
		status_bar.AddCssClass ("overlay-panel");
		StatusBarOpacity = new HoverOpacity (status_bar);

		main_overlay = new Gtk.Overlay { Vexpand = true };
		main_overlay.AddOverlay (top_panels);

		Gtk.Box shell_layout = Gtk.Box.New (Gtk.Orientation.Vertical, 0);
		shell_layout.Append (CreateMenuBar (app.Menubar!));
		shell_layout.Append (main_overlay);
		app_window.SetChild (shell_layout);

		app_window.Present ();
	}

	public Gtk.Widget TopPanels => top_panels;
	public Gtk.Widget StatusBar => status_bar;
	public HoverOpacity TopPanelsOpacity { get; }
	public HoverOpacity StatusBarOpacity { get; }

	/// <summary>
	/// Adds a pad floating over the workspace and the panels, which can be docked to the window edge.
	/// </summary>
	public void AddPad (FloatingPad pad)
	{
		pad.TopPanel = top_panels;
		pad.BottomPanel = status_bar;
		pad.DockedChanged += (_, _) => MovePad (pad);
		main_overlay.AddOverlay (pad);
	}

	private void MovePad (FloatingPad pad)
	{
		Gtk.Paned dock = pad.FloatingHalign == Gtk.Align.End ? right_dock : left_dock;
		bool top = pad.FloatingValign == Gtk.Align.Start;
		FloatingPad? docked = pad.Docked ? pad : null;

		if (pad.Docked)
			main_overlay.RemoveOverlay (pad);

		dock.StartChild = top ? docked : dock.StartChild;
		dock.EndChild = top ? dock.EndChild : docked;

		if (!pad.Docked)
			main_overlay.AddOverlay (pad);

		dock.Visible = dock.StartChild is not null || dock.EndChild is not null;
		UpdateDockInsets ();
	}

	// The docks lie under the toolbars and the status bar, so they start below them.
	private void UpdateDockInsets ()
	{
		left_dock.MarginTop = right_dock.MarginTop = top_panels.GetVisibleHeight ();
		left_dock.MarginBottom = right_dock.MarginBottom = status_bar.GetVisibleHeight ();
	}

	/// <summary>
	/// GTK hides the icon of a menu item that has a label, so the icons are revealed whenever a menu opens.
	/// </summary>
	private static Gtk.PopoverMenuBar CreateMenuBar (Gio.MenuModel model)
	{
		Gtk.PopoverMenuBar menuBar = Gtk.PopoverMenuBar.NewFromModel (model);

		foreach (Gtk.PopoverMenu popover in menuBar.GetDescendants ().OfType<Gtk.PopoverMenu> ())
			popover.OnShow += (_, _) => popover.ShowMenuIcons ();

		return menuBar;
	}

	public Gtk.ApplicationWindow Window => app_window;

	public Gtk.Box CreateToolBar (string name)
	{
		Gtk.Box toolbar = GtkExtensions.CreateToolBar ();
		toolbar.Name = name;
		Gtk.Widget.VisiblePropertyDefinition.Notify (toolbar, (_, _) => UpdateDockInsets ());

		top_panels.Append (toolbar);

		return toolbar;
	}

	public Gtk.Box CreateStatusBar (string name)
	{
		status_bar.Name = name;
		Gtk.Widget.VisiblePropertyDefinition.Notify (status_bar, (_, _) => UpdateDockInsets ());
		main_overlay.AddOverlay (status_bar);
		return status_bar;
	}

	/// <summary>
	/// The workspace fills the whole area; the docks lie over it, so the image is seen through translucent docked pads.
	/// </summary>
	public void CreateWorkspace (Gtk.Widget workspace)
	{
		main_overlay.Child = workspace;
		main_overlay.AddOverlay (left_dock);
		main_overlay.AddOverlay (right_dock);
	}
}
