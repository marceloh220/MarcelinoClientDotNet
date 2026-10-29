using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using VtNetCore.VirtualTerminal;
using VtNetCore.XTermParser;

namespace MarcelinoClient;

public sealed class SshTerminalView : FrameworkElement
{
    private VirtualTerminalController _terminal = null!;
    private DataConsumer _consumer = null!;
    private readonly Typeface _typeface = new("Consolas");
    private readonly Dictionary<string, Brush> _brushes = new();
    private const double FontSize = 13;
    private const double LineHeight = 17;
    private const double Padding = 8;
    public int Columns { get; private set; } = 120;
    public int Rows { get; private set; } = 25;
    public bool IsConnected { get; set; }
    public string ScreenText => _terminal.GetScreenText();
    public event Action<byte[]>? DataToSend;
    public event Action<int, int>? TerminalSizeChanged;

    public SshTerminalView()
    {
        Focusable = true;
        ClipToBounds = true;
        Cursor = Cursors.IBeam;
        MinHeight = 25 * LineHeight + Padding * 2;
        Reset();
        SizeChanged += (_, _) => ResizeTerminal();
    }

    public void Reset()
    {
        _terminal = new VirtualTerminalController { MaximumHistoryLines = 2000 };
        _terminal.ResizeView(Columns, Rows);
        _terminal.FullReset();
        _terminal.SendData += (_, args) => DataToSend?.Invoke(args.Data);
        _consumer = new DataConsumer(_terminal);
        _brushes.Clear();
        InvalidateVisual();
    }

    public void Feed(byte[] bytes)
    {
        _consumer.Push(bytes);
        InvalidateVisual();
    }

    private FormattedText Format(string text, Brush brush, double pixelsPerDip) => new(text, CultureInfo.CurrentCulture,
        FlowDirection.LeftToRight, _typeface, FontSize, brush, pixelsPerDip);

    private double CellWidth => Format("M", Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip).WidthIncludingTrailingWhitespace;

    private void ResizeTerminal()
    {
        if (ActualWidth < 80 || ActualHeight < 50)
        {
            return;
        }

        var columns = Math.Clamp((int)((ActualWidth - Padding * 2) / CellWidth), 10, 500);
        var rows = Math.Max(25, (int)((ActualHeight - Padding * 2) / LineHeight));
        if (columns == Columns && rows == Rows)
        {
            return;
        }

        Columns = columns;
        Rows = rows;
        _terminal.ResizeView(columns, Rows);
        TerminalSizeChanged?.Invoke(columns, Rows);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.DrawRectangle(Brushes.Black, null, new Rect(RenderSize));
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var cellWidth = Format("M", Brushes.White, pixelsPerDip).WidthIncludingTrailingWhitespace;
        var lines = _terminal.GetPageSpans(_terminal.ViewPort.TopRow, Rows, Columns);
        for (var row = 0; row < lines.Count; row++)
        {
            var column = 0;
            foreach (var span in lines[row].Spans)
            {
                if (span.Text.Length == 0)
                {
                    continue;
                }

                var foreground = ColorBrush(span.ForgroundColor);
                var background = ColorBrush(span.BackgroundColor);
                if (span.Text.All(character => character <= 0x7f))
                {
                    var origin = new Point(Padding + column * cellWidth, Padding + row * LineHeight);
                    var spanWidth = span.Text.Length * cellWidth;
                    drawingContext.DrawRectangle(background, null,
                        new Rect(origin, new Size(spanWidth, LineHeight)));
                    if (!span.Hidden)
                    {
                        DrawText(drawingContext, CreateText(span.Text, foreground, span.Bold, span.Underline, pixelsPerDip),
                            origin, spanWidth);
                    }

                    column += span.Text.Length;
                    continue;
                }

                var elements = StringInfo.GetTextElementEnumerator(span.Text);
                while (elements.MoveNext())
                {
                    var text = CreateText(elements.GetTextElement(), foreground, span.Bold, span.Underline, pixelsPerDip);
                    var cellCount = Math.Clamp(
                        (int)Math.Round(text.WidthIncludingTrailingWhitespace / cellWidth, MidpointRounding.AwayFromZero), 1, 2);
                    var origin = new Point(Padding + column * cellWidth, Padding + row * LineHeight);
                    var cellSpanWidth = cellCount * cellWidth;
                    drawingContext.DrawRectangle(background, null,
                        new Rect(origin, new Size(cellSpanWidth, LineHeight)));
                    if (!span.Hidden)
                    {
                        DrawText(drawingContext, text, origin, cellSpanWidth);
                    }

                    column += cellCount;
                }
            }
        }

        if (IsKeyboardFocused && IsConnected && _terminal.CursorState.ShowCursor)
        {
            var cursor = _terminal.ViewPort.CursorPosition;
            drawingContext.DrawRectangle(Brushes.LightGray, null,
                new Rect(Padding + cursor.Column * cellWidth, Padding + cursor.Row * LineHeight + LineHeight - 2, cellWidth, 2));
        }
    }

    private Brush ColorBrush(string color)
    {
        if (!_brushes.TryGetValue(color, out var brush))
        {
            brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze();
            if (_brushes.Count >= 512)
            {
                _brushes.Clear();
            }

            _brushes[color] = brush;
        }

        return brush;
    }

    private FormattedText CreateText(string value, Brush foreground, bool bold, bool underline, double pixelsPerDip)
    {
        var text = Format(value, foreground, pixelsPerDip);
        if (bold)
        {
            text.SetFontWeight(FontWeights.Bold);
        }

        if (underline)
        {
            text.SetTextDecorations(TextDecorations.Underline);
        }

        return text;
    }

    private static void DrawText(DrawingContext drawingContext, FormattedText text, Point origin, double targetWidth)
    {
        if (text.WidthIncludingTrailingWhitespace <= 0)
        {
            return;
        }

        drawingContext.PushTransform(new ScaleTransform(
            targetWidth / text.WidthIncludingTrailingWhitespace, 1, origin.X, origin.Y));
        drawingContext.DrawText(text, origin);
        drawingContext.Pop();
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        e.Handled = true;
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        InvalidateVisual();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        InvalidateVisual();
    }

    protected override void OnPreviewTextInput(TextCompositionEventArgs e)
    {
        base.OnPreviewTextInput(e);
        if (IsConnected && !string.IsNullOrEmpty(e.Text))
        {
            var prefix = Keyboard.Modifiers == ModifierKeys.Alt ? "\u001b" : "";
            DataToSend?.Invoke(Encoding.UTF8.GetBytes(prefix + e.Text));
        }

        e.Handled = true;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (!IsConnected)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (control && shift && key == Key.C)
        {
            Clipboard.SetText(ScreenText);
            e.Handled = true;
            return;
        }

        if (control && shift && key == Key.V)
        {
            PasteClipboard();
            e.Handled = true;
            return;
        }

        var bytes = key == Key.Return ? new byte[] { 13 } : key == Key.Escape ? new byte[] { 27 } :
            _terminal.GetKeySequence(key.ToString(), control && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt), shift);
        if (bytes is not null)
        {
            DataToSend?.Invoke(bytes);
            e.Handled = true;
        }
    }

    public void PasteClipboard()
    {
        if (!IsConnected || !Clipboard.ContainsText())
        {
            return;
        }

        var text = Clipboard.GetText().Replace("\r\n", "\n").Replace('\r', '\n');
        if (text.Contains('\n') && MessageBox.Show(Window.GetWindow(this),
            Localization.Get("PasteMultilineWarning"), Localization.Get("PasteTerminalTitle"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        if (_terminal.BracketedPasteMode)
        {
            text = "\u001b[200~" + text + "\u001b[201~";
        }

        DataToSend?.Invoke(Encoding.UTF8.GetBytes(text));
    }
}