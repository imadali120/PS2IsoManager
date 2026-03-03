using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using PS2IsoManager.Models;
using PS2IsoManager.Services;
using PS2IsoManager.Views;

namespace PS2IsoManager.ViewModels;

public class MainViewModel : ViewModelBase
{
    private string _usbPath = string.Empty;
    private GameEntryViewModel? _selectedGame;
    private string _statusText = "Ready";
    private bool _isBusy;

    public MainViewModel()
    {
        Games = new ObservableCollection<GameEntryViewModel>();

        SelectFolderCommand = new RelayCommand(SelectFolder);
        AddGameCommand = new RelayCommand(AddGame, () => !IsBusy && !string.IsNullOrEmpty(UsbPath));
        DeleteGameCommand = new RelayCommand(DeleteGame, () => !IsBusy && SelectedGame != null);
        RenameGameCommand = new RelayCommand(RenameGame, () => !IsBusy && SelectedGame != null);
        DownloadArtCommand = new RelayCommand(DownloadArt, () => !IsBusy && SelectedGame != null);
        DownloadAllArtCommand = new RelayCommand(DownloadAllArt, () => !IsBusy && Games.Count > 0);
        RefreshCommand = new RelayCommand(Refresh, () => !IsBusy && !string.IsNullOrEmpty(UsbPath));

        // Try to restore last path
        string? lastPath = LoadLastPath();
        if (!string.IsNullOrEmpty(lastPath) && Directory.Exists(lastPath))
        {
            UsbPath = lastPath;
            LoadGames();
        }
    }

    public ObservableCollection<GameEntryViewModel> Games { get; }

    public string UsbPath
    {
        get => _usbPath;
        set
        {
            if (SetProperty(ref _usbPath, value))
                OnPropertyChanged(nameof(HasUsbPath));
        }
    }

    public bool HasUsbPath => !string.IsNullOrEmpty(UsbPath);

    public GameEntryViewModel? SelectedGame
    {
        get => _selectedGame;
        set
        {
            if (SetProperty(ref _selectedGame, value))
                OnPropertyChanged(nameof(HasSelectedGame));
        }
    }

    public bool HasSelectedGame => SelectedGame != null;

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public ICommand SelectFolderCommand { get; }
    public ICommand AddGameCommand { get; }
    public ICommand DeleteGameCommand { get; }
    public ICommand RenameGameCommand { get; }
    public ICommand DownloadArtCommand { get; }
    public ICommand DownloadAllArtCommand { get; }
    public ICommand RefreshCommand { get; }

    private void SelectFolder()
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Select USB Drive / OPL Folder"
        };
        if (dlg.ShowDialog() == true)
        {
            UsbPath = dlg.FolderName;
            SaveLastPath(UsbPath);
            LoadGames();
        }
    }

    private void LoadGames()
    {
        Games.Clear();
        SelectedGame = null;

        string ulCfgPath = Path.Combine(UsbPath, "ul.cfg");
        var entries = UlCfgService.ReadAll(ulCfgPath);

        foreach (var entry in entries)
        {
            string? coverPath = CoverArtService.FindExistingCover(entry.GameId, UsbPath);
            Games.Add(new GameEntryViewModel(entry, coverPath));
        }

        StatusText = $"Loaded {Games.Count} game(s) from {UsbPath}";
    }

    private async void AddGame()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select PS2 ISO",
            Filter = "PS2 ISO Files (*.iso)|*.iso|All Files (*.*)|*.*",
            Multiselect = true
        };

        if (dlg.ShowDialog() != true) return;

        foreach (string isoPath in dlg.FileNames)
        {
            await AddSingleGame(isoPath);
        }
    }

    public async Task AddSingleGame(string isoPath)
    {
        // Extract game ID
        string? gameId = Iso9660Reader.ExtractGameId(isoPath);
        if (gameId == null)
        {
            // Prompt for manual entry
            gameId = PromptForInput("Game ID Not Found",
                "Could not extract Game ID from ISO.\nPlease enter it manually (e.g. SLUS_202.65):");
            if (string.IsNullOrWhiteSpace(gameId)) return;
        }

        // Look up the official game name from PSX Data Center
        StatusText = $"Looking up game name for {gameId}...";
        string defaultName = Path.GetFileNameWithoutExtension(isoPath);
        if (defaultName.Length > 32) defaultName = defaultName.Substring(0, 32);

        try
        {
            string? lookedUpName = await GameNameLookupService.LookupAsync(gameId);
            if (!string.IsNullOrEmpty(lookedUpName))
                defaultName = lookedUpName;
        }
        catch { /* fall back to filename */ }

        string? displayName = PromptForInput("Game Name",
            $"Enter display name for the game (max 32 characters):", defaultName);
        if (string.IsNullOrWhiteSpace(displayName)) return;
        if (displayName.Length > 32) displayName = displayName.Substring(0, 32);

        // Check for duplicate
        if (Games.Any(g => g.GameId == gameId))
        {
            MessageBox.Show($"Game {gameId} already exists.", "Duplicate",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Detect media type
        var mediaType = Iso9660Reader.DetectMediaType(isoPath);

        // Show progress dialog and split
        var progressDlg = new ProgressDialog();
        progressDlg.Owner = Application.Current.MainWindow;
        progressDlg.SetMessage($"Splitting: {displayName}");

        IsBusy = true;
        StatusText = $"Splitting {Path.GetFileName(isoPath)}...";

        byte chunkCount = 0;
        var progress = new Progress<double>(p => progressDlg.UpdateProgress(p));

        var splitTask = Task.Run(async () =>
        {
            chunkCount = await IsoSplitterService.SplitAsync(
                isoPath, UsbPath, displayName, gameId,
                progress, progressDlg.CancellationToken);
        });

        progressDlg.Loaded += async (_, _) =>
        {
            try
            {
                await splitTask;
                progressDlg.CloseDialog();
            }
            catch (OperationCanceledException)
            {
                // Clean up partial chunks
                if (chunkCount > 0)
                    IsoSplitterService.DeleteChunks(UsbPath, displayName, gameId, chunkCount);
                progressDlg.CloseDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error splitting ISO: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                progressDlg.CloseDialog();
            }
        };

        bool? result = progressDlg.ShowDialog();
        IsBusy = false;

        if (result != true)
        {
            StatusText = "Split cancelled.";
            return;
        }

        // Register in ul.cfg
        var entry = new GameEntry
        {
            DisplayName = displayName,
            GameId = gameId,
            ChunkCount = chunkCount,
            Media = mediaType
        };

        string ulCfgPath = Path.Combine(UsbPath, "ul.cfg");
        UlCfgService.AppendEntry(ulCfgPath, entry);

        // Add to UI
        string? coverPath = CoverArtService.FindExistingCover(gameId, UsbPath);
        var vm = new GameEntryViewModel(entry, coverPath);
        Games.Add(vm);
        SelectedGame = vm;

        StatusText = $"Added: {displayName} ({gameId}) - {chunkCount} part(s)";
    }

    private void DeleteGame()
    {
        if (SelectedGame == null) return;

        var result = MessageBox.Show(
            $"Delete \"{SelectedGame.DisplayName}\" ({SelectedGame.GameId})?\n\nThis will remove the ul.cfg entry and all chunk files.",
            "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            // Delete chunk files
            IsoSplitterService.DeleteChunks(UsbPath, SelectedGame.DisplayName,
                SelectedGame.GameId, SelectedGame.ChunkCount);

            // Remove from ul.cfg
            string ulCfgPath = Path.Combine(UsbPath, "ul.cfg");
            UlCfgService.DeleteEntry(ulCfgPath, SelectedGame.GameId);

            string name = SelectedGame.DisplayName;
            Games.Remove(SelectedGame);
            SelectedGame = null;

            StatusText = $"Deleted: {name}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error deleting game: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RenameGame()
    {
        if (SelectedGame == null) return;

        string? newName = PromptForInput("Rename Game",
            "Enter new display name (max 32 characters):", SelectedGame.DisplayName);

        if (string.IsNullOrWhiteSpace(newName) || newName == SelectedGame.DisplayName)
            return;
        if (newName.Length > 32) newName = newName.Substring(0, 32);

        try
        {
            string oldName = SelectedGame.DisplayName;

            // Rename chunk files (CRC changes with name)
            IsoSplitterService.RenameChunks(UsbPath, oldName, newName,
                SelectedGame.GameId, SelectedGame.ChunkCount);

            // Update ul.cfg
            string ulCfgPath = Path.Combine(UsbPath, "ul.cfg");
            UlCfgService.RenameEntry(ulCfgPath, SelectedGame.GameId, newName);

            // Update VM
            SelectedGame.DisplayName = newName;

            StatusText = $"Renamed: \"{oldName}\" → \"{newName}\"";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error renaming game: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void DownloadArt()
    {
        if (SelectedGame == null) return;

        var game = SelectedGame;
        IsBusy = true;
        StatusText = $"Downloading cover art for {game.GameId}...";

        try
        {
            var statusProgress = new Progress<string>(s => StatusText = s);
            string? path = await CoverArtService.DownloadCoverAsync(game.GameId, UsbPath, statusProgress);
            if (path != null)
            {
                game.CoverPath = path;
                // Binding is directly on GameEntryViewModel.CoverPath so PropertyChanged propagates automatically
                StatusText = $"Cover art downloaded for {game.GameId}";
            }
            else
            {
                StatusText = $"No cover art found for {game.GameId}";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error downloading art: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async void DownloadAllArt()
    {
        IsBusy = true;
        int downloaded = 0;
        int total = Games.Count;
        int skipped = 0;

        for (int i = 0; i < Games.Count; i++)
        {
            var game = Games[i];
            if (!string.IsNullOrEmpty(game.CoverPath))
            {
                skipped++;
                continue;
            }

            StatusText = $"Downloading art ({i + 1}/{total}): {game.GameId}...";
            try
            {
                string? path = await CoverArtService.DownloadCoverAsync(game.GameId, UsbPath);
                if (path != null)
                {
                    game.CoverPath = path;
                    downloaded++;

                    // Direct binding on CoverPath propagates automatically
                }
            }
            catch { /* skip failures */ }
        }

        IsBusy = false;
        StatusText = $"Downloaded {downloaded} cover(s), {skipped} already had art, {total - downloaded - skipped} not found";
    }

    private void Refresh()
    {
        LoadGames();
    }

    private static string? PromptForInput(string title, string message, string defaultValue = "")
    {
        // Simple input dialog using WPF
        var dlg = new Window
        {
            Title = title,
            Width = 420,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current.MainWindow,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize
        };

        var border = new System.Windows.Controls.Border
        {
            Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1A1A2E")),
            CornerRadius = new CornerRadius(8),
            BorderBrush = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2A2A4A")),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(8)
        };

        var grid = new System.Windows.Controls.Grid { Margin = new Thickness(24, 20, 24, 20) };
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });

        var msgBlock = new System.Windows.Controls.TextBlock
        {
            Text = message,
            Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E0E0E0")),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };
        System.Windows.Controls.Grid.SetRow(msgBlock, 0);
        grid.Children.Add(msgBlock);

        var textBox = new System.Windows.Controls.TextBox
        {
            Text = defaultValue,
            MaxLength = 32,
            Margin = new Thickness(0, 0, 0, 16),
            SelectionStart = defaultValue.Length
        };
        System.Windows.Controls.Grid.SetRow(textBox, 1);
        grid.Children.Add(textBox);

        var btnPanel = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var okBtn = new System.Windows.Controls.Button { Content = "OK", Padding = new Thickness(20, 6, 20, 6), Margin = new Thickness(4) };
        var cancelBtn = new System.Windows.Controls.Button
        {
            Content = "Cancel", Padding = new Thickness(20, 6, 20, 6), Margin = new Thickness(4),
            Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#333355"))
        };

        string? resultValue = null;
        okBtn.Click += (_, _) => { resultValue = textBox.Text; dlg.Close(); };
        cancelBtn.Click += (_, _) => { dlg.Close(); };
        textBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { resultValue = textBox.Text; dlg.Close(); } };

        btnPanel.Children.Add(cancelBtn);
        btnPanel.Children.Add(okBtn);
        System.Windows.Controls.Grid.SetRow(btnPanel, 2);
        grid.Children.Add(btnPanel);

        border.Child = grid;

        var effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = System.Windows.Media.Colors.Black,
            Opacity = 0.6,
            BlurRadius = 16,
            ShadowDepth = 2
        };
        border.Effect = effect;

        dlg.Content = border;
        dlg.Loaded += (_, _) => { textBox.Focus(); textBox.SelectAll(); };
        dlg.ShowDialog();

        return resultValue;
    }

    private static string SettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PS2IsoManager", "settings.txt");

    private static void SaveLastPath(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, path);
        }
        catch { }
    }

    private static string? LoadLastPath()
    {
        try
        {
            return File.Exists(SettingsPath) ? File.ReadAllText(SettingsPath).Trim() : null;
        }
        catch { return null; }
    }
}
