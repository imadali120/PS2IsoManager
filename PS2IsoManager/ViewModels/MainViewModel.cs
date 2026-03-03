using System.Collections.ObjectModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using PS2IsoManager.Models;
using PS2IsoManager.Services;
using PS2IsoManager.Views;

namespace PS2IsoManager.ViewModels;

public class MainViewModel : ViewModelBase
{
    private const long Fat32MaxFileSize = 4_294_967_295; // 4 GiB - 1
    private static readonly Regex IsoFileNameRegex = new(@"^([A-Z]{4}[_-]\d{3}\.\d{2})\.(.+)\.iso$", RegexOptions.IgnoreCase);

    private string _usbPath = string.Empty;
    private GameEntryViewModel? _selectedGame;
    private string _statusText = "Ready";
    private bool _isBusy;

    public MainViewModel()
    {
        Games = new ObservableCollection<GameEntryViewModel>();
        Games.CollectionChanged += (_, _) => RefreshStats();

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

    public int CdCount => Games.Count(g => g.Media == Models.MediaType.CD);
    public int DvdCount => Games.Count(g => g.Media == Models.MediaType.DVD);
    public int TotalParts => Games.Sum(g => (int)g.ChunkCount);
    public string EstimatedSize
    {
        get
        {
            double sizeGiB = TotalParts; // Each part is ~1 GiB
            return sizeGiB < 1 ? "0 GiB" : $"~{sizeGiB:F0} GiB";
        }
    }

    private void RefreshStats()
    {
        OnPropertyChanged(nameof(CdCount));
        OnPropertyChanged(nameof(DvdCount));
        OnPropertyChanged(nameof(TotalParts));
        OnPropertyChanged(nameof(EstimatedSize));
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

        // Load ul.cfg (split) games
        string ulCfgPath = Path.Combine(UsbPath, "ul.cfg");
        var entries = UlCfgService.ReadAll(ulCfgPath);
        var loadedGameIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            entry.Source = GameSource.UlCfg;
            string? coverPath = CoverArtService.FindExistingCover(entry.GameId, UsbPath);
            Games.Add(new GameEntryViewModel(entry, coverPath));
            loadedGameIds.Add(entry.GameId);
        }

        // Load direct ISO games from /DVD and /CD
        ScanIsoFolder("DVD", MediaType.DVD, loadedGameIds);
        ScanIsoFolder("CD", MediaType.CD, loadedGameIds);

        StatusText = $"Loaded {Games.Count} game(s) from {UsbPath}";
    }

    private void ScanIsoFolder(string folderName, MediaType mediaType, HashSet<string> loadedGameIds)
    {
        string folderPath = Path.Combine(UsbPath, folderName);
        if (!Directory.Exists(folderPath)) return;

        foreach (string filePath in Directory.EnumerateFiles(folderPath, "*.iso"))
        {
            string fileName = Path.GetFileName(filePath);
            var match = IsoFileNameRegex.Match(fileName);

            string gameId;
            string displayName;

            if (match.Success)
            {
                gameId = match.Groups[1].Value.ToUpperInvariant();
                displayName = match.Groups[2].Value;
            }
            else
            {
                // Non-OPL-named ISO — try to extract game ID from the ISO itself
                string? extractedId = Iso9660Reader.ExtractGameId(filePath);
                if (extractedId == null) continue;

                gameId = extractedId;
                displayName = Path.GetFileNameWithoutExtension(fileName);
            }

            if (loadedGameIds.Contains(gameId)) continue;

            var entry = new GameEntry
            {
                DisplayName = displayName,
                GameId = gameId,
                ChunkCount = 1,
                Media = mediaType,
                Source = GameSource.Iso,
                IsoFileName = fileName
            };

            string? coverPath = CoverArtService.FindExistingCover(gameId, UsbPath);
            Games.Add(new GameEntryViewModel(entry, coverPath));
            loadedGameIds.Add(gameId);
        }
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

    public Task AddSingleGame(string isoPath)
    {
        // Extract game ID
        string? gameId = Iso9660Reader.ExtractGameId(isoPath);
        if (gameId == null)
        {
            // Prompt for manual entry
            gameId = PromptForInput("Game ID Not Found",
                "Could not extract Game ID from ISO.\nPlease enter it manually (e.g. SLUS_202.65):");
            if (string.IsNullOrWhiteSpace(gameId)) return Task.CompletedTask;
        }

        string defaultName = Path.GetFileNameWithoutExtension(isoPath);
        if (defaultName.Length > 32) defaultName = defaultName.Substring(0, 32);

        string? displayName = PromptForInput("Game Name",
            $"Enter display name for the game (max 32 characters):", defaultName);
        if (string.IsNullOrWhiteSpace(displayName)) return Task.CompletedTask;
        if (displayName.Length > 32) displayName = displayName.Substring(0, 32);

        // Check for duplicate (both ul.cfg and direct ISO games)
        if (Games.Any(g => g.GameId.Equals(gameId, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show($"Game {gameId} already exists.", "Duplicate",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return Task.CompletedTask;
        }

        // Detect media type
        var mediaType = Iso9660Reader.DetectMediaType(isoPath);

        // Check file size to decide: direct copy vs split
        var fileInfo = new FileInfo(isoPath);
        bool useDirectCopy = fileInfo.Length <= Fat32MaxFileSize;

        // Show progress dialog
        var progressDlg = new ProgressDialog();
        progressDlg.Owner = Application.Current.MainWindow;

        IsBusy = true;
        string? isoFileName = null;
        byte chunkCount = 0;

        if (useDirectCopy)
        {
            progressDlg.SetMessage($"Copying: {displayName}");
            StatusText = $"Copying {Path.GetFileName(isoPath)}...";

            var progress = new Progress<double>(p => progressDlg.UpdateProgress(p));
            var statusProgress = new Progress<string>(s => progressDlg.SetMessage(s));

            var copyTask = Task.Run(async () =>
            {
                isoFileName = await IsoSplitterService.CopyIsoAsync(
                    isoPath, UsbPath, displayName, gameId, mediaType,
                    progress, progressDlg.CancellationToken, statusProgress);
            });

            progressDlg.Loaded += async (_, _) =>
            {
                try
                {
                    await copyTask;
                    progressDlg.CloseDialog();
                }
                catch (OperationCanceledException)
                {
                    // Clean up partial file
                    if (isoFileName != null)
                        IsoSplitterService.DeleteIso(UsbPath, isoFileName, mediaType);
                    progressDlg.CloseDialog();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error copying ISO: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    progressDlg.CloseDialog();
                }
            };
        }
        else
        {
            progressDlg.SetMessage($"Splitting: {displayName}");
            StatusText = $"Splitting {Path.GetFileName(isoPath)}...";

            var progress = new Progress<double>(p => progressDlg.UpdateProgress(p));
            var statusProgress = new Progress<string>(s => progressDlg.SetMessage(s));

            var splitTask = Task.Run(async () =>
            {
                chunkCount = await IsoSplitterService.SplitAsync(
                    isoPath, UsbPath, displayName, gameId,
                    progress, progressDlg.CancellationToken, statusProgress);
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
        }

        bool? result = progressDlg.ShowDialog();
        IsBusy = false;

        if (result != true)
        {
            StatusText = useDirectCopy ? "Copy cancelled." : "Split cancelled.";
            return Task.CompletedTask;
        }

        GameEntry entry;

        if (useDirectCopy)
        {
            entry = new GameEntry
            {
                DisplayName = displayName,
                GameId = gameId,
                ChunkCount = 1,
                Media = mediaType,
                Source = GameSource.Iso,
                IsoFileName = isoFileName
            };
            // No ul.cfg entry for direct ISOs
        }
        else
        {
            entry = new GameEntry
            {
                DisplayName = displayName,
                GameId = gameId,
                ChunkCount = chunkCount,
                Media = mediaType,
                Source = GameSource.UlCfg
            };

            string ulCfgPath = Path.Combine(UsbPath, "ul.cfg");
            UlCfgService.AppendEntry(ulCfgPath, entry);
        }

        // Add to UI
        string? coverPath = CoverArtService.FindExistingCover(gameId, UsbPath);
        var vm = new GameEntryViewModel(entry, coverPath);
        Games.Add(vm);
        SelectedGame = vm;

        string action = useDirectCopy ? "Copied" : "Split";
        string detail = useDirectCopy ? "ISO" : $"{chunkCount} part(s)";
        StatusText = $"{action}: {displayName} ({gameId}) - {detail}";
        return Task.CompletedTask;
    }

    private void DeleteGame()
    {
        if (SelectedGame == null) return;

        string deleteDetail = SelectedGame.IsDirectIso
            ? "This will delete the ISO file."
            : "This will remove the ul.cfg entry and all chunk files.";

        var result = MessageBox.Show(
            $"Delete \"{SelectedGame.DisplayName}\" ({SelectedGame.GameId})?\n\n{deleteDetail}",
            "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            if (SelectedGame.IsDirectIso)
            {
                // Delete the ISO file
                IsoSplitterService.DeleteIso(UsbPath, SelectedGame.IsoFileName!, SelectedGame.Media);
            }
            else
            {
                // Delete chunk files
                IsoSplitterService.DeleteChunks(UsbPath, SelectedGame.DisplayName,
                    SelectedGame.GameId, SelectedGame.ChunkCount);

                // Remove from ul.cfg
                string ulCfgPath = Path.Combine(UsbPath, "ul.cfg");
                UlCfgService.DeleteEntry(ulCfgPath, SelectedGame.GameId);
            }

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

            if (SelectedGame.IsDirectIso)
            {
                // Rename the ISO file
                string newFileName = $"{SelectedGame.GameId}.{newName}.iso";
                IsoSplitterService.RenameIso(UsbPath, SelectedGame.IsoFileName!, newFileName, SelectedGame.Media);
                SelectedGame.IsoFileName = newFileName;
            }
            else
            {
                // Rename chunk files (CRC changes with name)
                IsoSplitterService.RenameChunks(UsbPath, oldName, newName,
                    SelectedGame.GameId, SelectedGame.ChunkCount);

                // Update ul.cfg
                string ulCfgPath = Path.Combine(UsbPath, "ul.cfg");
                UlCfgService.RenameEntry(ulCfgPath, SelectedGame.GameId, newName);
            }

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

    private static System.Windows.Media.Brush FindBrush(string key)
    {
        return (System.Windows.Media.Brush)Application.Current.FindResource(key);
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
            Background = FindBrush("BackgroundBrush"),
            CornerRadius = new CornerRadius(8),
            BorderBrush = FindBrush("BorderBrush"),
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
            Foreground = FindBrush("TextBrush"),
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
            Background = FindBrush("SurfaceBrush")
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
