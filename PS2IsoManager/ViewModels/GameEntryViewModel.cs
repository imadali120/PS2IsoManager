using PS2IsoManager.Models;
using PS2IsoManager.Services;

namespace PS2IsoManager.ViewModels;

public class GameEntryViewModel : ViewModelBase
{
    private readonly GameEntry _model;
    private string? _coverPath;

    public GameEntryViewModel(GameEntry model, string? coverPath = null)
    {
        _model = model;
        _coverPath = coverPath;
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
        set => SetProperty(ref _coverPath, value);
    }
}
