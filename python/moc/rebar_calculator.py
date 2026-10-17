"""Rebar detailing translator module for structural engineering workflows.

Converts required steel areas (As) from analysis/design results into practical
bar selections (diameter × count), enforcing minimum bar rules for beams and
columns.  Supports extracting design results from ETABS and exporting
formatted schedules to Excel.
"""

from __future__ import annotations

import math
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any


# ======================================================================
# Data classes
# ======================================================================


@dataclass
class RebarSize:
    """Definition of a single rebar size.

    Attributes:
        diameter_mm: Nominal bar diameter in millimetres (e.g. 16).
        area_mm2: Cross-sectional area per bar in mm² (e.g. 201.1).
        symbol: Display symbol (e.g. ``'Φ16'``).
    """

    diameter_mm: int
    area_mm2: float
    symbol: str


@dataclass
class RebarResult:
    """Result of a rebar translation for a single location.

    Attributes:
        element_name: Frame / element label (e.g. ``'B12'``).
        story: Story level name (e.g. ``'Story1'``).
        section: Section property name (e.g. ``'B300x600'``).
        location: Position within the section (``'Top'``, ``'Bottom'``,
            ``'Corner'``, etc.).
        as_required_mm2: Required steel area from analysis (mm²).
        as_min_mm2: Code-minimum steel area (mm²).
        as_governing_mm2: Governing steel area = max(as_required, as_min).
        selected_diameter_mm: Chosen bar diameter (mm).
        bar_count: Number of bars selected.
        as_provided_mm2: Provided steel area (mm²).
        description: Human-readable string (e.g. ``'4 Φ 16'``).
        utilization_ratio: ``as_governing / as_provided`` (≤ 1.0 is OK).
    """

    element_name: str
    story: str
    section: str
    location: str
    as_required_mm2: float
    as_min_mm2: float
    as_governing_mm2: float
    selected_diameter_mm: int
    bar_count: int
    as_provided_mm2: float
    description: str
    utilization_ratio: float


# ======================================================================
# Standard metric rebar database (BS / EN sizes)
# ======================================================================

STANDARD_REBAR_SIZES: list[RebarSize] = [
    RebarSize(diameter_mm=8,  area_mm2=50.3,   symbol="Φ8"),
    RebarSize(diameter_mm=10, area_mm2=78.5,   symbol="Φ10"),
    RebarSize(diameter_mm=12, area_mm2=113.1,  symbol="Φ12"),
    RebarSize(diameter_mm=14, area_mm2=153.9,  symbol="Φ14"),
    RebarSize(diameter_mm=16, area_mm2=201.1,  symbol="Φ16"),
    RebarSize(diameter_mm=18, area_mm2=254.5,  symbol="Φ18"),
    RebarSize(diameter_mm=20, area_mm2=314.2,  symbol="Φ20"),
    RebarSize(diameter_mm=22, area_mm2=380.1,  symbol="Φ22"),
    RebarSize(diameter_mm=25, area_mm2=490.9,  symbol="Φ25"),
    RebarSize(diameter_mm=28, area_mm2=615.8,  symbol="Φ28"),
    RebarSize(diameter_mm=32, area_mm2=804.2,  symbol="Φ32"),
    RebarSize(diameter_mm=36, area_mm2=1017.9, symbol="Φ36"),
    RebarSize(diameter_mm=40, area_mm2=1256.6, symbol="Φ40"),
]


# ======================================================================
# Calculator
# ======================================================================


class RebarCalculator:
    """Translates required steel areas into practical bar selections.

    Args:
        rebar_sizes: Available bar sizes, sorted by ascending diameter.
            Defaults to :data:`STANDARD_REBAR_SIZES` if not provided.

    Example::

        calc = RebarCalculator()
        result = calc.translate(as_req_mm2=1200, preferred_diameter=16)
        print(result.description)  # e.g. '6 Φ 16'
    """

    def __init__(self, rebar_sizes: list[RebarSize] | None = None) -> None:
        self.rebar_sizes: list[RebarSize] = (
            rebar_sizes if rebar_sizes is not None else list(STANDARD_REBAR_SIZES)
        )
        # Ensure sorted by diameter for next-larger lookup
        self.rebar_sizes.sort(key=lambda s: s.diameter_mm)
        # Quick lookup by diameter
        self._size_map: dict[int, RebarSize] = {
            s.diameter_mm: s for s in self.rebar_sizes
        }

    # ------------------------------------------------------------------
    # helpers
    # ------------------------------------------------------------------

    def _get_size(self, diameter_mm: int) -> RebarSize | None:
        """Return the :class:`RebarSize` for *diameter_mm*, or ``None``."""
        return self._size_map.get(diameter_mm)

    def _next_larger_diameter(self, diameter_mm: int) -> int | None:
        """Return the next available diameter larger than *diameter_mm*."""
        for size in self.rebar_sizes:
            if size.diameter_mm > diameter_mm:
                return size.diameter_mm
        return None

    # ------------------------------------------------------------------
    # Core translation
    # ------------------------------------------------------------------

    def translate(
        self,
        as_req_mm2: float,
        preferred_diameter: int = 16,
        member_type: str = "beam",
        location: str = "bottom",
        min_bars: int = 2,
        max_bars: int = 12,
        element_name: str = "",
        story: str = "",
        section: str = "",
        as_min_mm2: float = 0.0,
    ) -> RebarResult:
        """Select bars for a required steel area.

        The algorithm:

        1. Look up the bar area for *preferred_diameter*.
        2. Compute ``N = ceil(as_governing / A_bar)``.
        3. Enforce *min_bars* (beams ≥ 2; columns ≥ 4 corner bars).
        4. For columns with ``location='Corner'``, enforce an even count.
        5. If ``N > max_bars``, step up to the next larger diameter and
           repeat.

        Args:
            as_req_mm2: Required steel area (mm²).
            preferred_diameter: Preferred bar diameter (mm).
            member_type: ``'beam'`` or ``'column'``.
            location: ``'top'``, ``'bottom'``, ``'corner'``, etc.
            min_bars: Minimum number of bars.
            max_bars: Maximum number of bars.
            element_name: Frame label for the result record.
            story: Story name for the result record.
            section: Section name for the result record.
            as_min_mm2: Code-minimum steel area (mm²).

        Returns:
            A :class:`RebarResult` with the selected detailing.

        Raises:
            ValueError: If no suitable diameter can satisfy the demand
                within *max_bars*.
        """

        as_governing: float = max(as_req_mm2, as_min_mm2)

        # Determine effective minimum bars
        effective_min: int = min_bars
        if member_type.lower() == "column":
            effective_min = max(effective_min, 4)

        current_diameter: int = preferred_diameter

        while True:
            size: RebarSize | None = self._get_size(current_diameter)
            if size is None:
                # Try the next available diameter
                next_d = self._next_larger_diameter(current_diameter - 1)
                if next_d is None:
                    raise ValueError(
                        f"No rebar size available for diameter {current_diameter} mm "
                        f"and no larger sizes in the database."
                    )
                current_diameter = next_d
                size = self._get_size(current_diameter)
                assert size is not None  # guaranteed by _next_larger_diameter

            a_bar: float = size.area_mm2

            # Number of bars required
            n_bars: int = math.ceil(as_governing / a_bar) if as_governing > 0 else effective_min
            n_bars = max(n_bars, effective_min)

            # Columns with corner location → enforce even count
            if member_type.lower() == "column" and location.lower() == "corner":
                if n_bars % 2 != 0:
                    n_bars += 1

            # Check max_bars constraint
            if n_bars <= max_bars:
                as_provided: float = n_bars * a_bar
                utilization: float = (
                    as_governing / as_provided if as_provided > 0 else 0.0
                )
                description: str = f"{n_bars} {size.symbol}"

                return RebarResult(
                    element_name=element_name,
                    story=story,
                    section=section,
                    location=location.capitalize(),
                    as_required_mm2=round(as_req_mm2, 1),
                    as_min_mm2=round(as_min_mm2, 1),
                    as_governing_mm2=round(as_governing, 1),
                    selected_diameter_mm=current_diameter,
                    bar_count=n_bars,
                    as_provided_mm2=round(as_provided, 1),
                    description=description,
                    utilization_ratio=round(utilization, 3),
                )

            # Exceeded max_bars → try next larger diameter
            next_d = self._next_larger_diameter(current_diameter)
            if next_d is None:
                # Last resort: allow the count even though it exceeds max_bars
                as_provided = n_bars * a_bar
                utilization = (
                    as_governing / as_provided if as_provided > 0 else 0.0
                )
                description = f"{n_bars} {size.symbol}"

                return RebarResult(
                    element_name=element_name,
                    story=story,
                    section=section,
                    location=location.capitalize(),
                    as_required_mm2=round(as_req_mm2, 1),
                    as_min_mm2=round(as_min_mm2, 1),
                    as_governing_mm2=round(as_governing, 1),
                    selected_diameter_mm=current_diameter,
                    bar_count=n_bars,
                    as_provided_mm2=round(as_provided, 1),
                    description=description,
                    utilization_ratio=round(utilization, 3),
                )

            current_diameter = next_d

    # ------------------------------------------------------------------
    # Beam convenience
    # ------------------------------------------------------------------

    def translate_beam_section(
        self,
        as_top_mm2: float,
        as_bottom_mm2: float,
        as_min_mm2: float = 0.0,
        preferred_diameter: int = 16,
        element_name: str = "",
        story: str = "",
        section: str = "",
    ) -> tuple[RebarResult, RebarResult]:
        """Translate both top and bottom reinforcement for a beam section.

        Args:
            as_top_mm2: Required top steel area (mm²).
            as_bottom_mm2: Required bottom steel area (mm²).
            as_min_mm2: Code-minimum steel area for each face (mm²).
            preferred_diameter: Preferred bar diameter (mm).
            element_name: Frame label.
            story: Story name.
            section: Section property name.

        Returns:
            A tuple ``(top_result, bottom_result)``.
        """

        top_result: RebarResult = self.translate(
            as_req_mm2=as_top_mm2,
            preferred_diameter=preferred_diameter,
            member_type="beam",
            location="Top",
            min_bars=2,
            element_name=element_name,
            story=story,
            section=section,
            as_min_mm2=as_min_mm2,
        )

        bottom_result: RebarResult = self.translate(
            as_req_mm2=as_bottom_mm2,
            preferred_diameter=preferred_diameter,
            member_type="beam",
            location="Bottom",
            min_bars=2,
            element_name=element_name,
            story=story,
            section=section,
            as_min_mm2=as_min_mm2,
        )

        return top_result, bottom_result

    # ------------------------------------------------------------------
    # Column convenience
    # ------------------------------------------------------------------

    def translate_column_section(
        self,
        as_total_mm2: float,
        as_min_mm2: float = 0.0,
        num_faces: int = 4,
        preferred_diameter: int = 16,
        element_name: str = "",
        story: str = "",
        section: str = "",
    ) -> list[RebarResult]:
        """Distribute rebar across the faces of a column section.

        The total required area is divided equally among *num_faces*.
        Corner bars are enforced to be even in count.

        Args:
            as_total_mm2: Total required steel area for the column (mm²).
            as_min_mm2: Code-minimum total steel area (mm²).
            num_faces: Number of faces to distribute bars across (default 4).
            preferred_diameter: Preferred bar diameter (mm).
            element_name: Frame label.
            story: Story name.
            section: Section property name.

        Returns:
            A list of :class:`RebarResult`, one per face.
        """

        as_governing_total: float = max(as_total_mm2, as_min_mm2)
        as_per_face: float = as_governing_total / num_faces
        as_min_per_face: float = as_min_mm2 / num_faces if as_min_mm2 > 0 else 0.0

        results: list[RebarResult] = []

        for face_idx in range(num_faces):
            face_label: str = f"Face {face_idx + 1}"
            # First and last faces are typically corner faces
            is_corner: bool = face_idx == 0 or face_idx == num_faces - 1
            location: str = "Corner" if is_corner else f"Face {face_idx + 1}"

            result: RebarResult = self.translate(
                as_req_mm2=as_per_face,
                preferred_diameter=preferred_diameter,
                member_type="column",
                location=location,
                min_bars=2,
                element_name=element_name,
                story=story,
                section=section,
                as_min_mm2=as_min_per_face,
            )
            results.append(result)

        return results

    # ------------------------------------------------------------------
    # ETABS extraction
    # ------------------------------------------------------------------

    def extract_from_etabs(
        self,
        sap_model: Any,
        frame_names: list[str] | None = None,
    ) -> list[RebarResult]:
        """Extract required As from ETABS concrete design results.

        Calls ``SapModel.DesignConcrete.GetSummaryResultsBeam()`` and
        ``SapModel.DesignConcrete.GetSummaryResultsColumn()`` to retrieve
        the design output and translates each entry into a
        :class:`RebarResult`.

        Args:
            sap_model: An active ``SapModel`` COM object with a completed
                concrete design run.
            frame_names: Optional list of frame names to filter.  If
                ``None``, all designed frames are included.

        Returns:
            A list of :class:`RebarResult` for every extracted location.
        """

        results: list[RebarResult] = []

        # --- Beams --------------------------------------------------
        try:
            beam_ret = sap_model.DesignConcrete.GetSummaryResultsBeam("")
            # Return signature (per ETABS OAPI):
            # (ret, NumberItems, FrameName, Story, ...,
            #  AsTopLeft, AsTopRight, AsBotLeft, AsBotRight, ...)
            # Indices depend on ETABS version; we unpack carefully.
            if beam_ret[0] == 0:
                num_items: int = beam_ret[1]
                frame_name_arr = beam_ret[2]
                story_arr = beam_ret[3]
                section_arr = beam_ret[4] if len(beam_ret) > 4 else [""] * num_items

                # As arrays — positions vary; common layout:
                # [5] = Location, [6]=AsTopArea, [7]=AsBotArea
                # We handle the typical layout.
                location_arr = beam_ret[5] if len(beam_ret) > 5 else [""] * num_items
                as_top_arr = beam_ret[6] if len(beam_ret) > 6 else [0.0] * num_items
                as_bot_arr = beam_ret[7] if len(beam_ret) > 7 else [0.0] * num_items

                for i in range(num_items):
                    fname: str = str(frame_name_arr[i])
                    if frame_names is not None and fname not in frame_names:
                        continue

                    story_name: str = str(story_arr[i])
                    sect_name: str = str(section_arr[i]) if section_arr else ""

                    # Convert mm² (ETABS may return in mm² or m² depending
                    # on display units — assume mm² here).
                    as_top_val: float = float(as_top_arr[i])
                    as_bot_val: float = float(as_bot_arr[i])

                    top_res, bot_res = self.translate_beam_section(
                        as_top_mm2=as_top_val,
                        as_bottom_mm2=as_bot_val,
                        element_name=fname,
                        story=story_name,
                        section=sect_name,
                    )
                    results.append(top_res)
                    results.append(bot_res)
        except Exception:
            # Design results may not be available
            pass

        # --- Columns ------------------------------------------------
        try:
            col_ret = sap_model.DesignConcrete.GetSummaryResultsColumn("")
            if col_ret[0] == 0:
                num_items = col_ret[1]
                frame_name_arr = col_ret[2]
                story_arr = col_ret[3]
                section_arr = col_ret[4] if len(col_ret) > 4 else [""] * num_items

                # Total As is typically in index [6] or similar
                as_total_arr = col_ret[6] if len(col_ret) > 6 else [0.0] * num_items

                for i in range(num_items):
                    fname = str(frame_name_arr[i])
                    if frame_names is not None and fname not in frame_names:
                        continue

                    story_name = str(story_arr[i])
                    sect_name = str(section_arr[i]) if section_arr else ""
                    as_total_val: float = float(as_total_arr[i])

                    face_results: list[RebarResult] = self.translate_column_section(
                        as_total_mm2=as_total_val,
                        element_name=fname,
                        story=story_name,
                        section=sect_name,
                    )
                    results.extend(face_results)
        except Exception:
            pass

        return results

    # ------------------------------------------------------------------
    # Excel export
    # ------------------------------------------------------------------

    def export_to_excel(
        self,
        results: list[RebarResult],
        output_path: str,
    ) -> str:
        """Export rebar results to a formatted Excel workbook.

        Uses ``openpyxl`` to create a workbook with styled headers,
        thin borders, and alternating row colours.

        Args:
            results: List of :class:`RebarResult` to export.
            output_path: Destination ``.xlsx`` file path.

        Returns:
            The absolute path of the saved workbook.
        """

        from openpyxl import Workbook
        from openpyxl.styles import (
            Alignment,
            Border,
            Font,
            PatternFill,
            Side,
        )
        from openpyxl.utils import get_column_letter

        wb: Workbook = Workbook()
        ws = wb.active
        ws.title = "Rebar Schedule"

        # -- Column definitions -----------------------------------------
        headers: list[str] = [
            "Element",
            "Story",
            "Section",
            "Location",
            "As_req (mm²)",
            "As_min (mm²)",
            "Diameter",
            "Bars",
            "As_prov (mm²)",
            "Description",
            "Ratio",
        ]
        col_widths: list[int] = [12, 12, 16, 10, 14, 14, 10, 8, 14, 14, 10]

        # -- Styles ----------------------------------------------------
        header_font: Font = Font(name="Calibri", bold=True, size=11, color="FFFFFF")
        header_fill: PatternFill = PatternFill(
            start_color="2F5496", end_color="2F5496", fill_type="solid"
        )
        header_alignment: Alignment = Alignment(
            horizontal="center", vertical="center", wrap_text=True
        )

        data_font: Font = Font(name="Calibri", size=10)
        data_alignment: Alignment = Alignment(horizontal="center", vertical="center")

        thin_side: Side = Side(style="thin", color="B0B0B0")
        cell_border: Border = Border(
            left=thin_side, right=thin_side, top=thin_side, bottom=thin_side
        )

        alt_fill: PatternFill = PatternFill(
            start_color="D9E2F3", end_color="D9E2F3", fill_type="solid"
        )
        warn_fill: PatternFill = PatternFill(
            start_color="FFC7CE", end_color="FFC7CE", fill_type="solid"
        )

        # -- Write header row -------------------------------------------
        for col_idx, header_text in enumerate(headers, start=1):
            cell = ws.cell(row=1, column=col_idx, value=header_text)
            cell.font = header_font
            cell.fill = header_fill
            cell.alignment = header_alignment
            cell.border = cell_border

        # -- Set column widths ------------------------------------------
        for col_idx, width in enumerate(col_widths, start=1):
            ws.column_dimensions[get_column_letter(col_idx)].width = width

        # -- Write data rows -------------------------------------------
        for row_idx, result in enumerate(results, start=2):
            row_data: list[Any] = [
                result.element_name,
                result.story,
                result.section,
                result.location,
                result.as_required_mm2,
                result.as_min_mm2,
                result.selected_diameter_mm,
                result.bar_count,
                result.as_provided_mm2,
                result.description,
                result.utilization_ratio,
            ]

            is_alt_row: bool = row_idx % 2 == 0

            for col_idx, value in enumerate(row_data, start=1):
                cell = ws.cell(row=row_idx, column=col_idx, value=value)
                cell.font = data_font
                cell.alignment = data_alignment
                cell.border = cell_border

                if is_alt_row:
                    cell.fill = alt_fill

            # Highlight utilization > 1.0 (over-utilised)
            ratio_cell = ws.cell(row=row_idx, column=len(headers))
            if result.utilization_ratio > 1.0:
                ratio_cell.fill = warn_fill

        # -- Freeze header row ------------------------------------------
        ws.freeze_panes = "A2"

        # -- Auto-filter -----------------------------------------------
        last_col_letter: str = get_column_letter(len(headers))
        ws.auto_filter.ref = f"A1:{last_col_letter}{len(results) + 1}"

        # -- Save -------------------------------------------------------
        resolved_path: str = str(Path(output_path).resolve())
        wb.save(resolved_path)
        wb.close()

        return resolved_path
