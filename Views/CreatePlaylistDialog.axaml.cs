using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace sopfiy.Views;

public partial class CreatePlaylistDialog : Window
{
    public string PlaylistName { get; private set; } = string.Empty;

    public CreatePlaylistDialog()
    {
        InitializeComponent();
        Opened += (s, e) =>
        {
            TxtPlaylistName.Focus();
            TxtPlaylistName.SelectAll();
        };
    }

    private void BtnCreate_Click(object? sender, RoutedEventArgs e)
    {
        ConfirmCreation();
    }

    private void BtnCancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private void TxtPlaylistName_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ConfirmCreation();
        }
        else if (e.Key == Key.Escape)
        {
            Close(false);
        }
    }

    private void ConfirmCreation()
    {
        string name = TxtPlaylistName.Text?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(name))
        {
            PlaylistName = name;
            Close(true);
        }
    }
}
