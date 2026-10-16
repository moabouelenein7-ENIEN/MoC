# MoC — ETABS Structural Modeling & Design Suite

**MoC** is a standalone structural engineering suite designed to accelerate structural modeling, analysis, and code design in **CSI ETABS** from scratch. 

Built for structural engineers adhering to the **Saudi Building Code (SBC 301 / SBC 304)** and **ACI 318 / ASCE 7**.

---

## 🌟 Key Features

### 1. 📐 DXF-Based Structural Modeling
- **Zero-Dependency DXF Parser**: Directly reads AutoCAD DXF plans (R12 to 2024).
- **Smart Layer Classifier**: Automatically detects member roles and section sizes from layer naming conventions (e.g. `COL-40x60`, `BM-30x60`, `WALL-250`, `SL-200`, `GRID`).
- **Interactive 2D Plan Canvas**: Smooth pan, zoom, fit-to-view, and layer color rendering.

### 2. 📏 Non-Uniform & Asymmetrical Grids & Stories
- **Individually Editable Rows**: Real projects are rarely uniform or symmetrical; edit any grid line position or story height freely.
- **Quick-Fill Sizing**: Pre-populate grids with shorthand like `3@6.0, 1@4.5, 2@5.5` or add multiple typical stories at once.
- **Direct ETABS Injection**: Injects Cartesian grids and story definitions directly into the active ETABS model via COM API.

### 3. 🏗️ Materials, Sections & Cracked Stiffness Modifiers
- **SBC 304 / ACI 318 Cracked Section Properties**: One-click application of code stiffness modifiers:
  - **Beams**: $I_{22} = 0.35, I_{33} = 0.35$
  - **Columns**: $I_{22} = 0.70, I_{33} = 0.70$
  - **Slabs**: $m_{11} = m_{22} = m_{12} = 0.25$
  - **Shear Walls**: $f_{11} = f_{22} = 0.70$ (uncracked) or $0.35$ (cracked)
- Concrete grades ($C25$ to $C50$) and Rebar grades (Grade 60 / 420 MPa, Grade 75 / 520 MPa).

### 4. ⚡ SBC 301 / ASCE 7 Dynamic Seismic & Lateral Design
- **Official Saudi Cities Seismic Database**: Built-in coordinates and spectral accelerations ($S_s, S_1$) for Riyadh, Jeddah, Makkah, Madinah, Dammam, Tabuk, Jizan, etc.
- **Automated Parameter Calculations**: Soil Site Classes (A–E) $\to$ automatic interpolation of $F_a, F_v, S_{DS}, S_{D1}, T_0, T_s$, and Seismic Design Category (SDC).
- **Interactive Design Spectrum**: Generates code response spectrum curve and injects user response spectrum functions into ETABS.

### 5. 🔀 SBC 304 Automated Load Combinations
- **Complete Code Combinations**:
  - Ultimate Gravity ($1.4D + 1.4SDL$, $1.2D + 1.6L + 0.5Lr$).
  - Dynamic Seismic with Vertical Effect ($E_v = 0.2 S_{DS} D$) and 100% + 30% directional combinations ($\pm E_x \pm 0.3 E_y$, $\pm 0.3 E_x \pm E_y$).
  - Wind load combinations with uplift ($0.9D + 1.0W$).
  - Service combinations for long-term deflections and foundation soil bearing checks.
  - Envelopes (`ENV-ULTIMATE`, `ENV-SERVICE`).

### 6. 📊 Analysis Verification & Overstress Scanner
- **Inelastic Story Drift Checks**: Calculates $\delta_x = \frac{C_d \delta_{xe}}{I_e}$ vs. SBC allowable limits ($\Delta_a = 0.020 h_{sx}$) with colored Pass/Fail badges.
- **Dynamic Mass Participation**: Verifies modal mass participation ratios ($\sum U_X \ge 90\%, \sum U_Y \ge 90\%$).
- **Overstressed Member Scan**: Spots columns and walls with P-M-M ratio $> 1.0$ or design error flags.

### 7. 🏛️ CSI SAFE Direct Foundation Export (.f2k)
- 1-Click extraction of base column and shear wall reactions under all service and ultimate combinations.
- Generates ready-to-run `.f2k` file for **CSI SAFE** to design raft/mat slabs, isolated footings, or pile caps without manual data entry.

---

## 🛠️ Technology Stack
- **Framework**: .NET 10.0 (Windows Forms & WPF)
- **API**: CSI API (`CSiAPIv1.dll`), dynamically late-bound to support ETABS v19, v20, v21, v22, and v23+
- **Architecture**: Zero external NuGet runtime dependencies for maximum stability
- **Tests**: xUnit automated test suite

---

## 🚀 Getting Started

### Prerequisites
- Windows 10 / 11 (x64)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- CSI ETABS (v19 to v22+)

### Build & Run
```powershell
# Clone the repository
git clone https://github.com/moabouelenein7-ENIEN/MoC.git
cd MoC

# Build solution
dotnet build EtabsHelper.slnx

# Run tests
dotnet test EtabsHelper.slnx

# Launch MoC application
dotnet run --project src/EtabsHelper/EtabsHelper.csproj
```

---

## 📄 License
MIT License
