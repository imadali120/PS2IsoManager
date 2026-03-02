using System.Windows;

namespace PS2IsoManager.Views;

public partial class ProgressDialog : Window
{
    private readonly CancellationTokenSource _cts = new();

    public ProgressDialog()
    {
        InitializeComponent();
    }

    public CancellationToken CancellationToken => _cts.Token;

    public void UpdateProgress(double fraction)
    {
        Dispatcher.Invoke(() =>
        {
            int pct = (int)(fraction * 100);
            Progress.Value = pct;
            PercentText.Text = $"{pct}%";
        });
    }

    public void SetMessage(string message)
    {
        Dispatcher.Invoke(() => MessageText.Text = message);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _cts.Cancel();
        CancelButton.IsEnabled = false;
        CancelButton.Content = "Cancelling...";
    }

    public void CloseDialog()
    {
        Dispatcher.Invoke(() =>
        {
            DialogResult = !_cts.IsCancellationRequested;
            Close();
        });
    }
}
