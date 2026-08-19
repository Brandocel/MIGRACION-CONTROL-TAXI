using System.Windows;
using ControlTaxiDesktop.Models;

namespace ControlTaxiDesktop;

public partial class RemissionProductsWindow : Window
{
    public RemissionProductsWindow(IEnumerable<LocalSaleLine> lines)
    {
        InitializeComponent();
        LinesGrid.ItemsSource = lines;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
