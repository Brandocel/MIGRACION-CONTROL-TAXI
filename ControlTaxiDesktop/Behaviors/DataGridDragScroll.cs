using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace ControlTaxiDesktop.Behaviors;

/// <summary>
/// Permite recorrer una tabla arrastrando con el boton izquierdo del mouse, como en un mapa:
/// se mantiene presionado sobre las filas y se mueve hacia abajo o hacia los lados, sin tener
/// que atinarle a las barras de scroll.
///
/// El clic simple sigue funcionando igual. Para lograrlo, el clic se "retiene" en el
/// MouseDown y la seleccion de la fila se aplica al soltar, solo si el puntero no se movio mas
/// alla del umbral de arrastre. Si se movio, fue un arrastre y no se selecciona nada.
///
/// Los controles interactivos que viven dentro de la tabla (el checkbox de seleccion y el boton
/// de Ticket) conservan su clic normal, igual que el doble clic sobre una fila.
///
/// Uso en XAML:
///   xmlns:behaviors="clr-namespace:ControlTaxiDesktop.Behaviors"
///   &lt;DataGrid behaviors:DataGridDragScroll.Enabled="True" ... /&gt;
/// </summary>
public static class DataGridDragScroll
{
    /// <summary>Pixeles que hay que mover antes de considerar que es un arrastre y no un clic.</summary>
    private const double DragThreshold = 6d;

    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached(
            "Enabled",
            typeof(bool),
            typeof(DataGridDragScroll),
            new PropertyMetadata(false, OnEnabledChanged));

    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);

    private static readonly DependencyProperty StateProperty =
        DependencyProperty.RegisterAttached("State", typeof(DragState), typeof(DataGridDragScroll), new PropertyMetadata(null));

    private sealed class DragState
    {
        public Point Origin;
        public double OriginHorizontal;
        public double OriginVertical;
        public bool Armed;
        public bool Dragging;
        public ScrollViewer? Scroll;
    }

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid grid) return;

        grid.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
        grid.PreviewMouseMove -= OnPreviewMouseMove;
        grid.PreviewMouseLeftButtonUp -= OnPreviewMouseLeftButtonUp;

        if (e.NewValue is true)
        {
            grid.SetValue(StateProperty, new DragState());
            grid.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
            grid.PreviewMouseMove += OnPreviewMouseMove;
            grid.PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;
        }
        else
        {
            grid.ClearValue(StateProperty);
        }
    }

    private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid || grid.GetValue(StateProperty) is not DragState state) return;

        state.Armed = false;
        state.Dragging = false;

        // El doble clic abre el detalle de la venta: se deja pasar tal cual.
        if (e.ClickCount > 1) return;

        // Checkbox de seleccion y boton de Ticket conservan su clic normal.
        if (FindAncestor<ButtonBase>(e.OriginalSource as DependencyObject) is not null) return;

        state.Scroll ??= FindScrollViewer(grid);
        if (state.Scroll is null) return;

        state.Origin = e.GetPosition(grid);
        state.OriginHorizontal = state.Scroll.HorizontalOffset;
        state.OriginVertical = state.Scroll.VerticalOffset;
        state.Armed = true;

        // Se retiene el clic para que arrastrar no cambie la seleccion. Si resulta ser un clic
        // simple, la seleccion se aplica al soltar (ver OnPreviewMouseLeftButtonUp).
        e.Handled = true;
    }

    private static void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not DataGrid grid || grid.GetValue(StateProperty) is not DragState state) return;
        if (!state.Armed || state.Scroll is null) return;

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndDrag(grid, state);
            return;
        }

        var current = e.GetPosition(grid);
        var deltaX = current.X - state.Origin.X;
        var deltaY = current.Y - state.Origin.Y;

        if (!state.Dragging)
        {
            if (Math.Abs(deltaX) < DragThreshold && Math.Abs(deltaY) < DragThreshold) return;
            state.Dragging = true;
            grid.CaptureMouse();
            grid.Cursor = Cursors.ScrollAll;
        }

        state.Scroll.ScrollToHorizontalOffset(state.OriginHorizontal - deltaX);
        state.Scroll.ScrollToVerticalOffset(state.OriginVertical - deltaY);
        e.Handled = true;
    }

    private static void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid || grid.GetValue(StateProperty) is not DragState state) return;
        if (!state.Armed) return;

        var wasDragging = state.Dragging;
        EndDrag(grid, state);

        if (wasDragging)
        {
            e.Handled = true;
            return;
        }

        // Fue un clic simple: se aplica la seleccion que se retuvo en el MouseDown.
        if (FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject) is not { } row) return;
        grid.SelectedItem = row.Item;
        if (FindAncestor<DataGridCell>(e.OriginalSource as DependencyObject) is { } cell)
            grid.CurrentCell = new DataGridCellInfo(cell);
        grid.Focus();
    }

    private static void EndDrag(DataGrid grid, DragState state)
    {
        if (state.Dragging)
        {
            grid.ReleaseMouseCapture();
            grid.Cursor = null;
        }
        state.Armed = false;
        state.Dragging = false;
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match) return match;
            source = source is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }
        return null;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer viewer) return viewer;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            if (FindScrollViewer(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
        }
        return null;
    }
}
