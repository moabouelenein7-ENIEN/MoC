# MoC — Structural Engineering Automation Suite for CSI ETABS
### Conforming to Saudi Building Code (SBC 301 & SBC 304 / ASCE 7-16)

[![Python 3.10+](https://img.shields.io/badge/python-3.10+-blue.svg)](https://www.python.org/downloads/)
[![ETABS API](https://img.shields.io/badge/CSI%20ETABS-v20%20%7C%20v21%20%7C%20v22-green.svg)](https://www.csiamerica.com/products/etabs)
[![Code](https://img.shields.io/badge/Code-SBC%20301%20%2F%20304-gold.svg)](https://sbc.gov.sa)

---

## 🏗️ Overview

**MoC** is a modular Python automation platform and graphical suite for CSI ETABS, architected for structural engineering consultants and design offices in the Kingdom of Saudi Arabia.

It automates the complete lifecycle:
1. **OAPI Connection & Initialization**: Auto-connects to active ETABS instances via COM (`comtypes`) and enforces `kN_m_C` units.
2. **CAD-to-BIM DXF Modeler**: Parses architectural/structural `.dxf` drawings (`ezdxf`), classifies layers, and draws 3D structural elements (columns, beams, shear walls, slabs, openings).
3. **SBC 301 Geo-Seismic & Wind Engine**: Built-in Saudi Arabia database (Riyadh, Jeddah, Makkah, Madinah, Dammam, NEOM, Tabuk, etc.), automated $F_a, F_v$ site coefficient interpolation, and auto-seismic pattern injection (`EQX`, `EQY` with 5% eccentricity) + ASCE 7 Wind (`WINDX`, `WINDY`).
4. **Automated Load Patterns & Combinations**: Programmatic generation of `DEAD`, `SIDL`, `WALLS`, `LIVE`, `EQX`, `EQY`, `WINDX`, `WINDY`, plus full SBC 301 ULS (U1–U14) and SLS combinations + governing Envelope.
5. **Smart Wall Load Calculator**: Interactive clear height calculator ($H_{net} = H_{story} - d_{beam}$) with customizable block densities (Hollow Block, Solid Block, AAC, Insulated Block, Red Brick), plaster allowance, and opening percentage reduction, assigning $w_{wall}$ directly to selected beams in ETABS.
6. **Analysis Execution & Rebar Detailing Translator**: Triggers analysis and concrete design, extracts required reinforcement areas ($A_{s,\text{req}}$ in $\text{mm}^2$), and translates them into practical bar counts ($N = \lceil A_s / A_{bar} \rceil$, e.g., $4\ \Phi\ 16$) with Excel schedule export (`.xlsx`).

---

## 📁 Architecture & File Layout

```
python/
├── moc/
│   ├── __init__.py            # MoC package entry point and exports
│   ├── etabs_connector.py     # CSI ETABS OAPI COM connection & session manager
│   ├── dxf_importer.py        # ezdxf structural drawing parser & 3D model builder
│   ├── sbc_data.json          # SBC 301 Saudi cities, site classes, block materials
│   ├── sbc_database.py        # SBC 301 Fa/Fv interpolation, SDS/SD1, SDC engine
│   ├── load_manager.py        # Load patterns & SBC 301 ULS/SLS combinations generator
│   ├── wall_calculator.py     # Wall clear height & linear load calculation tool
│   ├── rebar_calculator.py    # As_req to bar count translator & Excel exporter
│   └── main_gui.py            # CustomTkinter dark-themed desktop application
├── run_moc.py                 # 1-click launcher script
├── test_suite.py              # Automated verification test suite
└── requirements.txt           # Python package dependencies
```

---

## 🚀 Quick Start

### 1. Prerequisites
- **Python 3.10+** (64-bit)
- **CSI ETABS 20, 21, or 22** installed on Windows

### 2. Installation
```powershell
pip install -r requirements.txt
```

### 3. Launching the GUI
```powershell
python run_moc.py
```

### 4. Running Verification Tests
```powershell
python test_suite.py
```

---

## 🧮 Mathematical Formulations

### 1. SBC 301 Equivalent Lateral Force Parameters
$$\begin{aligned}
S_{MS} &= F_a \times S_s, \quad S_{M1} = F_v \times S_1 \\
S_{DS} &= \frac{2}{3} S_{MS}, \quad S_{D1} = \frac{2}{3} S_{M1} \\
T_0 &= 0.2 \frac{S_{D1}}{S_{DS}}, \quad T_s = \frac{S_{D1}}{S_{DS}} \\
C_s &= \frac{S_{DS}}{R / I_e} \quad \left(\text{bounded by } C_{s,\max} \text{ and } C_{s,\min} = \max(0.044 S_{DS} I_e, 0.01)\right)
\end{aligned}$$

### 2. Smart Wall Linear Load Formula
$$H_{net} = H_{story} - d_{beam}$$
$$w_{wall} = \left( H_{net} \cdot t_{block} \cdot \gamma_{block} + H_{net} \cdot t_{plaster,total} \cdot \gamma_{plaster} \right) \times (1 - \text{Opening \%})$$

### 3. Rebar Detailing Translation
$$N_{bars} = \left\lceil \frac{A_{s,\text{req}}}{A_{bar}} \right\rceil, \quad A_{bar} = \frac{\pi \cdot \Phi^2}{4}$$
- For beams: Minimum 2 bars per layer, $A_{s,\text{prov}} \ge A_{s,\text{req}}$.
- For columns: Symmetrical distribution, even number of bars enforced.
