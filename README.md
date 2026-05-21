# ccx-gmsh-fea — cyclic-symmetric modal POC

CalculiX port of an Ansys APDL workflow for the prestressed cyclic-symmetric
modal analysis of a labyrinth-seal disc. Reference data: 130 eigenfrequencies
from the equivalent Ansys Mechanical run (10 modes × 13 nodal diameters).

The disc cross-section is meshed in PrePoMax as a 2D axisymmetric CAX6 mesh
(`calculixmacro.inp`), then revolved by `revolve_mesh.py` into a 3D sector of
stacked C3D15 wedges. `disc_modal.inp` applies the material, the cyclic-symmetry
tie between the two cut faces, clamps the axial faces, runs a static centrifugal
preload at 10 000 rpm, then a perturbative `*FREQUENCY` step sweeping ND 0..12.

## Layout

```
pushes/
├── README.md                   <-- this file
├── .gitignore                  excludes ccx outputs (.dat, .frd, .eig, .cvg, ...)
├── ccx_trial_1/                LEGACY: 1-wedge baseline (missing-mode bug, kept for reference)
└── cyclic_modal_v3_ang5/       CURRENT: 5-wedge baseline, validated vs Ansys
```

Each push directory is **self-sufficient**: it contains the 2D source mesh, the
analysis deck, the preprocessor script, and the pre-generated 3D mesh. You can
clone, `cd` into the directory, and run ccx without needing to regenerate
anything first.

## How to run

```bash
cd cyclic_modal_v3_ang5
# Optional: regenerate mesh3d.inp from calculixmacro.inp (already committed)
python3 revolve_mesh.py
# Solve (ccx 2.23 verified; older versions also work)
ccx_2.23 disc_modal
```

Solve time: ~35 s for `ccx_trial_1`, ~3-5 min for `cyclic_modal_v3_ang5` on an
M-series Mac. Output appears in `disc_modal.dat` (eigenfrequencies),
`disc_modal.frd` (mode shapes), `disc_modal.eig` (reusable for downstream
forced-response analyses).

## Choosing angular resolution

The `WEDGES_PER_SECTOR` parameter in `revolve_mesh.py` controls how many stacked
C3D15 wedges span the 15° datum sector. **It is not just an accuracy knob — it
controls which mode families exist at all.** With one wedge across the sector
(the legacy `ccx_trial_1/` snapshot), the lowest 1-2 modes per nodal diameter
match Ansys to 0.2 %, but an entire family of mid-frequency torsional / shear
modes is missing from the spectrum — they can't fit in a single quadratic
angular shape function. Recovering them requires more wedges. Validated
behaviour vs Ansys (5/2026):

| WEDGES_PER_SECTOR | modes within 5 % of Ansys | median \|Δ\| | notes |
|---|---|---|---|
| 1 (ccx_trial_1)        | 13 / 65 | 98 %    | mid-frequency family missing |
| 5 (cyclic_modal_v3_ang5) | 52 / 62 | 0.53 % | current baseline |

For any new cyclic-modal case in this repo, start with `WEDGES_PER_SECTOR ≥ 5`.

## Analysis artifacts (in `cyclic_modal_v3_ang5/`)

- `frequencies_table.csv` / `frequencies_table.md` — all 130 modes from the
  ccx run (ND, mode, frequency in Hz).
- `ccx_v3_vs_ansys.csv` — side-by-side comparison with Ansys reference,
  showing v1 and v3 errors per mode.

## Why each design choice in the deck

- **C3D15 wedges, not C3D20 hex.** PrePoMax gave us a 2D CAX6 (6-node
  triangle) mesh. The natural quadratic 3D extrusion of a 6-node triangle is a
  15-node wedge. Going to hex would require rebuilding the cross-section as
  quads from scratch — possible but not worth it without CAD. Modal accuracy
  is governed by global shape capture, not element type.
- **LEFT_SURF (slave, θ=0) tied to RIGHT_SURF (master, θ=15°).**
  Cyclic-multiplication direction is slave → master.
- **FIX_FACES excludes layer-0 (slave) copies.** CalculiX forbids a node from
  being both an SPC dependent and an MPC dependent. The cyclic-symmetry MPC
  propagates `U=0` from master to slave automatically:
  `U_left = U_right · exp(i·θ) = 0`.
- **C3D15 orientation gotcha:** face S1 (nodes 1-2-3) right-hand normal must
  point **toward** face S2 (nodes 4-5-6), not away. In our revolution about +Y,
  the higher-θ layer sits at more negative Z, so we place the higher-θ corners
  on S1 to satisfy the convention.

## Working parameters baked into `disc_modal.inp`

- Material: E = 183 839.162 MPa, ν = 0.281, ρ = 7.77e-9 t/mm³ (MM_TON_S_C units).
- Rotation: 10 000 rpm → ω² = 1.0966227111e6 (rad/s)². CENTRIF axis = global Y.
- Cyclic symmetry: N = 24 sectors, NGRAPH = 1 (datum sector only in .frd).
- Static step: NLGEOM = YES, t = 1.0, initial increment 0.1.
- Frequency step: PERTURBATION, STORAGE = YES, 10 modes per ND, NMIN = 0, NMAX = 12.

## CCX output format note

CCX writes each cyclic-symmetric eigenvalue twice in `disc_modal.dat`. For
1 ≤ ND ≤ 11 the pair is the cosine / sine partners of a standing-wave mode
(both physically real). For ND = 0 and ND = 12 (the Nyquist for N=24) the pair
is one real eigenvalue plus a phantom with zero participation factors —
collapse to a single physical mode when comparing to references that list only
distinct modes.
