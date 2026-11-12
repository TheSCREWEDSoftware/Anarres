using Anarres.Core.Models;
using Anarres.Core.Mpq;
using Anarres.Core.Settings;
using Anarres.Core.Storage;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Anarres.App;

// The 3D tab: the spell's missile and kit effects, and what it summons, turns into or makes
public sealed class SpellModelsView : IField
{
    private readonly RecordBinding _binding;
    private readonly Session _session;
    private readonly ModelView _view;
    private readonly TextBlock _notes = new TextBlock { Classes = { "hint" }, TextWrapping = TextWrapping.Wrap };
    private IconLibrary? _client;
    private bool _active;
    private bool _queued;
    private int _version;

    public SpellModelsView(RecordBinding binding, Session session)
    {
        _binding = binding;
        _session = session;
        _view = new ModelView(ReadClientFile, 440);
        binding.Add(this);
        View = Ui.Vertical(8,
            Ui.Hint("Models from your client's own files (patches and custom models included): the spell's " +
                    "missile and the effects of its visual kits, then what it summons, turns into or makes. " +
                    "Creatures, objects and items need the world database."),
            _view.View,
            _notes);
    }

    public Control View { get; }

    // Only the shown tab loads models, so moving through spells stays quick
    public bool Active
    {
        get => _active;
        set
        {
            bool shown = value && !_active;
            _active = value;
            if (shown)
                Refresh();
        }
    }

    // Many refreshes come at once (every field of a spell); they're gathered into one load after them
    public void Refresh()
    {
        if (!_active || _queued)
            return;
        _queued = true;
        Dispatcher.UIThread.Post(async () =>
        {
            _queued = false;
            try
            {
                await LoadAsync();
            }
            catch (Exception exception)
            {
                _notes.Text = "The models couldn't be listed: " + exception.Message;
                CrashLog.Write(exception, "3D tab");
            }
        }, DispatcherPriority.Background);
    }

    private byte[]? ReadClientFile(string name)
    {
        return _client?.ReadFile(name);
    }

    private async Task LoadAsync()
    {
        int version = ++_version;
        if (!_binding.IsBound)
            return;
        List<SpellModelRequest> requests = SpellModels.Plan(_binding.Table!, _binding.Row);
        IconLibrary client = await _session.IconLibraryAsync();
        _client = client;
        if (!client.IsAvailable)
        {
            _view.Show(new List<ModelChoice>(), "Set the WoW client folder in the project settings to see models.");
            _notes.Text = string.Empty;
            return;
        }

        ModelSources sources = await _session.ModelSourcesAsync();
        List<ModelChoice> choices = new List<ModelChoice>();
        List<string> notes = new List<string>();
        foreach (SpellModelRequest request in requests)
        {
            switch (request.Kind)
            {
                case SpellModelKind.Visual:
                {
                    List<ModelChoice> found = sources.ForSpellVisual(request.Id);
                    choices.AddRange(found.Select(choice => choice with
                    {
                        Label = request.Label + ", " + choice.Label
                    }));
                    if (found.Count == 0)
                        notes.Add(request.Label + " has no models.");
                    break;
                }
                case SpellModelKind.Shapeshift:
                {
                    List<ModelChoice> found = sources.ForShapeshift(request.Id);
                    choices.AddRange(found.Select(choice => choice with { Label = request.Label }));
                    if (found.Count == 0)
                        notes.Add(request.Label + " has no creature display.");
                    break;
                }
                default:
                {
                    string table = request.Kind switch
                    {
                        SpellModelKind.Creature => WorldNames.CREATURES,
                        SpellModelKind.GameObject => WorldNames.GAME_OBJECTS,
                        _ => WorldNames.ITEMS
                    };
                    (uint Display, uint Slot)? display = await _session.WorldDisplayAsync(table, request.Id);
                    if (display == null)
                    {
                        notes.Add(request.Label + ": not found in the world database (or it isn't reachable).");
                        break;
                    }

                    ModelChoice? choice = request.Kind switch
                    {
                        SpellModelKind.Creature => sources.ForCreatureDisplay(display.Value.Display),
                        SpellModelKind.GameObject => sources.ForGameObjectDisplay(display.Value.Display),
                        _ => sources.ForItemDisplay(display.Value.Display)
                    };
                    string name = _session.WorldName(table, request.Id) is { Length: > 0 } known
                                      ? " (" + known + ")"
                                      : string.Empty;
                    if (choice != null)
                        choices.Add(choice with { Label = request.Label + name });
                    else
                        notes.Add(request.Label + name + ": display " + display.Value.Display +
                                  " has no model Anarres can show (buildings and character models aren't drawn).");
                    break;
                }
            }

            if (version != _version)
                return;
        }

        if (version != _version)
            return;
        _view.Show(choices, "This spell has no visual, and nothing it summons, turns into or makes.");
        _notes.Text = string.Join(Environment.NewLine, notes);
    }
}
