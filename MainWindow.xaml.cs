using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using BacnetSim.Models;
using BacnetSim.ViewModels;

namespace BacnetSim
{
    /// <summary>
    /// Converts object type label to a badge background color.
    /// </summary>
    public class TypeToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value?.ToString() switch
            {
                "AI" => new SolidColorBrush(Color.FromRgb(37, 99, 235)),   // blue
                "AO" => new SolidColorBrush(Color.FromRgb(5, 150, 105)),   // teal
                "AV" => new SolidColorBrush(Color.FromRgb(124, 58, 237)),  // violet
                "BI" => new SolidColorBrush(Color.FromRgb(220, 38, 38)),   // red
                "BO" => new SolidColorBrush(Color.FromRgb(245, 158, 11)),  // amber
                "BV" => new SolidColorBrush(Color.FromRgb(16, 185, 129)),  // green
                _    => new SolidColorBrush(Color.FromRgb(100, 116, 139)), // gray
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    public partial class MainWindow : Window
    {
        private readonly MainViewModel _vm;

        public MainWindow()
        {
            InitializeComponent();
            _vm = new MainViewModel();
            DataContext = _vm;

            // Auto-scroll log when it updates
            _vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(_vm.LogText))
                    Dispatcher.BeginInvoke(() => LogScroll.ScrollToBottom());
            };
        }

        protected override void OnClosed(EventArgs e)
        {
            _vm.Dispose();
            base.OnClosed(e);
        }

        private void ObjectTypeComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // Let the ViewModel auto-suggest the next free instance for the chosen type
            // (ViewModel already handles this via AutoInstance in the setter, but we also trigger here)
        }

        private void DataGrid_CellEditEnding(object sender, System.Windows.Controls.DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == System.Windows.Controls.DataGridEditAction.Commit
                && e.Row.Item is BacnetPoint pt)
            {
                // Commit happens asynchronously after this event; post to dispatcher
                Dispatcher.BeginInvoke(() => _vm.OnPointValueEdited(pt));
            }
        }
    }
}