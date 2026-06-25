using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using BacnetSim.Models;
using BacnetSim.Services;

namespace BacnetSim.ViewModels
{
    public class RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) : ICommand
    {
        private readonly Action<object?> _execute = execute;
        private readonly Func<object?, bool>? _canExecute = canExecute;

        public event EventHandler? CanExecuteChanged
        {
            add    => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? p) => _canExecute?.Invoke(p) ?? true;
        public void Execute(object? p)    => _execute(p);
    }

    public class MainViewModel : INotifyPropertyChanged, IDisposable
    {
        // ── backing fields ────────────────────────────────────────────────
        private readonly BacnetService _service = new();
        private bool _isRunning;
        private string _statusText = "Stopped";
        private string _logText = string.Empty;
        private BacnetPoint? _selectedPoint;

        // ── form fields for new-point entry ───────────────────────────────
        private string _newName = "Analog Input";
        private BacnetObjectType _newType = BacnetObjectType.AnalogInput;
        private uint _newInstance;
        private double _newValue = 0.0;
        private string _newUnits = "°C";
        private string _newDescription = "Temperature Sensor";

        // ── persistence path ──────────────────────────────────────────────
        private static readonly string SavePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BacnetSim", "points.json");

        // ─────────────────────────────────────────────────────────────────
        public SimulatorDevice Device { get; } = new();

        public ObservableCollection<BacnetPoint> Points => Device.Points;

        public bool IsRunning
        {
            get => _isRunning;
            private set { _isRunning = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotRunning)); OnPropertyChanged(nameof(StatusLed)); }
        }

        public bool IsNotRunning => !_isRunning;

        public string StatusText
        {
            get => _statusText;
            private set { _statusText = value; OnPropertyChanged(); }
        }

        public string StatusLed => _isRunning ? "🟢" : "🔴";

        public string LogText
        {
            get => _logText;
            private set { _logText = value; OnPropertyChanged(); }
        }

        public BacnetPoint? SelectedPoint
        {
            get => _selectedPoint;
            set { _selectedPoint = value; OnPropertyChanged(); }
        }

        // ── new-point form bindings ───────────────────────────────────────
        public string NewName
        {
            get => _newName;
            set { _newName = value; OnPropertyChanged(); }
        }

        public BacnetObjectType NewType
        {
            get => _newType;
            set
            {
                _newType = value;
                OnPropertyChanged();
                ApplyTypeDefaults();
                AutoInstance();
            }
        }

        public uint NewInstance
        {
            get => _newInstance;
            set { _newInstance = value; OnPropertyChanged(); }
        }

        public double NewValue
        {
            get => _newValue;
            set { _newValue = value; OnPropertyChanged(); }
        }

        public string NewUnits
        {
            get => _newUnits;
            set { _newUnits = value; OnPropertyChanged(); }
        }

        public string NewDescription
        {
            get => _newDescription;
            set { _newDescription = value; OnPropertyChanged(); }
        }

        public IEnumerable<BacnetObjectType> ObjectTypes => Enum.GetValues<BacnetObjectType>();

        // ── commands ──────────────────────────────────────────────────────
        public ICommand StartCommand   { get; }
        public ICommand StopCommand    { get; }
        public ICommand AddPointCommand    { get; }
        public ICommand RemovePointCommand { get; }
        public ICommand ClearLogCommand    { get; }
        public ICommand SaveCommand        { get; }
        public ICommand LoadCommand        { get; }

        // ─────────────────────────────────────────────────────────────────
        public MainViewModel()
        {
            StartCommand   = new RelayCommand(_ => Start(),   _ => !IsRunning);
            StopCommand    = new RelayCommand(_ => Stop(),    _ => IsRunning);
            AddPointCommand    = new RelayCommand(_ => AddPoint());
            RemovePointCommand = new RelayCommand(p => RemovePoint(p as BacnetPoint), _ => SelectedPoint != null);
            ClearLogCommand    = new RelayCommand(_ => LogText = string.Empty);
            SaveCommand        = new RelayCommand(_ => SavePoints());
            LoadCommand        = new RelayCommand(_ => LoadPoints());

            _service.LogMessage       += AppendLog;
            _service.PointValueChanged += pt => { /* value already updated by service on Dispatcher */ };

            // Auto-load saved points
            LoadPoints();

            // Auto-suggest next instance
            AutoInstance();
        }

        // ── server control ────────────────────────────────────────────────
        private void Start()
        {
            try
            {
                _service.Start(Device);
                IsRunning  = true;
                StatusText = $"Running · Device {Device.DeviceInstance} · Port {Device.Port}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to start BACnet server:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                AppendLog($"[ERROR] {ex.Message}");
            }
        }

        private void Stop()
        {
            _service.Stop();
            IsRunning  = false;
            StatusText = "Stopped";
        }

        // ── point management ──────────────────────────────────────────────
        private void AddPoint()
        {
            if (string.IsNullOrWhiteSpace(NewName))
            {
                MessageBox.Show("Please enter a point name.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Check duplicate instance for same type
            if (Points.Any(p => p.ObjectType == NewType && p.Instance == NewInstance))
            {
                MessageBox.Show($"A {NewType} with instance {NewInstance} already exists.", "Duplicate",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var pt = new BacnetPoint
            {
                Name         = NewName,
                ObjectType   = NewType,
                Instance     = NewInstance,
                PresentValue = NewValue,
                Units        = NewUnits,
                Description  = NewDescription
            };

            Points.Add(pt);

            // If server is running, notify service of the new point (server must be restarted to pick up new objects)
            if (_isRunning)
            {
                AppendLog("⚠ Stop and restart the server to expose newly added points to the network.");
            }

            AutoInstance();
            AppendLog($"Added: {pt.TypeLabel}:{pt.Instance} '{pt.Name}'");
        }

        private void RemovePoint(BacnetPoint? pt)
        {
            if (pt == null) return;
            Points.Remove(pt);
            AppendLog($"Removed: {pt.TypeLabel}:{pt.Instance} '{pt.Name}'");
            if (_isRunning)
                AppendLog("⚠ Restart the server to reflect removed points.");
        }

        // Called when a present-value cell is edited in the DataGrid
        public void OnPointValueEdited(BacnetPoint pt)
        {
            if (_isRunning)
                _service.UpdatePointValue(pt);
        }

        // ── persistence ───────────────────────────────────────────────────
        private void SavePoints()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SavePath)!);
                var json = JsonSerializer.Serialize(Points.ToList(),
                    new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SavePath, json);
                AppendLog($"Saved {Points.Count} points → {SavePath}");
            }
            catch (Exception ex)
            {
                AppendLog($"[ERROR] Save failed: {ex.Message}");
            }
        }

        private void LoadPoints()
        {
            if (!File.Exists(SavePath)) return;
            try
            {
                var json = File.ReadAllText(SavePath);
                var list = JsonSerializer.Deserialize<List<BacnetPoint>>(json);
                if (list == null) return;
                Points.Clear();
                foreach (var pt in list) Points.Add(pt);
                AppendLog($"Loaded {Points.Count} points from {SavePath}");
            }
            catch (Exception ex)
            {
                AppendLog($"[ERROR] Load failed: {ex.Message}");
            }
        }

        // ── helpers ───────────────────────────────────────────────────────
        private void ApplyTypeDefaults()
        {
            switch (_newType)
            {
                case BacnetObjectType.AnalogInput:
                    NewName        = "Analog Input";
                    NewValue       = 0.0;
                    NewUnits       = "°C";
                    NewDescription = "Temperature Sensor";
                    break;
                case BacnetObjectType.AnalogOutput:
                    NewName        = "Analog Output";
                    NewValue       = 0.0;
                    NewUnits       = "%";
                    NewDescription = "Output Control";
                    break;
                case BacnetObjectType.AnalogValue:
                    NewName        = "Analog Value";
                    NewValue       = 21.0;
                    NewUnits       = "°C";
                    NewDescription = "Setpoint";
                    break;
                case BacnetObjectType.BinaryInput:
                    NewName        = "Binary Input";
                    NewValue       = 0;
                    NewUnits       = string.Empty;
                    NewDescription = "Digital Input";
                    break;
                case BacnetObjectType.BinaryOutput:
                    NewName        = "Binary Output";
                    NewValue       = 0;
                    NewUnits       = string.Empty;
                    NewDescription = "Relay Output";
                    break;
                case BacnetObjectType.BinaryValue:
                    NewName        = "Binary Value";
                    NewValue       = 0;
                    NewUnits       = string.Empty;
                    NewDescription = "Binary Flag";
                    break;
            }
        }

        private void AutoInstance()
        {
            // Suggest the next unused instance for the current type
            var used = Points.Where(p => p.ObjectType == NewType).Select(p => p.Instance).ToHashSet();
            uint next = 0;
            while (used.Contains(next)) next++;
            NewInstance = next;
        }

        private void AppendLog(string msg)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                var lines = LogText.Split('\n');
                // Keep last 300 lines
                if (lines.Length > 300)
                    LogText = string.Join('\n', lines.TakeLast(300));
                LogText += msg + "\n";
            });
        }

        // ─────────────────────────────────────────────────────────────────
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public void Dispose()
        {
            SavePoints();
            _service.Dispose();
        }
    }
}
