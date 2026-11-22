using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using AlTamam.NativePresentation;

namespace AlTamam.NativeDesktop.Views;

/// <summary>
/// Pure native visual projection. The monthly archive service remains
/// read-only and owns each historical day cell and movement annotation.
/// </summary>
public partial class NativeMonthlyAttendanceView : UserControl
{
    private const int FixedColumns = 5;
    private MonthlyAttendancePageViewModel? _viewModel;

    public NativeMonthlyAttendanceView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) => Bind(e.NewValue as MonthlyAttendancePageViewModel);
        Loaded += (_, _) => Bind(DataContext as MonthlyAttendancePageViewModel);
        Unloaded += (_, _) => Bind(null);
    }

    private void Bind(MonthlyAttendancePageViewModel? next)
    {
        if (ReferenceEquals(_viewModel, next)) return;
        if (_viewModel is not null) _viewModel.PropertyChanged -= OnPageChanged;
        _viewModel = next;
        if (_viewModel is not null) _viewModel.PropertyChanged += OnPageChanged;
        RenderColumns();
    }

    private void OnPageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MonthlyAttendancePageViewModel.Days)
            or nameof(MonthlyAttendancePageViewModel.CustomColumns))
            RenderColumns();
    }

    private void RenderColumns()
    {
        while (MonthlyGrid.Columns.Count > FixedColumns)
            MonthlyGrid.Columns.RemoveAt(MonthlyGrid.Columns.Count - 1);
        if (_viewModel is not { IsDataConnected: true }) return;

        for (int i = 0; i < _viewModel.Days.Count; i++)
        {
            var template = new DataTemplate();
            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new Binding($"Days[{i}].Symbol"));
            text.SetBinding(FrameworkElement.ToolTipProperty,
                new Binding($"Days[{i}].Description"));
            text.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
            template.VisualTree = text;
            MonthlyGrid.Columns.Add(new DataGridTemplateColumn
            {
                Header = _viewModel.Days[i].ToString("00"),
                CellTemplate = template,
                Width = new DataGridLength(43),
                IsReadOnly = true
            });
        }
        for (int i = 0; i < _viewModel.CustomColumns.Count; i++)
        {
            MonthlyGrid.Columns.Add(new DataGridTextColumn
            {
                Header = _viewModel.CustomColumns[i].Caption,
                Binding = new Binding($"CustomCells[{i}]"),
                Width = new DataGridLength(130),
                IsReadOnly = true
            });
        }
    }

    private async void OnPeriodChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel is null || !IsLoaded || sender is not ComboBox box ||
            box.SelectedValue is not string period ||
            period == _viewModel.SelectedPeriod ||
            (!box.IsKeyboardFocusWithin && !box.IsDropDownOpen)) return;
        await _viewModel.SelectPeriodAsync(period);
    }

    private async void OnSiteChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel is null || !IsLoaded || sender is not ComboBox box ||
            box.SelectedValue is not string site ||
            site == _viewModel.SelectedSiteId ||
            (!box.IsKeyboardFocusWithin && !box.IsDropDownOpen)) return;
        await _viewModel.SelectSiteAsync(site);
    }
}
