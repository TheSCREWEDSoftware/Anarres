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
    // Row height is the text size plus this much room
    private const double ROW_PADDING = 11;
    private const double HEADER_HEIGHT = 30;
    private const double CELL_PADDING = 6;
    private const double NUMBER_WIDTH = 96;
    private const double STRING_WIDTH = 240;
    private const double MIN_COLUMN_WIDTH = 40;
    private const double RESIZE_GRIP = 5;
    private const double DEFAULT_FONT_SIZE = 13;
    private const double WHEEL_ROWS = 3;

    private readonly Surface _surface;
    private readonly ScrollBar _vertical = new ScrollBar { Orientation = Orientation.Vertical };
    private readonly ScrollBar _horizontal = new ScrollBar { Orientation = Orientation.Horizontal };
    private readonly Canvas _overlay = new Canvas();
    private readonly TextBox _editor = new TextBox { IsVisible = false, Padding = new Thickness(4, 2),
                                                     MinHeight = 0, FontSize = DEFAULT_FONT_SIZE };
    private DbcTable? _table;
    private List<int> _rows = new List<int>();
    private double[] _widths = Array.Empty<double>();
    // The columns shown, in order; hidden ones are left out
    private List<int> _order = new List<int>();
    private List<int> _baseRows = new List<int>();
    private int _sortColumn = -1;
    private bool _sortDescending;
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

    public double CellFontSize { get; private set; } = DEFAULT_FONT_SIZE;

    public double RowHeight => CellFontSize + ROW_PADDING;

    // Flags, Mask and Attributes columns drawn as 0x...; editing still shows and takes either form
    public bool FlagsInHex { get; set; } = true;

    // New tables open with their all-zero columns hidden
    public bool HideEmptyByDefault { get; set; }

    public void UseFontSize(double size)
    {
        CellFontSize = size;
        _editor.FontSize = size;
        UpdateScrollBars();
        _surface.InvalidateVisual();
    }

    // How a cell is drawn: as the table writes it, or in hex for flag columns
    private string Display(DbcTable table, int row, int column)
    {
        DbcColumn definition = table.Definition.Columns[column];
        if (FlagsInHex && DbcColumnTypes.IsInteger(definition.Type) && IsFlagColumn(definition.Name))
            return "0x" + table.GetRaw(row, column).ToString("X8", CultureInfo.InvariantCulture);
        return table.GetText(row, column);
    }

    private static bool IsFlagColumn(string name)
    {
        return name.Contains("Flags", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Mask", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("Attributes", StringComparison.OrdinalIgnoreCase);
    }

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
        _baseRows = rows ?? (table == null ? new List<int>() : Enumerable.Range(0, table.Rows.Count).ToList());
        if (!sameTable)
        {
            _widths = table?.Definition.Columns.Select(column => column.Type == DbcColumnType.String
                                                                     ? STRING_WIDTH
                                                                     : NUMBER_WIDTH).ToArray()
                      ?? Array.Empty<double>();
            _order = Enumerable.Range(0, _widths.Length).ToList();
            _sortColumn = -1;
            _vertical.Value = 0;
            _horizontal.Value = 0;
            SelectedColumn = 0;
        }

        _rows = Sorted(_baseRows);
        if (!sameTable && HideEmptyByDefault && table != null)
            HideEmptyColumns();

        SelectedIndex = _rows.Count == 0 ? -1 : Math.Clamp(SelectedIndex, 0, _rows.Count - 1);
        UpdateScrollBars();
        _surface.InvalidateVisual();
        SelectionChanged?.Invoke();
    }

    public void Refresh()
    {
        _surface.InvalidateVisual();
    }

    public bool IsSorted => _sortColumn >= 0;

    // Table rows in the order they're shown, after the filter and the sort
    public IReadOnlyList<int> ShownRows => _rows;

    public IReadOnlyList<int> ShownColumns => _order;

    public int HiddenColumnCount => _widths.Length - _order.Count;

    // Sorts the shown rows by a column, or back to the table's order with column -1
    public void SortBy(int column, bool descending)
    {
        int selected = SelectedRow;
        _sortColumn = column;
        _sortDescending = descending;
        _rows = Sorted(_baseRows);
        SelectedIndex = selected >= 0 ? _rows.IndexOf(selected) : SelectedIndex;
        _surface.InvalidateVisual();
        SelectionChanged?.Invoke();
    }

    public void HideColumn(int column)
    {
        if (_order.Count <= 1)
            return;
        int position = _order.IndexOf(column);
        _order.Remove(column);
        if (column == SelectedColumn)
            SelectedColumn = _order[Math.Clamp(position, 0, _order.Count - 1)];
        UpdateScrollBars();
        _surface.InvalidateVisual();
    }

    public void ShowAllColumns()
    {
        _order = Enumerable.Range(0, _widths.Length).ToList();
        UpdateScrollBars();
        _surface.InvalidateVisual();
    }

    // Hides the columns that are zero or empty in every row
    public void HideEmptyColumns()
    {
        if (_table == null)
            return;
        DbcTable table = _table;
        _order = _order.Where(column => column == 0 || table.Rows.Any(cells => cells[column] != 0)).ToList();
        if (!_order.Contains(SelectedColumn))
            SelectedColumn = _order[0];
        UpdateScrollBars();
        _surface.InvalidateVisual();
    }

    private List<int> Sorted(List<int> rows)
    {
        if (_table == null || _sortColumn < 0 || _sortColumn >= _widths.Length)
            return rows;
        DbcTable table = _table;
        int column = _sortColumn;
        DbcColumnType type = table.Definition.Columns[column].Type;
        Comparison<int> compare = type switch
        {
            DbcColumnType.String => (left, right) => string.Compare(table.GetString(left, column),
                                                                    table.GetString(right, column),
                                                                    StringComparison.OrdinalIgnoreCase),
            DbcColumnType.Float => (left, right) => table.GetFloat(left, column)
                                                         .CompareTo(table.GetFloat(right, column)),
            DbcColumnType.Int32 or DbcColumnType.SByte or DbcColumnType.Int16 =>
                (left, right) => table.GetInt(left, column).CompareTo(table.GetInt(right, column)),
            _ => (left, right) => table.GetUInt(left, column).CompareTo(table.GetUInt(right, column))
        };
        List<int> sorted = new List<int>(rows);
        // A stable sort, so rows with equal values keep their order
        List<(int Row, int Position)> keyed = sorted.Select((row, position) => (row, position)).ToList();
        keyed.Sort((left, right) =>
        {
            int result = compare(left.Row, right.Row);
            if (_sortDescending)
                result = -result;
            return result != 0 ? result : left.Position.CompareTo(right.Position);
        });
        return keyed.Select(entry => entry.Row).ToList();
    }

    private double LeftOf(int column)
    {
        double left = 0;
        foreach (int shown in _order)
        {
            if (shown == column)
                break;
            left += _widths[shown];
        }

        return left;
    }

    // The shown column delta places away from a column
    private int Step(int column, int delta)
    {
        int position = _order.IndexOf(column);
        if (position < 0)
            return _order.Count > 0 ? _order[0] : 0;
        return _order[Math.Clamp(position + delta, 0, _order.Count - 1)];
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

    private double ContentWidth => _order.Sum(column => _widths[column]);

    private void UpdateScrollBars()
    {
        double viewHeight = Math.Max(0, _surface.Bounds.Height - HEADER_HEIGHT);
        double viewWidth = _surface.Bounds.Width;
        _vertical.Maximum = Math.Max(0, _rows.Count * RowHeight - viewHeight);
        _vertical.ViewportSize = viewHeight;
        _vertical.SmallChange = RowHeight;
        _vertical.LargeChange = Math.Max(RowHeight, viewHeight - RowHeight);
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
        SelectedColumn = _order.Contains(column) ? column : Step(column, 0);
        ScrollIntoView();
        _surface.InvalidateVisual();
        SelectionChanged?.Invoke();
    }

    private void ScrollIntoView()
    {
        double top = SelectedIndex * RowHeight;
        double viewHeight = _surface.Bounds.Height - HEADER_HEIGHT;
        if (top < _vertical.Value)
            _vertical.Value = top;
        else if (top + RowHeight > _vertical.Value + viewHeight)
            _vertical.Value = Math.Min(_vertical.Maximum, top + RowHeight - viewHeight);

        double left = LeftOf(SelectedColumn);
        double right = left + _widths[SelectedColumn];
        if (left < _horizontal.Value)
            _horizontal.Value = left;
        else if (right > _horizontal.Value + _surface.Bounds.Width)
            _horizontal.Value = Math.Min(_horizontal.Maximum, right - _surface.Bounds.Width);
    }

    private (int Index, int Column) HitTest(Point point)
    {
        int index = point.Y < HEADER_HEIGHT ? -1 : (int)((point.Y - HEADER_HEIGHT + _vertical.Value) / RowHeight);
        double x = point.X + _horizontal.Value;
        double left = 0;
        foreach (int column in _order)
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
        foreach (int column in _order)
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
        double left = LeftOf(SelectedColumn) - _horizontal.Value;
        double top = HEADER_HEIGHT + SelectedIndex * RowHeight - _vertical.Value;
        Canvas.SetLeft(_editor, left);
        Canvas.SetTop(_editor, top);
        _editor.Width = _widths[SelectedColumn];
        _editor.Height = RowHeight;
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
                Select(SelectedIndex, Step(SelectedColumn, (e.KeyModifiers & KeyModifiers.Shift) != 0 ? -1 : 1));
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
            int first = Math.Max(0, (int)(offsetY / _grid.RowHeight));
            int last = Math.Min(_grid._rows.Count - 1,
                                first + (int)((Bounds.Height - HEADER_HEIGHT) / _grid.RowHeight) + 1);
            (int firstPosition, double firstLeft) = FirstColumn(offsetX);
            List<int> order = _grid._order;

            using (context.PushClip(new Rect(0, HEADER_HEIGHT, Bounds.Width, Math.Max(0, Bounds.Height -
                                                                                         HEADER_HEIGHT))))
            {
                for (int index = first; index <= last; ++index)
                {
                    int row = _grid._rows[index];
                    double top = HEADER_HEIGHT + index * _grid.RowHeight - offsetY;
                    Rect rowRect = new Rect(0, top, Bounds.Width, _grid.RowHeight);
                    if (index == _grid.SelectedIndex)
                        context.FillRectangle(selectedRow, rowRect);
                    else if (table.ChangedKeys.Contains(table.Key(row)))
                        context.FillRectangle(changed, rowRect);
                    else if (index % 2 == 1)
                        context.FillRectangle(alternate, rowRect);

                    double left = firstLeft - offsetX;
                    for (int position = firstPosition; position < order.Count && left < Bounds.Width; ++position)
                    {
                        int column = order[position];
                        double width = _grid._widths[column];
                        Rect cell = new Rect(left, top, width, _grid.RowHeight);
                        if (index == _grid.SelectedIndex && column == _grid.SelectedColumn)
                            context.FillRectangle(selectedCell, cell);
                        DrawText(context, _grid.Display(table, row, column), cell, text, _typeface,
                                 table.Definition.Columns[column].Type != DbcColumnType.String);
                        context.DrawLine(line, new Point(left + width - 0.5, top), new Point(left + width - 0.5,
                                                                                             top + _grid.RowHeight));
                        left += width;
                    }

                    double bottom = top + _grid.RowHeight - 0.5;
                    context.DrawLine(line, new Point(0, bottom), new Point(Bounds.Width, bottom));
                }
            }

            context.FillRectangle(header, new Rect(0, 0, Bounds.Width, HEADER_HEIGHT));
            double headerLeft = firstLeft - offsetX;
            for (int position = firstPosition; position < order.Count && headerLeft < Bounds.Width; ++position)
            {
                int column = order[position];
                double width = _grid._widths[column];
                DbcColumn definition = table.Definition.Columns[column];
                string arrow = column == _grid._sortColumn ? (_grid._sortDescending ? " v" : " ^") : string.Empty;
                DrawText(context, definition.Name + arrow, new Rect(headerLeft, 0, width, HEADER_HEIGHT),
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
            for (int position = 0; position < _grid._order.Count; ++position)
            {
                double width = _grid._widths[_grid._order[position]];
                if (left + width > offsetX)
                    return (position, left);
                left += width;
            }

            return (_grid._order.Count, left);
        }

        private void DrawText(DrawingContext context, string value, Rect cell, IBrush brush, Typeface typeface,
                              bool rightAlign)
        {
            if (value.Length == 0)
                return;
            FormattedText formatted = new FormattedText(value.Replace('\n', ' '), CultureInfo.InvariantCulture,
                                                        FlowDirection.LeftToRight, typeface, _grid.CellFontSize, brush)
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
                if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
                {
                    HeaderMenu(column).Open(this);
                    return;
                }

                // Clicking a header sorts by it: up, then down, then back to the table's order
                bool same = _grid._sortColumn == column;
                if (!same)
                    _grid.SortBy(column, descending: false);
                else if (!_grid._sortDescending)
                    _grid.SortBy(column, descending: true);
                else
                    _grid.SortBy(-1, descending: false);
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
                _grid._vertical.Value = Math.Clamp(_grid._vertical.Value - e.Delta.Y * _grid.RowHeight * WHEEL_ROWS, 0,
                                                   _grid._vertical.Maximum);
            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (_grid._table == null || _grid._order.Count == 0)
                return;
            int page = Math.Max(1, (int)((Bounds.Height - HEADER_HEIGHT) / _grid.RowHeight) - 1);
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
                    _grid.Select(_grid.SelectedIndex, _grid.Step(_grid.SelectedColumn, -1));
                    break;
                case Key.Right:
                case Key.Tab:
                    _grid.Select(_grid.SelectedIndex, _grid.Step(_grid.SelectedColumn, 1));
                    break;
                case Key.PageUp:
                    _grid.Select(_grid.SelectedIndex - page, _grid.SelectedColumn);
                    break;
                case Key.PageDown:
                    _grid.Select(_grid.SelectedIndex + page, _grid.SelectedColumn);
                    break;
                case Key.Home:
                    _grid.Select(control ? 0 : _grid.SelectedIndex, _grid._order[0]);
                    break;
                case Key.End:
                    _grid.Select(control ? _grid._rows.Count - 1 : _grid.SelectedIndex, _grid._order[^1]);
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

        private ContextMenu HeaderMenu(int column)
        {
            string name = _grid._table?.Definition.Columns[column].Name ?? string.Empty;
            ContextMenu menu = new ContextMenu();
            menu.Items.Add(MenuItem("Sort " + name + " up", () => _grid.SortBy(column, descending: false)));
            menu.Items.Add(MenuItem("Sort " + name + " down", () => _grid.SortBy(column, descending: true)));
            menu.Items.Add(MenuItem("Table order", () => _grid.SortBy(-1, descending: false)));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItem("Hide " + name, () => _grid.HideColumn(column)));
            menu.Items.Add(MenuItem("Hide empty columns", _grid.HideEmptyColumns));
            menu.Items.Add(MenuItem("Show all columns", _grid.ShowAllColumns));
            return menu;
        }

        private static MenuItem MenuItem(string header, Action action)
        {
            // Underscores are doubled so column names aren't read as access keys
            MenuItem item = new MenuItem { Header = header.Replace("_", "__") };
            item.Click += (_, _) => action();
            return item;
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
