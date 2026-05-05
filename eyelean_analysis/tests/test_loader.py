"""Loader column-alias resolution + boolean parsing tests."""

import pandas as pd

from eyelean_analysis.data.loader import EyeLeanLoader, read_csv_metadata


def test_canonical_alias_resolution(synthetic_eyelean_csv):
    """`HeadPos_X` and the mixed-case `CombinedEyeOriginX` should both
    resolve to canonical `head_pos_x` / `combined_origin_x` after
    `standardize_columns=True`."""
    loader = EyeLeanLoader()
    data = loader.load(synthetic_eyelean_csv, standardize_columns=True)

    canonical_present = set(data.df.columns)
    assert "head_pos_x" in canonical_present
    assert "head_pos_y" in canonical_present
    assert "combined_origin_x" in canonical_present
    assert "combined_origin_y" in canonical_present
    # Original aliases should be gone after rename
    assert "HeadPos_X" not in canonical_present
    assert "CombinedEyeOriginX" not in canonical_present


def test_alias_mapping_roundtrip_no_standardize(synthetic_eyelean_csv):
    """With `standardize_columns=False`, the column mapping is still
    populated (so callers can look up canonical names) but the dataframe
    columns are unchanged."""
    loader = EyeLeanLoader()
    data = loader.load(synthetic_eyelean_csv, standardize_columns=False)

    assert "HeadPos_X" in data.df.columns  # untouched
    assert data.column_map["HeadPos_X"] == "head_pos_x"
    assert data.column_map["CombinedEyeOriginX"] == "combined_origin_x"


def test_boolean_parsing(synthetic_eyelean_csv):
    """`"True"`/`"False"` text should be parsed to bool dtype."""
    loader = EyeLeanLoader()
    data = loader.load(synthetic_eyelean_csv, parse_booleans=True, standardize_columns=True)

    assert data.df["has_combined_origin"].dtype == bool
    assert data.df["is_tracking_valid"].dtype == bool
    assert data.df["has_combined_origin"].all()


def test_unknown_columns_are_kept_unchanged(tmp_path):
    """Columns the loader has no alias for must pass through verbatim;
    the alias system should never silently drop researcher-added columns."""
    df = pd.DataFrame({
        "UnityTimestamp": [0.0, 0.008, 0.016],
        "MyCustomMetric": [1, 2, 3],
        "HeadPos_X": [0.0, 0.0, 0.0],
    })
    p = tmp_path / "custom.csv"
    df.to_csv(p, index=False)

    data = EyeLeanLoader().load(p, standardize_columns=True)
    assert "MyCustomMetric" in data.df.columns
    assert list(data.df["MyCustomMetric"]) == [1, 2, 3]


def test_skips_metadata_header_lines(tmp_path):
    """SimpleEyeTracker prepends `# Key: value` lines (CoordinateOrigin,
    FileVersion, etc.) before the column header. The loader must treat
    them as comments — otherwise pandas misreads the first metadata line
    as the header and the entire alias system collapses."""
    csv_text = (
        "# CoordinateOrigin: 0.0000,0.0000,0.0000\n"
        "# CoordinateOriginSet: False\n"
        "# FileVersion: 1.0\n"
        "UnityTimestamp,CurrentPhase,HeadPos_X\n"
        "0.0,FreeExploration,0.1\n"
        "0.008,FreeExploration,0.2\n"
    )
    p = tmp_path / "with_metadata.csv"
    p.write_text(csv_text)

    data = EyeLeanLoader().load(p, standardize_columns=True)
    assert "phase" in data.df.columns
    assert list(data.df["phase"]) == ["FreeExploration", "FreeExploration"]
    assert "head_pos_x" in data.df.columns
    assert len(data) == 2


def test_read_csv_metadata_parses_full_block(tmp_path):
    """`read_csv_metadata` must capture every `# Key: value` line and
    handle the `# Profile:` line emitted post-V8."""
    csv_text = (
        "# CoordinateOrigin: 0.1234,5.6780,9.0000\n"
        "# CoordinateOriginSet: True\n"
        "# Profile: TestUser_2026\n"
        "# ProfileCombinedYawDeg: -0.2618\n"
        "# ProfileCombinedPitchDeg: -0.3985\n"
        "# FileVersion: 1.0\n"
        "# Eye_lean Research Data Export\n"  # no `:`, must be ignored
        "UnityTimestamp,HeadPos_X\n"
        "0.0,0.1\n"
    )
    p = tmp_path / "with_full_meta.csv"
    p.write_text(csv_text)

    meta = read_csv_metadata(p)
    assert meta["CoordinateOrigin"] == "0.1234,5.6780,9.0000"
    assert meta["CoordinateOriginSet"] == "True"
    assert meta["Profile"] == "TestUser_2026"
    assert meta["ProfileCombinedYawDeg"] == "-0.2618"
    assert meta["ProfileCombinedPitchDeg"] == "-0.3985"
    assert meta["FileVersion"] == "1.0"
    # Header section ends at the first non-`#` line; columns are not in metadata.
    assert "UnityTimestamp" not in meta


def test_read_csv_metadata_handles_no_metadata(tmp_path):
    """A CSV with no `#` lines must return an empty dict, not raise."""
    p = tmp_path / "nometa.csv"
    p.write_text("UnityTimestamp,HeadPos_X\n0.0,0.1\n")
    assert read_csv_metadata(p) == {}
