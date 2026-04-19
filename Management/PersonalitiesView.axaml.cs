using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.EntityFrameworkCore;

namespace PokerApp;

public partial class PersonalitiesView : UserControl
{
    private int? _editingId;

    public Action? NavigateBack { get; set; }

    public PersonalitiesView()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RefreshListAsync();
    }

    private void OnBackClick(object? sender, RoutedEventArgs e) => NavigateBack?.Invoke();

    private void OnNewClick(object? sender, RoutedEventArgs e)
    {
        PersonalitiesList.SelectedItem = null;
        _editingId = null;
        NameEditor.Text = string.Empty;
        DescriptionEditor.Text = string.Empty;
        ErrorText.Text = string.Empty;
    }

    private async void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        if (PersonalitiesList.SelectedItem is not LlmAgentPersonality selected)
        {
            ErrorText.Text = "Select a personality to delete.";
            return;
        }

        await using var db = PokerDbBootstrap.CreateContext();
        var tracked = await db.LlmAgentPersonalities.FindAsync(selected.Id);
        if (tracked == null)
        {
            await RefreshListAsync();
            return;
        }

        db.LlmAgentPersonalities.Remove(tracked);
        await db.SaveChangesAsync();
        _editingId = null;
        NameEditor.Text = string.Empty;
        DescriptionEditor.Text = string.Empty;
        PersonalitiesList.SelectedItem = null;
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

        var desc = DescriptionEditor.Text ?? string.Empty;
        var now = DateTimeOffset.UtcNow;

        await using var db = PokerDbBootstrap.CreateContext();
        if (_editingId == null)
        {
            db.LlmAgentPersonalities.Add(new LlmAgentPersonality
            {
                Name = name,
                PersonalityDescription = desc,
                CreatedAt = now,
                EditedAt = now
            });
        }
        else
        {
            var entity = await db.LlmAgentPersonalities.FindAsync(_editingId.Value);
            if (entity == null)
            {
                ErrorText.Text = "Record no longer exists.";
                await RefreshListAsync();
                return;
            }

            entity.Name = name;
            entity.PersonalityDescription = desc;
            entity.EditedAt = now;
        }

        await db.SaveChangesAsync();
        await RefreshListAsync();
    }

    private void OnListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        if (PersonalitiesList.SelectedItem is LlmAgentPersonality p)
        {
            _editingId = p.Id;
            NameEditor.Text = p.Name;
            DescriptionEditor.Text = p.PersonalityDescription;
        }
        else if (PersonalitiesList.SelectedItem == null)
            _editingId = null;
    }

    private async Task RefreshListAsync()
    {
        await using var db = PokerDbBootstrap.CreateContext();
        var raw = await db.LlmAgentPersonalities.AsNoTracking().ToListAsync();
        var list = raw.OrderByDescending(x => x.EditedAt).ToList();

        PersonalitiesList.ItemsSource = list;
        if (_editingId != null)
        {
            var match = list.FirstOrDefault(x => x.Id == _editingId);
            if (match != null)
                PersonalitiesList.SelectedItem = match;
        }
    }
}
