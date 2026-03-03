using System.IO;
using System.Windows.Media.Imaging;
using PS2IsoManager.Models;
using PS2IsoManager.Services;

namespace PS2IsoManager.ViewModels;

public class GameEntryViewModel : ViewModelBase
{
    private readonly GameEntry _model;
    private string? _coverPath;
    private BitmapImage? _coverImage;

    public GameEntryViewModel(GameEntry model, string? coverPath = null)
    {
        _model = model;
        if (coverPath != null)
            SetCoverFromPath(coverPath);
    }

    public GameEntry Model => _model;

    public string DisplayName
    {
        get => _model.DisplayName;
        set
        {
            if (_model.DisplayName != value)
            {
                _model.DisplayName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CrcHex));
            }
        }
    }

    public string GameId
    {
        get => _model.GameId;
        set
        {
            if (_model.GameId != value)
            {
                _model.GameId = value;
                OnPropertyChanged();
            }
        }
    }

    public byte ChunkCount
    {
        get => _model.ChunkCount;
        set
        {
            if (_model.ChunkCount != value)
            {
                _model.ChunkCount = value;
                OnPropertyChanged();
            }
        }
    }

    public MediaType Media
    {
        get => _model.Media;
        set
        {
            if (_model.Media != value)
            {
                _model.Media = value;
                OnPropertyChanged();
            }
        }
    }

    public string CrcHex => OplCrc32.ComputeHex(DisplayName);

    public string? CoverPath
    {
        get => _coverPath;
        set
        {
            if (_coverPath != value)
            {
                _coverPath = value;
                OnPropertyChanged();
                SetCoverFromPath(value);
            }
        }
    }

    public BitmapImage? CoverImage
    {
        get => _coverImage;
        private set => SetProperty(ref _coverImage, value);
    }

    private void SetCoverFromPath(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            CoverImage = null;
            return;
        }

        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bi.UriSource = new Uri(path, UriKind.Absolute);
            bi.EndInit();
            bi.Freeze();
            CoverImage = bi;
        }
        catch
        {
            CoverImage = null;
        }
    }
}
