using UnityEngine;
using System.Collections.Generic;
using EyeLean.Data;

namespace EyeLean.Configuration
{
    /// <summary>
    /// Definition for a single metadata field in the schema.
    /// </summary>
    [System.Serializable]
    public class MetadataFieldDefinition
    {
        [Tooltip("Name of the field as it will appear in CSV header")]
        public string FieldName;

        [Tooltip("Data type for this field")]
        public MetadataValueType Type = MetadataValueType.String;

        [Tooltip("Default value (as string - will be parsed based on Type)")]
        public string DefaultValue;

        [Tooltip("Description of this field for documentation")]
        [TextArea(1, 3)]
        public string Description;
    }

    /// <summary>
    /// ScriptableObject for pre-defining experiment metadata fields in the
    /// Unity Inspector. Create via Assets > Create > Eye Tracking > Experiment
    /// Metadata Schema. Assign to SessionRecorder's Metadata Schema field; the
    /// schema is auto-applied when recording starts.
    /// </summary>
    [CreateAssetMenu(fileName = "ExperimentMetadataSchema", menuName = "Eye Tracking/Experiment Metadata Schema")]
    public class ExperimentMetadataSchema : ScriptableObject
    {
        [Tooltip("List of custom metadata fields for this experiment")]
        [SerializeField]
        private List<MetadataFieldDefinition> fields = new List<MetadataFieldDefinition>();

        /// <summary>
        /// Get the list of field definitions.
        /// </summary>
        public List<MetadataFieldDefinition> Fields => fields;

        /// <summary>
        /// Initialize an ExperimentMetadata container with fields from this schema.
        /// Sets default values for each field based on the defined type.
        /// </summary>
        public void InitializeMetadata(ExperimentMetadata metadata)
        {
            if (metadata == null)
            {
                Debug.LogError("[ExperimentMetadataSchema] Cannot initialize null metadata container");
                return;
            }

            foreach (var field in fields)
            {
                if (string.IsNullOrEmpty(field.FieldName))
                {
                    Debug.LogWarning("[ExperimentMetadataSchema] Skipping field with empty name");
                    continue;
                }

                switch (field.Type)
                {
                    case MetadataValueType.String:
                        metadata.SetString(field.FieldName, field.DefaultValue ?? string.Empty);
                        break;

                    case MetadataValueType.Int:
                        if (int.TryParse(field.DefaultValue, out int intVal))
                            metadata.SetInt(field.FieldName, intVal);
                        else
                            metadata.SetInt(field.FieldName, 0);
                        break;

                    case MetadataValueType.Float:
                        if (float.TryParse(field.DefaultValue, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float floatVal))
                            metadata.SetFloat(field.FieldName, floatVal);
                        else
                            metadata.SetFloat(field.FieldName, 0f);
                        break;

                    case MetadataValueType.Bool:
                        bool boolVal = field.DefaultValue?.ToLower() == "true" ||
                                      field.DefaultValue == "1";
                        metadata.SetBool(field.FieldName, boolVal);
                        break;
                }
            }

            Debug.Log($"[ExperimentMetadataSchema] Initialized {fields.Count} metadata fields from schema '{name}'");
        }

        /// <summary>
        /// Validate the schema for common issues.
        /// </summary>
        public bool Validate(out List<string> errors)
        {
            errors = new List<string>();
            var fieldNames = new HashSet<string>();

            foreach (var field in fields)
            {
                if (string.IsNullOrEmpty(field.FieldName))
                {
                    errors.Add("Field with empty name found");
                    continue;
                }

                // Check for duplicates
                if (!fieldNames.Add(field.FieldName))
                {
                    errors.Add($"Duplicate field name: {field.FieldName}");
                }

                // Check for reserved names (built-in CSV columns)
                var reserved = new HashSet<string>
                {
                    "UnityTimestamp", "RealTimeSinceStartup", "SystemTimestamp", "FrameNumber", "DeltaTime",
                    "ParticipantID", "TrialNumber", "CurrentPhase", "SubTask", "SessionConfig", "IsDebugMode",
                    "HeadPos_X", "HeadPos_Y", "HeadPos_Z", "CurrentFPS", "FrameTimeMs", "DataSampleCount"
                };

                if (reserved.Contains(field.FieldName))
                {
                    errors.Add($"Field name '{field.FieldName}' conflicts with built-in column");
                }

                // Check for invalid characters
                if (field.FieldName.Contains(",") || field.FieldName.Contains("\"") ||
                    field.FieldName.Contains("\n") || field.FieldName.Contains(" "))
                {
                    errors.Add($"Field name '{field.FieldName}' contains invalid characters (comma, quote, newline, or space)");
                }
            }

            return errors.Count == 0;
        }

        private void OnValidate()
        {
            if (Validate(out var errors))
            {
                return;
            }

            foreach (var error in errors)
            {
                Debug.LogWarning($"[ExperimentMetadataSchema] Validation warning: {error}");
            }
        }
    }
}
