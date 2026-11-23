using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using AlTamam.NativePresentation;

namespace AlTamam.NativeDesktop.Views;

/// <summary>
/// Native WPF projection of canonical PersonRosterService current roster
/// and identity history. Never directly mutates the SQLCipher store.
/// </summary>
public partial class NativePersonnelDirectoryView : UserControl
{
    private const int FixedColumns = 6;
    private AdministrationPageViewModel? _vm;

    public NativePersonnelDirectoryView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) => Bind(e.NewValue as AdministrationPageViewModel);
        Loaded += (_, _) => Bind(DataContext as AdministrationPageViewModel);
        Unloaded += (_, _) => Bind(null);
    }

    private void Bind(AdministrationPageViewModel? vm)
    {
        if (ReferenceEquals(_vm, vm)) return;
        if (_vm is not null) _vm.PropertyChanged -= OnPageChanged;
        _vm = vm;
        if (_vm is not null) _vm.PropertyChanged += OnPageChanged;
        RenderCustomColumns();
    }

    private void OnPageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AdministrationPageViewModel.CustomColumns)
            or nameof(AdministrationPageViewModel.IsDataConnected))
            RenderCustomColumns();
    }

    private void RenderCustomColumns()
    {
        while (PersonnelGrid.Columns.Count > FixedColumns)
            PersonnelGrid.Columns.RemoveAt(PersonnelGrid.Columns.Count - 1);
        if (_vm is not { IsDataConnected: true }) return;
        for (int i = 0; i < _vm.CustomColumns.Count; i++)
            PersonnelGrid.Columns.Add(new DataGridTextColumn
            {
                Header = _vm.CustomColumns[i].Caption,
                Binding = new Binding($"CustomCells[{i}]"),
                Width = new DataGridLength(125),
                IsReadOnly = true
            });
    }

    private async void OnSiteChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_vm is null || !IsLoaded || sender is not ComboBox box ||
            box.SelectedValue is not string site ||
            site == _vm.SelectedSiteId ||
            (!box.IsKeyboardFocusWithin && !box.IsDropDownOpen)) return;
        await _vm.SelectSiteAsync(site);
    }

    private async void OnStatusChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_vm is null || !IsLoaded || sender is not ComboBox box ||
            box.SelectedValue is not int status ||
            status == _vm.SelectedStatus ||
            (!box.IsKeyboardFocusWithin && !box.IsDropDownOpen)) return;
        await _vm.SelectStatusAsync(status);
    }
}
