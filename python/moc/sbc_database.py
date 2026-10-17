"""
Saudi Building Code (SBC 301 & SBC 302) Geo-Seismic and Wind Database Module.
Provides mapped spectral response accelerations, site coefficients (Fa, Fv),
design response parameters (Sds, Sd1), seismic design category (SDC),
and seismic response coefficient (Cs) calculations.
"""

from __future__ import annotations

import json
from dataclasses import dataclass, field
from pathlib import Path
from typing import Dict, List, Optional, Tuple


@dataclass
class CityData:
    name: str
    name_ar: str
    province: str
    Ss: float
    S1: float
    TL: float = 4.0
    wind_speed_mps: float = 38.0
    latitude: Optional[float] = None
    longitude: Optional[float] = None


@dataclass
class SeismicSystem:
    name: str
    abbreviation: str
    material: str
    R: float
    Cd: float
    omega_0: float
    height_limit_sdc_d: Optional[float] = None
    height_limit_sdc_e: Optional[float] = None
    description: str = ""


@dataclass
class WallMaterial:
    name: str
    density_kn_per_m3: float
    description: str = ""


@dataclass
class RebarSize:
    diameter_mm: int
    area_mm2: float
    symbol: str = ""

    def __post_init__(self):
        if not self.symbol:
            self.symbol = f"Φ{self.diameter_mm}"


@dataclass
class SeismicDesignParams:
    city_name: str
    site_class: str
    Ss: float
    S1: float
    Fa: float
    Fv: float
    Sms: float
    Sm1: float
    Sds: float
    Sd1: float
    T0: float
    Ts: float
    TL: float
    risk_category: str
    importance_factor: float
    seismic_design_category: str
    wind_speed_mps: float
    system: Optional[SeismicSystem] = None
    spectrum_curve: List[Tuple[float, float]] = field(default_factory=list)

    def calculate_Cs(self, fundamental_period_t: float) -> float:
        """Calculates Seismic Response Coefficient Cs for period T."""
        if fundamental_period_t <= 0 or not self.system or self.system.R <= 0:
            return 0.0

        r_over_ie = self.system.R / self.importance_factor
        cs = self.Sds / r_over_ie

        # Upper limit per SBC 301 / ASCE 7-16
        if fundamental_period_t <= self.TL:
            cs_max = self.Sd1 / (fundamental_period_t * r_over_ie)
        else:
            cs_max = (self.Sd1 * self.TL) / (fundamental_period_t**2 * r_over_ie)

        if cs > cs_max:
            cs = cs_max

        # Lower limit
        cs_min = max(0.044 * self.Sds * self.importance_factor, 0.01)
        if cs < cs_min:
            cs = cs_min

        return round(cs, 5)


class SBCDatabase:
    """Manages SBC 301 geo-seismic, wind, and material databases."""

    _instance: Optional["SBCDatabase"] = None

    def __init__(self, json_path: Optional[str | Path] = None):
        if json_path is None:
            json_path = Path(__file__).parent / "sbc_data.json"
        self.json_path = Path(json_path)
        self._load_data()

    def _load_data(self) -> None:
        if not self.json_path.exists():
            raise FileNotFoundError(f"SBC database JSON not found at: {self.json_path}")

        with open(self.json_path, "r", encoding="utf-8") as f:
            data = json.load(f)

        self._cities: Dict[str, CityData] = {}
        for c in data.get("cities", []):
            cd = CityData(
                name=c["name"],
                name_ar=c.get("name_ar", ""),
                province=c.get("province", ""),
                Ss=float(c["Ss"]),
                S1=float(c["S1"]),
                TL=float(c.get("TL", 4.0)),
                wind_speed_mps=float(c.get("wind_speed_mps", 38.0)),
                latitude=c.get("latitude"),
                longitude=c.get("longitude"),
            )
            self._cities[cd.name.lower()] = cd

        self._site_coeffs = data.get("site_coefficients", {})

        self._systems: List[SeismicSystem] = []
        for s in data.get("seismic_systems", []):
            self._systems.append(
                SeismicSystem(
                    name=s["name"],
                    abbreviation=s.get("abbreviation", ""),
                    material=s.get("material", "Concrete"),
                    R=float(s["R"]),
                    Cd=float(s["Cd"]),
                    omega_0=float(s["omega_0"]),
                    height_limit_sdc_d=s.get("height_limit_sdc_d"),
                    height_limit_sdc_e=s.get("height_limit_sdc_e"),
                    description=s.get("description", ""),
                )
            )

        self._wall_materials: List[WallMaterial] = []
        for w in data.get("wall_materials", []):
            self._wall_materials.append(
                WallMaterial(
                    name=w["name"],
                    density_kn_per_m3=float(w.get("unit_weight_kn_m3", w.get("density_kn_per_m3", 12.0))),
                    description=w.get("description", ""),
                )
            )

        self._rebar_sizes: List[RebarSize] = []
        for r in data.get("rebar_diameters", []):
            self._rebar_sizes.append(
                RebarSize(
                    diameter_mm=int(r["diameter_mm"]),
                    area_mm2=float(r["area_mm2"]),
                    symbol=r.get("symbol", f"Φ{r['diameter_mm']}"),
                )
            )

    @classmethod
    def get_instance(cls) -> "SBCDatabase":
        if cls._instance is None:
            cls._instance = cls()
        return cls._instance

    def get_all_cities(self) -> List[CityData]:
        return list(self._cities.values())

    def get_city_names(self) -> List[str]:
        return [c.name for c in self._cities.values()]

    def get_city(self, name: str) -> Optional[CityData]:
        cleaned = name.strip().lower()
        if cleaned in self._cities:
            return self._cities[cleaned]
        for key, city in self._cities.items():
            if cleaned in key or key in cleaned:
                return city
        return None

    def compute_Fa(self, site_class: str, Ss: float) -> float:
        """Computes site coefficient Fa with linear interpolation per SBC 301 Table 11.4-1."""
        sc = site_class.strip().upper()
        fa_data = self._site_coeffs.get("Fa", {})
        bps = fa_data.get("breakpoints_Ss", [0.25, 0.50, 0.75, 1.00, 1.25])
        class_entry = fa_data.get(sc, {})
        values = class_entry.get("values", [1.0] * len(bps))

        if Ss <= bps[0]:
            return values[0]
        if Ss >= bps[-1]:
            return values[-1]

        for i in range(len(bps) - 1):
            if bps[i] <= Ss <= bps[i + 1]:
                ratio = (Ss - bps[i]) / (bps[i + 1] - bps[i])
                return round(values[i] + ratio * (values[i + 1] - values[i]), 3)

        return 1.0

    def compute_Fv(self, site_class: str, S1: float) -> float:
        """Computes site coefficient Fv with linear interpolation per SBC 301 Table 11.4-2."""
        sc = site_class.strip().upper()
        fv_data = self._site_coeffs.get("Fv", {})
        bps = fv_data.get("breakpoints_S1", [0.10, 0.20, 0.30, 0.40, 0.50])
        class_entry = fv_data.get(sc, {})
        values = class_entry.get("values", [1.0] * len(bps))

        if S1 <= bps[0]:
            return values[0]
        if S1 >= bps[-1]:
            return values[-1]

        for i in range(len(bps) - 1):
            if bps[i] <= S1 <= bps[i + 1]:
                ratio = (S1 - bps[i]) / (bps[i + 1] - bps[i])
                return round(values[i] + ratio * (values[i + 1] - values[i]), 3)

        return 1.0

    def determine_SDC(self, Sds: float, Sd1: float, risk_category: str = "II", S1: float = 0.0) -> str:
        """Determines Seismic Design Category (SDC A-F) based on Sds and Sd1 per SBC 301."""
        cat = risk_category.strip().upper()
        is_cat_iv = cat in ("IV", "4")

        # By SDS
        if Sds < 0.167:
            sdc_s = "A"
        elif Sds < 0.33:
            sdc_s = "B"
        elif Sds < 0.50:
            sdc_s = "C"
        else:
            sdc_s = "D"

        # By SD1
        if Sd1 < 0.067:
            sdc_1 = "A"
        elif Sd1 < 0.133:
            sdc_1 = "B"
        elif Sd1 < 0.20:
            sdc_1 = "C"
        else:
            sdc_1 = "D"

        order = {"A": 1, "B": 2, "C": 3, "D": 4, "E": 5, "F": 6}
        sdc_char = sdc_s if order[sdc_s] >= order[sdc_1] else sdc_1

        # Near-fault rules for high S1 (S1 >= 0.75) per SBC 301 Section 11.6
        if S1 >= 0.75:
            sdc_char = "F" if is_cat_iv else "E"

        return sdc_char

    def compute_design_parameters(
        self,
        city_name: str,
        site_class: str = "D",
        risk_category: str = "II",
        system: Optional[SeismicSystem] = None,
    ) -> SeismicDesignParams:
        """
        Computes all SBC 301 seismic parameters:
        Fa, Fv, Sms, Sm1, Sds, Sd1, T0, Ts, SDC, and full design response spectrum curve.
        """
        city = self.get_city(city_name)
        if city is None:
            city = CityData(name=city_name, name_ar="", province="", Ss=0.25, S1=0.10)

        importance_map = {"I": 1.0, "II": 1.0, "III": 1.25, "IV": 1.5}
        Ie = importance_map.get(risk_category.upper(), 1.0)

        Fa = self.compute_Fa(site_class, city.Ss)
        Fv = self.compute_Fv(site_class, city.S1)

        Sms = round(Fa * city.Ss, 4)
        Sm1 = round(Fv * city.S1, 4)

        Sds = round((2.0 / 3.0) * Sms, 4)
        Sd1 = round((2.0 / 3.0) * Sm1, 4)

        T0 = round(0.2 * (Sd1 / Sds), 4) if Sds > 0 else 0.0
        Ts = round(Sd1 / Sds, 4) if Sds > 0 else 0.0

        sdc = self.determine_SDC(Sds, Sd1, risk_category, S1=city.S1)

        if system is None and self._systems:
            system = self._systems[0]

        # Generate response spectrum curve (Period T vs Sa in g)
        spectrum_curve: List[Tuple[float, float]] = []
        max_period = 4.0
        steps = 60
        dt = max_period / steps
        for step in range(steps + 1):
            t = round(step * dt, 3)
            if t < T0 and T0 > 0:
                sa = Sds * (0.4 + 0.6 * (t / T0))
            elif t <= Ts:
                sa = Sds
            elif t <= city.TL and t > 0:
                sa = Sd1 / t
            elif t > 0:
                sa = (Sd1 * city.TL) / (t**2)
            else:
                sa = Sds * 0.4
            spectrum_curve.append((t, round(sa, 4)))

        # Explicitly insert critical corner points T0 and Ts for a crisp plateau
        if T0 > 0:
            spectrum_curve.append((T0, Sds))
        if Ts > 0:
            spectrum_curve.append((Ts, Sds))
        spectrum_curve.sort(key=lambda pt: pt[0])

        return SeismicDesignParams(
            city_name=city.name,
            site_class=site_class.upper(),
            Ss=city.Ss,
            S1=city.S1,
            Fa=Fa,
            Fv=Fv,
            Sms=Sms,
            Sm1=Sm1,
            Sds=Sds,
            Sd1=Sd1,
            T0=T0,
            Ts=Ts,
            TL=city.TL,
            risk_category=risk_category.upper(),
            importance_factor=Ie,
            seismic_design_category=sdc,
            wind_speed_mps=city.wind_speed_mps,
            system=system,
            spectrum_curve=spectrum_curve,
        )

    def get_seismic_systems(self) -> List[SeismicSystem]:
        return list(self._systems)

    def get_wall_materials(self) -> List[WallMaterial]:
        return list(self._wall_materials)

    def get_rebar_table(self) -> List[RebarSize]:
        return list(self._rebar_sizes)
