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
    private readonly ProgressBar _progress = new ProgressBar { Minimum = 0, Maximum = 1, Height = 6 };
    private readonly TextBlock _status = new TextBlock { Classes = { "hint" } };
    private List<CheckBox> _boxes = new List<CheckBox>();

    public ExportWindow(Session session)
    {
        _session = session;
        Title = "Export and deploy";
        Width = WINDOW_WIDTH;
        Height = WINDOW_HEIGHT;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        string serverFolder = session.Settings.ServerDbcFolder;
        string dataFolder = session.Settings.ClientDataFolder;
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
        top.Children.Add(Ui.Horizontal(all, none));

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
    }

    private async Task ExportAsync()
    {
        List<string> files = _boxes.Where(box => box.IsChecked == true)
                                   .Select(box => box.Tag as string ?? string.Empty).ToList();
        if (files.Count == 0)
        {
            _status.Text = "Tick the tables to export first.";
            return;
        }

        _session.Settings.ExportTables = files;
        IDbcStore store = await _session.StoreAsync();
        Progress<(string File, double Done)> progress = new Progress<(string File, double Done)>(update =>
        {
            _progress.Value = update.Done;
            if (update.File.Length > 0)
                _status.Text = "Writing " + update.File + "...";
        });
        bool server = _server.IsChecked == true;
        bool patch = _patch.IsChecked == true;
        try
        {
            ExportResult result = await Task.Run(() => Deployment.ExportAsync(store, _session.Settings, files,
                                                                               _session.Definitions, server, patch,
                                                                               progress));
            List<string> done = new List<string> { "Wrote " + result.DbcFiles.Count + " .dbc files." };
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
