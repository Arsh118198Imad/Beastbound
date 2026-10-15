"""Environment kit: weathered boulders and cut-stone blocks for Sunmeadow Vale (original assets).

Exports web/assets/models/env.glb with meshes rock0..rock5 (unit-size boulders, flat-ish base at
z = -0.35 like the old procedural rocks) and block0/block1 (1 m cut-stone blocks centred on the
origin: one intact with chipped edges, one with a broken top). Colour is per-vertex (stone tones,
moss on upward faces, lichen specks, darkened crevices); the game adds fine world-space stone detail
in its shader so blocks can be stretched into pillars and lintels without texture smearing.

Usage (from the repository root):
    blender -b -P art/blender/rocks.py            # build + export
    blender -b -P art/blender/rocks.py -- render  # also writes a labelled Blender preview render
"""
import math
import os
import sys

import bmesh
import bpy
from mathutils import Vector, noise

sys.path.insert(0, os.path.dirname(__file__))
import common as C  # noqa: E402

V = Vector
R = C.rng(9090)

STONE = [V((0.15, 0.145, 0.13)), V((0.19, 0.18, 0.155)), V((0.13, 0.13, 0.125))]
MOSS = V((0.075, 0.13, 0.035))
MOSS_LIGHT = V((0.15, 0.22, 0.06))
LICHEN = V((0.3, 0.3, 0.2))


def fbm(p, octaves=4):
    s, a, f = 0.0, 0.5, 1.0
    for _ in range(octaves):
        s += a * noise.noise(p * f)
        a *= 0.5
        f *= 2.03
    return s


def rock(name, seed):
    """Fractured boulder: convex hull of scattered points (big planar facets), then fine weathering."""
    bm = bmesh.new()
    off = V((seed * 3.17, seed * 1.31, seed * 2.07))
    stretch = V((R.uniform(0.9, 1.3), R.uniform(0.85, 1.2), R.uniform(0.65, 0.95)))
    pts = []
    for i in range(34):
        z = R.uniform(-1, 1)
        a = R.uniform(0, math.tau)
        rr = math.sqrt(1 - z * z)
        p = V((rr * math.cos(a), rr * math.sin(a), z)) * R.uniform(0.82, 1.08)
        pts.append(bm.verts.new(V((p.x * stretch.x, p.y * stretch.y, p.z * stretch.z))))
    bmesh.ops.convex_hull(bm, input=pts)
    bmesh.ops.triangulate(bm, faces=list(bm.faces))
    bmesh.ops.subdivide_edges(bm, edges=list(bm.edges), cuts=3, use_grid_fill=True)
    for v in bm.verts:
        p = v.co
        n = p.normalized()
        q = p + n * (0.035 * fbm(p * 3.5 + off, 3) + 0.012 * fbm(p * 12.0 + off, 2))
        if q.z < -0.35:
            q.z = -0.35 + (q.z + 0.35) * 0.2
        v.co = q
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = C.link(bpy.data.objects.new(name, me))
    flat_faceted(obj, angle=32)
    paint(obj, seed)
    return obj


def block(name, seed, broken=False):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.bevel(bm, geom=list(bm.edges), offset=0.035, segments=2, affect="EDGES")
    bmesh.ops.subdivide_edges(bm, edges=list(bm.edges), cuts=4, use_grid_fill=True)
    off = V((seed * 2.3, seed * 0.7, seed * 1.9))
    for v in bm.verts:
        p = v.co.copy()
        edge = max(abs(p.x), abs(p.y), abs(p.z))
        corner = sum(1 for c in (p.x, p.y, p.z) if abs(c) > 0.44)
        chip = max(0.0, fbm(p * 4.0 + off, 3)) * (0.07 if corner >= 2 else 0.02)
        n = p.normalized()
        q = p - n * chip + n * 0.01 * fbm(p * 11.0 + off, 2)
        if broken and q.z > 0.15:
            cut = 0.35 + 0.18 * fbm(V((q.x * 3.0, q.y * 3.0, 0)) + off, 3)
            if q.z > cut:
                q.z = cut + (q.z - cut) * 0.15
        v.co = q
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = C.link(bpy.data.objects.new(name, me))
    flat_faceted(obj, angle=35)
    paint(obj, seed, moss_threshold=0.75)
    return obj


def flat_faceted(obj, angle=40):
    """Smooth shading with sharp creases above `angle` degrees (crisp stone facets)."""
    C.shade_smooth(obj)
    me = obj.data
    if hasattr(me, "use_auto_smooth"):
        me.use_auto_smooth = True
        me.auto_smooth_angle = math.radians(angle)


def paint(obj, seed, moss_threshold=0.55):
    """Vertex colours: stone variation, crevice darkening, moss on top faces, lichen specks."""
    me = obj.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    concav = {}
    for v in bm.verts:
        n = v.normal
        acc, k = 0.0, 0
        for e in v.link_edges:
            o = e.other_vert(v)
            d = o.co - v.co
            if d.length > 1e-6:
                acc += n.dot(d.normalized())
                k += 1
        concav[v.index] = acc / max(k, 1)
    normals = {v.index: v.normal.copy() for v in bm.verts}
    bm.free()
    off = V((seed * 5.1, seed * 3.3, seed * 1.7))
    base = STONE[seed % len(STONE)]

    def col(i, p):
        n = normals[i]
        var = 0.82 + 0.36 * (0.5 + 0.5 * fbm(p * 3.0 + off, 3))
        c = base * var
        cav = max(0.0, min(1.0, concav[i] * 3.0))
        c = c * (1.0 - 0.55 * cav)
        up = max(0.0, min(1.0, (n.z - moss_threshold) / (1.0 - moss_threshold)))
        patch = max(0.0, min(1.0, 0.5 + 1.6 * fbm(p * 2.2 + off * 0.7, 3)))
        m = up * patch
        if m > 0:
            c = c.lerp(MOSS.lerp(MOSS_LIGHT, 0.5 + 0.5 * fbm(p * 9.0, 2)), min(1.0, m * 1.4))
        if fbm(p * 18.0 + off, 2) > 0.36 and n.z > -0.2:
            c = c.lerp(LICHEN, 0.35)
        return c
    attr = me.color_attributes.new("Col", "FLOAT_COLOR", "POINT")
    for v in me.vertices:
        attr.data[v.index].color = (*col(v.index, v.co), 1.0)


def main():
    C.reset_scene()
    objs = [rock(f"rock{i}", i + 1) for i in range(6)]
    objs.append(block("block0", 11))
    objs.append(block("block1", 12, broken=True))
    mat = C.vcol_material("EnvStone", 0.92)
    for o in objs:
        o.data.materials.append(mat)
    print("ENV tris:", {o.name: C.count_tris([o]) for o in objs})
    out = os.path.join(C.EXPORT_DIR, "env.glb")
    print("ENV glb bytes:", C.export_glb(out, objs))
    if "render" in sys.argv:
        # Lay the kit out on the ground for an overview.
        for i, o in enumerate(objs):
            o.location = ((i % 4) * 2.6 - 3.9, (i // 4) * 2.8, 0.35 if o.name.startswith("rock") else 0.5)
        C.preview_render(os.path.join(C.RENDER_DIR, "env_kit_blender_render.png"), (0, 1.4, 0.4), distance=11, height=4.5, yaw_deg=-10, sun_deg=(50, 0, -40))


main()
