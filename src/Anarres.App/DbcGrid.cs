using System.Globalization;
using Anarres.Core;
using Anarres.Core.Dbc;
using Anarres.Core.Settings;
using Anarres.Core.Spells;
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
        Children.Add(_surface.Hover);
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

    // The clean copy of the table, to mark rows added or changed since import; null marks only unsaved edits
    public DbcTable? Clean { get; set; }

    // How a row differs: unsaved edits first, then what the saved project has that the clean file doesn't
    internal RowMark Mark(DbcTable table, int row)
    {
        if (table.IsRowAdded(row))
            return RowMark.New;
        if (table.IsRowEdited(row))
            return RowMark.Edited;
        if (Clean == null || ReferenceEquals(Clean, table) || Clean.Definition.Columns.Count !=
            table.Definition.Columns.Count)
            return RowMark.None;
        int clean = Clean.FindRow(table.Key(row));
        if (clean < 0)
            return RowMark.SavedNew;
        for (int column = 0; column < table.Definition.Columns.Count; ++column)
        {
            if (!table.SameCell(row, column, Clean, clean))
                return RowMark.SavedEdited;
        }

        return RowMark.None;
    }

    internal bool IsCellMarked(DbcTable table, int row, int column, RowMark mark)
    {
        return mark switch
        {
            RowMark.Edited => table.IsCellEdited(row, column),
            RowMark.SavedEdited => Clean!.FindRow(table.Key(row)) is var clean and >= 0 &&
                                   !table.SameCell(row, column, Clean, clean),
            _ => false
        };
    }

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

    // The named list behind a cell (races, schools, attribute bits...), or null for a plain number
    public Func<DbcTable, int, int, EnumTable?>? Names { get; set; }

    // Names in brackets beside values; off by default, since hovering a cell lists them more readably
    public bool NamesInCells { get; set; }

    // How a cell is drawn: as the table writes it, or in hex for flag columns, then its names when asked for
    private string Display(DbcTable table, int row, int column)
    {
        DbcColumn definition = table.Definition.Columns[column];
        string text = FlagsInHex && DbcColumnTypes.IsInteger(definition.Type) && IsFlagColumn(definition.Name)
                          ? "0x" + table.GetRaw(row, column).ToString("X8", CultureInfo.InvariantCulture)
                          : table.GetText(row, column);
        if (!NamesInCells || !DbcColumnTypes.IsInteger(definition.Type) ||
            Names?.Invoke(table, column, row) is not { } names)
            return text;
        string named = names.Names(table.GetInt(row, column));
        return named.Length == 0 ? text : text + " (" + named + ")";
    }

    // A cell's value, one named bit or value per line, for its tooltip
    private string? CellTip(int row, int column)
    {
        if (_table != null && Money.IsColumn(_table.Definition.File, _table.Definition.Columns[column].Name))
        {
            uint copper = _table.GetRaw(row, column);
            return _table.Definition.Columns[column].Name + " = " + copper.ToString(CultureInfo.InvariantCulture) +
                   " copper\n\n" + Money.Text(copper);
        }

        if (_table == null || !DbcColumnTypes.IsInteger(_table.Definition.Columns[column].Type) ||
            Names?.Invoke(_table, column, row) is not { } names)
            return null;
        long value = names.Flags ? _table.GetRaw(row, column) : _table.GetInt(row, column);
        return _table.Definition.Columns[column].Name + " = " + value.ToString(CultureInfo.InvariantCulture) +
               "\n\n" + names.Lines(value);
    }

    private const double NAMED_WIDTH = 280;
    private const double MARK_STRIPE = 4;

    // Whether a column has names, judged by its first row (some lists depend on the row)
    private bool IsNamed(DbcTable table, int column)
    {
        return table.Rows.Count > 0 && Names?.Invoke(table, column, 0) != null;
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
            // Columns with names beside their values start wide enough to read a few of them
            _widths = table?.Definition.Columns.Select((column, index) =>
                          column.Type == DbcColumnType.String ? STRING_WIDTH
                          : NamesInCells && (Names?.Invoke(table, index, -1) != null || IsNamed(table, index))
                              ? NAMED_WIDTH
                          : NUMBER_WIDTH).ToArray()
                      ?? Array.Empty<double>();
            _order = Enumerable.Range(0, _widths.Length).ToList();
            _picked.Clear();
            _hidden.Clear();
            _surface.FitColumnsToContents();
            _sortColumn = -1;
            _vertical.Value = 0;
            _horizontal.Value = 0;
            SelectedColumn = 0;
        }

        _rows = Sorted(Unhidden(_baseRows));
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
        _rows = Sorted(Unhidden(_baseRows));
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

    // The other end of a Shift selection; equals SelectedIndex when one row is selected
    private int _anchor = -1;

    // Rows picked one by one with Ctrl+click, and rows hidden from the grid; held by their cells, as indexes move
    private readonly HashSet<uint[]> _picked = new HashSet<uint[]>();
    private readonly HashSet<uint[]> _hidden = new HashSet<uint[]>();

    // Rows of the table selected: the Shift range and any Ctrl+clicked rows, in the order shown
    public List<int> SelectedRows
    {
        get
        {
            if (SelectedIndex < 0 || _rows.Count == 0 || _table == null)
                return new List<int>();
            int from = Math.Clamp(Math.Min(_anchor < 0 ? SelectedIndex : _anchor, SelectedIndex), 0, _rows.Count - 1);
            int to = Math.Clamp(Math.Max(_anchor < 0 ? SelectedIndex : _anchor, SelectedIndex), 0, _rows.Count - 1);
            if (_picked.Count == 0)
                return _rows.GetRange(from, to - from + 1);
            DbcTable table = _table;
            return _rows.Where((row, index) => (index >= from && index <= to) || _picked.Contains(table.Rows[row]))
                        .ToList();
        }
    }

    public int HiddenRowCount => _hidden.Count;

    private List<int> Unhidden(List<int> rows)
    {
        if (_hidden.Count == 0 || _table == null)
            return rows;
        DbcTable table = _table;
        return rows.Where(row => row < table.Rows.Count && !_hidden.Contains(table.Rows[row])).ToList();
    }

    // Hides rows from the grid without changing the table; they come back with ShowHiddenRows
    public void HideRows(IEnumerable<int> rows)
    {
        if (_table == null)
            return;
        foreach (int row in rows)
            _hidden.Add(_table.Rows[row]);
        _picked.Clear();
        _anchor = -1;
        Show(_table, _baseRows);
    }

    public void ShowHiddenRows()
    {
        _hidden.Clear();
        if (_table != null)
            Show(_table, _baseRows);
    }

    // Selects these rows, such as the ones just pasted, and shows the last of them
    public void SelectRows(IReadOnlyCollection<int> rows)
    {
        if (_table == null || rows.Count == 0)
            return;
        DbcTable table = _table;
        _picked.Clear();
        foreach (int row in rows)
            _picked.Add(table.Rows[row]);
        int last = _rows.FindLastIndex(row => _picked.Contains(table.Rows[row]));
        if (last < 0)
            return;
        Select(last, SelectedColumn);
        foreach (int row in rows)
            _picked.Add(table.Rows[row]);
        _surface.InvalidateVisual();
    }

    // The row menu's commands that change the table; the editor carries them out
    public event Action? AddRowRequested;
    public event Action? CopyRowRequested;
    public event Action? DeleteRowsRequested;
    public event Action<string>? FilterRequested;

    // Ctrl+V with rows on the clipboard; the editor decides how they go in
    public event Action<string>? PasteRequested;

    private bool InRange(int index)
    {
        int anchor = _anchor < 0 ? SelectedIndex : _anchor;
        if (index >= Math.Min(anchor, SelectedIndex) && index <= Math.Max(anchor, SelectedIndex))
            return true;
        return _picked.Count > 0 && _table != null && index >= 0 && index < _rows.Count &&
               _picked.Contains(_table.Rows[_rows[index]]);
    }

    // extend is Shift: from the anchor to here; toggle is Ctrl: this row in or out, keeping the rest
    private void Select(int index, int column, bool extend = false, bool toggle = false)
    {
        if (_table == null || _rows.Count == 0)
            return;
        index = Math.Clamp(index, 0, _rows.Count - 1);
        if (toggle)
        {
            DbcTable table = _table;
            foreach (int row in SelectedRows)
                _picked.Add(table.Rows[row]);
            uint[] cells = table.Rows[_rows[index]];
            if (!_picked.Remove(cells))
                _picked.Add(cells);
            SelectedIndex = index;
            _anchor = index;
            // The pointed row stays the current one even when it was just taken out
            SelectedColumn = _order.Contains(column) ? column : Step(column, 0);
            _surface.InvalidateVisual();
            SelectionChanged?.Invoke();
            return;
        }

        if (!extend)
            _picked.Clear();
        SelectedIndex = index;
        if (!extend || _anchor < 0)
            _anchor = SelectedIndex;
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

    // One cell, or with several rows selected, those rows as tab-separated text under a line of column names
    private async void Copy()
    {
        if (_table == null || SelectedRow < 0 || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
            return;
        List<int> rows = SelectedRows;
        if (rows.Count <= 1)
        {
            await clipboard.SetTextAsync(_table.GetText(SelectedRow, SelectedColumn));
            return;
        }

        await clipboard.SetTextAsync(RowsText(_table, rows));
    }

    // Every column of the rows, so pasting brings back whole rows
    public static string RowsText(DbcTable table, IEnumerable<int> rows)
    {
        System.Text.StringBuilder text = new System.Text.StringBuilder();
        text.Append(string.Join('\t', table.Definition.Columns.Select(column => column.Name))).Append('\n');
        foreach (int row in rows)
        {
            IEnumerable<string> cells = Enumerable.Range(0, table.Definition.Columns.Count)
                                                  .Select(column => table.GetText(row, column).Replace('\t', ' ')
                                                                         .Replace("\n", "\\n"));
            text.Append(string.Join('\t', cells)).Append('\n');
        }

        return text.ToString();
    }

    private async Task CopyTextAsync(string text)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }

    private async Task PasteAsync()
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard ||
            await clipboard.GetTextAsync() is not { Length: > 0 } text)
            return;
        PasteRequested?.Invoke(text);
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
        private int _tipColumn = -1;
        private (int Row, int Column) _tipCell = (-1, -1);
        // Its own hover box: a tooltip reopened as the pointer moved between cells didn't always show
        private readonly Popup _hover = new Popup
        {
            Placement = PlacementMode.Pointer,
            IsLightDismissEnabled = false,
            IsHitTestVisible = false
        };
        private readonly TextBlock _hoverText = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 460 };
        private readonly Avalonia.Threading.DispatcherTimer _hoverDelay = new Avalonia.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(HOVER_DELAY_MS)
        };
        private const int HOVER_DELAY_MS = 350;

        public Surface(DbcGrid grid)
        {
            _grid = grid;
            Cursor = Cursor.Default;
            Border box = new Border
            {
                Child = _hoverText,
                Padding = new Thickness(10, 6),
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1)
            };
            Ui.Themed(box, Border.BackgroundProperty, ThemeColours.RAISED);
            Ui.Themed(box, Border.BorderBrushProperty, ThemeColours.BORDER);
            Ui.Themed(_hoverText, TextBlock.ForegroundProperty, ThemeColours.TEXT);
            _hover.Child = box;
            _hover.PlacementTarget = this;
            _hoverDelay.Tick += (_, _) =>
            {
                _hoverDelay.Stop();
                _hover.IsOpen = _hoverText.Text is { Length: > 0 };
            };
        }

        // Grid adds the hover box to the tree beside the surface, so it can open
        public Popup Hover => _hover;

        private void ShowHover(string? text)
        {
            _hover.IsOpen = false;
            _hoverDelay.Stop();
            _hoverText.Text = text;
            if (!string.IsNullOrEmpty(text))
                _hoverDelay.Start();
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
            RowLook look = new RowLook();
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
                    RowMark mark = _grid.Mark(table, row);
                    if (_grid.InRange(index))
                        context.FillRectangle(selectedRow, rowRect);
                    else if (mark != RowMark.None)
                        context.FillRectangle(look.Row(mark), rowRect);
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
                        else if (mark != RowMark.None && _grid.IsCellMarked(table, row, column, mark))
                            context.FillRectangle(look.Cell(mark), cell);
                        DrawText(context, _grid.Display(table, row, column), cell, text, _typeface,
                                 table.Definition.Columns[column].Type != DbcColumnType.String);
                        context.DrawLine(line, new Point(left + width - 0.5, top), new Point(left + width - 0.5,
                                                                                             top + _grid.RowHeight));
                        left += width;
                    }

                    if (mark != RowMark.None)
                        context.FillRectangle(look.Stripe(mark), new Rect(0, top, MARK_STRIPE, _grid.RowHeight));
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

        private const int MOST_ROWS_MEASURED = 20000;
        private const int ROWS_MEASURED_ON_OPEN = 200;
        private const int FEWEST_ROWS_MEASURED = 20;
        private const int CELLS_MEASURED_ON_OPEN = 12000;
        private const double MAX_OPENING_WIDTH = 360;
        private const double MAX_FITTED_WIDTH = 640;

        // Every column starts as wide as its name and its first rows need, up to a limit
        public void FitColumnsToContents()
        {
            DbcTable? table = _grid._table;
            if (table == null)
                return;
            // Wide tables such as Spell.dbc measure fewer rows, so they still open quickly
            int perColumn = Math.Clamp(CELLS_MEASURED_ON_OPEN / Math.Max(1, table.Definition.Columns.Count),
                                       FEWEST_ROWS_MEASURED, ROWS_MEASURED_ON_OPEN);
            int rows = Math.Min(_grid._baseRows.Count, perColumn);
            for (int column = 0; column < table.Definition.Columns.Count; ++column)
            {
                double widest = Measure(table.Definition.Columns[column].Name, _bold);
                for (int i = 0; i < rows; ++i)
                    widest = Math.Max(widest, Measure(_grid.Display(table, _grid._baseRows[i], column), _typeface));
                _grid._widths[column] = Math.Clamp(widest + CELL_PADDING * 2 + 2, MIN_COLUMN_WIDTH,
                                                   MAX_OPENING_WIDTH);
            }
        }

        private void FitColumn(int column)
        {
            DbcTable? table = _grid._table;
            if (table == null)
                return;
            double widest = Measure(table.Definition.Columns[column].Name, _bold);
            foreach (int row in _grid._rows.Take(MOST_ROWS_MEASURED))
                widest = Math.Max(widest, Measure(_grid.Display(table, row, column), _typeface));
            _grid._widths[column] = Math.Clamp(widest + CELL_PADDING * 2 + 2, MIN_COLUMN_WIDTH, MAX_FITTED_WIDTH);
            _grid.UpdateScrollBars();
            InvalidateVisual();
        }

        private double Measure(string text, Typeface typeface)
        {
            if (text.Length == 0)
                return 0;
            return new FormattedText(text.Replace('\n', ' '), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                     typeface, _grid.CellFontSize, Brushes.Black).WidthIncludingTrailingWhitespace;
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
            ShowHover(null);
            Point point = e.GetPosition(this);
            int resize = _grid.ResizeColumn(point);
            // Double-clicking a column's edge fits it to its widest value, as Excel does
            if (resize >= 0 && e.ClickCount == 2)
            {
                FitColumn(resize);
                return;
            }

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
            if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
            {
                // A right-click on a row outside the selection selects it first, as in a file list
                if (!_grid.InRange(index))
                    _grid.Select(index, column);
                RowMenu(_grid._rows[index], column).Open(this);
                return;
            }

            _grid.Select(index, column, (e.KeyModifiers & KeyModifiers.Shift) != 0,
                         (e.KeyModifiers & KeyModifiers.Control) != 0);
            if (e.ClickCount == 2 && (e.KeyModifiers & KeyModifiers.Control) == 0)
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
            (int index, int column) = _grid.HitTest(point);
            if (index >= 0 && index < _grid._rows.Count && column >= 0)
                ShowCellTip(_grid._rows[index], column);
            else
                ShowHeaderTip(index < 0 ? column : -1);
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            ShowHeaderTip(-1);
        }

        // Hovering a named cell lists its values, one per line
        private void ShowCellTip(int row, int column)
        {
            (int Row, int Column) cell = (row, column);
            if (cell == _tipCell)
                return;
            _tipCell = cell;
            _tipColumn = -1;
            ShowHover(_grid.CellTip(row, column));
        }

        // Hovering a header explains the column: what it's for, its type and its name in the file
        private void ShowHeaderTip(int column)
        {
            if (column == _tipColumn)
                return;
            _tipColumn = column;
            _tipCell = (-1, -1);
            if (column < 0 || _grid._table == null)
            {
                ShowHover(null);
                return;
            }

            DbcDefinition definition = _grid._table.Definition;
            DbcColumn info = definition.Columns[column];
            string tip = info.Name + " (" + info.Type + ")";
            string? note = ColumnDocs.Describe(definition.File, info.Name);
            ShowHover(note == null ? tip : tip + "\n\n" + note);
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
            bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
            switch (e.Key)
            {
                case Key.Up:
                    _grid.Select(_grid.SelectedIndex - 1, _grid.SelectedColumn, shift);
                    break;
                case Key.Down:
                    _grid.Select(_grid.SelectedIndex + 1, _grid.SelectedColumn, shift);
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
                case Key.V when control:
                    _ = _grid.PasteAsync();
                    break;
                case Key.A when control:
                {
                    _grid._picked.Clear();
                    _grid._anchor = 0;
                    _grid.Select(_grid._rows.Count - 1, _grid.SelectedColumn, extend: true);
                    break;
                }
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

        // Right-click on a row: copy and paste, add, copy and delete rows, hide them, and filter by the cell
        private ContextMenu RowMenu(int row, int column)
        {
            DbcTable table = _grid._table!;
            List<int> selected = _grid.SelectedRows;
            string rows = selected.Count == 1 ? "row" : selected.Count + " rows";
            string name = table.Definition.Columns[column].Name;
            string value = table.Definition.Columns[column].Type == DbcColumnType.String
                               ? table.GetString(row, column)
                               : _grid.Display(table, row, column);
            ContextMenu menu = new ContextMenu();
            menu.Items.Add(MenuItem("Copy cell (" + Shorten(value) + ")", () => _ = _grid.CopyTextAsync(value)));
            menu.Items.Add(MenuItem("Copy " + rows + "\tCtrl+C", () => _ = _grid.CopyTextAsync(RowsText(table,
                                                                                                    selected))));
            menu.Items.Add(MenuItem("Copy every shown row (" + _grid._rows.Count.ToString("N0",
                                        CultureInfo.InvariantCulture) + ")",
                                    () => _ = _grid.CopyTextAsync(RowsText(table, _grid._rows))));
            menu.Items.Add(MenuItem("Paste rows at the bottom\tCtrl+V", () => _ = _grid.PasteAsync()));
            menu.Items.Add(MenuItem("Select all\tCtrl+A", () =>
            {
                _grid._picked.Clear();
                _grid._anchor = 0;
                _grid.Select(_grid._rows.Count - 1, _grid.SelectedColumn, extend: true);
            }));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItem("Add a row", () => _grid.AddRowRequested?.Invoke()));
            menu.Items.Add(MenuItem("Copy this row to a new one", () => _grid.CopyRowRequested?.Invoke()));
            menu.Items.Add(MenuItem("Delete " + rows, () => _grid.DeleteRowsRequested?.Invoke()));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItem("Hide " + rows, () => _grid.HideRows(selected)));
            if (_grid.HiddenRowCount > 0)
                menu.Items.Add(MenuItem("Show the " + _grid.HiddenRowCount + " hidden rows", _grid.ShowHiddenRows));
            if (table.Definition.Columns[column].Type != DbcColumnType.String ||
                !value.Contains(' ', StringComparison.Ordinal))
            {
                string raw = table.Definition.Columns[column].Type == DbcColumnType.String
                                 ? value
                                 : table.GetText(row, column);
                menu.Items.Add(MenuItem("Show only rows where " + name + " = " + Shorten(raw),
                                        () => _grid.FilterRequested?.Invoke(name + "=" + raw)));
            }

            menu.Items.Add(MenuItem("Hide " + name, () => _grid.HideColumn(column)));
            return menu;
        }

        private static string Shorten(string text)
        {
            return text.Length <= SHORT_TEXT ? text : text[..SHORT_TEXT] + "...";
        }

        private const int SHORT_TEXT = 30;

        private static MenuItem MenuItem(string header, Action action)
        {
            // Underscores are doubled so names aren't access keys; "Copy\tCtrl+C" shows the shortcut on the right
            string[] parts = header.Split('\t');
            MenuItem item = new MenuItem { Header = parts[0].Replace("_", "__") };
            if (parts.Length > 1)
                item.InputGesture = KeyGesture.Parse(parts[1]);
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

// What a row is: added or changed and not saved yet, or added or changed since import and saved
internal enum RowMark
{
    None,
    New,
    Edited,
    SavedNew,
    SavedEdited
}

// The theme's colours for each kind of row: a light wash for the row, more for its changed cells, a solid stripe
internal sealed class RowLook
{
    private const byte ROW_ALPHA = 0x26;
    private const byte CELL_ALPHA = 0x55;
    private readonly Dictionary<(RowMark, byte), IBrush> _brushes = new Dictionary<(RowMark, byte), IBrush>();

    public static string ColourKey(RowMark mark)
    {
        return mark switch
        {
            RowMark.New => ThemeColours.ATTENTION,
            RowMark.Edited => ThemeColours.CHANGED,
            RowMark.SavedNew => ThemeColours.ADDED,
            _ => ThemeColours.EDITED
        };
    }

    public IBrush Row(RowMark mark) => Brush(mark, ROW_ALPHA);

    public IBrush Cell(RowMark mark) => Brush(mark, CELL_ALPHA);

    public IBrush Stripe(RowMark mark) => Brush(mark, byte.MaxValue);

    private IBrush Brush(RowMark mark, byte alpha)
    {
        if (_brushes.TryGetValue((mark, alpha), out IBrush? brush))
            return brush;
        Color colour = ThemeBrushes.ColourOf(ColourKey(mark));
        brush = new SolidColorBrush(Color.FromArgb(alpha, colour.R, colour.G, colour.B));
        _brushes[(mark, alpha)] = brush;
        return brush;
    }
}
