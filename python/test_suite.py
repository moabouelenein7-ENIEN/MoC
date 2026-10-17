"""
Unit tests for MoC Structural Suite Python Modules.
Verifies SBC 301 seismic parameters, wall load formulas, and rebar detailing logic.
"""

import sys
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

# Add python directory to path
sys.path.insert(0, str(Path(__file__).resolve().parent))

from moc.sbc_database import SBCDatabase
from moc.wall_calculator import WallLoadCalculator, WallLoadInput
from moc.rebar_calculator import RebarCalculator


def test_sbc_seismic_database():
    db = SBCDatabase.get_instance()

    # 1. Test Riyadh
    riyadh_params = db.compute_design_parameters("Riyadh", site_class="D", risk_category="II")
    assert riyadh_params.city_name == "Riyadh"
    assert riyadh_params.Ss == 0.243
    assert riyadh_params.S1 == 0.079
    # Fa for Ss=0.243 on Class D is 1.6
    assert riyadh_params.Fa == 1.6
    # Fv for S1=0.079 on Class D is 2.4
    assert riyadh_params.Fv == 2.4
    assert riyadh_params.Sds > 0
    assert riyadh_params.Sd1 > 0
    assert riyadh_params.seismic_design_category in ("A", "B", "C", "D")
    print(f"[PASS] Riyadh SBC 301: SDS={riyadh_params.Sds}g, SD1={riyadh_params.Sd1}g, SDC={riyadh_params.seismic_design_category}")

    # 2. Test Makkah (High seismicity)
    makkah_params = db.compute_design_parameters("Makkah", site_class="C", risk_category="IV")
    assert makkah_params.Ss == 1.013
    assert makkah_params.S1 == 0.401
    assert makkah_params.seismic_design_category in ("C", "D", "E", "F")
    print(f"[PASS] Makkah SBC 301: SDS={makkah_params.Sds}g, SD1={makkah_params.Sd1}g, SDC={makkah_params.seismic_design_category}")


def test_wall_calculator():
    calc = WallLoadCalculator()
    inp = WallLoadInput(
        story_height_m=3.0,
        beam_depth_m=0.60,
        wall_thickness_mm=200.0,
        material_density_kN_m3=12.0,  # Hollow cement block
        plaster_thickness_mm=15.0,     # per face (30mm total)
        plaster_density_kN_m3=21.0,
        opening_percentage=0.15,       # 15% doors/windows
    )
    res = calc.calculate(inp)

    # Expected:
    # H_net = 3.0 - 0.60 = 2.4 m
    # t_block = 0.20 m -> w_block = 2.4 * 0.20 * 12.0 = 5.76 kN/m
    # t_plaster = 0.03 m -> w_plaster = 2.4 * 0.03 * 21.0 = 1.512 kN/m
    # gross = 5.76 + 1.512 = 7.272 kN/m
    # net = 7.272 * 0.85 = 6.1812 kN/m
    assert abs(res.h_net_m - 2.4) < 1e-4
    assert abs(res.w_total_kn_per_m - 6.18) < 0.05
    print(f"[PASS] Wall Calculator: H_net={res.h_net_m}m, w_wall={res.w_total_kn_per_m} kN/m")


def test_rebar_translator():
    db = SBCDatabase.get_instance()
    calc = RebarCalculator(db.get_rebar_table())

    # Case 1: Beam bottom steel As_req = 1200 mm² with Φ16 (A_bar = 201.06 mm²)
    # N = ceil(1200 / 201.06) = 6 bars
    res1 = calc.translate(1200.0, preferred_diameter=16, member_type="beam", location="Bottom")
    assert res1.bar_count == 6
    assert res1.selected_diameter_mm == 16
    assert "6" in res1.description and "16" in res1.description
    assert res1.as_provided_mm2 >= 1200.0
    print(f"[PASS] Rebar Beam: As_req=1200 mm2 -> {res1.description} (As_prov={res1.as_provided_mm2:.1f} mm2)")

    # Case 2: Column steel As_req = 2200 mm² with Φ20 (A_bar = 314.16 mm²)
    # N = ceil(2200 / 314.16) = 7 -> enforced even for columns = 8 bars
    res2 = calc.translate(2200.0, preferred_diameter=20, member_type="column", location="All Faces")
    assert res2.bar_count % 2 == 0  # Even number for columns
    assert res2.bar_count == 8
    print(f"[PASS] Rebar Column: As_req=2200 mm2 -> {res2.description} (Even count enforced: {res2.bar_count} bars)")


if __name__ == "__main__":
    print("\n" + "=" * 60)
    print("  RUNNING MOC STRUCTURAL SUITE PYTHON TEST VERIFICATION")
    print("=" * 60)
    test_sbc_seismic_database()
    test_wall_calculator()
    test_rebar_translator()
    print("=" * 60)
    print("  ALL 3 TEST SUITES PASSED FLAWLESSLY! (100% SUCCESS)")
    print("=" * 60 + "\n")
