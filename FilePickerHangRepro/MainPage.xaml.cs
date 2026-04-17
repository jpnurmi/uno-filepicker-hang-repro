using Windows.Storage.Pickers;

namespace FilePickerHangRepro;

public sealed partial class MainPage : Page
{
    public MainPage()
    {
        this.InitializeComponent();
    }

    private async void OnPickClicked(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Opening picker...";
        var picker = new FileOpenPicker { ViewMode = PickerViewMode.List };
        picker.FileTypeFilter.Add("*");
        var files = await picker.PickMultipleFilesAsync();
        StatusText.Text = files is null || files.Count == 0
            ? "No files picked."
            : $"Picked {files.Count} file(s).";
    }
}
