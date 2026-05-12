"""Smoke test for fixation_entropy."""
import numpy as np
import pytest
from eyelean_analysis.classification.velocity_classifier import Fixation
from eyelean_analysis.metrics.entropy import fixation_entropy


def _mk(idx, t, dur, cx, cy):
    return Fixation(start_idx=idx, end_idx=idx+1, start_time=t, end_time=t+dur,
                    duration=dur, centroid_x=cx, centroid_y=cy, sample_count=10)


def test_uniform_grid_coverage_approaches_hmax():
    # 64 fixations, one per cell of an 8x8 grid uniformly => SGE = Hmax = 6 bits.
    fixs = []
    t = 0.0
    for ix in range(8):
        for iy in range(8):
            fixs.append(_mk(len(fixs), t, 0.2, ix + 0.5, iy + 0.5))
            t += 0.3
    r = fixation_entropy(fixs, horizontal_bins=8, vertical_bins=8,
                         x_range=(0, 8), y_range=(0, 8))
    assert r.is_valid
    assert r.sge == pytest.approx(6.0, abs=1e-9)
    assert r.sge_normalized == pytest.approx(1.0, abs=1e-9)
    assert r.n_bins_used == 64


def test_single_cell_zero_entropy():
    fixs = [_mk(i, i*0.5, 0.2, 0.5, 0.5) for i in range(5)]
    r = fixation_entropy(fixs, horizontal_bins=4, vertical_bins=4,
                         x_range=(0, 4), y_range=(0, 4))
    assert r.is_valid
    assert r.sge == 0.0
    assert r.gte == 0.0


def test_visual_search_pattern_low_gte():
    # Target-revisiting pattern: alternation between two cells is more
    # predictable than random scattering => GTE close to 0.
    fixs = []
    for i in range(12):
        cx = 0.5 if i % 2 == 0 else 3.5
        fixs.append(_mk(i, i*0.5, 0.2, cx, 1.5))
    r = fixation_entropy(fixs, horizontal_bins=4, vertical_bins=4,
                         x_range=(0, 4), y_range=(0, 4))
    assert r.is_valid
    assert r.gte == pytest.approx(0.0, abs=1e-9)  # perfectly predictable


def test_too_few_fixations_invalid():
    r = fixation_entropy([_mk(0, 0, 0.2, 1, 1)], horizontal_bins=4, vertical_bins=4)
    assert not r.is_valid
