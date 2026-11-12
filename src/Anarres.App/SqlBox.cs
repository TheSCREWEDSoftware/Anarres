using Anarres.Core.Export;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Anarres.App;

// Read-only SQL in a code font, coloured like an editor: keywords, `names`, 'text', numbers and comments
public sealed class SqlBox
{
    public const string CODE_FONT = "Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, monospace";

    private readonly SelectableTextBlock _text = new SelectableTextBlock
    {
        FontFamily = new FontFamily(CODE_FONT),
        TextWrapping = TextWrapping.Wrap
    };
    private string _sql = string.Empty;

    public SqlBox(double minHeight = 90)
    {
        View = new Border
        {
            Classes = { "group" },
            MinHeight = minHeight,
            Padding = new Thickness(10, 8),
            Background = ThemeBrushes.Get(Anarres.Core.Settings.ThemeColours.RAISED),
            Child = _text
        };
    }

    public Border View { get; }

    public string Sql
    {
        get => _sql;
        set
        {
            _sql = value;
            _text.Inlines = new InlineCollection();
            foreach (SqlToken token in SqlTokens.Split(value))
                _text.Inlines.Add(new Run(token.Text) { Foreground = Brush(token.Kind) });
        }
    }

    private static IBrush Brush(SqlTokenKind kind)
    {
        return kind switch
        {
            SqlTokenKind.Keyword => ThemeBrushes.Accent,
            SqlTokenKind.Identifier => ThemeBrushes.Get(Anarres.Core.Settings.ThemeColours.ATTENTION),
            SqlTokenKind.Text => ThemeBrushes.Added,
            SqlTokenKind.Number => ThemeBrushes.Warning,
            SqlTokenKind.Comment => ThemeBrushes.MutedText,
            _ => ThemeBrushes.Text
        };
    }
}
