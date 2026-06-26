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

    /// <summary>
    /// Controls how an analog point's present value is generated over time.
    /// </summary>
    public enum AnalogSimulationMode
    {
        Static,     // value never changes on its own
        Random,     // value jumps to a new random number inside [SimMin, SimMax]
        Increment   // value grows by a random delta each tick, wrapping at SimMax
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
        private AnalogSimulationMode _simulationMode = AnalogSimulationMode.Static;
        private double _simMin;
        private double _simMax = 100.0;
        private double _simStepMin = 0.5;
        private double _simStepMax = 1.5;

        private static readonly Random _rng = Random.Shared;

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

        /// <summary>
        /// How this analog point's present value changes over time while the
        /// server is running. Only meaningful for analog (non-binary) points.
        /// </summary>
        public AnalogSimulationMode SimulationMode
        {
            get => _simulationMode;
            set { _simulationMode = value; OnPropertyChanged(); OnPropertyChanged(nameof(SimulationLabel)); OnPropertyChanged(nameof(IsSimulated)); }
        }

        /// <summary>Lower bound used by Random and Increment simulation.</summary>
        public double SimMin
        {
            get => _simMin;
            set { _simMin = value; OnPropertyChanged(); }
        }

        /// <summary>Upper bound used by Random and Increment simulation.</summary>
        public double SimMax
        {
            get => _simMax;
            set { _simMax = value; OnPropertyChanged(); }
        }

        /// <summary>Minimum random delta added each tick when in Increment mode.</summary>
        public double SimStepMin
        {
            get => _simStepMin;
            set { _simStepMin = value; OnPropertyChanged(); }
        }

        /// <summary>Maximum random delta added each tick when in Increment mode.</summary>
        public double SimStepMax
        {
            get => _simStepMax;
            set { _simStepMax = value; OnPropertyChanged(); }
        }

        [JsonIgnore]
        public bool IsSimulated => IsAnalog && SimulationMode != AnalogSimulationMode.Static;

        [JsonIgnore]
        public string SimulationLabel => SimulationMode switch
        {
            AnalogSimulationMode.Random    => "Random",
            AnalogSimulationMode.Increment => "Increment",
            _ => "Static"
        };

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

        /// <summary>
        /// Computes and applies the next present value according to the configured
        /// <see cref="SimulationMode"/>. Returns true if the value changed.
        /// Static points (and binary points) are left untouched.
        /// </summary>
        public bool AdvanceSimulation()
        {
            if (!IsSimulated || OutOfService)
                return false;

            double next;
            switch (SimulationMode)
            {
                case AnalogSimulationMode.Random:
                {
                    // Pick a fresh random value inside [SimMin, SimMax].
                    double lo = Math.Min(SimMin, SimMax);
                    double hi = Math.Max(SimMin, SimMax);
                    double range = hi - lo;
                    next = range <= 0 ? lo : lo + _rng.NextDouble() * range;
                    break;
                }

                case AnalogSimulationMode.Increment:
                {
                    // Continuously rising accumulator: add a random delta within
                    // [SimStepMin, SimStepMax] each tick. The value only ever rises –
                    // there is no upper clamp, so it never gets stuck or goes down.
                    double stepLo = Math.Min(Math.Abs(SimStepMin), Math.Abs(SimStepMax));
                    double stepHi = Math.Max(Math.Abs(SimStepMin), Math.Abs(SimStepMax));
                    double delta  = stepLo + _rng.NextDouble() * (stepHi - stepLo);
                    if (delta <= 0)
                        return false; // both steps are zero – nothing to do
                    next = PresentValue + delta;
                    break;
                }

                default:
                    return false;
            }

            next = Math.Round(next, 2);
            if (Math.Abs(next - PresentValue) < double.Epsilon)
                return false;

            PresentValue = next;
            return true;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
