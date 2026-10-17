"""DXF Structural Importer for ETABS.

Parses architectural/structural DXF drawings and generates ETABS model
elements (columns, beams, walls, slabs, openings) via the CSI OAPI.

Dependencies:
    - ezdxf  (pip install ezdxf)
    - comtypes (for ETABS COM interaction)
"""

from __future__ import annotations

import math
import re
from dataclasses import dataclass, field
from typing import Any, List, Optional, Sequence, Tuple

import ezdxf
from ezdxf.document import Drawing
from ezdxf.entities import LWPolyline, Line
from ezdxf.layouts import BaseLayout


# ---------------------------------------------------------------------------
# Data classes
# ---------------------------------------------------------------------------

@dataclass
class ColumnData:
    """Parsed column rectangle from a DXF closed LWPOLYLINE."""

    centroid_x: float
    centroid_y: float
    width: float  # dimension along local-x after un-rotating
    depth: float  # dimension along local-y after un-rotating
    rotation_deg: float  # rotation of the first edge relative to +X (degrees)
    layer: str = ""

    def __repr__(self) -> str:
        return (
            f"ColumnData(cx={self.centroid_x:.4f}, cy={self.centroid_y:.4f}, "
            f"w={self.width:.4f}, d={self.depth:.4f}, rot={self.rotation_deg:.2f}°)"
        )


@dataclass
class BeamData:
    """Parsed beam centreline from a LINE or open LWPOLYLINE."""

    start_x: float
    start_y: float
    end_x: float
    end_y: float
    layer: str = ""

    @property
    def length(self) -> float:
        return math.hypot(self.end_x - self.start_x, self.end_y - self.start_y)


@dataclass
class WallData:
    """Parsed wall centreline."""

    start_x: float
    start_y: float
    end_x: float
    end_y: float
    layer: str = ""

    @property
    def length(self) -> float:
        return math.hypot(self.end_x - self.start_x, self.end_y - self.start_y)


@dataclass
class SlabData:
    """Parsed slab boundary polygon."""

    vertices_x: list[float] = field(default_factory=list)
    vertices_y: list[float] = field(default_factory=list)
    layer: str = ""

    @property
    def num_points(self) -> int:
        return len(self.vertices_x)


@dataclass
class OpeningData:
    """Parsed opening polygon (to be subtracted from a slab)."""

    vertices_x: list[float] = field(default_factory=list)
    vertices_y: list[float] = field(default_factory=list)
    layer: str = ""

    @property
    def num_points(self) -> int:
        return len(self.vertices_x)


@dataclass
class LayerInfo:
    """Summary information for a single DXF layer."""

    name: str
    entity_count: int
    detected_role: str  # auto-detected structural role
    color: int = 7


# ---------------------------------------------------------------------------
# Constants / helpers
# ---------------------------------------------------------------------------

_VALID_ROLES = frozenset({
    "column", "beam", "wall", "slab", "opening", "grid", "foundation", "ignore",
})

_ROLE_PATTERNS: list[tuple[str, re.Pattern[str]]] = [
    ("column",     re.compile(r"\b(COL|COLUMN|COLUMNS|CLM)\b", re.IGNORECASE)),
    ("beam",       re.compile(r"\b(BM|BEAM|BEAMS|BMX|BMY)\b", re.IGNORECASE)),
    ("wall",       re.compile(r"\b(WALL|WALLS|WL|SW|SHEARWALL|SHEAR[\-_]?WALL)\b", re.IGNORECASE)),
    ("slab",       re.compile(r"\b(SLAB|SLABS|SL|DECK|FLOOR|FLR)\b", re.IGNORECASE)),
    ("opening",    re.compile(r"\b(OPENING|OPENINGS|HOLE|VOID|CUTOUT)\b", re.IGNORECASE)),
    ("grid",       re.compile(r"\b(GRID|GRIDS|AXIS|AXES)\b", re.IGNORECASE)),
    ("foundation", re.compile(r"\b(FND|FOUND|FOUNDATION|FOOTING|FTNG|RAFT)\b", re.IGNORECASE)),
]

# Threshold in model-units to decide mm vs m.  If the bounding-box diagonal of
# all entities exceeds this value we assume the drawing is in millimetres.
_MM_DETECTION_THRESHOLD = 500.0  # if max extent > 500 → likely mm


def _angle_between(p1: tuple[float, float], p2: tuple[float, float]) -> float:
    """Return angle in degrees from *p1* to *p2* measured from +X axis."""
    dx = p2[0] - p1[0]
    dy = p2[1] - p1[1]
    return math.degrees(math.atan2(dy, dx))


def _distance(p1: tuple[float, float], p2: tuple[float, float]) -> float:
    return math.hypot(p2[0] - p1[0], p2[1] - p1[1])


def _vertices_from_lwpolyline(entity: LWPolyline) -> list[tuple[float, float]]:
    """Return 2-D vertices (x, y) from an LWPOLYLINE, stripping bulge/width info."""
    return [(v[0], v[1]) for v in entity.get_points(format="xy")]


def _is_closed_rectangle(verts: list[tuple[float, float]], tol: float = 1e-3) -> bool:
    """Check that *verts* form a closed 4-vertex rectangle (within tolerance).

    A rectangle is confirmed when:
    * exactly 4 unique vertices (closing vertex may duplicate the first)
    * both diagonals are equal in length
    * all interior angles ≈ 90°
    """
    pts = list(verts)
    # Remove duplicate closing vertex
    if len(pts) == 5 and _distance(pts[0], pts[4]) < tol:
        pts = pts[:4]
    if len(pts) != 4:
        return False

    # Diagonal check
    d1 = _distance(pts[0], pts[2])
    d2 = _distance(pts[1], pts[3])
    if abs(d1 - d2) > tol * max(d1, d2, 1.0):
        return False

    # Right-angle check via dot product on consecutive edges
    for i in range(4):
        ax = pts[(i + 1) % 4][0] - pts[i][0]
        ay = pts[(i + 1) % 4][1] - pts[i][1]
        bx = pts[(i + 2) % 4][0] - pts[(i + 1) % 4][0]
        by = pts[(i + 2) % 4][1] - pts[(i + 1) % 4][1]
        dot = ax * bx + ay * by
        cross = abs(ax * by - ay * bx)
        edge_product = math.hypot(ax, ay) * math.hypot(bx, by)
        if edge_product < tol:
            return False
        if abs(dot) / edge_product > 0.05:  # cos(angle) should be ≈ 0
            return False
    return True


# ---------------------------------------------------------------------------
# DxfStructuralParser
# ---------------------------------------------------------------------------

class DxfStructuralParser:
    """Parse a DXF drawing and extract structural element geometry.

    Usage::

        parser = DxfStructuralParser("plan.dxf")
        layers = parser.get_layers()
        parser.set_layer_mapping({"S-COLUMN": "column", "S-BEAM": "beam"})
        columns = parser.extract_columns(["S-COLUMN"])
    """

    def __init__(self, dxf_path: str) -> None:
        """Load a DXF file.

        Args:
            dxf_path: Absolute or relative path to the ``.dxf`` file.

        Raises:
            FileNotFoundError: If the file does not exist.
            ezdxf.DXFError: If the file cannot be parsed.
        """
        self._doc: Drawing = ezdxf.readfile(dxf_path)
        self._msp: BaseLayout = self._doc.modelspace()
        self._layer_mapping: dict[str, str] = {}
        self._scale: float = self._detect_scale()

    # ----- scale detection ----------------------------------------------------

    def _detect_scale(self) -> float:
        """Return a multiplier to convert DXF units to **metres**.

        Heuristic: if the bounding-box diagonal of all entities exceeds
        ``_MM_DETECTION_THRESHOLD`` the drawing is assumed to be in mm and the
        scale is set to 0.001.  Otherwise the drawing is treated as metres
        (scale = 1.0).
        """
        min_x = min_y = float("inf")
        max_x = max_y = float("-inf")

        for entity in self._msp:
            try:
                bbox = entity.dxf
                if hasattr(bbox, "insert"):
                    px, py = bbox.insert.x, bbox.insert.y
                    min_x, max_x = min(min_x, px), max(max_x, px)
                    min_y, max_y = min(min_y, py), max(max_y, py)
                if hasattr(bbox, "start"):
                    px, py = bbox.start.x, bbox.start.y
                    min_x, max_x = min(min_x, px), max(max_x, px)
                    min_y, max_y = min(min_y, py), max(max_y, py)
                if hasattr(bbox, "end"):
                    px, py = bbox.end.x, bbox.end.y
                    min_x, max_x = min(min_x, px), max(max_x, px)
                    min_y, max_y = min(min_y, py), max(max_y, py)
            except Exception:
                continue

        # Also scan LWPOLYLINE vertices for a tighter bound
        for entity in self._msp.query("LWPOLYLINE"):
            for vx, vy in _vertices_from_lwpolyline(entity):
                min_x, max_x = min(min_x, vx), max(max_x, vx)
                min_y, max_y = min(min_y, vy), max(max_y, vy)

        if min_x == float("inf"):
            return 1.0  # empty drawing

        diag = math.hypot(max_x - min_x, max_y - min_y)
        if diag > _MM_DETECTION_THRESHOLD:
            return 0.001  # mm → m
        return 1.0  # already in metres

    @property
    def scale(self) -> float:
        """Multiplier applied to convert DXF coordinate units to metres."""
        return self._scale

    @scale.setter
    def scale(self, value: float) -> None:
        self._scale = value

    # ----- layer inspection ---------------------------------------------------

    def get_layers(self) -> list[LayerInfo]:
        """Return information about every layer that contains at least one entity.

        Returns:
            Sorted list of :class:`LayerInfo` objects.
        """
        counts: dict[str, int] = {}
        for entity in self._msp:
            layer_name: str = entity.dxf.layer
            counts[layer_name] = counts.get(layer_name, 0) + 1

        result: list[LayerInfo] = []
        for name, count in sorted(counts.items()):
            layer_table_entry = self._doc.layers.get(name)
            color = layer_table_entry.color if layer_table_entry is not None else 7
            role = self.auto_detect_layer_role(name)
            result.append(LayerInfo(name=name, entity_count=count, detected_role=role, color=color))
        return result

    def auto_detect_layer_role(self, layer_name: str) -> str:
        """Classify *layer_name* into a structural role using regex patterns.

        Returns:
            One of ``'column'``, ``'beam'``, ``'wall'``, ``'slab'``,
            ``'opening'``, ``'grid'``, ``'foundation'``, or ``'ignore'``.
        """
        for role, pattern in _ROLE_PATTERNS:
            if pattern.search(layer_name):
                return role
        return "ignore"

    def set_layer_mapping(self, mapping: dict[str, str]) -> None:
        """Manually map layer names to structural roles.

        Args:
            mapping: ``{layer_name: role}`` where *role* is one of
                ``'column'``, ``'beam'``, ``'wall'``, ``'slab'``,
                ``'opening'``, ``'grid'``, ``'foundation'``, ``'ignore'``.

        Raises:
            ValueError: If an invalid role string is provided.
        """
        for layer_name, role in mapping.items():
            if role not in _VALID_ROLES:
                raise ValueError(
                    f"Invalid role '{role}' for layer '{layer_name}'. "
                    f"Valid roles: {sorted(_VALID_ROLES)}"
                )
        self._layer_mapping.update(mapping)

    # ----- entity extraction helpers ------------------------------------------

    def _iter_entities(self, layer_names: list[str], dxf_type: str | None = None):
        """Yield entities on the given layers, optionally filtered by DXF type."""
        layer_set = set(layer_names)
        for entity in self._msp:
            if entity.dxf.layer not in layer_set:
                continue
            if dxf_type is not None and entity.dxftype() != dxf_type:
                continue
            yield entity

    # ----- column extraction --------------------------------------------------

    def extract_columns(self, layer_names: list[str]) -> list[ColumnData]:
        """Extract column data from closed rectangular LWPOLYLINEs.

        Each closed 4-vertex LWPOLYLINE on the specified layers is analysed to
        determine the centroid, width, depth, and rotation angle.

        Args:
            layer_names: DXF layer names expected to contain column outlines.

        Returns:
            List of :class:`ColumnData` objects with coordinates in **metres**.
        """
        s = self._scale
        columns: list[ColumnData] = []

        for entity in self._iter_entities(layer_names, "LWPOLYLINE"):
            lwp: LWPolyline = entity  # type: ignore[assignment]
            if not lwp.closed:
                continue

            verts = _vertices_from_lwpolyline(lwp)
            if not _is_closed_rectangle(verts):
                continue

            # Strip duplicate closing vertex if present
            pts = list(verts)
            if len(pts) == 5 and _distance(pts[0], pts[4]) < 1e-3:
                pts = pts[:4]

            # Centroid (average of 4 vertices)
            cx = sum(p[0] for p in pts) / 4.0
            cy = sum(p[1] for p in pts) / 4.0

            # Edge lengths
            edge0 = _distance(pts[0], pts[1])
            edge1 = _distance(pts[1], pts[2])

            # Width = shorter dimension, Depth = longer (convention)
            width = min(edge0, edge1)
            depth = max(edge0, edge1)

            # Rotation = angle of the *longer* edge relative to +X
            if edge0 >= edge1:
                rot = _angle_between(pts[0], pts[1])
            else:
                rot = _angle_between(pts[1], pts[2])

            # Normalise rotation to [0, 180) – columns are symmetric
            rot = rot % 180.0

            columns.append(ColumnData(
                centroid_x=cx * s,
                centroid_y=cy * s,
                width=width * s,
                depth=depth * s,
                rotation_deg=rot,
                layer=lwp.dxf.layer,
            ))

        return columns

    # ----- beam extraction ----------------------------------------------------

    def extract_beams(self, layer_names: list[str]) -> list[BeamData]:
        """Extract beam centrelines from LINE and open LWPOLYLINE entities.

        Args:
            layer_names: DXF layer names expected to contain beam centrelines.

        Returns:
            List of :class:`BeamData` objects with coordinates in **metres**.
        """
        s = self._scale
        beams: list[BeamData] = []

        # LINE entities
        for entity in self._iter_entities(layer_names, "LINE"):
            line: Line = entity  # type: ignore[assignment]
            beams.append(BeamData(
                start_x=line.dxf.start.x * s,
                start_y=line.dxf.start.y * s,
                end_x=line.dxf.end.x * s,
                end_y=line.dxf.end.y * s,
                layer=line.dxf.layer,
            ))

        # Open LWPOLYLINE entities (each segment becomes a beam)
        for entity in self._iter_entities(layer_names, "LWPOLYLINE"):
            lwp: LWPolyline = entity  # type: ignore[assignment]
            if lwp.closed:
                continue
            verts = _vertices_from_lwpolyline(lwp)
            for i in range(len(verts) - 1):
                beams.append(BeamData(
                    start_x=verts[i][0] * s,
                    start_y=verts[i][1] * s,
                    end_x=verts[i + 1][0] * s,
                    end_y=verts[i + 1][1] * s,
                    layer=lwp.dxf.layer,
                ))

        return beams

    # ----- wall extraction ----------------------------------------------------

    def extract_walls(self, layer_names: list[str]) -> list[WallData]:
        """Extract wall centrelines from LINE and open LWPOLYLINE entities.

        Args:
            layer_names: DXF layer names expected to contain wall centrelines.

        Returns:
            List of :class:`WallData` objects with coordinates in **metres**.
        """
        s = self._scale
        walls: list[WallData] = []

        for entity in self._iter_entities(layer_names, "LINE"):
            line: Line = entity  # type: ignore[assignment]
            walls.append(WallData(
                start_x=line.dxf.start.x * s,
                start_y=line.dxf.start.y * s,
                end_x=line.dxf.end.x * s,
                end_y=line.dxf.end.y * s,
                layer=line.dxf.layer,
            ))

        for entity in self._iter_entities(layer_names, "LWPOLYLINE"):
            lwp: LWPolyline = entity  # type: ignore[assignment]
            if lwp.closed:
                continue
            verts = _vertices_from_lwpolyline(lwp)
            for i in range(len(verts) - 1):
                walls.append(WallData(
                    start_x=verts[i][0] * s,
                    start_y=verts[i][1] * s,
                    end_x=verts[i + 1][0] * s,
                    end_y=verts[i + 1][1] * s,
                    layer=lwp.dxf.layer,
                ))

        return walls

    # ----- slab extraction ----------------------------------------------------

    def extract_slabs(self, layer_names: list[str]) -> list[SlabData]:
        """Extract slab boundary polygons from closed LWPOLYLINEs.

        Args:
            layer_names: DXF layer names expected to contain slab outlines.

        Returns:
            List of :class:`SlabData` objects with coordinates in **metres**.
        """
        s = self._scale
        slabs: list[SlabData] = []

        for entity in self._iter_entities(layer_names, "LWPOLYLINE"):
            lwp: LWPolyline = entity  # type: ignore[assignment]
            if not lwp.closed:
                continue
            verts = _vertices_from_lwpolyline(lwp)
            slabs.append(SlabData(
                vertices_x=[v[0] * s for v in verts],
                vertices_y=[v[1] * s for v in verts],
                layer=lwp.dxf.layer,
            ))

        return slabs

    # ----- opening extraction -------------------------------------------------

    def extract_openings(self, layer_names: list[str]) -> list[OpeningData]:
        """Extract opening polygons from closed LWPOLYLINEs.

        Args:
            layer_names: DXF layer names expected to contain opening outlines.

        Returns:
            List of :class:`OpeningData` objects with coordinates in **metres**.
        """
        s = self._scale
        openings: list[OpeningData] = []

        for entity in self._iter_entities(layer_names, "LWPOLYLINE"):
            lwp: LWPolyline = entity  # type: ignore[assignment]
            if not lwp.closed:
                continue
            verts = _vertices_from_lwpolyline(lwp)
            openings.append(OpeningData(
                vertices_x=[v[0] * s for v in verts],
                vertices_y=[v[1] * s for v in verts],
                layer=lwp.dxf.layer,
            ))

        return openings


# ---------------------------------------------------------------------------
# EtabsElementGenerator
# ---------------------------------------------------------------------------

class EtabsElementGenerator:
    """Create ETABS model elements from parsed DXF structural data.

    All coordinates and dimensions are expected in **metres** (SI).

    Usage::

        gen = EtabsElementGenerator(SapModel)
        gen.define_frame_section("C300x500", 0.3, 0.5)
        count = gen.create_columns(columns, stories, "C300x500")
    """

    def __init__(self, sap_model: Any) -> None:
        """Store the live ETABS ``SapModel`` COM reference.

        Args:
            sap_model: A ``comtypes`` pointer to ``cSapModel`` obtained via
                the ETABS OAPI.
        """
        self._model = sap_model

    # ----- section definitions ------------------------------------------------

    def define_frame_section(
        self,
        name: str,
        width_m: float,
        depth_m: float,
        material: str = "C30",
    ) -> bool:
        """Define a rectangular concrete frame section.

        Calls ``SapModel.PropFrame.SetRectangle(name, material, depth, width)``.

        Args:
            name: Section label, e.g. ``"C300x500"``.
            width_m: Section width in metres.
            depth_m: Section depth in metres.
            material: Material property name already defined in the model.

        Returns:
            ``True`` if the API call returned ``0`` (success).
        """
        ret: int = self._model.PropFrame.SetRectangle(name, material, depth_m, width_m)
        return ret == 0

    def define_wall_section(
        self,
        name: str,
        thickness_m: float,
        material: str = "C30",
    ) -> bool:
        """Define a wall area section.

        Calls ``SapModel.PropArea.SetWall(name, eWallPropType, ShellType, material, thickness)``.

        Args:
            name: Section label, e.g. ``"W200"``.
            thickness_m: Wall thickness in metres.
            material: Material property name already defined in the model.

        Returns:
            ``True`` if the API call returned ``0`` (success).
        """
        # eWallPropType = 1 (Specified), ShellType = 0 (ShellThin)
        ret: int = self._model.PropArea.SetWall(name, 1, 0, material, thickness_m)
        return ret == 0

    def define_slab_section(
        self,
        name: str,
        thickness_m: float,
        material: str = "C30",
    ) -> bool:
        """Define a slab area section.

        Calls ``SapModel.PropArea.SetSlab(name, eSlabType, ShellType, material, thickness)``.

        Args:
            name: Section label, e.g. ``"SL200"``.
            thickness_m: Slab thickness in metres.
            material: Material property name already defined in the model.

        Returns:
            ``True`` if the API call returned ``0`` (success).
        """
        # eSlabType = 2 (Slab), ShellType = 0 (ShellThin)
        ret: int = self._model.PropArea.SetSlab(name, 2, 0, material, thickness_m)
        return ret == 0

    # ----- element creation ---------------------------------------------------

    def create_columns(
        self,
        columns: list[ColumnData],
        stories: list[tuple[str, float]],
        section_name: str,
    ) -> int:
        """Draw vertical column frame objects for every story.

        For each column location and each consecutive pair of story elevations a
        frame object is added using
        ``SapModel.FrameObj.AddByCoord(x, y, z_bot, x, y, z_top, …)``.

        Args:
            columns: Parsed column locations (in metres).
            stories: Ordered list of ``(story_name, elevation_m)`` **from bottom
                to top**.  E.g. ``[("Base", 0.0), ("Story1", 3.2), ("Story2", 6.4)]``.
            section_name: Frame section property name to assign.

        Returns:
            Number of frame objects successfully created.
        """
        if len(stories) < 2:
            return 0

        count = 0
        name_holder = [""]  # mutable container for COM out-parameter

        for col in columns:
            for i in range(len(stories) - 1):
                story_label = stories[i + 1][0]
                z_bot = stories[i][1]
                z_top = stories[i + 1][1]

                frame_name = f"COL_{story_label}_{count + 1}"
                ret: int = self._model.FrameObj.AddByCoord(
                    col.centroid_x, col.centroid_y, z_bot,
                    col.centroid_x, col.centroid_y, z_top,
                    "",       # FrameName (empty → auto)
                    frame_name,
                    section_name,
                )
                if ret == 0:
                    # Apply local-axis rotation if the column is rotated
                    if abs(col.rotation_deg) > 0.01:
                        self._model.FrameObj.SetLocalAxes(frame_name, col.rotation_deg)
                    count += 1

        return count

    def create_beams(
        self,
        beams: list[BeamData],
        story_elevation: float,
        section_name: str,
    ) -> int:
        """Draw horizontal beam frame objects at a given elevation.

        Uses ``SapModel.FrameObj.AddByCoord(x1, y1, z, x2, y2, z, …)``.

        Args:
            beams: Parsed beam centrelines (in metres, XY only).
            story_elevation: Z-coordinate (elevation in metres) to place beams.
            section_name: Frame section property name to assign.

        Returns:
            Number of frame objects successfully created.
        """
        count = 0
        for bm in beams:
            frame_name = f"BM_{count + 1}"
            ret: int = self._model.FrameObj.AddByCoord(
                bm.start_x, bm.start_y, story_elevation,
                bm.end_x, bm.end_y, story_elevation,
                "",
                frame_name,
                section_name,
            )
            if ret == 0:
                count += 1
        return count

    def create_walls(
        self,
        walls: list[WallData],
        z_bottom: float,
        z_top: float,
        section_name: str,
    ) -> int:
        """Create wall area objects from wall centreline data.

        Each wall segment is turned into a 4-node planar shell using
        ``SapModel.AreaObj.AddByCoord(4, xs, ys, zs, …)``.

        Args:
            walls: Parsed wall segments (in metres, XY only).
            z_bottom: Bottom elevation in metres.
            z_top: Top elevation in metres.
            section_name: Wall area section property name.

        Returns:
            Number of area objects successfully created.
        """
        count = 0
        for wl in walls:
            xs = [wl.start_x, wl.end_x, wl.end_x, wl.start_x]
            ys = [wl.start_y, wl.start_y, wl.end_y, wl.end_y]

            # For a wall defined by two endpoints the 4 corners are:
            #   bottom-start, bottom-end, top-end, top-start
            xs_wall = [wl.start_x, wl.end_x, wl.end_x, wl.start_x]
            ys_wall = [wl.start_y, wl.end_y, wl.end_y, wl.start_y]
            zs_wall = [z_bottom, z_bottom, z_top, z_top]

            area_name = f"WALL_{count + 1}"
            ret: int = self._model.AreaObj.AddByCoord(
                4,          # NumberPoints
                xs_wall,    # X[]
                ys_wall,    # Y[]
                zs_wall,    # Z[]
                "",         # AreaName (empty → auto)
                area_name,
                section_name,
            )
            if ret == 0:
                count += 1
        return count

    def create_slabs(
        self,
        slabs: list[SlabData],
        elevation: float,
        section_name: str,
    ) -> int:
        """Create slab area objects from parsed boundary polygons.

        Uses ``SapModel.AreaObj.AddByCoord(n, xs, ys, zs, …)``.

        Args:
            slabs: Parsed slab outlines (in metres).
            elevation: Z-coordinate (elevation in metres) of the slab.
            section_name: Slab area section property name.

        Returns:
            Number of area objects successfully created.
        """
        count = 0
        for slab in slabs:
            n = slab.num_points
            if n < 3:
                continue

            zs = [elevation] * n
            area_name = f"SLAB_{count + 1}"
            ret: int = self._model.AreaObj.AddByCoord(
                n,
                slab.vertices_x,
                slab.vertices_y,
                zs,
                "",
                area_name,
                section_name,
            )
            if ret == 0:
                count += 1
        return count

    def create_openings(
        self,
        openings: list[OpeningData],
        elevation: float,
    ) -> int:
        """Create slab opening area objects.

        Openings are added as ``None``-section area objects and then flagged as
        openings using ``SapModel.AreaObj.SetOpening``.

        Args:
            openings: Parsed opening outlines (in metres).
            elevation: Z-coordinate (elevation in metres) for the opening.

        Returns:
            Number of opening area objects successfully created.
        """
        count = 0
        for opn in openings:
            n = opn.num_points
            if n < 3:
                continue

            zs = [elevation] * n
            area_name = f"OPENING_{count + 1}"
            ret: int = self._model.AreaObj.AddByCoord(
                n,
                opn.vertices_x,
                opn.vertices_y,
                zs,
                "",
                area_name,
                "Default",  # placeholder section; opening flag overrides
            )
            if ret == 0:
                ret2: int = self._model.AreaObj.SetOpening(area_name, True)
                if ret2 == 0:
                    count += 1
        return count
