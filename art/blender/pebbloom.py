"""Pebbloom — Beastbound's starter creature, redesigned as a sculpted, rigged model.

Keeps the original identity: a round, fox-sized creature with mossy stone plates on its back, petal
ears, an amber brow crystal that glows when it is happy, big glossy eyes and a living flower on the
tip of its curled tail. Same pipeline as thalorix.py; bone names match so QuadrupedAnimator drives it.

Usage (from the repository root):
    blender -b -P art/blender/pebbloom.py            # model + bake + export
    blender -b -P art/blender/pebbloom.py -- render  # also writes a labelled Blender preview render
"""
import math
import os
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(__file__))
import common as C  # noqa: E402

V = Vector
R = C.rng(808)

# Blender space: Z up, the creature faces -Y.
J = {
    "pelvis": V((0, 0.24, 0.44)), "mid": V((0, 0.02, 0.48)), "chest": V((0, -0.2, 0.47)),
    "neck": V((0, -0.33, 0.6)), "skullB": V((0, -0.39, 0.72)), "skull": V((0, -0.47, 0.73)),
    "muzzle": V((0, -0.61, 0.67)), "nose": V((0, -0.67, 0.66)),
    "tail0": V((0, 0.45, 0.5)), "tail1": V((0, 0.58, 0.64)), "tail2": V((0, 0.62, 0.82)), "tail3": V((0, 0.57, 0.97)),
}
LEGS = {
    "FL": [V((0.16, -0.2, 0.36)), V((0.17, -0.18, 0.2)), V((0.165, -0.2, 0.08)), V((0.165, -0.24, 0.035)), V((0.165, -0.29, 0.03))],
    "HL": [V((0.17, 0.24, 0.37)), V((0.18, 0.18, 0.22)), V((0.17, 0.28, 0.09)), V((0.17, 0.25, 0.035)), V((0.17, 0.2, 0.03))],
}
for k in list(LEGS):
    LEGS[k.replace("L", "R")] = [V((-p.x, p.y, p.z)) for p in LEGS[k]]

PAL = {
    "body": (0.13, 0.17, 0.11), "back": (0.07, 0.095, 0.065), "belly": (0.6, 0.53, 0.38),
    "stone": V((0.3, 0.3, 0.27)), "moss": V((0.12, 0.3, 0.07)), "petal": V((0.95, 0.32, 0.4)), "petalIn": V((1.0, 0.65, 0.25)),
}


def build_body():
    joints, edges = [], []

    def add(p, r):
        joints.append((p, (r, r)))
        return len(joints) - 1

    spine = [add(J["tail0"], 0.08), add(J["pelvis"], 0.27), add(J["mid"], 0.3), add(J["chest"], 0.27),
             add(J["neck"], 0.17), add(J["skullB"], 0.22), add(J["skull"], 0.22), add(J["muzzle"], 0.11), add(J["nose"], 0.06)]
    tail = [spine[0], add(J["tail1"], 0.06), add(J["tail2"], 0.05), add(J["tail3"], 0.04)]
    edges += list(zip(spine, spine[1:])) + list(zip(tail, tail[1:]))
    radii = {"F": [0.09, 0.07, 0.058, 0.064, 0.05], "H": [0.11, 0.08, 0.06, 0.064, 0.05]}
    for name, pts in LEGS.items():
        idx = [add(p, r) for p, r in zip(pts, radii[name[0]])]
        edges.append((spine[3] if name[0] == "F" else spine[1], idx[0]))
        edges += list(zip(idx, idx[1:]))
    body = C.skin_mesh("PebbloomBody", joints, edges, subdiv=2)
    for v in body.data.vertices:  # rounder cheeks, plumper belly
        p = v.co
        if p.y < -0.38 and 0.62 < p.z < 0.78:
            p.x *= 1.12
        if -0.15 < p.y < 0.25 and p.z < 0.4 and abs(p.x) < 0.22:
            p.z -= 0.025
    return body


def build_plates():
    """Mossy stone plates over the back (the 'pebble' in Pebbloom)."""
    rocks = []
    spots = [(-0.14, 0.0), (-0.05, -0.15), (-0.05, 0.15), (0.06, 0.0), (0.08, -0.18), (0.08, 0.18), (0.19, -0.08), (0.19, 0.1), (0.28, 0.0)]
    for i, (y, x) in enumerate(spots):
        top = J["chest"].lerp(J["pelvis"], (y + 0.2) / 0.44)
        z = top.z + 0.3 - abs(x) * 0.5
        n = V((x * 2.2, 0, 1)).normalized()
        c = V((x, y, z))
        r = C.rock_mesh(f"plate{i}", c, (0.12, 0.13, 0.07), R, detail=2, flatten=0.8)
        # Tilt about Y so each plate lies on the curved back (its up axis follows the body normal).
        a = math.atan2(n.x, n.z)
        ca, sa = math.cos(a), math.sin(a)
        for v in r.data.vertices:
            d = v.co - c
            v.co = c + V((d.x * ca + d.z * sa, d.y, -d.x * sa + d.z * ca))
        rocks.append(r)
    plates = C.join(rocks, "PebbloomPlates")

    def col(p):
        top = max(0.0, min(1.0, (p.z - 0.7) / 0.1))
        k = 0.85 + 0.15 * math.sin(p.x * 60 + p.y * 40)
        return (PAL["stone"] * k).lerp(PAL["moss"], top ** 0.8)
    C.colorize(plates, col)
    # Moss tufts on top of the plates.
    tufts = []
    for i in range(40):
        y = R.uniform(-0.2, 0.32)
        x = R.uniform(-0.2, 0.2)
        top = J["chest"].lerp(J["pelvis"], (y + 0.2) / 0.52)
        root = V((x, y, top.z + 0.35 - abs(x) * 0.5))
        tufts.append(C.fur_clump(f"moss{i}", root, V((R.uniform(-0.5, 0.5), R.uniform(-0.3, 0.6), 1.0)), R.uniform(0.04, 0.07), 0.014, R, droop=0.4, sides=4, segs=4))
    moss = C.join(tufts, "PebbloomMoss")
    C.colorize(moss, lambda p: PAL["moss"] * R.uniform(0.8, 1.3))
    return plates, moss


def leaf_shape(name, base, tip, width, normal):
    """A flat, pointed petal/leaf (two-sided via thickness) from base to tip."""
    axis = (tip - base)
    side = axis.cross(normal).normalized() * width
    pts = [base, base + axis * 0.3, base + axis * 0.65, tip]
    radii = [width * 0.35, width, width * 0.75, 0.003]
    obj = C.tube_from_points(name, pts, radii, sides=8)
    # Flatten along the normal so the tube becomes a petal.
    c = base
    for v in obj.data.vertices:
        d = v.co - c
        along_n = d.dot(normal)
        v.co = v.co - normal * along_n * 0.62
    return obj


def build_ears():
    ears = []
    for s in (-1, 1):
        base = V((0.12 * s, -0.4, 0.88))
        tip = base + V((0.12 * s, 0.06, 0.26))
        ear = leaf_shape(f"ear{s}", base, tip, 0.09, V((0.45 * s, -1, 0.2)).normalized())
        C.colorize(ear, lambda p, base=base: PAL["petal"].lerp(PAL["petalIn"], max(0.0, min(1.0, (0.06 - abs(p.x - base.x * 1.15)) / 0.06)) * 0.6))
        ears.append(ear)
    return ears


def build_face():
    eyes, shines = [], []
    for s in (-1, 1):
        eyes.append(C.uv_sphere(f"eye{s}", (0.1 * s, -0.62, 0.765), (0.055, 0.035, 0.065), 16, 10))
        shines.append(C.uv_sphere(f"shine{s}", (0.092 * s, -0.655, 0.79), (0.014, 0.008, 0.016), 8, 6))
    for e in eyes:
        C.colorize(e, lambda p: V((0.015, 0.012, 0.02)))
    for e in shines:
        C.colorize(e, lambda p: V((1, 1, 1)))
    # Iris with a darker rim and a pupil, set into the front of each eye.
    for s_ in (-1, 1):
        c = V((0.1 * s_, -0.648, 0.768))
        iris = C.uv_sphere(f"iris{s_}", tuple(c), (0.041, 0.011, 0.048), 16, 8)
        C.colorize(iris, lambda p, c=c: V((0.62, 0.42, 0.08)).lerp(V((0.18, 0.1, 0.02)), min(1.0, ((p.x - c.x) ** 2 / 0.041 ** 2 + (p.z - c.z) ** 2 / 0.048 ** 2) ** 0.5) ** 3))
        pupil = C.uv_sphere(f"pupil{s_}", (0.099 * s_, -0.657, 0.77), (0.021, 0.006, 0.029), 12, 6)
        C.colorize(pupil, lambda p: V((0.01, 0.008, 0.012)))
        eyes.append(iris); eyes.append(pupil)
    for sh in shines:
        for v in sh.data.vertices:
            v.co.y -= 0.01
    nose = C.uv_sphere("nose", (0, -0.725, 0.68), (0.03, 0.02, 0.022), 10, 6)
    C.colorize(nose, lambda p: V((0.03, 0.02, 0.02)))
    return eyes, shines, nose


def build_tufts():
    """Soft fur tufts: cheeks, chest ruff, crown and elbows (breaks up the smooth silhouette)."""
    parts, cols = [], []
    body, belly = V(PAL["body"]), V(PAL["belly"])
    for s_ in (-1, 1):
        for i in range(9):
            root = J["skull"] + V((0.19 * s_, R.uniform(-0.02, 0.08), R.uniform(-0.12, -0.02)))
            parts.append(C.fur_clump(f"cheek{s_}{i}", root, V((0.8 * s_, R.uniform(0.2, 0.6), -0.35)), R.uniform(0.045, 0.075), 0.018, R, droop=0.4, sides=4, segs=4))
            cols.append(belly * 0.75)
        for leg in ("F", "H"):
            pts = LEGS[f"{leg}{'L' if s_ > 0 else 'R'}"]
            for i in range(4):
                root = pts[0].lerp(pts[1], R.uniform(0.5, 0.95)) + V((0, 0.05 if leg == "F" else 0.07, 0))
                parts.append(C.fur_clump(f"elb{leg}{s_}{i}", root, V((0.2 * s_, 1.0, -0.4)), R.uniform(0.05, 0.08), 0.015, R, droop=0.3, sides=4, segs=4))
                cols.append(body * 1.1)
    for i in range(22):
        root = J["chest"].lerp(J["neck"], R.uniform(0.1, 0.9)) + V((R.uniform(-0.13, 0.13), -0.16, -0.1))
        parts.append(C.fur_clump(f"ruff{i}", root, V((R.uniform(-0.3, 0.3), -0.3, -1.0)), R.uniform(0.045, 0.07), 0.022, R, droop=0.5, sides=4, segs=4))
        cols.append(belly * 0.72)
    for i in range(6):
        root = J["skullB"] + V((R.uniform(-0.05, 0.05), R.uniform(-0.03, 0.04), 0.2))
        parts.append(C.fur_clump(f"crown{i}", root, V((R.uniform(-0.4, 0.4), 0.6, 1.0)), R.uniform(0.05, 0.08), 0.016, R, droop=0.3, sides=4, segs=4))
        cols.append(body * 1.2)
    for obj, col in zip(parts, cols):
        C.colorize(obj, lambda p, col=col: col)
    return C.join(parts, "PebbloomTufts")


def build_crystal():
    c = C.tube_from_points("crystal", [V((0, -0.6, 0.86)), V((0, -0.615, 0.885)), V((0, -0.63, 0.915)), V((0, -0.645, 0.95))], [0.018, 0.03, 0.024, 0.002], sides=6)
    C.colorize(c, lambda p: V((1, 1, 1)))
    return c


def build_bloom():
    tip = J["tail3"] + (J["tail3"] - J["tail2"]).normalized() * 0.04
    parts = []
    up = (J["tail3"] - J["tail2"]).normalized()
    ref = V((1, 0, 0))
    u = up.cross(ref).normalized()
    w = up.cross(u).normalized()
    for k in range(6):
        a = k / 6 * math.tau
        d = (u * math.cos(a) + w * math.sin(a)) * 0.9 + up * 0.45
        parts.append(leaf_shape(f"petal{k}", tip, tip + d.normalized() * 0.13, 0.05, up.cross(d).cross(d).normalized()))
    petals = C.join(parts, "PebbloomPetals")
    C.colorize(petals, lambda p: PAL["petal"].lerp(PAL["petalIn"], max(0.0, 1 - (p - tip).length / 0.06)))
    center = C.uv_sphere("bloomCenter", tip + up * 0.02, (0.035, 0.035, 0.03), 10, 6)
    C.colorize(center, lambda p: V((1.0, 0.75, 0.2)))
    leaves = [leaf_shape(f"tleaf{k}", J["tail2"].lerp(J["tail3"], 0.6), J["tail2"].lerp(J["tail3"], 0.6) + V((0.12 * k, 0.05, -0.03)), 0.03, V((0, 1, 0))) for k in (-1, 1)]
    for l in leaves:
        C.colorize(l, lambda p: V((0.1, 0.3, 0.06)))
    return C.join([petals, center, *leaves], "PebbloomBloom")


def fur_material():
    mat = bpy.data.materials.new("PebbloomFurProc")
    N = C.Nodes(mat)
    co = N.coords("Object")
    nrm = N.coords("Normal")
    sepn = N.add("ShaderNodeSeparateXYZ")
    N.link(nrm, sepn.inputs[0])
    sep = N.add("ShaderNodeSeparateXYZ")
    N.link(co, sep.inputs[0])
    sb = N.add("ShaderNodeVectorMath", operation="MULTIPLY")
    N.link(co, sb.inputs[0]); sb.inputs[1].default_value = (120, 14, 120)
    streak = N.noise(1.0, 8, 0.6, sb.outputs[0]).outputs["Fac"]
    up = N.math("MULTIPLY", N.math("SUBTRACT", sepn.outputs["Z"], 0.2), 1.6, clamp=True)
    under = N.math("MULTIPLY", N.math("SUBTRACT", -0.1, sepn.outputs["Z"]), 2.2, clamp=True)
    face = N.math("MULTIPLY", N.math("SUBTRACT", -0.52, sep.outputs["Y"]), 8.0, clamp=True)  # muzzle/cheeks lighter
    lowface = N.math("MULTIPLY", face, N.math("SUBTRACT", 1.0, N.math("MULTIPLY", N.math("SUBTRACT", sep.outputs["Z"], 0.7), 8.0, clamp=True), clamp=True))
    base = N.mix(up, PAL["body"], PAL["back"])
    base = N.mix(N.math("MAXIMUM", under, lowface), base, PAL["belly"])
    paws = N.math("SUBTRACT", 1.0, N.math("MULTIPLY", N.math("SUBTRACT", sep.outputs["Z"], 0.06), 20.0, clamp=True), clamp=True)
    base = N.mix(paws, base, (0.5, 0.45, 0.33))
    shade = N.ramp(streak, [(0.3, (0.7, 0.7, 0.7)), (0.7, (1.15, 1.15, 1.15))])
    alb = N.add("ShaderNodeMix", data_type="RGBA", blend_type="MULTIPLY")
    alb.inputs[0].default_value = 1.0
    N.link(base, alb.inputs[6]); N.link(shade, alb.inputs[7])
    albedo = alb.outputs[2]
    bsdf = N.add("ShaderNodeBsdfPrincipled", (500, 0))
    bump = N.add("ShaderNodeBump")
    bump.inputs["Strength"].default_value = 0.6
    bump.inputs["Distance"].default_value = 0.002
    N.link(streak, bump.inputs["Height"])
    N.link(bump.outputs[0], bsdf.inputs["Normal"])
    N.link(albedo, bsdf.inputs["Base Color"])
    N.link(bsdf.outputs[0], N.out.inputs["Surface"])
    return mat, (lambda: albedo)


def bones():
    B = [("root", V((0, 0, 0)), V((0, 0, 0.2)), None, False),
         ("hips", J["pelvis"], J["mid"], "root", True),
         ("spine", J["mid"], J["chest"], "hips", True),
         ("chest", J["chest"], J["neck"], "spine", True),
         ("neck", J["neck"], J["skullB"], "chest", True),
         ("head", J["skullB"], J["nose"], "neck", True),
         ("earL", V((0.12, -0.4, 0.88)), V((0.24, -0.34, 1.14)), "head", True),
         ("earR", V((-0.12, -0.4, 0.88)), V((-0.24, -0.34, 1.14)), "head", True),
         ("eyeL", V((0.1, -0.6, 0.765)), V((0.1, -0.66, 0.765)), "head", True),
         ("eyeR", V((-0.1, -0.6, 0.765)), V((-0.1, -0.66, 0.765)), "head", True),
         ("tail1", J["tail0"], J["tail1"], "hips", True),
         ("tail2", J["tail1"], J["tail2"], "tail1", True),
         ("tail3", J["tail2"], J["tail3"], "tail2", True),
         ("bloom", J["tail3"], J["tail3"] + (J["tail3"] - J["tail2"]) * 0.8, "tail3", True)]
    for name, pts in LEGS.items():
        parent = "chest" if name[0] == "F" else "hips"
        B += [(f"leg{name}1", pts[0], pts[1], parent, True),
              (f"leg{name}2", pts[1], pts[2], f"leg{name}1", True),
              (f"leg{name}3", pts[2], pts[4], f"leg{name}2", True)]
    return B


def main():
    C.reset_scene()
    arm = C.build_armature("PebbloomRig", bones())
    body = build_body()
    C.smart_uv(body, margin=0.006)
    proc, albedo_get = fur_material()
    body.data.materials.append(proc)
    imgs = C.bake_channels(body, proc, {"albedo": (albedo_get, False), "normal": ("NORMAL", True)}, size=1024)
    body.data.materials.clear()
    body.data.materials.append(C.baked_material("PebbloomFur", imgs, roughness=0.75))
    C.colorize(body, lambda p: V((1, 1, 1)))
    print("PEBBLOOM unweighted body verts:", C.skin_to_armature(body, arm))

    plates, moss = build_plates()
    plates.data.materials.append(C.vcol_material("PebbloomStone", 0.85))
    moss.data.materials.append(C.vcol_material("PebbloomMoss", 0.9))
    C.transfer_weights(plates, body)
    C.transfer_weights(moss, body)
    ears = build_ears()
    for e in ears:
        e.data.materials.append(C.vcol_material("PebbloomPetal", 0.55))
    C.rigid_to_bone(ears[0], "earR")
    C.rigid_to_bone(ears[1], "earL")
    eyes, shines, nose = build_face()
    eye_mat = C.flat_material("PebbloomEye", (0.02, 0.015, 0.025), 0.05)
    shine_mat = C.flat_material("PebbloomShine", (1, 1, 1), 0.1, emission=(1, 1, 1), strength=1.5)
    iris_mat = C.vcol_material("PebbloomIris", 0.08)
    for i, e in enumerate(eyes):
        e.data.materials.append(eye_mat if i < 2 else iris_mat)
        C.rigid_to_bone(e, "eyeR" if e.data.vertices[0].co.x < 0 else "eyeL")
    for sh in shines:
        sh.data.materials.append(shine_mat)
        C.rigid_to_bone(sh, "eyeR" if sh.data.vertices[0].co.x < 0 else "eyeL")
    tufts = build_tufts()
    tufts.data.materials.append(C.vcol_material("PebbloomTuft", 0.85))
    C.transfer_weights(tufts, body)
    nose.data.materials.append(C.vcol_material("PebbloomNose", 0.3))
    C.rigid_to_bone(nose, "head")
    crystal = build_crystal()
    crystal.data.materials.append(C.flat_material("PebbloomCrystal", (1.0, 0.5, 0.1), 0.15, emission=(1.0, 0.45, 0.08), strength=1.6))
    C.rigid_to_bone(crystal, "head")
    bloom = build_bloom()
    bloom.data.materials.append(C.vcol_material("PebbloomPetal", 0.55))
    C.rigid_to_bone(bloom, "bloom")
    model = C.join([body, plates, moss, tufts, *ears, *eyes, *shines, nose, crystal, bloom], "Pebbloom")
    print("PEBBLOOM tris:", C.count_tris([model]))
    out = os.path.join(C.EXPORT_DIR, "pebbloom.glb")
    print("PEBBLOOM glb bytes:", C.export_glb(out, [model, arm]))
    if "render" in sys.argv:
        C.preview_render(os.path.join(C.RENDER_DIR, "pebbloom_blender_render.png"), (0, -0.15, 0.48), distance=3.0, height=0.25, yaw_deg=-40, sun_deg=(55, 0, -30))


main()
