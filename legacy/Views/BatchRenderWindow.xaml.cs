using AIRenderer.Models;
using AIRenderer.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace AIRenderer.Views
{
    public partial class BatchRenderWindow : Window
    {
        private readonly BatchRenderViewModel _vm;

        public BatchRenderWindow()
        {
            InitializeComponent();
            _vm = new BatchRenderViewModel();
            DataContext = _vm;
        }

        private void CaptureOneButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is ViewRenderItem item)
                _vm.CaptureItem(item);
        }

        private void SaveOneButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is ViewRenderItem item)
                _vm.SaveItem(item);
        }
    }
}
