"""Load pattern definition, seismic/wind parameter application, and SBC 301 load combinations.

This module provides two manager classes for automating ETABS load setup:
- ``LoadPatternManager``: defines standard load patterns and applies auto-lateral parameters.
- ``CombinationManager``: generates ULS/SLS combinations per SBC 301 (ASCE 7-16 Ch. 2.3/2.4).

Typical usage::

    mgr = LoadPatternManager(sap_model)
    mgr.define_standard_patterns()
    mgr.apply_seismic_parameters(Sds=0.6, Sd1=0.3, TL=8.0, R=5.0, Cd=4.5, Omega0=2.5, Ie=1.0)
    mgr.apply_wind_parameters(wind_speed_mps=45.0)

    combo = CombinationManager(sap_model)
    combo.generate_sbc301_combinations()
"""

from __future__ import annotations

import logging
from dataclasses import dataclass, field
from typing import Any, List, Tuple

logger = logging.getLogger(__name__)


# ---------------------------------------------------------------------------
# Internal helpers
# ---------------------------------------------------------------------------

@dataclass
class _PatternSpec:
    """Specification for a single load pattern."""

    name: str
    load_type: int
    self_weight_multiplier: float


# Canonical list of standard patterns used throughout the project.
_STANDARD_PATTERNS: list[_PatternSpec] = [
    _PatternSpec("DEAD", 1, 1.0),
    _PatternSpec("SIDL", 2, 0.0),
    _PatternSpec("WALLS", 2, 0.0),
    _PatternSpec("LIVE", 3, 0.0),
    _PatternSpec("LIVE_ROOF", 11, 0.0),
    _PatternSpec("EQX", 5, 0.0),
    _PatternSpec("EQY", 5, 0.0),
    _PatternSpec("WINDX", 6, 0.0),
    _PatternSpec("WINDY", 6, 0.0),
]


def _check_ret(ret: int, context: str) -> None:
    """Raise if an ETABS API call returns a non-zero status.

    Args:
        ret: Return value from the OAPI call (0 = success).
        context: Human-readable description for the error message.
    """
    if ret != 0:
        raise RuntimeError(f"ETABS API error ({ret}) in: {context}")


# ---------------------------------------------------------------------------
# LoadPatternManager
# ---------------------------------------------------------------------------

class LoadPatternManager:
    """Create and configure ETABS load patterns and auto-lateral parameters.

    Args:
        sap_model: A live ``SapModel`` COM reference obtained from ETABS via
            comtypes or the ETABS application object.
    """

    def __init__(self, sap_model: Any) -> None:
        self._model: Any = sap_model

    # ----- pattern creation ------------------------------------------------

    def define_standard_patterns(self) -> list[str]:
        """Define the full set of standard load patterns in the active model.

        Existing patterns with the same name are silently skipped (the OAPI
        returns 0 even when the pattern already exists with the same type).

        Returns:
            List of pattern names that were created or already present.
        """
        created: list[str] = []
        for spec in _STANDARD_PATTERNS:
            ret = self._model.LoadPatterns.Add(
                spec.name,
                spec.load_type,
                spec.self_weight_multiplier,
                True,  # AddLoadCase – also create an associated load case
            )
            _check_ret(ret, f"LoadPatterns.Add('{spec.name}')")
            created.append(spec.name)
            logger.info(
                "Load pattern '%s' (type=%d, SW=%s) created.",
                spec.name,
                spec.load_type,
                spec.self_weight_multiplier,
            )
        return created

    # ----- seismic ---------------------------------------------------------

    def apply_seismic_parameters(
        self,
        Sds: float,
        Sd1: float,
        TL: float,
        R: float,
        Cd: float,
        Omega0: float,
        Ie: float,
        city_name: str = "",
    ) -> bool:
        """Apply IBC 2006 equivalent-lateral-force seismic parameters to EQX/EQY.

        The method calls ``SapModel.LoadPatterns.AutoSeismic.SetIBC2006`` for
        both principal directions.  ``Cd`` is stored for reference but is not
        part of the OAPI call signature (deflection amplification is handled
        at the design stage).

        Args:
            Sds: Design spectral response acceleration at short periods.
            Sd1: Design spectral response acceleration at 1-second period.
            TL: Long-period transition period (seconds).
            R: Response modification factor.
            Cd: Deflection amplification factor (stored, not sent to API).
            Omega0: System overstrength factor.
            Ie: Seismic importance factor.
            city_name: Optional label for logging/documentation.

        Returns:
            ``True`` if both directions were set successfully.
        """
        # Direction constants: 1 = X global, 2 = Y global
        eccentricity: float = 0.05
        user_defined_period: bool = False  # let ETABS compute
        user_t: float = 0.0
        top_story: str = ""
        bottom_story: str = ""

        directions: list[tuple[str, int]] = [("EQX", 1), ("EQY", 2)]

        for pattern_name, direction in directions:
            ret = self._model.LoadPatterns.AutoSeismic.SetIBC2006(
                pattern_name,
                direction,
                eccentricity,
                user_defined_period,
                user_t,
                top_story,
                bottom_story,
                Sds,
                Sd1,
                TL,
                R,
                Omega0,
                Ie,
            )
            _check_ret(
                ret,
                f"AutoSeismic.SetIBC2006('{pattern_name}', dir={direction})",
            )
            logger.info(
                "Seismic parameters applied to '%s' (dir=%d, Sds=%.3f, "
                "Sd1=%.3f, TL=%.1f, R=%.1f, Omega0=%.1f, Ie=%.1f)%s.",
                pattern_name,
                direction,
                Sds,
                Sd1,
                TL,
                R,
                Omega0,
                Ie,
                f" [{city_name}]" if city_name else "",
            )

        logger.info("Cd=%.2f stored for reference (not part of OAPI call).", Cd)
        return True

    # ----- wind ------------------------------------------------------------

    def apply_wind_parameters(
        self,
        wind_speed_mps: float,
        exposure: str = "B",
        importance: float = 1.0,
    ) -> bool:
        """Apply ASCE 7-16 auto-wind parameters to WINDX and WINDY patterns.

        Uses ``SapModel.LoadPatterns.AutoWind.SetASCE716`` for each wind
        direction.

        Args:
            wind_speed_mps: Basic wind speed in m/s (3-second gust).
            exposure: Exposure category (``'B'``, ``'C'``, or ``'D'``).
            importance: Wind importance factor.

        Returns:
            ``True`` if both directions were set successfully.
        """
        # ASCE 7-16 exposure category mapping:
        #   1 = B, 2 = C, 3 = D
        _exposure_map: dict[str, int] = {"B": 1, "C": 2, "D": 3}
        exposure_type: int = _exposure_map.get(exposure.upper(), 1)

        # Direction constants for wind: 1 = X, 2 = Y
        wind_patterns: list[tuple[str, int]] = [("WINDX", 1), ("WINDY", 2)]

        for pattern_name, direction in wind_patterns:
            ret = self._model.LoadPatterns.AutoWind.SetASCE716(
                pattern_name,       # Name
                direction,          # Direction (1=X, 2=Y)
                wind_speed_mps,     # Wind speed (m/s)
                exposure_type,      # Exposure category
                importance,         # Importance factor
            )
            _check_ret(
                ret,
                f"AutoWind.SetASCE716('{pattern_name}', dir={direction})",
            )
            logger.info(
                "Wind parameters applied to '%s' (dir=%d, V=%.1f m/s, "
                "exposure=%s, Iw=%.2f).",
                pattern_name,
                direction,
                wind_speed_mps,
                exposure,
                importance,
            )

        return True

    # ----- queries ---------------------------------------------------------

    def get_existing_patterns(self) -> list[str]:
        """Return the names of all load patterns currently in the model.

        Returns:
            Sorted list of load-pattern name strings.
        """
        number_names: int = 0
        names: list[str] = []
        ret, number_names, names = self._model.LoadPatterns.GetNameList(
            number_names,
            names,
        )
        _check_ret(ret, "LoadPatterns.GetNameList")
        return sorted(names)


# ---------------------------------------------------------------------------
# CombinationManager
# ---------------------------------------------------------------------------

@dataclass
class _ComboSpec:
    """Internal specification for a single response combination."""

    name: str
    combo_type: int  # 0 = linear additive, 1 = envelope
    cases: list[tuple[str, float]] = field(default_factory=list)
    sub_combos: list[str] = field(default_factory=list)  # for envelope combos


class CombinationManager:
    """Generate and manage response combinations per SBC 301 / ASCE 7-16.

    Args:
        sap_model: A live ``SapModel`` COM reference.
    """

    # caseType constants for SetCaseList / SetCombList
    _CASE_TYPE_LOAD_CASE: int = 0
    _COMBO_TYPE_LINEAR: int = 0
    _COMBO_TYPE_ENVELOPE: int = 1

    def __init__(self, sap_model: Any) -> None:
        self._model: Any = sap_model

    # ----- private helpers -------------------------------------------------

    def _add_combo(self, spec: _ComboSpec) -> None:
        """Create a single combo and populate its case/combo lists."""
        ret = self._model.RespCombo.Add(spec.name, spec.combo_type)
        _check_ret(ret, f"RespCombo.Add('{spec.name}')")

        for case_name, scale in spec.cases:
            ret = self._model.RespCombo.SetCaseList(
                spec.name,
                self._CASE_TYPE_LOAD_CASE,
                case_name,
                scale,
            )
            _check_ret(
                ret,
                f"RespCombo.SetCaseList('{spec.name}', '{case_name}', SF={scale})",
            )

        for sub_name in spec.sub_combos:
            ret = self._model.RespCombo.SetCaseList(
                spec.name,
                1,  # caseType = 1 → combo
                sub_name,
                1.0,
            )
            _check_ret(
                ret,
                f"RespCombo.SetCaseList('{spec.name}', combo='{sub_name}')",
            )

        logger.info("Combination '%s' created.", spec.name)

    # ----- ULS specs -------------------------------------------------------

    @staticmethod
    def _build_uls_specs(
        include_seismic: bool,
        include_wind: bool,
    ) -> list[_ComboSpec]:
        """Build the list of ULS (strength-design) combo specifications."""
        specs: list[_ComboSpec] = []

        # U1: 1.4D + 1.4SIDL + 1.4WALLS
        specs.append(
            _ComboSpec(
                name="U1",
                combo_type=0,
                cases=[
                    ("DEAD", 1.4),
                    ("SIDL", 1.4),
                    ("WALLS", 1.4),
                ],
            )
        )

        # U2: 1.2D + 1.2SIDL + 1.2WALLS + 1.6L + 0.5Lr
        specs.append(
            _ComboSpec(
                name="U2",
                combo_type=0,
                cases=[
                    ("DEAD", 1.2),
                    ("SIDL", 1.2),
                    ("WALLS", 1.2),
                    ("LIVE", 1.6),
                    ("LIVE_ROOF", 0.5),
                ],
            )
        )

        if include_seismic:
            # U3–U6: gravity + seismic
            specs.append(
                _ComboSpec(
                    name="U3",
                    combo_type=0,
                    cases=[
                        ("DEAD", 1.2),
                        ("SIDL", 1.2),
                        ("WALLS", 1.2),
                        ("LIVE", 1.0),
                        ("EQX", 1.0),
                    ],
                )
            )
            specs.append(
                _ComboSpec(
                    name="U4",
                    combo_type=0,
                    cases=[
                        ("DEAD", 1.2),
                        ("SIDL", 1.2),
                        ("WALLS", 1.2),
                        ("LIVE", 1.0),
                        ("EQX", -1.0),
                    ],
                )
            )
            specs.append(
                _ComboSpec(
                    name="U5",
                    combo_type=0,
                    cases=[
                        ("DEAD", 1.2),
                        ("SIDL", 1.2),
                        ("WALLS", 1.2),
                        ("LIVE", 1.0),
                        ("EQY", 1.0),
                    ],
                )
            )
            specs.append(
                _ComboSpec(
                    name="U6",
                    combo_type=0,
                    cases=[
                        ("DEAD", 1.2),
                        ("SIDL", 1.2),
                        ("WALLS", 1.2),
                        ("LIVE", 1.0),
                        ("EQY", -1.0),
                    ],
                )
            )
            # U7–U10: 0.9D ± seismic (dead-load counteracting)
            specs.append(
                _ComboSpec(
                    name="U7",
                    combo_type=0,
                    cases=[
                        ("DEAD", 0.9),
                        ("SIDL", 0.9),
                        ("EQX", 1.0),
                    ],
                )
            )
            specs.append(
                _ComboSpec(
                    name="U8",
                    combo_type=0,
                    cases=[
                        ("DEAD", 0.9),
                        ("SIDL", 0.9),
                        ("EQX", -1.0),
                    ],
                )
            )
            specs.append(
                _ComboSpec(
                    name="U9",
                    combo_type=0,
                    cases=[
                        ("DEAD", 0.9),
                        ("SIDL", 0.9),
                        ("EQY", 1.0),
                    ],
                )
            )
            specs.append(
                _ComboSpec(
                    name="U10",
                    combo_type=0,
                    cases=[
                        ("DEAD", 0.9),
                        ("SIDL", 0.9),
                        ("EQY", -1.0),
                    ],
                )
            )

        if include_wind:
            # U11–U14: gravity + wind
            specs.append(
                _ComboSpec(
                    name="U11",
                    combo_type=0,
                    cases=[
                        ("DEAD", 1.2),
                        ("SIDL", 1.2),
                        ("WALLS", 1.2),
                        ("LIVE", 1.0),
                        ("WINDX", 1.0),
                    ],
                )
            )
            specs.append(
                _ComboSpec(
                    name="U12",
                    combo_type=0,
                    cases=[
                        ("DEAD", 1.2),
                        ("SIDL", 1.2),
                        ("WALLS", 1.2),
                        ("LIVE", 1.0),
                        ("WINDX", -1.0),
                    ],
                )
            )
            specs.append(
                _ComboSpec(
                    name="U13",
                    combo_type=0,
                    cases=[
                        ("DEAD", 1.2),
                        ("SIDL", 1.2),
                        ("WALLS", 1.2),
                        ("LIVE", 1.0),
                        ("WINDY", 1.0),
                    ],
                )
            )
            specs.append(
                _ComboSpec(
                    name="U14",
                    combo_type=0,
                    cases=[
                        ("DEAD", 1.2),
                        ("SIDL", 1.2),
                        ("WALLS", 1.2),
                        ("LIVE", 1.0),
                        ("WINDY", -1.0),
                    ],
                )
            )

        return specs

    # ----- SLS specs -------------------------------------------------------

    @staticmethod
    def _build_sls_specs(
        include_seismic: bool,
        include_wind: bool,
    ) -> list[_ComboSpec]:
        """Build serviceability (SLS) combo specifications."""
        specs: list[_ComboSpec] = []

        # SLS1: 1.0D + 1.0SIDL + 1.0WALLS + 1.0L
        specs.append(
            _ComboSpec(
                name="SLS1",
                combo_type=0,
                cases=[
                    ("DEAD", 1.0),
                    ("SIDL", 1.0),
                    ("WALLS", 1.0),
                    ("LIVE", 1.0),
                ],
            )
        )

        if include_seismic:
            # SLS2: 1.0D + 1.0SIDL + 1.0WALLS + 0.7EQX
            specs.append(
                _ComboSpec(
                    name="SLS2",
                    combo_type=0,
                    cases=[
                        ("DEAD", 1.0),
                        ("SIDL", 1.0),
                        ("WALLS", 1.0),
                        ("EQX", 0.7),
                    ],
                )
            )
            # SLS3: 1.0D + 1.0SIDL + 1.0WALLS + 0.7EQY
            specs.append(
                _ComboSpec(
                    name="SLS3",
                    combo_type=0,
                    cases=[
                        ("DEAD", 1.0),
                        ("SIDL", 1.0),
                        ("WALLS", 1.0),
                        ("EQY", 0.7),
                    ],
                )
            )

        return specs

    # ----- public API ------------------------------------------------------

    def generate_sbc301_combinations(
        self,
        include_seismic: bool = True,
        include_wind: bool = True,
    ) -> list[str]:
        """Create ULS and SLS response combinations per SBC 301 / ASCE 7-16.

        ULS combinations follow ASCE 7-16 §2.3 (strength design) and SLS
        combinations follow §2.4 (serviceability).  An ``ENVELOPE`` combo is
        appended that envelopes all ULS combinations for design use.

        Args:
            include_seismic: If ``True``, include EQX/EQY combinations (U3-U10).
            include_wind: If ``True``, include WINDX/WINDY combinations (U11-U14).

        Returns:
            Sorted list of all created combination names.
        """
        created_names: list[str] = []

        # --- ULS ---
        uls_specs = self._build_uls_specs(include_seismic, include_wind)
        uls_names: list[str] = []
        for spec in uls_specs:
            self._add_combo(spec)
            created_names.append(spec.name)
            uls_names.append(spec.name)

        # --- ULS ENVELOPE ---
        envelope = _ComboSpec(
            name="ENVELOPE",
            combo_type=self._COMBO_TYPE_ENVELOPE,
            sub_combos=uls_names,
        )
        self._add_combo(envelope)
        created_names.append(envelope.name)

        # --- SLS ---
        sls_specs = self._build_sls_specs(include_seismic, include_wind)
        for spec in sls_specs:
            self._add_combo(spec)
            created_names.append(spec.name)

        logger.info(
            "Generated %d SBC 301 combinations (%d ULS + envelope + %d SLS).",
            len(created_names),
            len(uls_names),
            len(sls_specs),
        )
        return sorted(created_names)

    def delete_all_combinations(self) -> int:
        """Delete every response combination in the model.

        Returns:
            Number of combinations deleted.
        """
        existing = self.get_existing_combinations()
        for name in existing:
            ret = self._model.RespCombo.Delete(name)
            _check_ret(ret, f"RespCombo.Delete('{name}')")
            logger.info("Deleted combination '%s'.", name)
        return len(existing)

    def get_existing_combinations(self) -> list[str]:
        """Return the names of all response combinations in the model.

        Returns:
            Sorted list of combo name strings.
        """
        number_names: int = 0
        names: list[str] = []
        ret, number_names, names = self._model.RespCombo.GetNameList(
            number_names,
            names,
        )
        _check_ret(ret, "RespCombo.GetNameList")
        return sorted(names)
