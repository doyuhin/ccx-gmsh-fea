#!/usr/bin/env python3
"""
revolve_mesh.py
---------------
Reads the 2D axisymmetric CAX6 mesh produced by PrePoMax (calculixmacro.inp)
and revolves it about the global Y axis into a 3D sector mesh suitable for
CalculiX cyclic-symmetric modal analysis.

Mapping:
    CAX6  (6-node 2D triangle, axisymmetric)  ->  C3D15 (15-node 3D wedge)

Geometry:
    The PrePoMax 2D coords are interpreted as (r, z): X_2D = radius, Y_2D = axial.
    Three angular layers are generated:
        - Layer 0 at theta =  0          (LEFT_SURF,  slave for cyclic-sym tie)
        - Layer 1 at theta =  SECTOR/2   (only corner-type 2D nodes -> vertical midsides)
        - Layer 2 at theta =  SECTOR     (RIGHT_SURF, master for cyclic-sym tie)
    Rotation is performed with the standard Y-axis rotation matrix.
    Layer 2 lies on -Z because positive Y rotation maps +X toward -Z; this
    makes the bottom-face normal of every wedge (from 2D CCW corners 1->2->3
    viewed from +Z) point AWAY from the wedge body, satisfying the C3D15
    orientation convention.

Cyclic boundary:
    Slave  surface = LEFT_SURF  (all layer-0 copies of every 2D node)
    Master surface = RIGHT_SURF (all layer-2 copies of every 2D node)
    CalculiX multiplies the datum sector by rotating slave -> master, i.e.
    in the direction of increasing theta about +Y.

Fix set:
    The 'rightandleft' Nset that PrePoMax wrote is the cross-section's top
    and bottom edges (the axial faces once revolved). Every 2D node in that
    set is copied across all three angular layers into FIX_FACES, so the
    *BOUNDARY in the .inp deck fixes the full annular front+back surfaces.

Output:
    mesh3d.inp   - included by disc_modal.inp via *INCLUDE.
"""

from __future__ import annotations

import math
import os
import re
import sys
from pathlib import Path

SOURCE_INP = Path(__file__).with_name("calculixmacro.inp")
OUT_INP    = Path(__file__).with_name("mesh3d.inp")

# -- analysis parameters that affect ONLY the mesh ----------------------------
SECTOR_DEG       = 15.0       # degrees of the datum sector (= 360 / N_sectors)
# The number of physical sectors (N=24 for 15 deg) is set in disc_modal.inp,
# not here -- the mesh itself just covers the datum sector.

# -- element-type names -------------------------------------------------------
ELSET_3D         = "DISC_ELEMS"
NSET_LEFT        = "LEFT_SURF"
NSET_RIGHT       = "RIGHT_SURF"
NSET_FIX         = "FIX_FACES"


def parse_2d_inp(path: Path):
    """
    Returns:
        nodes2d : dict {node_id: (x, y)}
        elems   : list of (elem_id, [n1..n6])    -- CAX6 connectivity
        fix2d   : set of 2D node IDs from 'rightandleft' Nset
    """
    nodes2d: dict[int, tuple[float, float]] = {}
    elems: list[tuple[int, list[int]]] = []
    fix2d: set[int] = set()

    text = path.read_text()
    lines = text.splitlines()

    section = None   # one of "node", "elem", "nset_fix", or None
    for raw in lines:
        line = raw.strip()
        if not line:
            continue
        if line.startswith("**"):
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
                nid = int(parts[0])
                x = float(parts[1])
                y = float(parts[2])
                nodes2d[nid] = (x, y)
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

    nodes2d, elems, fix2d = parse_2d_inp(SOURCE_INP)
    print(f"Parsed: {len(nodes2d)} 2D nodes, {len(elems)} CAX6 elements, "
          f"{len(fix2d)} fix-face 2D nodes")

    # ---- classify nodes as corner-type vs midside-type ---------------------
    corner_nodes: set[int] = set()
    midside_nodes: set[int] = set()
    for _eid, conn in elems:
        for n in conn[0:3]:
            corner_nodes.add(n)
        for n in conn[3:6]:
            midside_nodes.add(n)

    overlap = corner_nodes & midside_nodes
    if overlap:
        # If a node serves as both, we'll treat it as a corner (safer: we
        # generate a layer-1 copy for it). Warn so the user is aware.
        print(f"WARNING: {len(overlap)} nodes appear as both corner and midside; "
              "treating them as corners.")
        midside_nodes -= overlap

    print(f"  Corner-type 2D nodes:  {len(corner_nodes)}")
    print(f"  Midside-type 2D nodes: {len(midside_nodes)}")

    # ---- choose ID offsets so 3D node IDs are unique -----------------------
    max_2d_id = max(nodes2d)
    # Round up to next power of 10 for human-readable ID ranges
    base = 10 ** (len(str(max_2d_id)))
    offset_L0 = 0            # layer 0 (theta = 0)         : same IDs as 2D
    offset_L1 = base         # layer 1 (theta = SECTOR/2)  : only corners
    offset_L2 = 2 * base     # layer 2 (theta = SECTOR)

    sector_rad = math.radians(SECTOR_DEG)
    angles = (0.0, sector_rad / 2.0, sector_rad)

    def rotate_y(x: float, y: float, theta: float) -> tuple[float, float, float]:
        """
        Rotate (X_2D, Y_2D, 0) about +Y axis by theta (rad).
        R_y = [[cos, 0, sin], [0,1,0], [-sin, 0, cos]] applied to (x, y, 0)
        => (x*cos, y, -x*sin)
        """
        c = math.cos(theta)
        s = math.sin(theta)
        return (x * c, y, -x * s)

    # ---- build 3D node table -----------------------------------------------
    # nodes3d : dict {3D_id: (X, Y, Z)}
    nodes3d: dict[int, tuple[float, float, float]] = {}

    left_set:  list[int] = []   # layer 0 nodes  (LEFT_SURF)
    right_set: list[int] = []   # layer 2 nodes  (RIGHT_SURF)
    fix_set:   list[int] = []   # all layers' copies of fix-face 2D nodes

    for nid, (x, y) in nodes2d.items():
        # layer 0
        X0, Y0, Z0 = rotate_y(x, y, angles[0])
        id0 = nid + offset_L0
        nodes3d[id0] = (X0, Y0, Z0)
        left_set.append(id0)

        # layer 2
        X2, Y2, Z2 = rotate_y(x, y, angles[2])
        id2 = nid + offset_L2
        nodes3d[id2] = (X2, Y2, Z2)
        right_set.append(id2)

        # layer 1 -- only corner-type 2D nodes
        if nid in corner_nodes:
            X1, Y1, Z1 = rotate_y(x, y, angles[1])
            id1 = nid + offset_L1
            nodes3d[id1] = (X1, Y1, Z1)

        if nid in fix2d:
            # Layer 0 (LEFT_SURF) is the dependent side of the cyclic-sym tie.
            # CalculiX forbids a node from being both an SPC dependent AND an
            # MPC dependent (see ccx 2.23 manual section 7.133 *TIE).
            # We therefore SPC only the master (layer 2) and interior
            # (layer 1) copies. The cyclic MPC propagates U=0 to layer 0:
            #     U_left = U_right * exp(i*2*pi*N/M) = 0 * phase = 0.
            # fix_set.append(id0)        # <-- intentionally omitted
            fix_set.append(id2)
            if nid in corner_nodes:
                fix_set.append(nid + offset_L1)

    # ---- build C3D15 connectivity ------------------------------------------
    # CalculiX/Abaqus C3D15 orientation rule: the right-hand normal of nodes
    # 1->2->3 (face S1) must point TOWARD nodes 4-5-6 (face S2), so that the
    # (xi, eta, zeta) local frame is right-handed and det(J) > 0.
    #
    # The CAX6 corners (c1,c2,c3) from PrePoMax are CCW in the (X_2D, Y_2D)
    # plane, so their right-hand normal is in the +Z direction (out of the
    # 2D page). Our revolution puts theta=+15deg at NEGATIVE Z (positive Y
    # rotation maps +X toward -Z). Therefore we must place the +15deg layer
    # as face S1 (so its normal points away from -Z, i.e. toward +Z, where
    # the theta=0 face sits) -- in other words, layer 2 is the "bottom"
    # face in the wedge connectivity and layer 0 is the "top".
    wedges: list[tuple[int, list[int]]] = []
    for eid, conn in elems:
        c1, c2, c3, m12, m23, m31 = conn
        # face S1 (nodes 1..3) at LAYER 2 (theta = +15deg)
        b1 = c1 + offset_L2
        b2 = c2 + offset_L2
        b3 = c3 + offset_L2
        # face S2 (nodes 4..6) at LAYER 0 (theta = 0)
        t1 = c1 + offset_L0
        t2 = c2 + offset_L0
        t3 = c3 + offset_L0
        # midsides on face S1 (layer 2)
        b12 = m12 + offset_L2
        b23 = m23 + offset_L2
        b31 = m31 + offset_L2
        # midsides on face S2 (layer 0)
        t12 = m12 + offset_L0
        t23 = m23 + offset_L0
        t31 = m31 + offset_L0
        # vertical midsides at layer 1 (between corresponding corners of S1, S2)
        v1 = c1 + offset_L1
        v2 = c2 + offset_L1
        v3 = c3 + offset_L1

        wedge_nodes = [b1, b2, b3,    # 1..3  face S1 corners (layer 2)
                       t1, t2, t3,    # 4..6  face S2 corners (layer 0)
                       b12, b23, b31, # 7..9  face S1 midsides
                       t12, t23, t31, # 10..12 face S2 midsides
                       v1, v2, v3]    # 13..15 vertical midsides (layer 1)
        wedges.append((eid, wedge_nodes))

    # ---- write output ------------------------------------------------------
    with OUT_INP.open("w") as f:
        f.write("** ---------------------------------------------------------\n")
        f.write("** mesh3d.inp -- generated by revolve_mesh.py\n")
        f.write(f"** Sector angle: {SECTOR_DEG} deg ({sector_rad:.10f} rad)\n")
        f.write(f"** 2D source   : {SOURCE_INP.name}\n")
        f.write(f"** 3D nodes    : {len(nodes3d)}\n")
        f.write(f"** 3D elements : {len(wedges)} C3D15\n")
        f.write("** ---------------------------------------------------------\n")

        # nodes
        f.write("*NODE, NSET=NALL\n")
        for nid in sorted(nodes3d):
            X, Y, Z = nodes3d[nid]
            f.write(f"{nid}, {X:.10E}, {Y:.10E}, {Z:.10E}\n")

        # elements
        f.write(f"*ELEMENT, TYPE=C3D15, ELSET={ELSET_3D}\n")
        for eid, conn in wedges:
            # CalculiX accepts continuation lines; keep <=16 entries per line
            # to be safe. 1 (id) + 15 (nodes) = 16 entries -- fits.
            f.write(f"{eid}," + ",".join(str(n) for n in conn) + "\n")

        # node sets
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
