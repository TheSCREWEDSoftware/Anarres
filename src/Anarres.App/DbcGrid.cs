using System.Globalization;
using Anarres.Core.Dbc;
using Anarres.Core.Settings;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;

namespace Anarres.App;

// A table of a whole .dbc file, drawn cell by cell so even Spell.dbc's 50,000 rows by 234 columns stay quick
public sealed class DbcGrid : Grid
{
    private const double ROW_HEIGHT = 24;
    private const double HEADER_HEIGHT = 30;
    private const double CELL_PADDING = 6;
    private const double NUMBER_WIDTH = 96;
    private const double STRING_WIDTH = 240;
    private const double MIN_COLUMN_WIDTH = 40;
    private const double RESIZE_GRIP = 5;
    private const double FONT_SIZE = 13;
    private const double WHEEL_ROWS = 3;

    private readonly Surface _surface;
    private readonly ScrollBar _vertical = new ScrollBar { Orientation = Orientation.Vertical };
    private readonly ScrollBar _horizontal = new ScrollBar { Orientation = Orientation.Horizontal };
    private readonly Canvas _overlay = new Canvas();
    private readonly TextBox _editor = new TextBox { IsVisible = false, Padding = new Thickness(4, 2),
                                                     MinHeight = 0, FontSize = FONT_SIZE };
    private DbcTable? _table;
    private List<int> _rows = new List<int>();
    private double[] _widths = Array.Empty<double>();
    private int _editRow = -1;
    private int _editColumn = -1;

    public DbcGrid()
    {
        RowDefinitions = new RowDefinitions("*,Auto");
        ColumnDefinitions = new ColumnDefinitions("*,Auto");
        _surface = new Surface(this) { Focusable = true, ClipToBounds = true };
        Children.Add(_surface);
        Children.Add(_overlay);
        SetColumn(_vertical, 1);
        Children.Add(_vertical);
        SetRow(_horizontal, 1);
        Children.Add(_horizontal);
        _overlay.Children.Add(_editor);
        _vertical.Scroll += (_, _) => _surface.InvalidateVisual();
        _horizontal.Scroll += (_, _) => _surface.InvalidateVisual();
        _vertical.PropertyChanged += (_, change) =>
        {
            if (change.Property == RangeBase.ValueProperty)
                _surface.InvalidateVisual();
        };
        _horizontal.PropertyChanged += (_, change) =>
        {
            if (change.Property == RangeBase.ValueProperty)
                _surface.InvalidateVisual();
        };
        _surface.SizeChanged += (_, _) => UpdateScrollBars();
        _editor.KeyDown += EditorKeyDown;
        _editor.LostFocus += (_, _) => EndEdit(commit: true);
    }

    public DbcTable? Table => _table;

    // Row of the table under the selection, or -1
    public int SelectedRow => SelectedIndex >= 0 && SelectedIndex < _rows.Count ? _rows[SelectedIndex] : -1;

    public int SelectedIndex { get; private set; } = -1;

    public int SelectedColumn { get; private set; }

    public int VisibleRowCount => _rows.Count;

    public event Action? SelectionChanged;

    public event Action<int, int>? CellEdited;

    public event Action<string>? EditRejected;

    public void Show(DbcTable? table, List<int>? rows = null)
    {
        EndEdit(commit: false);
        bool sameTable = ReferenceEquals(table, _table);
        _table = table;
        _rows = rows ?? (table == null ? new List<int>() : Enumerable.Range(0, table.Rows.Count).ToList());
        if (!sameTable)
        {
            _widths = table?.Definition.Columns.Select(column => column.Type == DbcColumnType.String
                                                                     ? STRING_WIDTH
                                                                     : NUMBER_WIDTH).ToArray()
                      ?? Array.Empty<double>();
            _vertical.Value = 0;
            _horizontal.Value = 0;
            SelectedColumn = 0;
        }

        SelectedIndex = _rows.Count == 0 ? -1 : Math.Clamp(SelectedIndex, 0, _rows.Count - 1);
        UpdateScrollBars();
        _surface.InvalidateVisual();
        SelectionChanged?.Invoke();
    }

    public void Refresh()
    {
        _surface.InvalidateVisual();
    }

    public void SelectRow(int tableRow)
    {
        int index = _rows.IndexOf(tableRow);
        if (index >= 0)
            Select(index, SelectedColumn);
    }

    public void FocusGrid()
    {
        _surface.Focus();
    }

    private double ContentWidth => _widths.Sum();

    private void UpdateScrollBars()
    {
        double viewHeight = Math.Max(0, _surface.Bounds.Height - HEADER_HEIGHT);
        double viewWidth = _surface.Bounds.Width;
        _vertical.Maximum = Math.Max(0, _rows.Count * ROW_HEIGHT - viewHeight);
        _vertical.ViewportSize = viewHeight;
        _vertical.SmallChange = ROW_HEIGHT;
        _vertical.LargeChange = Math.Max(ROW_HEIGHT, viewHeight - ROW_HEIGHT);
        _horizontal.Maximum = Math.Max(0, ContentWidth - viewWidth);
        _horizontal.ViewportSize = viewWidth;
        _horizontal.SmallChange = NUMBER_WIDTH / 2;
        _horizontal.LargeChange = Math.Max(NUMBER_WIDTH, viewWidth - NUMBER_WIDTH);
    }

    private void Select(int index, int column)
    {
        if (_table == null || _rows.Count == 0)
            return;
        SelectedIndex = Math.Clamp(index, 0, _rows.Count - 1);
        SelectedColumn = Math.Clamp(column, 0, _widths.Length - 1);
        ScrollIntoView();
        _surface.InvalidateVisual();
        SelectionChanged?.Invoke();
    }

    private void ScrollIntoView()
    {
        double top = SelectedIndex * ROW_HEIGHT;
        double viewHeight = _surface.Bounds.Height - HEADER_HEIGHT;
        if (top < _vertical.Value)
            _vertical.Value = top;
        else if (top + ROW_HEIGHT > _vertical.Value + viewHeight)
            _vertical.Value = Math.Min(_vertical.Maximum, top + ROW_HEIGHT - viewHeight);

        double left = _widths.Take(SelectedColumn).Sum();
        double right = left + _widths[SelectedColumn];
        if (left < _horizontal.Value)
            _horizontal.Value = left;
        else if (right > _horizontal.Value + _surface.Bounds.Width)
            _horizontal.Value = Math.Min(_horizontal.Maximum, right - _surface.Bounds.Width);
    }

    private (int Index, int Column) HitTest(Point point)
    {
        int index = point.Y < HEADER_HEIGHT ? -1 : (int)((point.Y - HEADER_HEIGHT + _vertical.Value) / ROW_HEIGHT);
        double x = point.X + _horizontal.Value;
        double left = 0;
        for (int column = 0; column < _widths.Length; ++column)
        {
            if (x < left + _widths[column])
                return (index, column);
            left += _widths[column];
        }

        return (index, -1);
    }

    // The column whose right edge is under the pointer, for resizing
    private int ResizeColumn(Point point)
    {
        if (point.Y > HEADER_HEIGHT)
            return -1;
        double x = point.X + _horizontal.Value;
        double right = 0;
        for (int column = 0; column < _widths.Length; ++column)
        {
            right += _widths[column];
            if (Math.Abs(x - right) <= RESIZE_GRIP)
                return column;
        }

        return -1;
    }

    private void BeginEdit(string? startText = null)
    {
        if (_table == null || SelectedRow < 0)
            return;
        ScrollIntoView();
        _editRow = SelectedRow;
        _editColumn = SelectedColumn;
        double left = _widths.Take(SelectedColumn).Sum() - _horizontal.Value;
        double top = HEADER_HEIGHT + SelectedIndex * ROW_HEIGHT - _vertical.Value;
        Canvas.SetLeft(_editor, left);
        Canvas.SetTop(_editor, top);
        _editor.Width = _widths[SelectedColumn];
        _editor.Height = ROW_HEIGHT;
        _editor.Text = startText ?? _table.GetText(_editRow, _editColumn);
        _editor.IsVisible = true;
        _editor.Focus();
        if (startText == null)
            _editor.SelectAll();
        else
            _editor.CaretIndex = _editor.Text.Length;
    }

    private void EndEdit(bool commit)
    {
        if (_editRow < 0 || _table == null)
            return;
        int row = _editRow;
        int column = _editColumn;
        _editRow = -1;
        _editor.IsVisible = false;
        if (commit && _editor.Text != _table.GetText(row, column))
        {
            if (_table.TrySetText(row, column, _editor.Text ?? string.Empty, out string error))
                CellEdited?.Invoke(row, column);
            else
                EditRejected?.Invoke(_table.Definition.Columns[column].Name + ": " + error);
        }

        _surface.InvalidateVisual();
        _surface.Focus();
    }

    private void EditorKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
            {
                EndEdit(commit: true);
                Select(SelectedIndex + 1, SelectedColumn);
                e.Handled = true;
                break;
            }
            case Key.Tab:
            {
                EndEdit(commit: true);
                Select(SelectedIndex, SelectedColumn + ((e.KeyModifiers & KeyModifiers.Shift) != 0 ? -1 : 1));
                e.Handled = true;
                break;
            }
            case Key.Escape:
            {
                EndEdit(commit: false);
                e.Handled = true;
                break;
            }
            default:
                break;
        }
    }

    private async void Copy()
    {
        if (_table == null || SelectedRow < 0 || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
            return;
        await clipboard.SetTextAsync(_table.GetText(SelectedRow, SelectedColumn));
    }

    // Draws the visible cells and handles the pointer and keyboard for the grid
    private sealed class Surface : Control
    {
        private readonly DbcGrid _grid;
        private readonly Typeface _typeface = new Typeface(FontFamily.Default);
        private readonly Typeface _bold = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);
        private int _resizing = -1;
        private double _resizeStart;
        private double _resizeWidth;

        public Surface(DbcGrid grid)
        {
            _grid = grid;
            Cursor = Cursor.Default;
        }

        public override void Render(DrawingContext context)
        {
            Rect bounds = new Rect(Bounds.Size);
            context.FillRectangle(ThemeBrushes.Surface, bounds);
            DbcTable? table = _grid._table;
            if (table == null)
                return;

            IBrush text = ThemeBrushes.Text;
            IBrush muted = ThemeBrushes.MutedText;
            IBrush header = ThemeBrushes.Get(ThemeColours.GRID_HEADER);
            IBrush alternate = ThemeBrushes.Get(ThemeColours.GRID_ALTERNATE);
            IBrush changed = ThemeBrushes.Get(ThemeColours.CHANGED);
            Color accent = ThemeBrushes.ColourOf(ThemeColours.ACCENT);
            IBrush selectedRow = new SolidColorBrush(Color.FromArgb(0x30, accent.R, accent.G, accent.B));
            IBrush selectedCell = new SolidColorBrush(Color.FromArgb(0x70, accent.R, accent.G, accent.B));
            Pen line = new Pen(ThemeBrushes.Get(ThemeColours.GRID_LINE), 1);

            double offsetX = _grid._horizontal.Value;
            double offsetY = _grid._vertical.Value;
            int first = Math.Max(0, (int)(offsetY / ROW_HEIGHT));
            int last = Math.Min(_grid._rows.Count - 1,
                                first + (int)((Bounds.Height - HEADER_HEIGHT) / ROW_HEIGHT) + 1);
            (int firstColumn, double firstLeft) = FirstColumn(offsetX);

            using (context.PushClip(new Rect(0, HEADER_HEIGHT, Bounds.Width, Math.Max(0, Bounds.Height -
                                                                                         HEADER_HEIGHT))))
            {
                for (int index = first; index <= last; ++index)
                {
                    int row = _grid._rows[index];
                    double top = HEADER_HEIGHT + index * ROW_HEIGHT - offsetY;
                    Rect rowRect = new Rect(0, top, Bounds.Width, ROW_HEIGHT);
                    if (index == _grid.SelectedIndex)
                        context.FillRectangle(selectedRow, rowRect);
                    else if (table.ChangedKeys.Contains(table.Key(row)))
                        context.FillRectangle(changed, rowRect);
                    else if (index % 2 == 1)
                        context.FillRectangle(alternate, rowRect);

                    double left = firstLeft - offsetX;
                    for (int column = firstColumn; column < _grid._widths.Length && left < Bounds.Width; ++column)
                    {
                        double width = _grid._widths[column];
                        Rect cell = new Rect(left, top, width, ROW_HEIGHT);
                        if (index == _grid.SelectedIndex && column == _grid.SelectedColumn)
                            context.FillRectangle(selectedCell, cell);
                        DrawText(context, table.GetText(row, column), cell, text, _typeface,
                                 table.Definition.Columns[column].Type != DbcColumnType.String);
                        context.DrawLine(line, new Point(left + width - 0.5, top), new Point(left + width - 0.5,
                                                                                             top + ROW_HEIGHT));
                        left += width;
                    }

                    context.DrawLine(line, new Point(0, top + ROW_HEIGHT - 0.5), new Point(Bounds.Width,
                                                                                           top + ROW_HEIGHT - 0.5));
                }
            }

            context.FillRectangle(header, new Rect(0, 0, Bounds.Width, HEADER_HEIGHT));
            double headerLeft = firstLeft - offsetX;
            for (int column = firstColumn; column < _grid._widths.Length && headerLeft < Bounds.Width; ++column)
            {
                double width = _grid._widths[column];
                DbcColumn definition = table.Definition.Columns[column];
                DrawText(context, definition.Name, new Rect(headerLeft, 0, width, HEADER_HEIGHT),
                         column == _grid.SelectedColumn ? text : muted, _bold, rightAlign: false);
                context.DrawLine(line, new Point(headerLeft + width - 0.5, 0),
                                 new Point(headerLeft + width - 0.5, HEADER_HEIGHT));
                headerLeft += width;
            }

            context.DrawLine(line, new Point(0, HEADER_HEIGHT - 0.5), new Point(Bounds.Width, HEADER_HEIGHT - 0.5));
        }

        private (int Column, double Left) FirstColumn(double offsetX)
        {
            double left = 0;
            for (int column = 0; column < _grid._widths.Length; ++column)
            {
                if (left + _grid._widths[column] > offsetX)
                    return (column, left);
                left += _grid._widths[column];
            }

            return (_grid._widths.Length, left);
        }

        private static void DrawText(DrawingContext context, string value, Rect cell, IBrush brush,
                                     Typeface typeface, bool rightAlign)
        {
            if (value.Length == 0)
                return;
            FormattedText formatted = new FormattedText(value.Replace('\n', ' '), CultureInfo.InvariantCulture,
                                                        FlowDirection.LeftToRight, typeface, FONT_SIZE, brush)
            {
                MaxTextWidth = Math.Max(1, cell.Width - CELL_PADDING * 2),
                MaxLineCount = 1,
                Trimming = TextTrimming.CharacterEllipsis
            };
            double x = rightAlign ? cell.Right - CELL_PADDING - formatted.Width : cell.X + CELL_PADDING;
            double y = cell.Y + (cell.Height - formatted.Height) / 2;
            using (context.PushClip(cell))
                context.DrawText(formatted, new Point(x, y));
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            Focus();
            Point point = e.GetPosition(this);
            int resize = _grid.ResizeColumn(point);
            if (resize >= 0)
            {
                _resizing = resize;
                _resizeStart = point.X;
                _resizeWidth = _grid._widths[resize];
                e.Pointer.Capture(this);
                return;
            }

            (int index, int column) = _grid.HitTest(point);
            if (column < 0)
                return;
            if (index < 0)
            {
                _grid.Select(_grid.SelectedIndex, column);
                return;
            }

            if (index >= _grid._rows.Count)
                return;
            _grid.Select(index, column);
            if (e.ClickCount == 2)
                _grid.BeginEdit();
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            Point point = e.GetPosition(this);
            if (_resizing >= 0)
            {
                _grid._widths[_resizing] = Math.Max(MIN_COLUMN_WIDTH, _resizeWidth + point.X - _resizeStart);
                _grid.UpdateScrollBars();
                InvalidateVisual();
                return;
            }

            Cursor = _grid.ResizeColumn(point) >= 0 ? new Cursor(StandardCursorType.SizeWestEast) : Cursor.Default;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (_resizing < 0)
                return;
            _resizing = -1;
            e.Pointer.Capture(null);
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            bool sideways = (e.KeyModifiers & KeyModifiers.Shift) != 0 || Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y);
            if (sideways)
            {
                double delta = Math.Abs(e.Delta.X) > 0 ? e.Delta.X : e.Delta.Y;
                _grid._horizontal.Value = Math.Clamp(_grid._horizontal.Value - delta * NUMBER_WIDTH, 0,
                                                     _grid._horizontal.Maximum);
            }
            else
                _grid._vertical.Value = Math.Clamp(_grid._vertical.Value - e.Delta.Y * ROW_HEIGHT * WHEEL_ROWS, 0,
                                                   _grid._vertical.Maximum);
            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int page = Math.Max(1, (int)((Bounds.Height - HEADER_HEIGHT) / ROW_HEIGHT) - 1);
            bool control = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
            switch (e.Key)
            {
                case Key.Up:
                    _grid.Select(_grid.SelectedIndex - 1, _grid.SelectedColumn);
                    break;
                case Key.Down:
                    _grid.Select(_grid.SelectedIndex + 1, _grid.SelectedColumn);
                    break;
                case Key.Left:
                    _grid.Select(_grid.SelectedIndex, _grid.SelectedColumn - 1);
                    break;
                case Key.Right:
                case Key.Tab:
                    _grid.Select(_grid.SelectedIndex, _grid.SelectedColumn + 1);
                    break;
                case Key.PageUp:
                    _grid.Select(_grid.SelectedIndex - page, _grid.SelectedColumn);
                    break;
                case Key.PageDown:
                    _grid.Select(_grid.SelectedIndex + page, _grid.SelectedColumn);
                    break;
                case Key.Home:
                    _grid.Select(control ? 0 : _grid.SelectedIndex, 0);
                    break;
                case Key.End:
                    _grid.Select(control ? _grid._rows.Count - 1 : _grid.SelectedIndex, _grid._widths.Length - 1);
                    break;
                case Key.Enter:
                case Key.F2:
                    _grid.BeginEdit();
                    break;
                case Key.C when control:
                    _grid.Copy();
                    break;
                default:
                    return;
            }

            e.Handled = true;
        }

        // Typing on a selected cell starts editing it with what was typed
        protected override void OnTextInput(TextInputEventArgs e)
        {
            base.OnTextInput(e);
            if (string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0]))
                return;
            _grid.BeginEdit(e.Text);
            e.Handled = true;
        }
    }
}
