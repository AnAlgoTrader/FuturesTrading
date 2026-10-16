using System;
using NinjaTrader.Gui.Tools;
// NOTE: deliberately no "using System.Windows.Controls;" — NinjaTrader.Gui.Tools.TabControl
// would clash with the WPF one otherwise.

namespace FootPrintNT
{
    /// <summary>
    /// Parent window for the order-flow panel tabs. Styled like native NT windows
    /// and persisted with the workspace.
    /// </summary>
    public class FlowPanelWindow : NTWindow, IWorkspacePersistence
    {
        public FlowPanelWindow()
        {
            Caption = "Order Flow Panel";
            Width = 420;
            Height = 900;

            // TabControl should be created for window content if tab features are wanted
            var tc = new TabControl();
            var factory = new FlowPanelTabFactory();
            tc.Factories.Add(factory);
            Content = tc;

            Loaded += (o, e) =>
            {
                if (WorkspaceOptions == null)
                    WorkspaceOptions = new WorkspaceOptions("FootPrintPanel-" + Guid.NewGuid().ToString("N"), this);
            };
        }
    }

    /// <summary>
    /// Creates tabs (+ button) inside the FlowPanelWindow.
    /// </summary>
    public class FlowPanelTabFactory : INTTabFactory
    {
        // INTTabFactory member. Creates the parent window that contains tabs
        public NTWindow CreateParentWindow()
        {
            return new FlowPanelWindow();
        }

        // INTTabFactory member. Creates new tab pages whenever the user presses the + button
        public NTTabPage CreateTabPage(string typeName, bool isNewWindow = false)
        {
            return new FlowPanelTab();
        }
    }
}
