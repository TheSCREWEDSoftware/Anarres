using System.Globalization;
using Anarres.Core;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Anarres.App;

// Money as the stored copper value and as gold, silver and copper; editing either side updates the other
public sealed class MoneyBox
{
    private const double TOTAL_WIDTH = 130;
    private const double PART_WIDTH = 70;

    private readonly TextBox _total = new TextBox { Width = TOTAL_WIDTH, Text = "0", Watermark = "Raw value" };
    private readonly TextBox _gold = Part("Gold");
    private readonly TextBox _silver = Part("Silver");
    private readonly TextBox _copper = Part("Copper");
    private bool _setting;

    public MoneyBox()
    {
        ToolTip.SetTip(_total, "The whole amount in copper, as the database stores it");
        _total.TextChanged += (_, _) =>
        {
            if (!_setting)
                Show(Read(_total), fromTotal: true);
        };
        foreach (TextBox box in new[] { _gold, _silver, _copper })
        {
            box.TextChanged += (_, _) =>
            {
                if (!_setting)
                    Show(Money.ToCopper(Read(_gold), Read(_silver), Read(_copper)), fromTotal: false);
            };
        }

        View = Ui.Horizontal(Labelled("Raw value", _total), Labelled("Gold", _gold), Labelled("Silver", _silver),
                             Labelled("Copper", _copper));
    }

    public StackPanel View { get; }

    public uint Copper { get; private set; }

    public event Action? Changed;

    public void Set(uint copper)
    {
        Show(copper, fromTotal: false);
        _setting = true;
        _total.Text = copper.ToString(CultureInfo.InvariantCulture);
        _setting = false;
    }

    // Writes the value into the side the user isn't typing in
    private void Show(uint copper, bool fromTotal)
    {
        Copper = copper;
        _setting = true;
        if (fromTotal)
        {
            (uint gold, uint silver, uint rest) = Money.Split(copper);
            _gold.Text = gold.ToString(CultureInfo.InvariantCulture);
            _silver.Text = silver.ToString(CultureInfo.InvariantCulture);
            _copper.Text = rest.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            _total.Text = copper.ToString(CultureInfo.InvariantCulture);
        }

        _setting = false;
        Changed?.Invoke();
    }

    private static uint Read(TextBox box)
    {
        return uint.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint value) ? value : 0;
    }

    private static TextBox Part(string name)
    {
        return new TextBox { Width = PART_WIDTH, Text = "0", Watermark = name };
    }

    // A box with its name above it
    private static StackPanel Labelled(string name, TextBox box)
    {
        return new StackPanel
        {
            Spacing = 2,
            Children = { new TextBlock { Text = name, Classes = { "hint" } }, box }
        };
    }
}
