using System;
using System.Collections.Generic;
using System.Text;
using System.Globalization;

namespace EyeLean.Data
{
    /// <summary>
    /// Type of value stored in a metadata field.
    /// </summary>
    public enum MetadataValueType
    {
        String,
        Int,
        Float,
        Bool
    }

    /// <summary>
    /// Type-safe storage for a single metadata value.
    /// Handles CSV formatting based on the value type.
    /// </summary>
    [Serializable]
    public struct MetadataValue
    {
        public MetadataValueType Type;
        public string StringValue;
        public int IntValue;
        public float FloatValue;
        public bool BoolValue;

        /// <summary>
        /// Create a string metadata value.
        /// </summary>
        public static MetadataValue FromString(string value)
        {
            return new MetadataValue
            {
                Type = MetadataValueType.String,
                StringValue = value ?? string.Empty,
                IntValue = 0,
                FloatValue = 0f,
                BoolValue = false
            };
        }

        /// <summary>
        /// Create an integer metadata value.
        /// </summary>
        public static MetadataValue FromInt(int value)
        {
            return new MetadataValue
            {
                Type = MetadataValueType.Int,
                StringValue = string.Empty,
                IntValue = value,
                FloatValue = 0f,
                BoolValue = false
            };
        }

        /// <summary>
        /// Create a float metadata value.
        /// </summary>
        public static MetadataValue FromFloat(float value)
        {
            return new MetadataValue
            {
                Type = MetadataValueType.Float,
                StringValue = string.Empty,
                IntValue = 0,
                FloatValue = value,
                BoolValue = false
            };
        }

        /// <summary>
        /// Create a boolean metadata value.
        /// </summary>
        public static MetadataValue FromBool(bool value)
        {
            return new MetadataValue
            {
                Type = MetadataValueType.Bool,
                StringValue = string.Empty,
                IntValue = 0,
                FloatValue = 0f,
                BoolValue = value
            };
        }

        /// <summary>
        /// Get the value formatted for CSV output.
        /// Strings are escaped if they contain commas or quotes.
        /// </summary>
        public string ToCSVString()
        {
            switch (Type)
            {
                case MetadataValueType.String:
                    // Escape strings containing commas, quotes, or newlines
                    if (StringValue.Contains(",") || StringValue.Contains("\"") || StringValue.Contains("\n"))
                    {
                        return "\"" + StringValue.Replace("\"", "\"\"") + "\"";
                    }
                    return StringValue;

                case MetadataValueType.Int:
                    return IntValue.ToString(CultureInfo.InvariantCulture);

                case MetadataValueType.Float:
                    return FloatValue.ToString("F6", CultureInfo.InvariantCulture);

                case MetadataValueType.Bool:
                    return BoolValue ? "TRUE" : "FALSE";

                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// Create a copy of this value.
        /// </summary>
        public MetadataValue Clone()
        {
            return new MetadataValue
            {
                Type = this.Type,
                StringValue = this.StringValue,
                IntValue = this.IntValue,
                FloatValue = this.FloatValue,
                BoolValue = this.BoolValue
            };
        }
    }

    /// <summary>
    /// Thread-safe container for custom experiment metadata fields, preserving
    /// insertion order so CSV columns are stable across runs. Once
    /// <see cref="LockSchema"/> is called (typically when the CSV header is
    /// written) only existing fields may be updated; declare all fields up
    /// front so header and data rows stay aligned.
    /// </summary>
    public class ExperimentMetadata
    {
        // Dictionary + parallel field-order list so iteration matches insertion order.
        private readonly Dictionary<string, MetadataValue> _values = new Dictionary<string, MetadataValue>();
        private readonly List<string> _fieldOrder = new List<string>();
        private readonly object _lock = new object();

        private bool _schemaLocked = false;

        /// <summary>
        /// Number of metadata fields currently stored.
        /// </summary>
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _values.Count;
                }
            }
        }

        /// <summary>
        /// Whether the schema is locked (no new fields can be added).
        /// </summary>
        public bool IsSchemaLocked
        {
            get
            {
                lock (_lock)
                {
                    return _schemaLocked;
                }
            }
        }

        /// <summary>
        /// Get the ordered list of field names.
        /// </summary>
        public List<string> FieldNames
        {
            get
            {
                lock (_lock)
                {
                    return new List<string>(_fieldOrder);
                }
            }
        }

        /// <summary>
        /// Lock the schema to prevent new fields from being added.
        /// Call this after writing the CSV header to ensure data rows match.
        /// </summary>
        public void LockSchema()
        {
            lock (_lock)
            {
                _schemaLocked = true;
                UnityEngine.Debug.Log($"[ExperimentMetadata] Schema locked with {_fieldOrder.Count} fields: {string.Join(", ", _fieldOrder)}");
            }
        }

        /// <summary>
        /// Unlock the schema (typically only for testing or resetting).
        /// </summary>
        public void UnlockSchema()
        {
            lock (_lock)
            {
                _schemaLocked = false;
            }
        }

        /// <summary>
        /// Declare a metadata field without setting a value.
        /// Use this to pre-declare all fields before recording starts.
        /// </summary>
        public void DeclareField(string fieldName, MetadataValueType type)
        {
            if (string.IsNullOrEmpty(fieldName))
            {
                throw new ArgumentException("Field name cannot be null or empty", nameof(fieldName));
            }

            lock (_lock)
            {
                if (_schemaLocked)
                {
                    throw new InvalidOperationException(
                        $"Cannot declare new field '{fieldName}' - schema is locked. " +
                        "Declare all metadata fields BEFORE starting recording.");
                }

                if (!_values.ContainsKey(fieldName))
                {
                    _fieldOrder.Add(fieldName);
                    switch (type)
                    {
                        case MetadataValueType.String:
                            _values[fieldName] = MetadataValue.FromString("");
                            break;
                        case MetadataValueType.Int:
                            _values[fieldName] = MetadataValue.FromInt(0);
                            break;
                        case MetadataValueType.Float:
                            _values[fieldName] = MetadataValue.FromFloat(0f);
                            break;
                        case MetadataValueType.Bool:
                            _values[fieldName] = MetadataValue.FromBool(false);
                            break;
                    }
                }
            }
        }

        /// <summary>
        /// Set a string metadata value.
        /// </summary>
        public void SetString(string fieldName, string value)
        {
            SetValue(fieldName, MetadataValue.FromString(value));
        }

        /// <summary>
        /// Set an integer metadata value.
        /// </summary>
        public void SetInt(string fieldName, int value)
        {
            SetValue(fieldName, MetadataValue.FromInt(value));
        }

        /// <summary>
        /// Set a float metadata value.
        /// </summary>
        public void SetFloat(string fieldName, float value)
        {
            SetValue(fieldName, MetadataValue.FromFloat(value));
        }

        /// <summary>
        /// Set a boolean metadata value.
        /// </summary>
        public void SetBool(string fieldName, bool value)
        {
            SetValue(fieldName, MetadataValue.FromBool(value));
        }

        /// <summary>
        /// Set a metadata value directly.
        /// If schema is locked, only existing fields can be updated.
        /// </summary>
        private void SetValue(string fieldName, MetadataValue value)
        {
            if (string.IsNullOrEmpty(fieldName))
            {
                throw new ArgumentException("Field name cannot be null or empty", nameof(fieldName));
            }

            lock (_lock)
            {
                bool fieldExists = _values.ContainsKey(fieldName);

                if (!fieldExists)
                {
                    if (_schemaLocked)
                    {
                        UnityEngine.Debug.LogError(
                            $"[ExperimentMetadata] Cannot add new field '{fieldName}' - schema is locked! " +
                            $"Declare all metadata fields BEFORE starting recording. " +
                            $"Existing fields: {string.Join(", ", _fieldOrder)}");
                        // Drop the write rather than throwing — header is already on disk.
                        return;
                    }
                    _fieldOrder.Add(fieldName);
                }
                _values[fieldName] = value;
            }
        }

        /// <summary>
        /// Remove a metadata field.
        /// </summary>
        public bool Remove(string fieldName)
        {
            lock (_lock)
            {
                if (_values.Remove(fieldName))
                {
                    _fieldOrder.Remove(fieldName);
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Clear all metadata fields.
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _values.Clear();
                _fieldOrder.Clear();
            }
        }

        /// <summary>
        /// Check if a field exists.
        /// </summary>
        public bool HasField(string fieldName)
        {
            lock (_lock)
            {
                return _values.ContainsKey(fieldName);
            }
        }

        /// <summary>
        /// Try to get a metadata value.
        /// </summary>
        public bool TryGetValue(string fieldName, out MetadataValue value)
        {
            lock (_lock)
            {
                return _values.TryGetValue(fieldName, out value);
            }
        }

        /// <summary>
        /// Generate CSV header string for custom metadata columns.
        /// Returns empty string if no metadata fields are defined.
        /// </summary>
        public string GetCSVHeader()
        {
            lock (_lock)
            {
                if (_fieldOrder.Count == 0)
                    return string.Empty;

                var sb = new StringBuilder();
                for (int i = 0; i < _fieldOrder.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(_fieldOrder[i]);
                }
                return sb.ToString();
            }
        }

        /// <summary>
        /// Generate CSV row string for current metadata values.
        /// Returns empty string if no metadata fields are defined.
        /// </summary>
        public string GetCSVRow()
        {
            lock (_lock)
            {
                if (_fieldOrder.Count == 0)
                    return string.Empty;

                var sb = new StringBuilder();
                for (int i = 0; i < _fieldOrder.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    if (_values.TryGetValue(_fieldOrder[i], out var value))
                    {
                        sb.Append(value.ToCSVString());
                    }
                }
                return sb.ToString();
            }
        }

        /// <summary>
        /// Create a thread-safe snapshot of current metadata values.
        /// Use this when collecting data samples to avoid holding locks.
        /// </summary>
        public Dictionary<string, MetadataValue> CreateSnapshot()
        {
            lock (_lock)
            {
                var snapshot = new Dictionary<string, MetadataValue>(_values.Count);
                foreach (var kvp in _values)
                {
                    snapshot[kvp.Key] = kvp.Value.Clone();
                }
                return snapshot;
            }
        }

        /// <summary>
        /// Create a snapshot that preserves field order.
        /// Returns a tuple of (fieldNames, values dictionary).
        /// </summary>
        public (List<string> fieldOrder, Dictionary<string, MetadataValue> values) CreateOrderedSnapshot()
        {
            lock (_lock)
            {
                var order = new List<string>(_fieldOrder);
                var snapshot = new Dictionary<string, MetadataValue>(_values.Count);
                foreach (var kvp in _values)
                {
                    snapshot[kvp.Key] = kvp.Value.Clone();
                }
                return (order, snapshot);
            }
        }

        /// <summary>
        /// Generate CSV row from a snapshot dictionary.
        /// Uses the provided field order for consistency.
        /// </summary>
        public static string GetCSVRowFromSnapshot(List<string> fieldOrder, Dictionary<string, MetadataValue> snapshot)
        {
            if (fieldOrder == null || fieldOrder.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();
            for (int i = 0; i < fieldOrder.Count; i++)
            {
                if (i > 0) sb.Append(',');
                if (snapshot != null && snapshot.TryGetValue(fieldOrder[i], out var value))
                {
                    sb.Append(value.ToCSVString());
                }
            }
            return sb.ToString();
        }
    }
}
