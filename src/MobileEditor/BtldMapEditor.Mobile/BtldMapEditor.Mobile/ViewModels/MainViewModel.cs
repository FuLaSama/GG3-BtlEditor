using CommunityToolkit.Mvvm.ComponentModel;

namespace BtldMapEditor.Mobile.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _greeting = "Welcome to Avalonia!";
}
