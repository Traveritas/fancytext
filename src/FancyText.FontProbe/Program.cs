using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FancyText.FontProbe;

// Dev-only matrix: which rendering strategy attaches Latin combining marks to CJK base glyphs
// without tofu. Rows = strategies, columns = problem cases.
internal static class Program
{
    private const string YaHei = "Microsoft YaHei UI";
    private const string CurrentChainPlusArial =
        "Microsoft YaHei UI, Segoe UI, Segoe UI Symbol, Segoe UI Historic, " +
        "Nirmala UI, Ebrima, Microsoft Himalaya, Leelawadee UI, Lao UI, Sylfaen, Arial";

    [STAThread]
    private static void Main()
    {
        var app = new Application();
        var win = new Window
        {
            Title = "FontProbe",
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            SizeToContent = SizeToContent.WidthAndHeight,
            Background = Brushes.White,
        };

        var cases = new (string Label, string Text)[]
        {
            ("U+0333+0313", "好\u0333\u0313的"),
            ("U+0353+0330", "的\u0353\u0330好"),
            ("U+0488 ring", "好\u0488好\u0489"),
            ("Latin base", "a\u0333\u0313b"),
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });

        AddHeader(grid, 0, "strategy", cases);

        AddRow(grid, 1, "1 YaHei only", cases, text => Make(text, YaHei));
        AddRow(grid, 2, "2 chain+Arial", cases, text => Make(text, CurrentChainPlusArial));
        AddRow(grid, 3, "3 Arial first", cases, text => Make(text, "Arial, " + YaHei));
        AddRow(grid, 4, "4 GlobalUserInterface", cases, text => Make(text, "Global User Interface"));
        AddRow(grid, 5, "5 custom CompositeFont", cases, text => Make(text,
            new FontFamily(new Uri("pack://application:,,,/"), "./FancyProbe.CompositeFont")));
        AddRow(grid, 6, "6 Run-split YaHei+Arial", cases, MakeRunSplit);

        var scroll = new ScrollViewer { Content = grid };
        win.Content = scroll;
        app.Run(win);
    }

    private static TextBlock Make(string text, string fontFamily) => Make(text, new FontFamily(fontFamily));

    private static TextBlock Make(string text, FontFamily fontFamily) => new()
    {
        Text = text,
        FontFamily = fontFamily,
        FontSize = 26,
        Foreground = Brushes.Black,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(6),
    };

    // Strategy 6: base chars and combining marks in separate Runs so each run gets a font
    // that actually contains its glyphs; combining glyphs are zero-advance so they still
    // overlay the preceding base glyph.
    private static TextBlock MakeRunSplit(string text)
    {
        var tb = new TextBlock { FontSize = 26, Foreground = Brushes.Black, Margin = new Thickness(6) };
        var yaHei = new FontFamily(YaHei);
        var arial = new FontFamily("Arial");
        var pending = new List<(char c, bool isMark)>();
        foreach (var ch in text)
        {
            bool isMark = ch >= '\u0300' && ch <= '\u036F' || ch >= '\u0483' && ch <= '\u0489';
            pending.Add((ch, isMark));
        }
        FontFamily current = yaHei;
        var buffer = new System.Text.StringBuilder();
        foreach (var (ch, isMark) in pending)
        {
            var want = isMark ? arial : yaHei;
            if (want != current && buffer.Length > 0)
            {
                tb.Inlines.Add(new System.Windows.Documents.Run(buffer.ToString()) { FontFamily = current });
                buffer.Clear();
            }
            current = want;
            buffer.Append(ch);
        }
        if (buffer.Length > 0)
            tb.Inlines.Add(new System.Windows.Documents.Run(buffer.ToString()) { FontFamily = current });
        return tb;
    }

    private static void AddHeader(Grid grid, int row, string label, (string, string)[] cases)
    {
        grid.RowDefinitions.Add(new RowDefinition());
        AddCell(grid, row, 0, new TextBlock
        {
            Text = label,
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            Foreground = Brushes.DimGray,
        });
        for (int i = 0; i < cases.Length; i++)
        {
            AddCell(grid, row, i + 1, new TextBlock
            {
                Text = cases[i].Item1,
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                Foreground = Brushes.DimGray,
            });
        }
    }

    private static void AddRow(Grid grid, int row, string label, (string, string)[] cases,
        Func<string, TextBlock> make)
    {
        grid.RowDefinitions.Add(new RowDefinition());
        AddCell(grid, row, 0, new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = Brushes.DimGray,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6),
        });
        for (int i = 0; i < cases.Length; i++)
            AddCell(grid, row, i + 1, make(cases[i].Item2));
    }

    private static void AddCell(Grid grid, int row, int col, FrameworkElement el)
    {
        Grid.SetRow(el, row);
        Grid.SetColumn(el, col);
        grid.Children.Add(el);
    }
}
