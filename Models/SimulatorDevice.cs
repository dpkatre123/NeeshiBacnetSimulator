using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BacnetSim.Models
{
    public class SimulatorDevice : INotifyPropertyChanged
    {
        private uint _deviceInstance = 1234;
        private string _deviceName = "NeeshiBacnetSim";
        private string _vendorName = "Riddhi Technologies";
        private string _modelName = "NeeshiBacnetSim v1.0";
        private string _networkInterface = "0.0.0.0";
        private int _port = 47808;

        public uint DeviceInstance
        {
            get => _deviceInstance;
            set { _deviceInstance = value; OnPropertyChanged(); }
        }

        public string DeviceName
        {
            get => _deviceName;
            set { _deviceName = value; OnPropertyChanged(); }
        }

        public string VendorName
        {
            get => _vendorName;
            set { _vendorName = value; OnPropertyChanged(); }
        }

        public string ModelName
        {
            get => _modelName;
            set { _modelName = value; OnPropertyChanged(); }
        }

        public string NetworkInterface
        {
            get => _networkInterface;
            set { _networkInterface = value; OnPropertyChanged(); }
        }

        public int Port
        {
            get => _port;
            set { _port = value; OnPropertyChanged(); }
        }

        public ObservableCollection<BacnetPoint> Points { get; set; } = [];

        // Schedules available on the virtual device (not yet part of original project)
        public System.Collections.ObjectModel.ObservableCollection<BacnetSchedule> Schedules { get; set; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
