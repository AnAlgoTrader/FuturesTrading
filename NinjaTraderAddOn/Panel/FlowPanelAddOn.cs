using System;
using System.Windows;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;

namespace FootPrintNT
{
    /// <summary>
    /// Entry point. Adds "Order Flow Panel" to the Control Center's New menu.
    /// </summary>
    public class FlowPanelAddOn : AddOnBase
    {
        private NTMenuItem newMenuItem;
        private NTMenuItem existingMenuItem;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Order flow side panel: footprint analyzer, interpreter, bias and signals";
                Name = "FootPrintPanel";
            }
        }

        // Will be called as a new NTWindow is created. It will be called on the thread of that window
        protected override void OnWindowCreated(Window window)
        {
            // We want to place our AddOn in the Control Center's "New" menu.
            // Found by automation id so this file has no hard compile-time dependency
            // on the ControlCenter type (and simply does nothing on other windows).
            existingMenuItem = window.FindFirst("ControlCenterMenuItemNew") as NTMenuItem;
            if (existingMenuItem == null)
                return;

            // 'Header' sets the name of our AddOn seen in the menu structure
            newMenuItem = new NTMenuItem
            {
                Header = "Order Flow Panel",
                Style = Application.Current.TryFindResource("MainMenuItem") as Style
            };

            existingMenuItem.Items.Add(newMenuItem);
            newMenuItem.Click += OnMenuItemClick;
        }

        protected override void OnWindowDestroyed(Window window)
        {
            if (newMenuItem == null || existingMenuItem == null)
                return;

            // Only clean up when the Control Center itself is closing
            if (window.GetType().Name != "ControlCenter")
                return;

            if (existingMenuItem.Items.Contains(newMenuItem))
                existingMenuItem.Items.Remove(newMenuItem);

            newMenuItem.Click -= OnMenuItemClick;
            newMenuItem = null;
            existingMenuItem = null;
        }

        private void OnMenuItemClick(object sender, RoutedEventArgs e)
        {
            NinjaTrader.Core.Globals.RandomDispatcher.InvokeAsync(new Action(() => new FlowPanelWindow().Show()));
        }
    }
}
