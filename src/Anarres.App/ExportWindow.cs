using Anarres.Core.Export;
using Anarres.Core.Mpq;
using Anarres.Core.Storage;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Anarres.App;

// Writes tables out of the project as .dbc files, copies them to the server and packs the client patch MPQ
public sealed class ExportWindow : Window
{
    private const double WINDOW_WIDTH = 720;
    private const double WINDOW_HEIGHT = 640;

    private readonly Session _session;
    private readonly StackPanel _tables = new StackPanel();
    private readonly CheckBox _server = new CheckBox();
    private readonly CheckBox _patch = new CheckBox();
    private readonly CheckBox _editedOnly = new CheckBox
    {
        Content = "Only tables edited since they were imported (the client patch then holds just those)"
    };
    private readonly ProgressBar _progress = new ProgressBar { Minimum = 0, Maximum = 1, Height = 6 };
    private readonly TextBlock _status = new TextBlock { Classes = { "hint" } };
    private List<CheckBox> _boxes = new List<CheckBox>();
    private HashSet<string> _edited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public ExportWindow(Session session)
    {
        _session = session;
        Title = "Export and deploy";
        Width = WINDOW_WIDTH;
        Height = WINDOW_HEIGHT;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        string serverFolder = session.Settings.ServerDbc();
        string dataFolder = session.Settings.ClientData();
        _server.Content = serverFolder.Length > 0 ? "Copy to the server: " + serverFolder
                                                  : "Copy to the server (set its dbc folder in Project settings)";
        _server.IsEnabled = serverFolder.Length > 0;
        _server.IsChecked = serverFolder.Length > 0;
        _patch.Content = dataFolder.Length > 0 ? "Build the client patch: " + session.Settings.PatchPath()
                                               : "Build the client patch (set the WoW Data folder in Project settings)";
        _patch.IsEnabled = dataFolder.Length > 0;
        _patch.IsChecked = dataFolder.Length > 0;

        Button all = Ui.Button("All", () => _boxes.ForEach(box => box.IsChecked = true));
        Button none = Ui.Button("None", () => _boxes.ForEach(box => box.IsChecked = false));
        Button check = Ui.AsyncButton("Check against clean files", CheckAsync);
        ToolTip.SetTip(check, "Compare every table with its clean file; tables that match, such as ones whose " +
                              "edits were undone, no longer count as edited");
        _editedOnly.IsChecked = session.Settings.ExportEditedOnly;
        _editedOnly.IsCheckedChanged += (_, _) => ShowEdited();
        Button export = Ui.AsyncButton("Export", ExportAsync, accent: true);
        Button open = Ui.Button("Open export folder", () => SystemShell.Open(Path.Combine(
            session.Settings.ExportFolder, ClientArchives.DBC_FOLDER)));
        Button close = new Button { Content = "Close", IsCancel = true };
        close.Click += (_, _) => Close();

        StackPanel top = new StackPanel { Margin = new Thickness(20, 16, 20, 0), Spacing = 6 };
        top.Children.Add(Ui.Hint("The ticked tables are written to " +
                                 Path.Combine(session.Settings.ExportFolder, ClientArchives.DBC_FOLDER) +
                                 ". The server needs them in its dbc folder, and the client in a patch MPQ under " +
                                 "DBFilesClient. Restart the worldserver and the client to see the changes."));
        if (session.Settings.Store == Anarres.Core.Settings.StoreKind.MySql &&
            session.Settings.MySqlNaming == TableNaming.AzerothCore)
            top.Children.Add(Ui.Hint("AzerothCore's tables hold only the rows that differ, so each table is laid " +
                                     "over its clean file from the Clean DBC folder or the client's MPQs."));
        top.Children.Add(_server);
        top.Children.Add(_patch);
        top.Children.Add(_editedOnly);
        top.Children.Add(Ui.Horizontal(all, none, check));

        StackPanel bottom = new StackPanel { Margin = new Thickness(20, 8, 20, 16), Spacing = 8 };
        bottom.Children.Add(_progress);
        bottom.Children.Add(_status);
        StackPanel buttons = Ui.Horizontal(open, export, close);
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
            Child = new ScrollViewer { Content = _tables }
        });
        Content = root;
        Opened += async (_, _) => await ListAsync();
    }

    private async Task ListAsync()
    {
        IDbcStore store = await _session.StoreAsync();
        List<StoredTable> tables = await store.ListTablesAsync();
        HashSet<string> previous = new HashSet<string>(_session.Settings.ExportTables,
                                                       StringComparer.OrdinalIgnoreCase);
        _boxes = tables.Select(table => new CheckBox
        {
            Content = table.File.Replace("_", "__"),
            Tag = table.File,
            IsChecked = previous.Contains(table.File)
        }).ToList();
        _tables.Children.Clear();
        foreach (CheckBox box in _boxes)
            _tables.Children.Add(box);
        _status.Text = tables.Count == 0 ? "The project has no tables yet." : tables.Count + " tables in the project.";
        if (tables.Count > 0)
            await FindEditedAsync(check: false);
    }

    // Marks the edited tables; older projects have their tables compared with the clean files once
    private async Task FindEditedAsync(bool check)
    {
        IDbcStore store = await _session.StoreAsync();
        Progress<(string File, double Done)> progress = Progress("Comparing", _boxes.Count);
        try
        {
            List<string> unsaved = _session.ModifiedTables.Select(table => table.Definition.File).ToList();
            List<string> edited = check
                                      ? await Task.Run(() => Deployment.CheckEditedAsync(
                                                           store, _session.Settings,
                                                           _boxes.Select(box => box.Tag as string ?? string.Empty)
                                                                 .ToList(), _session.Definitions, progress))
                                      : await Task.Run(() => Deployment.EditedFilesAsync(
                                                           store, _session.Settings, _session.Definitions, unsaved,
                                                           progress));
            _edited = new HashSet<string>(edited.Concat(unsaved), StringComparer.OrdinalIgnoreCase);
            _status.Text = _edited.Count + (_edited.Count == 1 ? " table" : " tables") + " edited since import.";
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or
                                              InvalidOperationException or System.Data.Common.DbException)
        {
            _status.Text = "Couldn't tell which tables were edited: " + exception.Message;
        }

        _progress.Value = 0;
        ShowEdited();
    }

    private Task CheckAsync()
    {
        return FindEditedAsync(check: true);
    }

    private void ShowEdited()
    {
        bool editedOnly = _editedOnly.IsChecked == true;
        foreach (CheckBox box in _boxes)
        {
            string file = box.Tag as string ?? string.Empty;
            bool edited = _edited.Contains(file);
            box.Content = file.Replace("_", "__") + (edited ? "   (edited)" : string.Empty);
            box.IsEnabled = !editedOnly;
            if (editedOnly)
                box.IsChecked = edited;
        }
    }

    private Progress<(string File, double Done)> Progress(string verb, int total)
    {
        return Ui.Steps(verb, total, _status, _progress);
    }

    private async Task ExportAsync()
    {
        List<string> files = _boxes.Where(box => box.IsChecked == true)
                                   .Select(box => box.Tag as string ?? string.Empty).ToList();
        if (files.Count == 0)
        {
            _status.Text = _editedOnly.IsChecked == true ? "No table has been edited since it was imported."
                                                         : "Tick the tables to export first.";
            return;
        }

        _session.Settings.ExportEditedOnly = _editedOnly.IsChecked == true;
        if (!_session.Settings.ExportEditedOnly)
            _session.Settings.ExportTables = files;
        IDbcStore store = await _session.StoreAsync();
        Progress<(string File, double Done)> progress = Progress("Writing", files.Count);
        bool server = _server.IsChecked == true;
        bool patch = _patch.IsChecked == true;
        try
        {
            ExportResult result = await Task.Run(() => Deployment.ExportAsync(store, _session.Settings, files,
                                                                               _session.Definitions, server, patch,
                                                                               progress));
            List<string> done = new List<string> { "Wrote " + result.DbcFiles.Count + " .dbc files." };
            if (result.BackedUp > 0)
                done.Add("Kept the server's own copy of " + result.BackedUp + " in " + Deployment.BACKUP_FOLDER + ".");
            if (result.ServerFolder != null)
                done.Add("Copied them to the server; restart the worldserver.");
            if (result.PatchPath != null)
                done.Add("Built " + Path.GetFileName(result.PatchPath) + "; restart the client.");
            _status.Text = string.Join(" ", done);
            _progress.Value = 1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                              InvalidOperationException or System.Data.Common.DbException)
        {
            _status.Text = "The export stopped: " + exception.Message;
        }
    }
}
