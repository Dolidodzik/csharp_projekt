using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.EntityFrameworkCore;

namespace PokerApp;

public partial class PresetsView : UserControl
{
    private int? _editingId;

    public Action? NavigateBack { get; set; }

    public PresetsView()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RefreshListAsync();
    }

    private void OnBackClick(object? sender, RoutedEventArgs e) => NavigateBack?.Invoke();

    private void OnNewClick(object? sender, RoutedEventArgs e)
    {
        PresetsList.SelectedItem = null;
        _editingId = null;
        NameEditor.Text = string.Empty;
        UrlEditor.Text = string.Empty;
        KeyEditor.Text = string.Empty;
        ModelEditor.Text = string.Empty;
        ModelEditor.IsReadOnly = false;
        ErrorText.Text = string.Empty;
    }

    private async void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        if (PresetsList.SelectedItem is not OpenAiPreset selected)
        {
            ErrorText.Text = "Select a preset to delete.";
            return;
        }

        await using var db = PokerDbBootstrap.CreateContext();
        var tracked = await db.OpenAiPresets.FindAsync(selected.Id);
        if (tracked == null)
        {
            await RefreshListAsync();
            return;
        }

        db.OpenAiPresets.Remove(tracked);
        await db.SaveChangesAsync();
        _editingId = null;
        NameEditor.Text = string.Empty;
        UrlEditor.Text = string.Empty;
        KeyEditor.Text = string.Empty;
        ModelEditor.Text = string.Empty;
        ModelEditor.IsReadOnly = false;
        PresetsList.SelectedItem = null;
        await RefreshListAsync();
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        var name = NameEditor.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(name))
        {
            ErrorText.Text = "Name is required.";
            return;
        }

        var url = UrlEditor.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(url))
        {
            ErrorText.Text = "API URL is required.";
            return;
        }

        var key = KeyEditor.Text ?? string.Empty;
        var model = ModelEditor.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(model))
        {
            ErrorText.Text = "Model name is required.";
            return;
        }

        var now = DateTimeOffset.UtcNow;

        await using var db = PokerDbBootstrap.CreateContext();
        if (_editingId == null)
        {
            db.OpenAiPresets.Add(new OpenAiPreset
            {
                Name = name,
                ApiUrl = url,
                ApiKey = key,
                ModelName = model,
                CreatedAt = now,
                EditedAt = now
            });
        }
        else
        {
            var entity = await db.OpenAiPresets.FindAsync(_editingId.Value);
            if (entity == null)
            {
                ErrorText.Text = "Record no longer exists.";
                await RefreshListAsync();
                return;
            }

            entity.Name = name;
            entity.ApiUrl = url;
            entity.ApiKey = key;
            entity.EditedAt = now;
        }

        await db.SaveChangesAsync();
        await RefreshListAsync();
    }

    private void OnListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        if (PresetsList.SelectedItem is OpenAiPreset p)
        {
            _editingId = p.Id;
            NameEditor.Text = p.Name;
            UrlEditor.Text = p.ApiUrl;
            KeyEditor.Text = p.ApiKey;
            ModelEditor.Text = p.ModelName;
            ModelEditor.IsReadOnly = true;
        }
        else if (PresetsList.SelectedItem == null)
        {
            _editingId = null;
            ModelEditor.IsReadOnly = false;
        }
    }

    private async Task RefreshListAsync()
    {
        await using var db = PokerDbBootstrap.CreateContext();
        var raw = await db.OpenAiPresets.AsNoTracking().ToListAsync();
        var list = raw.OrderByDescending(x => x.EditedAt).ToList();

        PresetsList.ItemsSource = list;
        if (_editingId != null)
        {
            var match = list.FirstOrDefault(x => x.Id == _editingId);
            if (match != null)
                PresetsList.SelectedItem = match;
        }
    }
}
