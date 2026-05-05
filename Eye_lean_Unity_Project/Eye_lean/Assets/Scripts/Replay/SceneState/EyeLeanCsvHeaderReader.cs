namespace EyeLean.Replay.SceneState
{
    /// <summary>
    /// Shared helper for parsing <c># Key: value</c> metadata header lines
    /// in Eye_lean CSVs (main + sidecars). Lets
    /// <see cref="SceneStateCSVParser"/> reuse the splitting + BOM logic
    /// without depending on the full main parser.
    /// </summary>
    public static class EyeLeanCsvHeaderReader
    {
        /// <summary>
        /// Split a header comment line into (key, value). Returns false for
        /// non-comment / malformed lines. Strips the leading <c>#</c> and
        /// trims whitespace. Caller is expected to have already dropped the
        /// UTF-8 BOM.
        /// </summary>
        public static bool TryParse(string line, out string key, out string value)
        {
            key = null;
            value = null;
            if (string.IsNullOrEmpty(line) || line[0] != '#') return false;
            int colon = line.IndexOf(':');
            if (colon < 0 || colon <= 1) return false;
            // Strip leading "#" plus optional space, normalize.
            key = line.Substring(1, colon - 1).Trim();
            value = line.Substring(colon + 1).Trim();
            return key.Length > 0;
        }
    }
}
