#!/usr/bin/env python3
"""
revolve_mesh.py  (v2 -- variable angular resolution)
----------------------------------------------------
Reads the 2D axisymmetric CAX6 mesh produced by PrePoMax (calculixmacro.inp)
and revolves it about the global Y axis into a 3D sector mesh of N stacked
C3D15 wedges, suitable for CalculiX cyclic-symmetric modal analysis.

Compared to v1 the only behavioural change is the WEDGES_PER_SECTOR knob:
    v1: WEDGES_PER_SECTOR = 1  (3 angular node layers, theta = 0, S/2, S)
    v2: WEDGES_PER_SECTOR = N  (2N+1 angular node layers in total)

Layer structure for general N:
    Corner layers at theta = k*S/N,  k = 0..N
        - All 2D nodes (corners + midsides) get a copy here.
    Vertical-midside layers at theta = (k+0.5)*S/N,  k = 0..N-1
        - Only 2D-corner nodes get a copy (these become the vertical
          midsides of wedge k).

Cyclic boundary surfaces:
    LEFT_SURF  = all node copies at the very first corner layer (theta = 0)
    RIGHT_SURF = all node copies at the very last  corner layer (theta = S)
    (independent of N -- intermediate corner layers are interior.)

C3D15 orientation (unchanged from v1):
    Face S1 (nodes 1-3) at the HIGHER-theta corner layer (more negative Z)
    Face S2 (nodes 4-6) at the LOWER-theta  corner layer (closer to +Z)
    so that the right-hand normal of S1 points toward S2 (det J > 0).
"""

from __future__ import annotations

import math
import sys
from pathlib import Path

SOURCE_INP = Path(__file__).with_name("calculixmacro.inp")
OUT_INP    = Path(__file__).with_name("mesh3d.inp")

# -- analysis parameters that affect ONLY the mesh ----------------------------
SECTOR_DEG          = 15.0  # degrees of the datum sector (= 360 / N_sectors)
WEDGES_PER_SECTOR   = 5     # stacked C3D15 wedges across the sector

# -- element-type / set names -------------------------------------------------
ELSET_3D         = "DISC_ELEMS"
NSET_LEFT        = "LEFT_SURF"
NSET_RIGHT       = "RIGHT_SURF"
NSET_FIX         = "FIX_FACES"


def parse_2d_inp(path: Path):
    """Returns nodes2d {nid:(x,y)}, elems [(eid,[n1..n6])], fix2d set of 2D nids."""
    nodes2d: dict[int, tuple[float, float]] = {}
    elems: list[tuple[int, list[int]]] = []
    fix2d: set[int] = set()

    section = None
    for raw in path.read_text().splitlines():
        line = raw.strip()
        if not line or line.startswith("**"):
            continue
        if line.startswith("*"):
            head = line.split(",")[0].strip().lower()
            rest = line.lower()
            if head == "*node":
                section = "node"
            elif head == "*element" and "cax6" in rest:
                section = "elem"
            elif head == "*nset" and "nset=rightandleft" in rest.replace(" ", ""):
                section = "nset_fix"
            else:
                section = None
            continue

        if section == "node":
            parts = [p.strip() for p in line.split(",") if p.strip()]
            if len(parts) >= 3:
                nodes2d[int(parts[0])] = (float(parts[1]), float(parts[2]))
        elif section == "elem":
            parts = [p.strip() for p in line.split(",") if p.strip()]
            if len(parts) >= 7:
                eid = int(parts[0])
                conn = [int(p) for p in parts[1:7]]
                elems.append((eid, conn))
        elif section == "nset_fix":
            for tok in line.split(","):
                tok = tok.strip()
                if tok:
                    fix2d.add(int(tok))

    return nodes2d, elems, fix2d


def main() -> int:
    if not SOURCE_INP.exists():
        print(f"ERROR: cannot find {SOURCE_INP}", file=sys.stderr)
        return 1

    N = WEDGES_PER_SECTOR
    if N < 1:
        print("ERROR: WEDGES_PER_SECTOR must be >= 1", file=sys.stderr)
        return 1

    nodes2d, elems, fix2d = parse_2d_inp(SOURCE_INP)
    print(f"Parsed: {len(nodes2d)} 2D nodes, {len(elems)} CAX6 elements, "
          f"{len(fix2d)} fix-face 2D nodes")
    print(f"Wedges per sector: {N}  (=> {2*N+1} angular layers)")

    # classify 2D nodes as corner-type vs midside-type
    corner_nodes: set[int] = set()
    midside_nodes: set[int] = set()
    for _eid, conn in elems:
        for n in conn[0:3]:
            corner_nodes.add(n)
        for n in conn[3:6]:
            midside_nodes.add(n)
    overlap = corner_nodes & midside_nodes
    if overlap:
        print(f"WARNING: {len(overlap)} nodes are both corner and midside; "
              "treating them as corners.")
        midside_nodes -= overlap
    print(f"  Corner-type 2D nodes:  {len(corner_nodes)}")
    print(f"  Midside-type 2D nodes: {len(midside_nodes)}")

    # ---- ID offsets --------------------------------------------------------
    # Two flavours of layer:
    #   - "corner" layer k,  k in 0..N        (host both 2D corners + midsides)
    #   - "vmid"   layer k,  k in 0..N-1      (host only 2D corners; sits at
    #                                          theta = (k+0.5)*S/N)
    # Use a large base so every (kind, layer) gets a unique ID range.
    max_2d_id = max(nodes2d)
    base = 10 ** (len(str(max_2d_id)))    # round-up to next power of 10
    # corner layer k uses offset = k * base
    # vmid layer   k uses offset = (N + 1 + k) * base
    def corner_offset(k: int) -> int:
        return k * base
    def vmid_offset(k: int) -> int:
        return (N + 1 + k) * base

    sector_rad = math.radians(SECTOR_DEG)

    def theta_corner(k: int) -> float:
        return k * sector_rad / N
    def theta_vmid(k: int) -> float:
        return (k + 0.5) * sector_rad / N

    def rotate_y(x: float, y: float, theta: float) -> tuple[float, float, float]:
        """Rotate (X_2D, Y_2D, 0) about +Y by theta (rad).
        R_y = [[cos,0,sin],[0,1,0],[-sin,0,cos]] => (x*cos, y, -x*sin)."""
        c, s = math.cos(theta), math.sin(theta)
        return (x * c, y, -x * s)

    # ---- build 3D node table ----------------------------------------------
    nodes3d: dict[int, tuple[float, float, float]] = {}

    left_set:  list[int] = []   # corner layer 0
    right_set: list[int] = []   # corner layer N
    fix_set:   list[int] = []   # all interior + master copies of fix-face 2D nodes

    # corner layers: every 2D node gets a copy
    for k in range(N + 1):
        th = theta_corner(k)
        off = corner_offset(k)
        for nid, (x, y) in nodes2d.items():
            X, Y, Z = rotate_y(x, y, th)
            nodes3d[nid + off] = (X, Y, Z)
            if k == 0:
                left_set.append(nid + off)
            if k == N:
                right_set.append(nid + off)
            # FIX_FACES: include all corner-layer copies EXCEPT layer 0 (slave).
            # CalculiX forbids a node from being both an SPC dependent AND an
            # MPC dependent. The cyclic MPC propagates U=0 to layer 0
            # implicitly via U_left = U_right * exp(i*phase) = 0.
            if nid in fix2d and k != 0:
                fix_set.append(nid + off)

    # vmid layers: only 2D-corner nodes get a copy (they become C3D15 verts 13-15)
    for k in range(N):
        th = theta_vmid(k)
        off = vmid_offset(k)
        for nid in corner_nodes:
            x, y = nodes2d[nid]
            X, Y, Z = rotate_y(x, y, th)
            nodes3d[nid + off] = (X, Y, Z)
            # vmid layer is interior (k=0..N-1 all interior), include in FIX
            if nid in fix2d:
                fix_set.append(nid + off)

    # ---- build C3D15 connectivity -----------------------------------------
    # For each 2D element and each wedge k in 0..N-1:
    #   S1 face (nodes 1-3) at corner layer k+1   (higher theta, more -Z)
    #   S2 face (nodes 4-6) at corner layer k     (lower theta)
    #   Vertical midsides at vmid layer k
    wedges: list[tuple[int, list[int]]] = []
    next_eid = max(eid for eid, _ in elems) + 1
    for eid, conn in elems:
        c1, c2, c3, m12, m23, m31 = conn
        for k in range(N):
            off_S1 = corner_offset(k + 1)   # higher theta
            off_S2 = corner_offset(k)       # lower  theta
            off_V  = vmid_offset(k)

            b1, b2, b3       = c1 + off_S1, c2 + off_S1, c3 + off_S1
            t1, t2, t3       = c1 + off_S2, c2 + off_S2, c3 + off_S2
            b12, b23, b31    = m12 + off_S1, m23 + off_S1, m31 + off_S1
            t12, t23, t31    = m12 + off_S2, m23 + off_S2, m31 + off_S2
            v1, v2, v3       = c1 + off_V,  c2 + off_V,  c3 + off_V

            wedge_nodes = [b1, b2, b3,
                           t1, t2, t3,
                           b12, b23, b31,
                           t12, t23, t31,
                           v1, v2, v3]
            new_eid = eid if k == 0 else next_eid
            if k > 0:
                next_eid += 1
            wedges.append((new_eid, wedge_nodes))

    # ---- write output ------------------------------------------------------
    with OUT_INP.open("w") as f:
        f.write("** ---------------------------------------------------------\n")
        f.write("** mesh3d.inp -- generated by revolve_mesh.py (v2)\n")
        f.write(f"** Sector angle: {SECTOR_DEG} deg ({sector_rad:.10f} rad)\n")
        f.write(f"** Wedges/sector: {N}  (2N+1 = {2*N+1} angular layers)\n")
        f.write(f"** 2D source   : {SOURCE_INP.name}\n")
        f.write(f"** 3D nodes    : {len(nodes3d)}\n")
        f.write(f"** 3D elements : {len(wedges)} C3D15\n")
        f.write("** ---------------------------------------------------------\n")

        f.write("*NODE, NSET=NALL\n")
        for nid in sorted(nodes3d):
            X, Y, Z = nodes3d[nid]
            f.write(f"{nid}, {X:.10E}, {Y:.10E}, {Z:.10E}\n")

        f.write(f"*ELEMENT, TYPE=C3D15, ELSET={ELSET_3D}\n")
        for eid, conn in wedges:
            f.write(f"{eid}," + ",".join(str(n) for n in conn) + "\n")

        def write_nset(name: str, ids: list[int]) -> None:
            uniq = sorted(set(ids))
            f.write(f"*NSET, NSET={name}\n")
            line_ids: list[str] = []
            for i, nid in enumerate(uniq, 1):
                line_ids.append(str(nid))
                if i % 8 == 0:
                    f.write(", ".join(line_ids) + "\n")
                    line_ids = []
            if line_ids:
                f.write(", ".join(line_ids) + "\n")

        write_nset(NSET_LEFT, left_set)
        write_nset(NSET_RIGHT, right_set)
        write_nset(NSET_FIX, fix_set)

    print(f"\nWrote {OUT_INP}")
    print(f"  3D nodes   : {len(nodes3d)}")
    print(f"  3D elems   : {len(wedges)} C3D15")
    print(f"  {NSET_LEFT:<12s}: {len(set(left_set))} nodes")
    print(f"  {NSET_RIGHT:<12s}: {len(set(right_set))} nodes")
    print(f"  {NSET_FIX:<12s}: {len(set(fix_set))} nodes")

    return 0


if __name__ == "__main__":
    sys.exit(main())
