# Criteria-check rewrite — design spec

Agreed 2026-06-10. This is the authoritative spec for `criteriachecks/rewrite/`.
The photo transcription in `criteriachecks/reconstructed/` is the historical
reference; this folder is the rewrite. Reference formula photos:
`criteriachecks/photos/3rd upload/6f0f61f2…~1.jpg` (cylinder equation set) and
`…/bcbdfea2…~1.jpg` (disc equation set, Figure 31 section).

## ⚠️ Open validation item — counterpart cylindrical waves

The reference document and the legacy Excel macro (used by many people, and
matched by the original C#) **disagree** on the counterpart-side cylindrical
traveling waves:

| | formula |
|---|---|
| Excel macro / old code / **THIS CODE** | `f0₂ ∓ 2nω₁/(n²+λ₂+1) ± nω₁(1−K)` — seal speed ω₁ and the Doppler term, same function as the seal side |
| Reference document (fr₂fwd₂/fr₂bkwd₂) | `f0₂ ± 2nKω₁/(n²+λ₂+1)` — counterpart's own speed Kω₁, **no** Doppler term |

Either source could be the wrong one. **User will run comparison tests once
the code is usable.** If the document wins, only
`TravelingWaveCalculator.CalculateSide` (cylindrical counterpart branch) and
possibly `SealMath` need touching — the formula lives in exactly one place.

Exception already following the document: a **disc counterpart** has no
observable wave split (`Forward = Backward = f0`), per the document's
observer argument. The old code applied the seal-style ± split there.

## Architecture (three layers)

```
Inputs   (LakeComponent tree, UI-bound)   Inputs/  + 3 infra files kept verbatim
Math     (every formula exactly once)     SealMath.cs, CriteriaChecks/HoneycombCalculator.cs
Checks   (calculators + engine)           CriteriaChecks/
Results  (plain classes, NOT in UI tree)  ResultObjects/
```

Entry point: `CriteriaCheckEngine.Run(sealCriteria [, maxNd, maxModes]) → CriteriaResults`.
Calculators are static, stateless, and never reach into each other.

## Locked decisions

- **Conditions**: enum `Assembly`/`Redline` (cold/hot). All checks evaluate at
  Redline; acoustic checks also compute an Assembly (cold) curve via the
  assembly speed of sound. fr = Redline frequencies straight from
  **prestressed FEA** — therefore the document's spin-stiffening
  `fr = √(fs²+Bω²)` is **not applied** (would double-count). `CylinderB` /
  `SpinStiffenedFrequency` exist in SealMath as documented, unused helpers.
  No B for discs.
- **Radius vs diameter**: `StructuralLambda = 3R²/(n²l²)` is derived for a
  RADIUS (inextensional shell theory). Geometric inputs store diameters;
  calculators pass `diameter/2`. ⚠️ The old program passed the diameter
  straight in → its λ was 4× larger. This is a deliberate numerical change.
- **Two lambdas**: structural λ (Campbell + cylindrical waves) vs acoustic
  wavelength λg = 2πRc/nd (circ) and 2Pt/m (axial). Named distinctly in
  SealMath; not interchangeable.
- **Cylindrical fwd/bkwd are full mirrors** (per document):
  `fwd = f0 − 2nω₁/(n²+λ+1) + nω₁(1−K)`, `bkwd = f0 + 2nω₁/(n²+λ+1) − nω₁(1−K)`.
  The old code's bkwd was a copy-paste of fwd.
- **One ω₁** (seal redline / 60) and **one K** (`±ω₂/ω₁` by dynamic type:
  rotor +, counter-rotor −, stator 0) in all wave formulas.
- **Each side walks its own frequency table** with its own f0 and its own
  geometry/shape (the old code fed seal f0 into the counterpart side —
  superseded).
- **Cavity acoustic signs unified**: fwd = f + Δ, bkwd = f − Δ (old cavity
  code had them flipped relative to the tooth check).
- **Acoustic rows are driven by the seal Redline table** (its nds for circ +
  cavity, its modes at nd = 0 for axial) so they align 1:1 with the modal
  curves on the plots. Comparison values are NOT duplicated into the acoustic
  results — the plot layer reads both sides of the comparison from
  `CriteriaResults`.
- **Single `HoneyCombParameters`** under `GeometricParameters` (the
  `SealCriteria` duplicate is gone).
- **Out of scope** by agreement: plotting, pass/fail booleans (Campbell's
  `Margin` and aeroelastic `Wr` are stored as numbers; thresholds are the
  consumer's business), `OutOfRoundStability`.

## Sparse-data contract

`NodalFrequencyData.FrequencyTable` is sparse. Calculators iterate what
exists — any number of diameters, any subset of modes per diameter, per side.
Missing entry → **no row** (the old `?? 0.0` fallback is banned: it produced
garbage rows from f0 = 0). Empty input → empty result objects, never null.
`nd = 0` is skipped wherever formulas are singular (structural λ, RRS,
circumferential λg); nd = 0 is used only by the axial acoustic check
(umbrella modes). Scalars over empty sets are `double.NaN` (`Campbell.Margin`).

## Compatibility constraints

- C# 7.3 (.NET Framework): no `??=`, `using var`, records, switch
  expressions, target-typed `new`. Tuples avoided in public APIs
  (fwd/bkwd are separate functions).
- `IDataModel.cs`, `IDataModelCollection.cs`, `LakeComponent.cs` kept
  verbatim (UI tree contract).
- Input property names kept exactly as the original (camelCase
  `supportInnerDiameter` etc.) — `SetValueToProperty` reflection bindings.
  Enum member names kept too (including `Strator`).
- Only external dependency: MathNet.Numerics (Brent root-find in
  `HoneycombCalculator`).

## File map

```
rewrite/
├── DESIGN.md                      this file
├── IDataModel.cs                  ┐
├── IDataModelCollection.cs        │ kept verbatim (UI tree)
├── LakeComponent.cs               ┘
├── SealMath.cs                    every formula once (incl. FORMULATION FLAG)
├── Inputs/
│   ├── SealCriteria.cs            hub node (tree root for criteria)
│   ├── CategoricalParameters.cs   + 5 enums
│   ├── GeometricParameters.cs     diameters AS diameters; owns honeycomb
│   ├── HoneyCombParameters.cs     s, a, L
│   ├── Conditions.cs              temps/speeds/Δp/g/w + speed-of-sound getters
│   └── NodalFrequencyData.cs      AnalysisCondition + sparse FrequencyTable
├── CriteriaChecks/                calculators + engine (namespace LakeCore.CriteriaChecks)
│   ├── HoneycombCalculator.cs     Brent root-find (MathNet)
│   ├── CampbellCalculator.cs
│   ├── AeroelasticCalculator.cs
│   ├── TravelingWaveCalculator.cs
│   ├── ToothAcousticCalculator.cs
│   ├── CavityAcousticCalculator.cs
│   └── CriteriaCheckEngine.cs     Run(criteria) → CriteriaResults
└── ResultObjects/                 plain result classes (namespace LakeCore.CriteriaChecks)
    ├── CampbellResult.cs          rows: f₁, λ, RRS; MinRRS, Margin
    ├── AeroelasticResult.cs       rows: f₁, Wr
    ├── TravelingWaveResults.cs    WaveRow, TravelingWaveSet, InteractionsResult + RelativeLine[nd]
    ├── ToothAcousticResult.cs     Circ + Axial rows
    ├── CavityAcousticResult.cs    Up/Down rows
    └── CriteriaResults.cs         container returned by the engine
```

Folder names do not dictate namespaces (same as the real LakeCore solution):
both CriteriaChecks/ and ResultObjects/ compile into LakeCore.CriteriaChecks.

## Usage sketch

```csharp
var criteria = new SealCriteria();
// ... UI / loader populates parameters and frequency tables ...
criteria.SealNodalFrequencyData.AddData(
    NodalFrequencyData.AnalysisCondition.Redline, nd: 2, mode: 1, frequency: 5203.5);

CriteriaResults r = CriteriaCheckEngine.Run(criteria);

double margin = r.Campbell.Margin;                       // NaN if no data
WaveRow w = r.Waves.Seal.Get(2, 1);                      // null if absent
ToothAcousticCircRow a = r.ToothAcoustic.Circumferential[2];
// plot layer overlays w.Forward/w.Backward against a.ForwardAcoustic/...
```

Status: written 2026-06-10, transcription-reviewed, **not build-verified**
(no C# toolchain in the sandbox). First compile happens in the user's
LakeCore solution; MathNet.Numerics NuGet required.
