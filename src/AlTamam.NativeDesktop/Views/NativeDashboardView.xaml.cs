using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using AlTamam.NativePresentation;

namespace AlTamam.NativeDesktop.Views;

/// <summary>
/// UI-only WPF wiring: dynamic custom-column headers and the actual site
/// ComboBox. All data, validation, filtering, security and SQL live in C#
/// services/ViewModels. There is NO WebView2/HTML or business mutation here.
/// </summary>
public partial class NativeDashboardView : UserControl
{
    private const int StandardColumns = 5;
    private DashboardPageViewModel? _viewModel;

    public NativeDashboardView()
    {
        InitializeComponent();
        DataContextChanged += OnContextChanged;
        Loaded += (_, _) => UpdateViewModel(DataContext as DashboardPageViewModel);
        Unloaded += (_, _) => UpdateViewModel(null);
    }

    private void OnContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        => UpdateViewModel(e.NewValue as DashboardPageViewModel);

    private void UpdateViewModel(DashboardPageViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel)) return;
        if (_viewModel is not null) _viewModel.PropertyChanged -= OnPageChanged;
        _viewModel = viewModel;
        if (_viewModel is not null) _viewModel.PropertyChanged += OnPageChanged;
        RebuildCustomColumns();
    }

    private void OnPageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DashboardPageViewModel.CustomColumns))
            RebuildCustomColumns();
    }

    private void RebuildCustomColumns()
    {
        while (PersonnelGrid.Columns.Count > StandardColumns)
            PersonnelGrid.Columns.RemoveAt(PersonnelGrid.Columns.Count - 1);
        if (_viewModel is null || !_viewModel.IsDataConnected) return;

        var columns = _viewModel.CustomColumns;
        for (int i = 0; i < columns.Count; i++)
        {
            PersonnelGrid.Columns.Add(new DataGridTextColumn
            {
                Header = columns[i].Caption,
                Binding = new Binding($"CustomCells[{i}]"),
                Width = new DataGridLength(140, DataGridLengthUnitType.Pixel),
                IsReadOnly = true
            });
        }
    }

    private async void OnSiteSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel is null || !IsLoaded || sender is not ComboBox picker ||
            picker.SelectedValue is not string siteId || siteId == _viewModel.SelectedSiteId)
            return;

        // SelectionChanged also fires while ItemsSource is rebuilt from the
        // server. Only actual mouse/keyboard changes should issue a new query.
        if (!picker.IsKeyboardFocusWithin && !picker.IsDropDownOpen) return;
        await _viewModel.SelectSiteAsync(siteId);
    }
}
