using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BacnetSim.Models
{
    /// <summary>
    /// Simple schedule model for controlling a BACnet point over time.
    /// Includes a small helper to produce a sample schedule instance.
    /// </summary>
    public class BacnetSchedule
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Collection of schedule entries. Evaluated in order.
        /// </summary>
        public List<ScheduleEntry> Entries { get; set; } = new();

        [JsonIgnore]
        public string Display => $"{Name} ({Entries.Count} entries)";

        /// <summary>
        /// Creates a sample schedule suitable for demos: weekday daytime setpoint + night setpoint.
        /// </summary>
        public static BacnetSchedule Sample()
        {
            return new BacnetSchedule
            {
                Name = "Office Temperature Schedule",
                Enabled = true,
                Entries = new List<ScheduleEntry>
                {
                    new ScheduleEntry
                    {
                        Name = "Weekday Daytime",
                        Days = DayFlags.Monday | DayFlags.Tuesday | DayFlags.Wednesday | DayFlags.Thursday | DayFlags.Friday,
                        Start = TimeSpan.FromHours(8),
                        End = TimeSpan.FromHours(18),
                        Value = 22.5
                    },
                    new ScheduleEntry
                    {
                        Name = "Weekday Night",
                        Days = DayFlags.Monday | DayFlags.Tuesday | DayFlags.Wednesday | DayFlags.Thursday | DayFlags.Friday,
                        Start = TimeSpan.FromHours(18),
                        End = TimeSpan.FromHours(8),
                        Value = 18.0
                    },
                    new ScheduleEntry
                    {
                        Name = "Weekend",
                        Days = DayFlags.Saturday | DayFlags.Sunday,
                        Start = TimeSpan.Zero,
                        End = TimeSpan.FromHours(24),
                        Value = 16.0
                    }
                }
            };
        }

        public class ScheduleEntry
        {
            public string Name { get; set; } = string.Empty;

            /// <summary>Bit flags for days this entry applies to.</summary>
            public DayFlags Days { get; set; } = DayFlags.Everyday;

            /// <summary>Start time (inclusive) within a day.</summary>
            public TimeSpan Start { get; set; }

            /// <summary>End time (exclusive) within a day. If End &lt;= Start it is treated as wrapping to next day.</summary>
            public TimeSpan End { get; set; }

            /// <summary>Value to apply while this entry is active (e.g. setpoint or binary value).</summary>
            public double Value { get; set; }

            [JsonIgnore]
            public string TimeRange => $"{Start:hh\\:mm} - {End:hh\\:mm}";
        }

        [Flags]
        public enum DayFlags : byte
        {
            None = 0,
            Sunday = 1 << 0,
            Monday = 1 << 1,
            Tuesday = 1 << 2,
            Wednesday = 1 << 3,
            Thursday = 1 << 4,
            Friday = 1 << 5,
            Saturday = 1 << 6,
            Everyday = Sunday | Monday | Tuesday | Wednesday | Thursday | Friday | Saturday
        }
    }
}
