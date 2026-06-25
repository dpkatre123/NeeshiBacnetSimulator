using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace BacnetSim.Models
{
    public enum BacnetObjectType
    {
        AnalogInput,
        AnalogOutput,
        AnalogValue,
        BinaryInput,
        BinaryOutput,
        BinaryValue
    }

    public class BacnetPoint : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private BacnetObjectType _objectType;
        private uint _instance;
        private double _presentValue;
        private string _units = string.Empty;
        private string _description = string.Empty;
        private bool _outOfService;

        public Guid Id { get; set; } = Guid.NewGuid();

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public BacnetObjectType ObjectType
        {
            get => _objectType;
            set { _objectType = value; OnPropertyChanged(); OnPropertyChanged(nameof(TypeLabel)); OnPropertyChanged(nameof(IsBinary)); OnPropertyChanged(nameof(IsAnalog)); }
        }

        public uint Instance
        {
            get => _instance;
            set { _instance = value; OnPropertyChanged(); }
        }

        public double PresentValue
        {
            get => _presentValue;
            set { _presentValue = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayValue)); }
        }

        public string Units
        {
            get => _units;
            set { _units = value; OnPropertyChanged(); }
        }

        public string Description
        {
            get => _description;
            set { _description = value; OnPropertyChanged(); }
        }

        public bool OutOfService
        {
            get => _outOfService;
            set { _outOfService = value; OnPropertyChanged(); }
        }

        [JsonIgnore]
        public string TypeLabel => ObjectType switch
        {
            BacnetObjectType.AnalogInput   => "AI",
            BacnetObjectType.AnalogOutput  => "AO",
            BacnetObjectType.AnalogValue   => "AV",
            BacnetObjectType.BinaryInput   => "BI",
            BacnetObjectType.BinaryOutput  => "BO",
            BacnetObjectType.BinaryValue   => "BV",
            _ => "??"
        };

        [JsonIgnore]
        public bool IsBinary => ObjectType is BacnetObjectType.BinaryInput
                                            or BacnetObjectType.BinaryOutput
                                            or BacnetObjectType.BinaryValue;

        [JsonIgnore]
        public bool IsAnalog => !IsBinary;

        [JsonIgnore]
        public string DisplayValue => IsBinary
            ? (PresentValue > 0 ? "Active" : "Inactive")
            : $"{PresentValue:F2} {Units}".Trim();

        [JsonIgnore]
        public string BacnetObjectIdentifier => $"{TypeLabel}:{Instance}";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
