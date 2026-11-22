using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using AlTamam.NativePresentation;

namespace AlTamam.NativeDesktop.Views;

/// <summary>
/// Presentation only. The read-only SQLCipher register is authorized in the
/// native application adapter, never by a UI button or a fabricated role.
/// </summary>
public partial class NativeAutoPreparedRegisterView : UserControl
{
    private const int StandardColumns = 8;
    private NativeAutoPreparedRegisterViewModel? _model;

    public NativeAutoPreparedRegisterView()
    {
        InitializeComponent();
        DataContextChanged += OnContextChanged;
        Loaded += (_, _) => Bind(DataContext as NativeAutoPreparedRegisterViewModel);
        Unloaded += (_, _) => Bind(null);
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
                Dispatcher.BeginInvoke(new Action(() => {
                    if (IsVisible) CloseButton.Focus();
                }), DispatcherPriority.Input);
        };
        PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape && _model is { IsOpen: true })
            {
                args.Handled = true;
                _model.Close();
            }
        };
    }

    private void OnContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        => Bind(e.NewValue as NativeAutoPreparedRegisterViewModel);

    private void Bind(NativeAutoPreparedRegisterViewModel? model)
    {
        if (ReferenceEquals(_model, model)) return;
        if (_model is not null) _model.PropertyChanged -= OnChanged;
        _model = model;
        if (_model is not null) _model.PropertyChanged += OnChanged;
        RebuildColumns();
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NativeAutoPreparedRegisterViewModel.CustomColumns)
            or nameof(NativeAutoPreparedRegisterViewModel.IsOpen))
            RebuildColumns();
    }

    private void RebuildColumns()
    {
        while (RegisterGrid.Columns.Count > StandardColumns)
            RegisterGrid.Columns.RemoveAt(RegisterGrid.Columns.Count - 1);
        if (_model is not { IsOpen: true }) return;
        var columns = _model.CustomColumns;
        for (int i = 0; i < columns.Count; i++)
            RegisterGrid.Columns.Add(new DataGridTextColumn
            {
                Header = columns[i].Caption,
                Binding = new Binding($"CustomCells[{i}]"),
                Width = new DataGridLength(135, DataGridLengthUnitType.Pixel),
                IsReadOnly = true
            });
    }
}
