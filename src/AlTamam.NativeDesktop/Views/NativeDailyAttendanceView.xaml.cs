using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using AlTamam.NativePresentation;

namespace AlTamam.NativeDesktop.Views;

/// <summary>UI-only binding and dynamic roster columns; never mutates attendance.</summary>
public partial class NativeDailyAttendanceView : UserControl
{
    private const int FixedColumns = 6;
    private DailyAttendancePageViewModel? _viewModel;

    public NativeDailyAttendanceView()
    {
        InitializeComponent();
        DataContextChanged += (_, args) => Bind(args.NewValue as DailyAttendancePageViewModel);
        Loaded += (_, _) => Bind(DataContext as DailyAttendancePageViewModel);
        Unloaded += (_, _) => Bind(null);
    }

    private void Bind(DailyAttendancePageViewModel? next)
    {
        if (ReferenceEquals(_viewModel, next)) return;
        if (_viewModel is not null) _viewModel.PropertyChanged -= OnPageChanged;
        _viewModel = next;
        if (_viewModel is not null) _viewModel.PropertyChanged += OnPageChanged;
        UpdateColumns();
    }

    private void OnPageChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(DailyAttendancePageViewModel.CustomColumns))
            UpdateColumns();
    }

    private void UpdateColumns()
    {
        while (DailyGrid.Columns.Count > FixedColumns)
            DailyGrid.Columns.RemoveAt(DailyGrid.Columns.Count - 1);
        if (_viewModel is not { IsDataConnected: true }) return;
        for (int i = 0; i < _viewModel.CustomColumns.Count; i++)
        {
            DailyGrid.Columns.Add(new DataGridTextColumn
            {
                Header = _viewModel.CustomColumns[i].Caption,
                Binding = new Binding($"CustomCells[{i}]"),
                Width = new DataGridLength(135),
                IsReadOnly = true
            });
        }
    }

    private async void OnSiteChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_viewModel is null || !IsLoaded || sender is not ComboBox picker ||
            picker.SelectedValue is not string id || id == _viewModel.SelectedSiteId ||
            (!picker.IsKeyboardFocusWithin && !picker.IsDropDownOpen))
            return;
        await _viewModel.SelectSiteAsync(id);
    }

    private async void OnStatusChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_viewModel is null || !IsLoaded || sender is not ComboBox picker ||
            picker.SelectedValue is not string id || id == _viewModel.SelectedStatusId ||
            (!picker.IsKeyboardFocusWithin && !picker.IsDropDownOpen))
            return;
        await _viewModel.SelectStatusAsync(id);
    }
}
