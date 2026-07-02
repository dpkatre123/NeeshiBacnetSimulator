using System.IO;
using System.IO.BACnet;
using System.IO.BACnet.Storage;
using System.Windows;
using BacnetSim.Models;

namespace BacnetSim.Services
{
    /// <summary>
    /// BACnet/IP server that wraps DeviceStorage + BacnetClient.
    /// Handles Who-Is, ReadProperty, ReadPropertyMultiple, and WriteProperty.
    /// All event handlers are wrapped in try-catch so a bad request never
    /// causes a silent timeout – we always send either a value or an error response.
    /// </summary>
    public class BacnetService : IDisposable
    {
        // ── private fields ─────────────────────────────────────────────────
        private BacnetClient? _client;
        private DeviceStorage? _storage;
        private readonly object _lock = new();
        private bool _running;
        private string? _tmpXmlPath;

        // ── public state ───────────────────────────────────────────────────
        public bool IsRunning => _running;
        public event Action<string>? LogMessage;
        public event Action<BacnetPoint>? PointValueChanged;

        // Maps BacnetObjectId → BacnetPoint for write-back from network
        private readonly Dictionary<BacnetObjectId, BacnetPoint> _pointMap = [];

        // ──────────────────────────────────────────────────────────────────
        public void Start(SimulatorDevice device)
        {
            if (_running) return;
            _pointMap.Clear();

            try
            {
                // 1. Write XML to a temp file (DeviceStorage.Load only accepts file path)
                _tmpXmlPath = Path.Combine(Path.GetTempPath(), $"bacnet_sim_{device.DeviceInstance}.xml");
                var xml = BuildXml(device, includeSchedules: true);
                File.WriteAllText(_tmpXmlPath, xml);
                Log($"Storage XML written to: {_tmpXmlPath}");

                // 2. Load storage from file (try with schedules first; on failure try without schedules)
                try
                {
                    _storage = DeviceStorage.Load(_tmpXmlPath, device.DeviceInstance);
                    Log($"Storage loaded. Device {_storage?.DeviceId}");
                }
                catch (Exception storageEx)
                {
                    Log($"[ERROR] Storage load failed (with schedules): {storageEx}");
                    _storage = null;

                    // Attempt a fallback: rebuild XML without schedules and try again so points remain discoverable
                    if (device.Schedules != null && device.Schedules.Count > 0)
                    {
                        try
                        {
                            var tmpNoSched = Path.Combine(Path.GetTempPath(), $"bacnet_sim_{device.DeviceInstance}_nosched.xml");
                            var xml2 = BuildXml(device, includeSchedules: false);
                            File.WriteAllText(tmpNoSched, xml2);
                            Log($"Attempting fallback storage (no schedules) written to: {tmpNoSched}");
                            _storage = DeviceStorage.Load(tmpNoSched, device.DeviceInstance);
                            _tmpXmlPath = tmpNoSched; // replace path so we keep the working storage file
                            Log($"Fallback storage loaded (no schedules). Device {_storage?.DeviceId}");
                        }
                        catch (Exception fallbackEx)
                        {
                            Log($"[ERROR] Fallback storage load failed: {fallbackEx}");
                            _storage = null;
                        }
                    }
                }

                // If storage failed to load we must not start the BACnet server because
                // incoming ReadProperty requests require a valid DeviceStorage instance.
                if (_storage == null)
                {
                    Log("[ERROR] DeviceStorage is null after load; aborting start.");
                    Stop();
                    return;
                }

                // 3. Register point map for write-back
                foreach (var pt in device.Points)
                    _pointMap[ToObjectId(pt)] = pt;

                // 4. Start BACnet/IP UDP server
                var transport = new BacnetIpUdpProtocolTransport(device.Port, false);
                _client = new BacnetClient(transport);

                _client.OnWhoIs                       += OnWhoIs;
                _client.OnReadPropertyRequest         += OnReadPropertyRequest;
                _client.OnWritePropertyRequest        += OnWritePropertyRequest;
                _client.OnReadPropertyMultipleRequest += OnReadPropertyMultipleRequest;

                _client.Start();
                _running = true;
                Log($"Started BACnet/IP · Device {device.DeviceInstance} · Port {device.Port}");

                // Announce ourselves on the network so clients discover us immediately
                _client.Iam(device.DeviceInstance, BacnetSegmentations.SEGMENTATION_NONE);
                Log("I-Am broadcast sent.");
            }
            catch (Exception ex)
            {
                Log($"[ERROR] Start failed: {ex.Message}");
                Stop();
            }
        }

        public void Stop()
        {
            if (!_running) return;
            _client?.Dispose();
            _client = null;
            _storage = null;
            _pointMap.Clear();
            _running = false;

            if (_tmpXmlPath != null && File.Exists(_tmpXmlPath))
                try { File.Delete(_tmpXmlPath); } catch { /* ignore */ }
            _tmpXmlPath = null;

            Log("Server stopped.");
        }

        /// <summary>Called by the ViewModel when a user edits a present value in the grid.</summary>
        public void UpdatePointValue(BacnetPoint point)
        {
            if (_storage == null) return;
            var objId  = ToObjectId(point);
            var propId = BacnetPropertyIds.PROP_PRESENT_VALUE;
            lock (_lock)
            {
                try
                {
                    _storage.WriteProperty(objId, propId, uint.MaxValue, [ToValue(point)], false);
                }
                catch (Exception ex)
                {
                    Log($"[ERROR] UpdatePointValue: {ex.Message}");
                }
            }
        }

        // ──────────────────────────────────────────────────────────────────
        #region XML Storage Builder

        private static string BuildXml(SimulatorDevice device, bool includeSchedules = true)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\"?>");
            sb.AppendLine("<DeviceStorage xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\">");
            sb.AppendLine("  <Objects>");

            // ── Device object ──────────────────────────────────────────────
            sb.AppendLine($"    <Object Type=\"OBJECT_DEVICE\" Instance=\"{device.DeviceInstance}\">");
            sb.AppendLine("      <Properties>");
            AppendProp(sb, "PROP_OBJECT_IDENTIFIER", "BACNET_APPLICATION_TAG_OBJECT_ID",
                       $"OBJECT_DEVICE:{device.DeviceInstance}");

            // OBJECT_LIST: device + all points (required by explorers for discovery)
            sb.AppendLine("        <Property Id=\"PROP_OBJECT_LIST\" Tag=\"BACNET_APPLICATION_TAG_OBJECT_ID\">");
            sb.AppendLine($"          <Value>OBJECT_DEVICE:{device.DeviceInstance}</Value>");
            foreach (var pt in device.Points)
                sb.AppendLine($"          <Value>{XmlObjType(pt.ObjectType)}:{pt.Instance}</Value>");
            sb.AppendLine("        </Property>");

            AppendProp(sb, "PROP_OBJECT_NAME",           "BACNET_APPLICATION_TAG_CHARACTER_STRING", Esc(device.DeviceName));
            AppendProp(sb, "PROP_OBJECT_TYPE",           "BACNET_APPLICATION_TAG_ENUMERATED",       "8");
            AppendProp(sb, "PROP_VENDOR_NAME",           "BACNET_APPLICATION_TAG_CHARACTER_STRING", Esc(device.VendorName));
            AppendProp(sb, "PROP_MODEL_NAME",            "BACNET_APPLICATION_TAG_CHARACTER_STRING", Esc(device.ModelName));
            AppendProp(sb, "PROP_VENDOR_IDENTIFIER",     "BACNET_APPLICATION_TAG_UNSIGNED_INT",     "0");
            AppendProp(sb, "PROP_APPLICATION_SOFTWARE_VERSION", "BACNET_APPLICATION_TAG_CHARACTER_STRING", "1.0");
            AppendProp(sb, "PROP_FIRMWARE_REVISION",    "BACNET_APPLICATION_TAG_CHARACTER_STRING", "1.0");
            AppendProp(sb, "PROP_PROTOCOL_VERSION",      "BACNET_APPLICATION_TAG_UNSIGNED_INT",     "1");
            AppendProp(sb, "PROP_PROTOCOL_REVISION",     "BACNET_APPLICATION_TAG_UNSIGNED_INT",     "14");
            AppendProp(sb, "PROP_APDU_TIMEOUT",          "BACNET_APPLICATION_TAG_UNSIGNED_INT",     "6000");
            AppendProp(sb, "PROP_NUMBER_OF_APDU_RETRIES","BACNET_APPLICATION_TAG_UNSIGNED_INT",     "3");
            AppendProp(sb, "PROP_MAX_APDU_LENGTH_ACCEPTED","BACNET_APPLICATION_TAG_UNSIGNED_INT",   "1476");
            AppendProp(sb, "PROP_SEGMENTATION_SUPPORTED","BACNET_APPLICATION_TAG_ENUMERATED",       "3"); // NO_SEGMENTATION
            AppendProp(sb, "PROP_SYSTEM_STATUS",         "BACNET_APPLICATION_TAG_ENUMERATED",       "0"); // OPERATIONAL
            AppendProp(sb, "PROP_PROTOCOL_SERVICES_SUPPORTED", "BACNET_APPLICATION_TAG_BIT_STRING", "00000000000000000000000000000000");

            sb.AppendLine("      </Properties>");
            sb.AppendLine("    </Object>");

            // ── Point objects ──────────────────────────────────────────────
            foreach (var pt in device.Points)
                AppendPointXml(sb, pt);

            // ── Schedule objects ───────────────────────────────────────────
            if (includeSchedules && device.Schedules != null)
            {
                uint schedBase = 1000;
                for (int i = 0; i < device.Schedules.Count; i++)
                {
                    AppendScheduleXml(sb, device.Schedules[i], schedBase + (uint)i);
                }
            }

            sb.AppendLine("  </Objects>");
            sb.AppendLine("</DeviceStorage>");
            return sb.ToString();
        }

        private static void AppendScheduleXml(System.Text.StringBuilder sb, BacnetSchedule sched, uint instance)
        {
            sb.AppendLine($"    <Object Type=\"OBJECT_SCHEDULE\" Instance=\"{instance}\">");
            sb.AppendLine("      <Properties>");
            sb.AppendLine("        <Property Id=\"PROP_PROPERTY_LIST\" Tag=\"BACNET_APPLICATION_TAG_OBJECT_ID\">");
            sb.AppendLine("          <Value>PROP_OBJECT_IDENTIFIER</Value>");
            sb.AppendLine("          <Value>PROP_OBJECT_NAME</Value>");
            sb.AppendLine("          <Value>PROP_OBJECT_TYPE</Value>");
            sb.AppendLine("          <Value>PROP_DESCRIPTION</Value>");
            sb.AppendLine("          <Value>PROP_EFFECTIVE_PERIOD</Value>");
            sb.AppendLine("          <Value>PROP_WEEKLY_SCHEDULE</Value>");
            sb.AppendLine("        </Property>");

            AppendProp(sb, "PROP_OBJECT_IDENTIFIER", "BACNET_APPLICATION_TAG_OBJECT_ID", $"OBJECT_SCHEDULE:{instance}");
            AppendProp(sb, "PROP_OBJECT_NAME", "BACNET_APPLICATION_TAG_CHARACTER_STRING", Esc(sched.Name));
            AppendProp(sb, "PROP_DESCRIPTION", "BACNET_APPLICATION_TAG_CHARACTER_STRING", Esc(sched.Name ?? string.Empty));

            // Minimal schedule representation: expose number of entries via description or custom property
            AppendProp(sb, "PROP_SCHEDULE_ENTRY_COUNT", "BACNET_APPLICATION_TAG_UNSIGNED_INT", sched.Entries?.Count.ToString() ?? "0");

            sb.AppendLine("      </Properties>");
            sb.AppendLine("    </Object>");
        }

        private static void AppendPointXml(System.Text.StringBuilder sb, BacnetPoint pt)
        {
            var xmlType = XmlObjType(pt.ObjectType);
            sb.AppendLine($"    <Object Type=\"{xmlType}\" Instance=\"{pt.Instance}\">");
            sb.AppendLine("      <Properties>");

            // PROP_PROPERTY_LIST: List of all properties (required by explorers)
            sb.AppendLine("        <Property Id=\"PROP_PROPERTY_LIST\" Tag=\"BACNET_APPLICATION_TAG_OBJECT_ID\">");
            sb.AppendLine("          <Value>PROP_OBJECT_IDENTIFIER</Value>");
            sb.AppendLine("          <Value>PROP_OBJECT_NAME</Value>");
            sb.AppendLine("          <Value>PROP_OBJECT_TYPE</Value>");
            sb.AppendLine("          <Value>PROP_DESCRIPTION</Value>");
            sb.AppendLine("          <Value>PROP_PRESENT_VALUE</Value>");
            sb.AppendLine("          <Value>PROP_OUT_OF_SERVICE</Value>");
            sb.AppendLine("          <Value>PROP_STATUS_FLAGS</Value>");
            sb.AppendLine("          <Value>PROP_EVENT_STATE</Value>");
            if (!pt.IsBinary)
            {
                sb.AppendLine("          <Value>PROP_UNITS</Value>");
                sb.AppendLine("          <Value>PROP_POLARITY</Value>");
            }
            sb.AppendLine("        </Property>");

            AppendProp(sb, "PROP_OBJECT_IDENTIFIER", "BACNET_APPLICATION_TAG_OBJECT_ID",         $"{xmlType}:{pt.Instance}");
            AppendProp(sb, "PROP_OBJECT_NAME",        "BACNET_APPLICATION_TAG_CHARACTER_STRING",  Esc(pt.Name));
            AppendProp(sb, "PROP_OBJECT_TYPE",        "BACNET_APPLICATION_TAG_ENUMERATED",        ((int)ToBacnetObjType(pt.ObjectType)).ToString());
            AppendProp(sb, "PROP_DESCRIPTION",        "BACNET_APPLICATION_TAG_CHARACTER_STRING",  Esc(pt.Description));

            if (pt.IsBinary)
            {
                AppendProp(sb, "PROP_PRESENT_VALUE", "BACNET_APPLICATION_TAG_ENUMERATED", pt.PresentValue > 0 ? "1" : "0");
                AppendProp(sb, "PROP_POLARITY",      "BACNET_APPLICATION_TAG_ENUMERATED", "0"); // NORMAL
            }
            else
            {
                var v = ((float)pt.PresentValue).ToString(System.Globalization.CultureInfo.InvariantCulture);
                AppendProp(sb, "PROP_PRESENT_VALUE", "BACNET_APPLICATION_TAG_REAL", v);
                // Map unit string to BACnet unit enumeration
                var unitCode = GetBacnetUnitCode(pt.Units);
                AppendProp(sb, "PROP_UNITS",         "BACNET_APPLICATION_TAG_ENUMERATED", unitCode);
            }

            AppendProp(sb, "PROP_OUT_OF_SERVICE", "BACNET_APPLICATION_TAG_BOOLEAN",    pt.OutOfService ? "True" : "False");
            AppendProp(sb, "PROP_STATUS_FLAGS",   "BACNET_APPLICATION_TAG_BIT_STRING", "0000");

            // EVENT_STATE required by many explorers
            AppendProp(sb, "PROP_EVENT_STATE", "BACNET_APPLICATION_TAG_ENUMERATED", "0"); // NORMAL

            sb.AppendLine("      </Properties>");
            sb.AppendLine("    </Object>");
        }

        private static void AppendProp(System.Text.StringBuilder sb, string id, string tag, string value)
        {
            sb.AppendLine($"        <Property Id=\"{id}\" Tag=\"{tag}\">");
            sb.AppendLine($"          <Value>{value}</Value>");
            sb.AppendLine("        </Property>");
        }

        private static string Esc(string s) => System.Security.SecurityElement.Escape(s) ?? s;

        /// <summary>
        /// Maps common unit strings to BACnet unit enumeration codes.
        /// Reference: ASHRAE 135 BACnet Unit Codes
        /// </summary>
        private static string GetBacnetUnitCode(string unitString)
        {
            return unitString?.Trim().ToLowerInvariant() switch
            {
                "°c" or "c" or "celsius" => "0",      // UNITS_DEGREES_CELSIUS
                "°f" or "f" or "fahrenheit" => "1",   // UNITS_DEGREES_FAHRENHEIT
                "k" or "kelvin" => "2",                // UNITS_DEGREES_KELVIN
                "%" or "percent" => "5",               // UNITS_PERCENT_RELATIVE_HUMIDITY
                "ppm" => "6",                          // UNITS_PARTS_PER_MILLION
                "rpm" => "7",                          // UNITS_REVOLUTIONS_PER_MINUTE
                "hz" or "hertz" => "8",                // UNITS_HERTZ
                "v" or "volt" or "volts" => "9",      // UNITS_VOLTS
                "ma" or "milliamp" => "10",            // UNITS_MILLIAMPERES
                "a" or "amp" or "ampere" => "11",     // UNITS_AMPERES
                "w" or "watt" or "watts" => "14",      // UNITS_WATTS
                "kw" or "kilowatt" => "15",            // UNITS_KILOWATTS
                "kwh" or "kilowatt-hour" => "17",      // UNITS_KILOWATT_HOURS
                "pa" or "pascal" => "28",              // UNITS_PASCALS
                "kpa" or "kilopascal" => "29",         // UNITS_KILOPASCALS
                "psi" => "30",                         // UNITS_POUNDS_FORCE_PER_SQUARE_INCH
                "gpm" or "gallon" => "32",             // UNITS_GALLONS_PER_MINUTE
                "cfm" or "cubic-foot" => "33",         // UNITS_CUBIC_FEET_PER_MINUTE
                "m3/s" or "cubic-meter" => "34",       // UNITS_CUBIC_METERS_PER_SECOND
                "psi_delta" => "64",                   // UNITS_PASCALS_PER_SECOND
                "" or "nounit" or "no-unit" => "62",   // UNITS_NO_UNITS
                _ => "62"  // Default to no-units for unknown
            };
        }

        #endregion

        // ──────────────────────────────────────────────────────────────────
        #region BACnet event handlers

        private void OnWhoIs(BacnetClient sender, BacnetAddress adr, int lowLimit, int highLimit)
        {
            if (_storage == null) return;
            try
            {
                uint devId = _storage.DeviceId;
                if (lowLimit != -1 && devId < (uint)lowLimit) return;
                if (highLimit != -1 && devId > (uint)highLimit) return;
                sender.Iam(devId, BacnetSegmentations.SEGMENTATION_NONE);
                Log($"Who-Is from {adr} → I-Am {devId}");
            }
            catch (Exception ex) { Log($"[ERROR] Who-Is: {ex.Message}"); }
        }

        private void OnReadPropertyRequest(BacnetClient sender, BacnetAddress adr, byte invokeId,
            BacnetObjectId objectId, BacnetPropertyReference property, BacnetMaxSegments maxSegments)
        {
            if (_storage == null)
            {
                Log($"[ERROR] ReadProperty request but storage is null!");
                return;
            }

            var propId = (BacnetPropertyIds)property.propertyIdentifier;
            Log($"ReadProperty: {objectId} → {propId} (invoke={invokeId})");

            try
            {
                IList<BacnetValue>? value;
                DeviceStorage.ErrorCodes code;
                lock (_lock)
                {
                    code = _storage.ReadProperty(objectId, propId, property.propertyArrayIndex, out value);
                }

                if (code == DeviceStorage.ErrorCodes.Good && value != null)
                {
                    Log($"  ✓ ReadProperty success: {value.Count} values");
                    sender.ReadPropertyResponse(adr, invokeId, sender.GetSegmentBuffer(maxSegments),
                        objectId, property, value);
                }
                else
                {
                    var errCode = code == DeviceStorage.ErrorCodes.UnknownObject
                        ? BacnetErrorCodes.ERROR_CODE_UNKNOWN_OBJECT
                        : BacnetErrorCodes.ERROR_CODE_UNKNOWN_PROPERTY;
                    Log($"  ✗ ReadProperty failed: {code}");
                    sender.ErrorResponse(adr, BacnetConfirmedServices.SERVICE_CONFIRMED_READ_PROPERTY,
                        invokeId, BacnetErrorClasses.ERROR_CLASS_OBJECT, errCode);
                }
            }
            catch (Exception ex)
            {
                Log($"[ERROR] ReadProperty exception: {ex.Message}");
                try
                {
                    sender.ErrorResponse(adr, BacnetConfirmedServices.SERVICE_CONFIRMED_READ_PROPERTY,
                        invokeId, BacnetErrorClasses.ERROR_CLASS_OBJECT, BacnetErrorCodes.ERROR_CODE_OTHER);
                }
                catch { /* best effort */ }
            }
        }

        private void OnWritePropertyRequest(BacnetClient sender, BacnetAddress adr, byte invokeId,
            BacnetObjectId objectId, BacnetPropertyValue value, BacnetMaxSegments maxSegments)
        {
            if (_storage == null) return;
            try
            {
                DeviceStorage.ErrorCodes code;
                lock (_lock)
                {
                    var propId = (BacnetPropertyIds)value.property.propertyIdentifier;
                    code = _storage.WriteProperty(objectId, propId,
                        value.property.propertyArrayIndex, value.value, false);
                }

                if (code == DeviceStorage.ErrorCodes.Good)
                {
                    sender.SimpleAckResponse(adr, BacnetConfirmedServices.SERVICE_CONFIRMED_WRITE_PROPERTY, invokeId);

                    if (_pointMap.TryGetValue(objectId, out var pt) && value.value?.Count > 0)
                    {
                        double newVal = value.value[0].Value is float f ? f
                                      : Convert.ToDouble(value.value[0].Value);
                        Application.Current?.Dispatcher.Invoke(() =>
                        {
                            pt.PresentValue = newVal;
                            PointValueChanged?.Invoke(pt);
                        });
                        Log($"Write {pt.TypeLabel}:{pt.Instance} '{pt.Name}' ← {newVal}");
                    }
                }
                else
                {
                    sender.ErrorResponse(adr, BacnetConfirmedServices.SERVICE_CONFIRMED_WRITE_PROPERTY,
                        invokeId, BacnetErrorClasses.ERROR_CLASS_PROPERTY, BacnetErrorCodes.ERROR_CODE_WRITE_ACCESS_DENIED);
                }
            }
            catch (Exception ex)
            {
                Log($"[ERROR] WriteProperty: {ex.Message}");
                try
                {
                    sender.ErrorResponse(adr, BacnetConfirmedServices.SERVICE_CONFIRMED_WRITE_PROPERTY,
                        invokeId, BacnetErrorClasses.ERROR_CLASS_PROPERTY, BacnetErrorCodes.ERROR_CODE_OTHER);
                }
                catch { /* best effort */ }
            }
        }

        private void OnReadPropertyMultipleRequest(BacnetClient sender, BacnetAddress adr, byte invokeId,
            IList<BacnetReadAccessSpecification> properties, BacnetMaxSegments maxSegments)
        {
            if (_storage == null) return;
            try
            {
                var results = new List<BacnetReadAccessResult>();
                lock (_lock)
                {
                    foreach (var spec in properties)
                    {
                        IList<BacnetPropertyValue>? vals;

                        // PROP_ALL (8) or PROP_REQUIRED (9): use ReadPropertyAll
                        bool wantsAll = spec.propertyReferences.Any(p =>
                            p.propertyIdentifier == (uint)BacnetPropertyIds.PROP_ALL ||
                            p.propertyIdentifier == (uint)BacnetPropertyIds.PROP_REQUIRED);

                        if (wantsAll)
                        {
                            _storage.ReadPropertyAll(spec.objectIdentifier, out vals);
                        }
                        else
                        {
                            _storage.ReadPropertyMultiple(spec.objectIdentifier,
                                spec.propertyReferences, out vals);
                        }

                        results.Add(new BacnetReadAccessResult(spec.objectIdentifier, vals ?? []));
                    }
                }
                sender.ReadPropertyMultipleResponse(adr, invokeId,
                    sender.GetSegmentBuffer(maxSegments), results);
            }
            catch (Exception ex)
            {
                Log($"[ERROR] ReadPropertyMultiple: {ex.Message}");
                try
                {
                    sender.ErrorResponse(adr, BacnetConfirmedServices.SERVICE_CONFIRMED_READ_PROP_MULTIPLE,
                        invokeId, BacnetErrorClasses.ERROR_CLASS_OBJECT, BacnetErrorCodes.ERROR_CODE_OTHER);
                }
                catch { /* best effort */ }
            }
        }

        #endregion

        // ──────────────────────────────────────────────────────────────────
        #region Helpers

        private static BacnetObjectId ToObjectId(BacnetPoint pt)
            => new(ToBacnetObjType(pt.ObjectType), pt.Instance);

        private static BacnetObjectTypes ToBacnetObjType(BacnetObjectType t) => t switch
        {
            BacnetObjectType.AnalogInput  => BacnetObjectTypes.OBJECT_ANALOG_INPUT,
            BacnetObjectType.AnalogOutput => BacnetObjectTypes.OBJECT_ANALOG_OUTPUT,
            BacnetObjectType.AnalogValue  => BacnetObjectTypes.OBJECT_ANALOG_VALUE,
            BacnetObjectType.BinaryInput  => BacnetObjectTypes.OBJECT_BINARY_INPUT,
            BacnetObjectType.BinaryOutput => BacnetObjectTypes.OBJECT_BINARY_OUTPUT,
            BacnetObjectType.BinaryValue  => BacnetObjectTypes.OBJECT_BINARY_VALUE,
            _ => BacnetObjectTypes.OBJECT_ANALOG_INPUT
        };

        private static string XmlObjType(BacnetObjectType t) => t switch
        {
            BacnetObjectType.AnalogInput  => "OBJECT_ANALOG_INPUT",
            BacnetObjectType.AnalogOutput => "OBJECT_ANALOG_OUTPUT",
            BacnetObjectType.AnalogValue  => "OBJECT_ANALOG_VALUE",
            BacnetObjectType.BinaryInput  => "OBJECT_BINARY_INPUT",
            BacnetObjectType.BinaryOutput => "OBJECT_BINARY_OUTPUT",
            BacnetObjectType.BinaryValue  => "OBJECT_BINARY_VALUE",
            _ => "OBJECT_ANALOG_INPUT"
        };

        private static BacnetValue ToValue(BacnetPoint pt)
        {
            if (pt.IsBinary)
                return new BacnetValue(BacnetApplicationTags.BACNET_APPLICATION_TAG_ENUMERATED,
                                       (uint)(pt.PresentValue > 0 ? 1 : 0));
            return new BacnetValue(BacnetApplicationTags.BACNET_APPLICATION_TAG_REAL, (float)pt.PresentValue);
        }

        private void Log(string msg) => LogMessage?.Invoke($"[{DateTime.Now:HH:mm:ss}] {msg}");

        #endregion

        public void Dispose() => Stop();
    }
}
