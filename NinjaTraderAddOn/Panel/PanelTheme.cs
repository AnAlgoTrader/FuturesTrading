using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FootPrintNT
{
    /// <summary>
    /// Dark theme + small WPF builders for the order-flow side panel.
    /// Colours mirror wwwroot/assets/main.css of the FootPrintTrader Photino app.
    /// </summary>
    internal static class PanelTheme
    {
        public const string BgHex = "#0f131a";
        public const string PanelHex = "#171c26";
        public const string CardHex = "#1c2230";
        public const string BorderHex = "#262d3b";
        public const string TextHex = "#e4e8f0";
        public const string MutedHex = "#8a93a6";
        public const string UpHex = "#2fcf93";
        public const string DownHex = "#f0647b";
        public const string WarnHex = "#f2b705";
        public const string InfoHex = "#38bdf8";

        public static readonly Brush Bg = Freeze(BgHex);
        public static readonly Brush Panel = Freeze(PanelHex);
        public static readonly Brush Card = Freeze(CardHex);
        public static readonly Brush Border = Freeze(BorderHex);
        public static readonly Brush Text = Freeze(TextHex);
        public static readonly Brush Muted = Freeze(MutedHex);
        public static readonly Brush Up = Freeze(UpHex);
        public static readonly Brush Down = Freeze(DownHex);
        public static readonly Brush Warn = Freeze(WarnHex);
        public static readonly Brush Info = Freeze(InfoHex);

        public static Brush Tone(string tone)
        {
            switch (tone)
            {
                case "bullish": return Up;
                case "bearish": return Down;
                case "warning": return Warn;
                default: return Muted;
            }
        }

        private static Brush Freeze(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }

        public static TextBlock TextBlock(string text, Brush foreground, double fontSize = 12, FontWeight? weight = null)
        {
            var tb = new TextBlock
            {
                Text = text,
                Foreground = foreground,
                FontSize = fontSize,
                TextWrapping = TextWrapping.Wrap
            };
            if (weight.HasValue) tb.FontWeight = weight.Value;
            return tb;
        }

        /// <summary>Card with a title row; body is returned for filling.</summary>
        public static Border MakeCard(string title, Brush accent, out StackPanel body)
        {
            var titleTb = TextBlock(title, Muted, 10, FontWeights.Bold);

            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            header.Children.Add(titleTb);

            body = new StackPanel();

            var root = new StackPanel();
            root.Children.Add(header);
            root.Children.Add(body);

            return new Border
            {
                Background = Card,
                BorderBrush = accent ?? Border,
                BorderThickness = accent != null ? new Thickness(3, 0, 0, 0) : new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 8),
                Child = root
            };
        }

        /// <summary>Label + value box pair for metric grids.</summary>
        public static FrameworkElement Metric(string label, out TextBlock value)
        {
            var stack = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
            stack.Children.Add(TextBlock(label, Muted, 10));
            value = TextBlock("-", Text, 13, FontWeights.SemiBold);
            stack.Children.Add(value);
            return stack;
        }

        public static Grid MetricsGrid(int columns)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 0) };
            for (var i = 0; i < columns; i++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            return grid;
        }

        public static void AddMetric(Grid grid, int row, int col, FrameworkElement element)
        {
            while (grid.RowDefinitions.Count <= row)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(element, row);
            Grid.SetColumn(element, col);
            grid.Children.Add(element);
        }
    }
}
