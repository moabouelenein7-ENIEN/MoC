"""Wall load calculator module for structural engineering workflows.

Calculates distributed wall loads (kN/m) on beams based on wall geometry,
material properties, and opening percentages. Supports applying loads
directly to ETABS frame objects via the OAPI.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any


@dataclass
class WallLoadInput:
    """Input parameters for wall load calculation.

    Attributes:
        story_height_m: Storey height in metres (e.g. 3.0).
        beam_depth_m: Beam depth in metres to deduct from wall height (e.g. 0.60).
        wall_thickness_mm: Wall thickness in millimetres (e.g. 200).
        material_density_kN_m3: Unit weight of the wall material in kN/m³
            (e.g. 12.0 for hollow concrete block).
        plaster_thickness_mm: Plaster thickness on *each* side in mm (default 30).
        plaster_density_kN_m3: Unit weight of plaster in kN/m³ (default 21.0).
        opening_percentage: Fraction of wall area occupied by openings,
            between 0.0 (no openings) and 1.0 (fully open).
    """

    story_height_m: float
    beam_depth_m: float
    wall_thickness_mm: float
    material_density_kN_m3: float
    plaster_thickness_mm: float = 30.0
    plaster_density_kN_m3: float = 21.0
    opening_percentage: float = 0.0

    def __post_init__(self) -> None:
        if not 0.0 <= self.opening_percentage <= 1.0:
            raise ValueError(
                f"opening_percentage must be between 0 and 1, "
                f"got {self.opening_percentage}"
            )
        if self.beam_depth_m >= self.story_height_m:
            raise ValueError(
                f"beam_depth_m ({self.beam_depth_m}) must be less than "
                f"story_height_m ({self.story_height_m})"
            )


@dataclass
class WallLoadResult:
    """Result of a wall load calculation.

    Attributes:
        h_net_m: Net wall height after beam deduction (m).
        w_block_kn_per_m: Load contribution from the block/masonry (kN/m).
        w_plaster_kn_per_m: Load contribution from plaster on both sides (kN/m).
        w_total_kn_per_m: Final distributed load on the beam (kN/m),
            after applying the opening reduction.
        opening_reduction: Fraction of load removed due to openings (0–1).
        summary: Human-readable summary of the calculation.
    """

    h_net_m: float
    w_block_kn_per_m: float
    w_plaster_kn_per_m: float
    w_total_kn_per_m: float
    opening_reduction: float
    summary: str


class WallLoadCalculator:
    """Calculates distributed wall loads on beams and applies them to ETABS.

    Example::

        calc = WallLoadCalculator()
        inp = WallLoadInput(
            story_height_m=3.0,
            beam_depth_m=0.60,
            wall_thickness_mm=200,
            material_density_kN_m3=12.0,
        )
        result = calc.calculate(inp)
        print(result.summary)
    """

    # ------------------------------------------------------------------
    # Core calculation
    # ------------------------------------------------------------------

    def calculate(self, input: WallLoadInput) -> WallLoadResult:
        """Compute the distributed wall load in kN/m.

        The calculation follows the standard approach:

        1. ``H_net = story_height - beam_depth``
        2. ``t_wall_m = wall_thickness_mm / 1000``
        3. ``t_plaster_total_m = 2 * plaster_thickness_mm / 1000``
        4. ``w_block = H_net * t_wall_m * material_density``
        5. ``w_plaster = H_net * t_plaster_total_m * plaster_density``
        6. ``w_total = (w_block + w_plaster) * (1 - opening_percentage)``

        Args:
            input: A :class:`WallLoadInput` instance with wall parameters.

        Returns:
            A :class:`WallLoadResult` with all intermediate values and
            the final ``w_total`` in kN/m.
        """

        h_net: float = input.story_height_m - input.beam_depth_m
        t_wall_m: float = input.wall_thickness_mm / 1000.0
        t_plaster_total_m: float = 2.0 * input.plaster_thickness_mm / 1000.0

        w_block: float = h_net * t_wall_m * input.material_density_kN_m3
        w_plaster: float = h_net * t_plaster_total_m * input.plaster_density_kN_m3

        w_total: float = (w_block + w_plaster) * (1.0 - input.opening_percentage)

        summary_lines: list[str] = [
            "=== Wall Load Calculation ===",
            f"  Story height        : {input.story_height_m:.2f} m",
            f"  Beam depth          : {input.beam_depth_m:.2f} m",
            f"  Net wall height     : {h_net:.2f} m",
            f"  Wall thickness      : {input.wall_thickness_mm:.0f} mm",
            f"  Material density    : {input.material_density_kN_m3:.1f} kN/m³",
            f"  Plaster (each side) : {input.plaster_thickness_mm:.0f} mm",
            f"  Plaster density     : {input.plaster_density_kN_m3:.1f} kN/m³",
            f"  Opening fraction    : {input.opening_percentage:.0%}",
            "",
            f"  w_block             : {w_block:.3f} kN/m",
            f"  w_plaster           : {w_plaster:.3f} kN/m",
            f"  w_total             : {w_total:.3f} kN/m",
        ]

        return WallLoadResult(
            h_net_m=h_net,
            w_block_kn_per_m=w_block,
            w_plaster_kn_per_m=w_plaster,
            w_total_kn_per_m=w_total,
            opening_reduction=input.opening_percentage,
            summary="\n".join(summary_lines),
        )

    # ------------------------------------------------------------------
    # ETABS integration
    # ------------------------------------------------------------------

    def apply_to_etabs(
        self,
        sap_model: Any,
        frame_names: list[str],
        load_kn_per_m: float,
        load_pattern: str = "WALLS",
        direction: int = 6,
    ) -> int:
        """Apply a uniform distributed load to ETABS frame objects.

        Uses ``SapModel.FrameObj.SetLoadDistributed`` to assign the load
        along the full length of each frame.

        Args:
            sap_model: An active ``SapModel`` COM object.
            frame_names: List of frame object names to receive the load.
            load_kn_per_m: Magnitude of the distributed load (kN/m).
            load_pattern: Name of the load pattern (default ``'WALLS'``).
            direction: Load direction code.  ``6`` corresponds to gravity
                (−Z global) in the ETABS API.

        Returns:
            The number of frames to which the load was successfully applied.
        """

        success_count: int = 0

        for frame_name in frame_names:
            # SetLoadDistributed(Name, LoadPat, MyType, Dir,
            #   Dist1, Dist2, Val1, Val2, CSys, RelDist, Replace)
            ret: int = sap_model.FrameObj.SetLoadDistributed(
                frame_name,       # Frame name
                load_pattern,     # Load pattern name
                1,                # MyType: 1 = Force per unit length
                direction,        # Direction: 6 = Gravity (-Z global)
                0.0,              # Dist1: start relative distance
                1.0,              # Dist2: end relative distance
                load_kn_per_m,    # Val1: load at start
                load_kn_per_m,    # Val2: load at end
                "Global",         # Coordinate system
                True,             # RelDist: distances are relative (0–1)
                True,             # Replace: replace existing loads of same pattern
            )
            if ret == 0:
                success_count += 1

        return success_count

    # ------------------------------------------------------------------
    # Common presets
    # ------------------------------------------------------------------

    def get_common_presets(self) -> list[dict[str, Any]]:
        """Return a list of commonly used wall configurations.

        Each dict contains the preset ``name`` and a pre-built
        :class:`WallLoadInput`.  The caller can adjust fields (e.g.
        ``story_height_m``) before passing to :meth:`calculate`.

        Returns:
            A list of dicts with keys ``'name'`` and ``'input'``.
        """

        return [
            {
                "name": "Exterior 200mm Hollow Block",
                "input": WallLoadInput(
                    story_height_m=3.0,
                    beam_depth_m=0.60,
                    wall_thickness_mm=200,
                    material_density_kN_m3=12.0,
                    plaster_thickness_mm=30.0,
                    plaster_density_kN_m3=21.0,
                    opening_percentage=0.0,
                ),
            },
            {
                "name": "Interior 150mm Hollow Block",
                "input": WallLoadInput(
                    story_height_m=3.0,
                    beam_depth_m=0.60,
                    wall_thickness_mm=150,
                    material_density_kN_m3=12.0,
                    plaster_thickness_mm=15.0,
                    plaster_density_kN_m3=21.0,
                    opening_percentage=0.0,
                ),
            },
            {
                "name": "Exterior 200mm Insulated",
                "input": WallLoadInput(
                    story_height_m=3.0,
                    beam_depth_m=0.60,
                    wall_thickness_mm=200,
                    material_density_kN_m3=10.0,
                    plaster_thickness_mm=30.0,
                    plaster_density_kN_m3=21.0,
                    opening_percentage=0.0,
                ),
            },
            {
                "name": "Parapet 200mm",
                "input": WallLoadInput(
                    story_height_m=1.0,
                    beam_depth_m=0.0,
                    wall_thickness_mm=200,
                    material_density_kN_m3=12.0,
                    plaster_thickness_mm=30.0,
                    plaster_density_kN_m3=21.0,
                    opening_percentage=0.0,
                ),
            },
            {
                "name": "AAC 200mm",
                "input": WallLoadInput(
                    story_height_m=3.0,
                    beam_depth_m=0.60,
                    wall_thickness_mm=200,
                    material_density_kN_m3=6.0,
                    plaster_thickness_mm=20.0,
                    plaster_density_kN_m3=21.0,
                    opening_percentage=0.0,
                ),
            },
        ]
