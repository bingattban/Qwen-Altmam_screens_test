using System.ComponentModel;
using System.Windows.Input;

namespace AlTamam.NativePresentation;

public sealed class NativeShellViewModel : INotifyPropertyChanged
{
    private readonly IReadOnlyDictionary<NativeScreen, NativePageViewModel> _pages;
    private NativePageViewModel _currentPage;

    public NativeShellViewModel()
    {
        Pages = new NativePageViewModel[]
        {
            new DashboardPageViewModel(),
            new DailyAttendancePageViewModel(),
            new MonthlyAttendancePageViewModel(),
            new AdministrationPageViewModel(),
            new ReportsPageViewModel(),
            new SettingsPageViewModel()
        };
        _pages = Pages.ToDictionary(page => page.Screen);
        _currentPage = _pages[NativeScreen.Dashboard];
        NavigateCommand = new NativeCommand(
            parameter =>
            {
                if (parameter is NativeScreen screen && _pages.TryGetValue(screen, out var selected))
                    CurrentPage = selected;
            },
            parameter => parameter is NativeScreen screen && _pages.ContainsKey(screen));
    }

    public IReadOnlyList<NativePageViewModel> Pages { get; }
    public ICommand NavigateCommand { get; }

    public NativePageViewModel CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (ReferenceEquals(_currentPage, value)) return;
            _currentPage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentPage)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class NativeCommand(Action<object?> execute, Predicate<object?> canExecute) : ICommand
{
    public bool CanExecute(object? parameter) => canExecute(parameter);
    public void Execute(object? parameter)
    {
        if (CanExecute(parameter)) execute(parameter);
    }

    // Navigation availability is immutable in this non-operational pilot.
    public event EventHandler? CanExecuteChanged { add { } remove { } }
}
