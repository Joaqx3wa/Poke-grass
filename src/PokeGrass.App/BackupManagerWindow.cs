using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace PokeGrass;

internal sealed class BackupManagerWindow : Window
{
    private readonly string _savePath;
    private readonly Action<string> _restartAfterRestore;
    private readonly System.Windows.Controls.ListBox _backups = new();
    private IReadOnlyList<SaveBackupInfo> _items = [];

    public BackupManagerWindow(Window owner, string savePath, Action<string> restartAfterRestore)
    {
        Owner = owner;
        _savePath = savePath;
        _restartAfterRestore = restartAfterRestore;
        Title = "PokéGrass · Copias de seguridad";
        Width = 520;
        Height = 390;
        MinWidth = 460;
        MinHeight = 330;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(251, 248, 226));
        Topmost = true;

        var layout = new Grid { Margin = new Thickness(18) };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var title = new TextBlock { Text = "COPIAS DE SEGURIDAD", FontFamily = PixelUi.PixelFont, FontSize = 22,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(47, 44, 31)), Margin = new Thickness(4, 0, 0, 8) };
        layout.Children.Add(title);
        var description = new TextBlock { Text = "Las automáticas conservan las 10 más recientes. Las manuales y previas a migración se guardan aparte.",
            TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(71, 64, 43)), Margin = new Thickness(4, 0, 4, 8) };
        Grid.SetRow(description, 1);
        layout.Children.Add(description);
        _backups.Margin = new Thickness(2, 0, 2, 10);
        _backups.DisplayMemberPath = nameof(SaveBackupInfo.DisplayName);
        Grid.SetRow(_backups, 2);
        layout.Children.Add(_backups);

        var buttons = new UniformGrid { Columns = 3, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
        var create = PixelButtonUi.Button("Crear ahora", "green", 140, 52, CreateBackup, fontSize: 15);
        var restore = PixelButtonUi.Button("Restaurar", "blue", 140, 52, RestoreBackup, fontSize: 15);
        var close = PixelButtonUi.Button("Cerrar", "red", 110, 52, Close, fontSize: 15);
        create.Margin = restore.Margin = close.Margin = new Thickness(4, 0, 4, 0);
        buttons.Children.Add(create);
        buttons.Children.Add(restore);
        buttons.Children.Add(close);
        Grid.SetRow(buttons, 3);
        layout.Children.Add(buttons);
        Content = layout;
        RefreshBackups();
    }

    private void RefreshBackups()
    {
        _items = SaveBackupService.ListValid(_savePath);
        _backups.ItemsSource = _items;
        if (_backups.Items.Count > 0 && _backups.SelectedIndex < 0) _backups.SelectedIndex = 0;
    }

    private void CreateBackup()
    {
        try
        {
            var backup = SaveBackupService.CreateManual(_savePath);
            RefreshBackups();
            _backups.SelectedItem = _items.FirstOrDefault(item => item.DirectoryPath == backup.DirectoryPath);
            System.Windows.MessageBox.Show(this, $"Copia manual creada.\n\n{backup.CreatedUtc.ToLocalTime():g}", "PokéGrass",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(this, $"No se pudo crear la copia.\n{exception.Message}", "PokéGrass",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RestoreBackup()
    {
        if (_backups.SelectedItem is not SaveBackupInfo backup)
        {
            System.Windows.MessageBox.Show(this, "Selecciona una copia válida.", "PokéGrass", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!PixelConfirmWindow.Confirm(this, "Restaurar copia",
                $"La partida actual será reemplazada por la copia del {backup.CreatedUtc.ToLocalTime():g}. Se preservará una copia del archivo actual y PokéGrass se reiniciará. ¿Continuar?")) return;
        try
        {
            SaveBackupService.Restore(_savePath, backup);
            _restartAfterRestore(backup.SavePath);
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(this, $"No se pudo restaurar la copia.\n{exception.Message}", "PokéGrass",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
