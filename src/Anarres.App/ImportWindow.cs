using Anarres.Core;
using Anarres.Core.Dbc;
using Anarres.Core.Export;
using Anarres.Core.Mpq;
using Anarres.Core.Settings;
using Anarres.Core.Spells;
using Anarres.Core.Storage;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Anarres.App;

// Brings .dbc files into the project: from a folder of clean files, or straight out of the client's MPQs
public sealed class ImportWindow : Window
{
    private const double WINDOW_WIDTH = 760;
    private const double WINDOW_HEIGHT = 680;

    // Spell.dbc and the tables the spell editor shows names and values from
    private static readonly string[] SpellSet =
    {
        "Spell.dbc", "SpellCastTimes.dbc", "SpellDuration.dbc", "SpellRange.dbc", "SpellRadius.dbc", "SpellIcon.dbc",
        "SpellCategory.dbc", "SpellVisual.dbc", "SpellRuneCost.dbc", "SpellDifficulty.dbc",
        "SpellDescriptionVariables.dbc"
    };

    private readonly Session _session;
    private readonly RadioButton _fromFolder = new RadioButton
    {
        Content = "A folder of .dbc files",
        GroupName = "from"
    };
    private readonly RadioButton _fromClient = new RadioButton { Content = "The client's MPQs (WoW folder)",
                                                                 GroupName = "from" };
    private readonly TextBox _folder = new TextBox();
    private readonly TextBox _client = new TextBox();
    private readonly LocalePicker _locale;
    private readonly TextBox _filter = new TextBox { Watermark = "Filter", Width = 220 };
    private readonly StackPanel _files = new StackPanel { Spacing = 0 };
    private readonly ProgressBar _progress = new ProgressBar { Minimum = 0, Maximum = 1, Height = 6 };
    private readonly TextBlock _status = new TextBlock { Classes = { "hint" } };
    private readonly Button _import;
    private List<CheckBox> _boxes = new List<CheckBox>();
    private readonly CheckBox _skipImported = new CheckBox
    {
        Content = "Skip tables already in the project (to carry on after an import that stopped)",
        IsChecked = true
    };
    private readonly TextBlock _language = new TextBlock { Classes = { "hint" } };
    private HashSet<string> _inProject = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public ImportWindow(Session session)
    {
        _session = session;
        Title = "Import";
        Width = WINDOW_WIDTH;
        Height = WINDOW_HEIGHT;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _folder.Text = session.Settings.SourceDbcFolder;
        _client.Text = session.Settings.ClientDataFolder;
        _locale = new LocalePicker(session.Settings.Locale);
        _locale.Detect(session.Settings.ClientDataFolder);
        _client.TextChanged += (_, _) => _locale.Detect(_client.Text ?? string.Empty);
        _fromFolder.IsChecked = true;

        Button list = Ui.AsyncButton("List files", ListAsync);
        Button spellSet = Ui.Button("Spell editor set", () => Tick(name => SpellSet.Contains(name,
                                                                         StringComparer.OrdinalIgnoreCase)));
        Button all = Ui.Button("All", () => Tick(_ => true));
        Button none = Ui.Button("None", () => Tick(_ => false));
        ToolTip.SetTip(spellSet, "Spell.dbc and the tables its fields refer to: cast times, durations, ranges...");
        _filter.TextChanged += (_, _) =>
        {
            string text = _filter.Text ?? string.Empty;
            foreach (CheckBox box in _boxes)
                box.IsVisible = (box.Tag as string ?? string.Empty).Contains(text, StringComparison.OrdinalIgnoreCase);
        };

        _import = Ui.AsyncButton("Import", ImportAsync, accent: true);
        Button close = new Button { Content = "Close", IsCancel = true };
        close.Click += (_, _) => Close();

        StackPanel top = new StackPanel { Margin = new Thickness(20, 16, 20, 0), Spacing = 4 };
        top.Children.Add(_fromFolder);
        top.Children.Add(Ui.Row("Folder", Ui.FolderPicker(this, _folder, "Folder of .dbc files")));
        top.Children.Add(Ui.Hint("A folder of .dbc files, or the worldserver's Data folder (its dbc folder is used)."));
        top.Children.Add(_fromClient);
        top.Children.Add(Ui.Row("WoW client folder", Ui.FolderPicker(this, _client, "WoW client folder")));
        top.Children.Add(Ui.Row("Locale", _locale));
        top.Children.Add(_language);
        top.Children.Add(_skipImported);
        top.Children.Add(Ui.Hint("Each table is saved as soon as it's imported, replacing the project's copy. Files " +
                                 "without a known layout get one guessed from their data; rename its columns later " +
                                 "in the DBC editor."));
        top.Children.Add(Ui.Horizontal(list, spellSet, all, none, _filter));

        StackPanel bottom = new StackPanel { Margin = new Thickness(20, 8, 20, 16), Spacing = 8 };
        bottom.Children.Add(_progress);
        bottom.Children.Add(_status);
        StackPanel buttons = Ui.Horizontal(_import, close);
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        bottom.Children.Add(buttons);

        DockPanel root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(bottom);
        root.Children.Add(new Border
        {
            Classes = { "group" },
            Margin = new Thickness(20, 8, 20, 0),
            Child = new ScrollViewer { Content = _files }
        });
        Content = root;
        Opened += async (_, _) => await ListAsync();
    }

    // True once anything was imported, so the window behind reloads
    public bool Imported { get; private set; }

    private void Tick(Func<string, bool> choose)
    {
        foreach (CheckBox box in _boxes.Where(box => box.IsVisible))
            box.IsChecked = choose(box.Tag as string ?? string.Empty);
    }

    private async Task ListAsync()
    {
        List<string> names = new List<string>();
        try
        {
            if (_fromFolder.IsChecked == true)
            {
                string folder = ProjectSettings.ResolveServerDbc((_folder.Text ?? string.Empty).Trim());
                if (Directory.Exists(folder))
                    names = Directory.EnumerateFiles(folder, "*.dbc", Files.AnyCase).Select(Path.GetFileName)
                                     .OfType<string>().Order(StringComparer.OrdinalIgnoreCase).ToList();
            }
            else
            {
                string data = ProjectSettings.ResolveClientData(_client.Text ?? string.Empty);
                string locale = _locale.Locale;
                if (Directory.Exists(data))
                    names = await Task.Run(() =>
                    {
                        using ClientArchives archives = ClientArchives.Open(data, locale);
                        return archives.ListDbcFiles().Select(Path.GetFileName).OfType<string>().ToList();
                    });
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or
                                              UnauthorizedAccessException)
        {
            _status.Text = "The files couldn't be listed: " + exception.Message;
        }

        // In AzerothCore's tables, only files AzerothCore has a table for can go in
        bool acTables = _session.Settings.Store == Anarres.Core.Settings.StoreKind.MySql &&
                        _session.Settings.MySqlNaming == TableNaming.AzerothCore;
        // Underscores are doubled so Achievement_Category isn't read as an access key
        _boxes = names.Select(name => new CheckBox
        {
            Content = name.Replace("_", "__"),
            Tag = name,
            IsChecked = SpellSet.Contains(name, StringComparer.OrdinalIgnoreCase),
            IsEnabled = !acTables || _session.Definitions.Find(name) is { Table.Length: > 0 }
        }).ToList();
        _files.Children.Clear();
        foreach (CheckBox box in _boxes)
            _files.Children.Add(box);
        if (_status.Text is null or "" || names.Count > 0)
            _status.Text = names.Count == 0 ? "No .dbc files found there." : names.Count + " files found.";
        await MarkImportedAsync();
        if (names.Count > 0)
            await DetectLanguageAsync();
    }

    // Files already in the project are marked, so a stopped import can carry on with the rest
    private async Task MarkImportedAsync()
    {
        try
        {
            IDbcStore store = await _session.StoreAsync(createDatabase: true);
            _inProject = new HashSet<string>((await store.ListTablesAsync()).Select(table => table.File),
                                             StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or
                                              System.Data.Common.DbException)
        {
            _inProject.Clear();
        }

        foreach (CheckBox box in _boxes)
        {
            string name = box.Tag as string ?? string.Empty;
            if (_inProject.Contains(name))
                box.Content = name.Replace("_", "__") + "   (in the project)";
        }
    }

    // Which of the 16 text columns is filled tells the files' language
    private async Task DetectLanguageAsync()
    {
        _language.Text = "Finding the files' language...";
        bool fromFolder = _fromFolder.IsChecked == true;
        string folder = (_folder.Text ?? string.Empty).Trim();
        string data = ProjectSettings.ResolveClientData(_client.Text ?? string.Empty);
        string locale = _locale.Locale;
        string patch = _session.Settings.PatchPath();
        DbcDefinitions definitions = _session.Definitions;
        DetectedLocale? found = null;
        try
        {
            found = await Task.Run(() => fromFolder ? LocaleDetector.DetectFolder(folder, definitions)
                                                    : LocaleDetector.DetectClient(data, locale, patch, definitions));
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or
                                              UnauthorizedAccessException)
        {
            _language.Text = string.Empty;
            return;
        }

        _language.Text = found == null
                             ? "The files' language couldn't be told (no ChrRaces, ChrClasses or Spell .dbc)."
                             : "Text in these files is " + found.Locale + " (" + found.Rows + " of " + found.Total +
                               " rows of " + found.File + " have it).";
    }

    private async Task ImportAsync()
    {
        List<string> chosen = _boxes.Where(box => box.IsChecked == true && box.IsEnabled)
                                    .Select(box => box.Tag as string ?? string.Empty).ToList();
        int already = 0;
        if (_skipImported.IsChecked == true)
        {
            already = chosen.Count(name => _inProject.Contains(name));
            chosen = chosen.Where(name => !_inProject.Contains(name)).ToList();
        }

        if (chosen.Count == 0)
        {
            _status.Text = already > 0 ? "Every ticked table is already in the project; untick \"Skip tables " +
                                         "already in the project\" to import them again."
                                       : "Tick the files to import first.";
            return;
        }

        // AzerothCore's tables override the client's files row by row, so a whole file there overrides every row
        if (_session.Settings.Store == Anarres.Core.Settings.StoreKind.MySql &&
            _session.Settings.MySqlNaming == TableNaming.AzerothCore &&
            !await Dialogs.ConfirmAsync(this, "Import into AzerothCore's tables",
                                        "This replaces the rows of " + string.Join(", ", chosen.Take(5)) +
                                        (chosen.Count > 5 ? " and more" : string.Empty) + " in " +
                                        _session.Settings.MySqlDatabase + " with every row of the files. The " +
                                        "worldserver then loads all of them over its .dbc files, including " +
                                        "AzerothCore's own fixes in those tables. Usually only changed rows " +
                                        "belong there; use DBC names for a full working copy.", "Import anyway"))
            return;

        IDbcStore store = await _session.StoreAsync(createDatabase: true);
        Progress<(string File, double Done)> progress = Ui.Steps("Importing", chosen.Count, _status, _progress);

        try
        {
            List<ImportResult> results;
            if (_fromFolder.IsChecked == true)
            {
                string folder = ProjectSettings.ResolveServerDbc((_folder.Text ?? string.Empty).Trim());
                _session.Settings.SourceDbcFolder = folder;
                results = await Task.Run(() => Deployment.ImportFolderAsync(store, folder, _session.Definitions,
                                                                             chosen, progress));
            }
            else
            {
                string data = ProjectSettings.ResolveClientData(_client.Text ?? string.Empty);
                string locale = _locale.Locale;
                _session.Settings.ClientDataFolder = (_client.Text ?? string.Empty).Trim();
                _session.Settings.Locale = locale;
                results = await Task.Run(() => Deployment.ImportClientAsync(store, data, locale, _session.Definitions,
                                                                             chosen, progress));
            }

            Imported = true;
            List<string> unreadable = results.Where(result => result.Problem != null)
                                             .Select(result => result.File).ToList();
            results = results.Where(result => !result.Skipped).ToList();
            int guessed = results.Count(result => result.Inferred);
            _progress.Value = 1;
            _status.Text = "Imported " + results.Count + (results.Count == 1 ? " table" : " tables") + " (" +
                           results.Sum(result => result.Rows).ToString("N0") + " rows)" +
                           (guessed > 0 ? "; " + guessed + " with a guessed layout" : string.Empty) +
                           (unreadable.Count > 0
                                ? ". Skipped " + string.Join(", ", unreadable) + ": empty or unreadable, as a few " +
                                  "of the client's own files are."
                                : ".") +
                           (already > 0 ? " " + already + " already in the project were left as they were." : "");
            await MarkImportedAsync();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or
                                              InvalidOperationException or NotSupportedException or
                                              System.Data.Common.DbException)
        {
            Imported = true;
            _status.Text = "The import stopped: " + exception.Message + " The tables before it are saved; Import " +
                           "again carries on with the rest.";
            await MarkImportedAsync();
        }
    }
}
