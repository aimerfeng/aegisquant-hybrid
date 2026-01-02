using System.ComponentModel;
using System.Windows;
using AegisQuant.UI.ViewModels;

namespace AegisQuant.UI.Views;

/// <summary>
/// Interaction logic for OptimizationWindow.xaml
/// </summary>
public partial class OptimizationWindow : Window
{
    public OptimizationWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (DataContext is OptimizationViewModel vm)
        {
            vm.Dispose();
        }
        base.OnClosing(e);
    }
}
