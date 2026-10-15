"""Thalorix — Forest Guardian (original Beastbound creature).

A maned wolf-lion with swept-back horns tipped in blue crystal, moss-green mane and glowing blue
vein markings. Built entirely from script: skin-modifier body, fur-clump mane, procedural fur and
vein materials baked to textures, named-bone rig with automatic weights, exported as .glb.

Usage (from the repository root):
    blender -b -P art/blender/thalorix.py            # model + bake + export
    blender -b -P art/blender/thalorix.py -- render  # also writes a labelled Blender preview render
"""
import math
import os
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(__file__))
import common as C  # noqa: E402

V = Vector
R = C.rng(1701)

# Blender space: Z up, the creature faces -Y (exports to +Z forward, Y up).
J = {
    "pelvis": V((0, 0.48, 0.98)), "mid": V((0, 0.05, 1.02)), "chest": V((0, -0.38, 1.04)),
    "neck": V((0, -0.66, 1.24)), "skullB": V((0, -0.84, 1.38)), "skull": V((0, -0.97, 1.38)),
    "muzzle": V((0, -1.14, 1.32)), "nose": V((0, -1.245, 1.3)),
    "tail0": V((0, 0.7, 1.0)), "tail1": V((0, 0.95, 0.86)), "tail2": V((0, 1.15, 0.66)), "tail3": V((0, 1.28, 0.44)),
}
LEGS = {
    # name: (shoulder/hip, elbow/knee, wrist/hock, paw, toe)
    "FL": [V((0.2, -0.4, 0.92)), V((0.21, -0.33, 0.55)), V((0.2, -0.38, 0.2)), V((0.2, -0.45, 0.065)), V((0.2, -0.54, 0.05))],
    "HL": [V((0.21, 0.45, 0.9)), V((0.22, 0.3, 0.55)), V((0.21, 0.55, 0.28)), V((0.21, 0.48, 0.065)), V((0.21, 0.39, 0.05))],
}
for k in list(LEGS):
    LEGS[k.replace("L", "R")] = [V((-p.x, p.y, p.z)) for p in LEGS[k]]


def build_body():
    joints, edges = [], []

    def add(p, rx, ry=None):
        joints.append((p, (rx, ry if ry is not None else rx)))
        return len(joints) - 1

    spine = [add(J["tail0"], 0.11), add(J["pelvis"], 0.27, 0.29), add(J["mid"], 0.29, 0.31), add(J["chest"], 0.34, 0.38),
             add(J["neck"], 0.25, 0.26), add(J["skullB"], 0.19, 0.19), add(J["skull"], 0.19, 0.17),
             add(J["muzzle"], 0.105, 0.095), add(J["nose"], 0.06, 0.05)]
    tail = [spine[0], add(J["tail1"], 0.085), add(J["tail2"], 0.07), add(J["tail3"], 0.06)]
    jaw = [spine[5], add(V((0, -0.98, 1.26)), 0.12, 0.1), add(V((0, -1.12, 1.235)), 0.07, 0.06)]
    edges += list(zip(jaw, jaw[1:]))
    edges += list(zip(spine, spine[1:])) + list(zip(tail, tail[1:]))
    radii = {"F": [0.17, 0.12, 0.085, 0.095, 0.078], "H": [0.2, 0.14, 0.085, 0.095, 0.078]}
    for name, pts in LEGS.items():
        r = radii[name[0]]
        idx = [add(p, rr) for p, rr in zip(pts, r)]
        edges.append((spine[3] if name[0] == "F" else spine[1], idx[0]))
        edges += list(zip(idx, idx[1:]))
    body = C.skin_mesh("ThalorixBody", joints, edges, subdiv=2)
    # Sculpt-style shaping: deepen the chest, slim the waist, square the muzzle.
    for v in body.data.vertices:
        p = v.co
        if -0.6 < p.y < -0.15 and 0.6 < p.z < 1.0 and abs(p.x) < 0.3:
            p.z -= 0.07 * math.sin((p.y + 0.6) / 0.45 * math.pi) * min(1, (1.0 - p.z) / 0.25)
        if 0.1 < p.y < 0.4 and 0.65 < p.z < 0.95 and abs(p.x) < 0.3:
            p.z += 0.05 * math.sin((p.y - 0.1) / 0.3 * math.pi)
        if p.y < -0.9 and p.z > 1.2:
            p.x *= 1.1
    return body


def fur_clump(name, root, direction, length, radius, droop=0.25, sides=5, segs=5):
    d = direction.normalized()
    side = d.cross(V((0, 0, 1)))
    side = side.normalized() if side.length > 1e-4 else V((1, 0, 0))
    bend = V((0, 0, -1)) * droop * length + side * R.uniform(-0.12, 0.12) * length
    p0, p3 = root, root + d * length + bend
    p1, p2 = root + d * length * 0.35, root + d * length * 0.75 + bend * 0.4
    pts = C.bezier_points(p0, p1, p2, p3, segs)
    radii = [radius * (1 - i / (segs - 1)) ** 0.8 + 0.002 for i in range(segs)]
    return C.tube_from_points(name, pts, radii, sides=sides)


def colorize(obj, fn):
    """Per-vertex colour (linear RGB) written to the 'Col' attribute exported as COLOR_0."""
    me = obj.data
    attr = me.color_attributes.get("Col") or me.color_attributes.new("Col", "FLOAT_COLOR", "POINT")
    for v in me.vertices:
        attr.data[v.index].color = (*fn(v.co), 1.0)


def build_mane():
    parts = []
    tone = []
    # Mane: dense locks rooted around the neck and shoulders, falling back and down.
    neck_axis = [J["chest"] + V((0, 0.05, 0.12)), J["neck"], J["skullB"]]
    for i in range(330):
        t = R.random() ** 0.85
        a = neck_axis[0].lerp(neck_axis[1], min(1, t * 2)) if t < 0.5 else neck_axis[1].lerp(neck_axis[2], (t - 0.5) * 2)
        ang = R.uniform(-2.05, 2.05)
        rad = (0.33 - 0.15 * t)
        root = a + V((math.sin(ang) * rad, 0, math.cos(ang) * rad * 0.95))
        out = V((math.sin(ang) * 0.55, 1.0, math.cos(ang) * 0.35 - 0.15)).normalized()
        L = R.uniform(0.24, 0.42) * (1.1 - 0.4 * t) * (0.75 if abs(ang) > 1.7 else 1.0)
        parts.append(fur_clump(f"mane{i}", root, out, L, R.uniform(0.022, 0.034), droop=R.uniform(0.35, 0.6), sides=4))
        tone.append(R.uniform(0.7, 1.25))
    # Chest ruff hanging down between the forelegs.
    for i in range(60):
        root = J["chest"].lerp(J["neck"], R.uniform(0.0, 0.85)) + V((R.uniform(-0.16, 0.16), -0.16, -0.2))
        out = V((R.uniform(-0.25, 0.25), -0.25, -1.0))
        parts.append(fur_clump(f"ruff{i}", root, out, R.uniform(0.16, 0.28), 0.026, droop=0.05, sides=4))
        tone.append(R.uniform(0.7, 1.1))
    # Cheek tufts framing the face.
    for s_ in (-1, 1):
        for i in range(16):
            root = J["skull"] + V((0.13 * s_, R.uniform(0.0, 0.1), R.uniform(-0.12, 0.02)))
            out = V((0.7 * s_, 0.8, R.uniform(-0.5, 0.0)))
            parts.append(fur_clump(f"cheek{s_}{i}", root, out, R.uniform(0.1, 0.17), 0.018, droop=0.15, sides=4))
            tone.append(R.uniform(0.8, 1.2))
    # Ridge of longer fur along the spine.
    for i in range(40):
        root = J["chest"].lerp(J["pelvis"], i / 39) + V((R.uniform(-0.04, 0.04), 0, 0.3 - 0.03 * (i / 39)))
        out = V((R.uniform(-0.25, 0.25), 1.0, 0.2))
        parts.append(fur_clump(f"ridge{i}", root, out, R.uniform(0.1, 0.17) * (1 - i / 60), 0.022, droop=0.3, sides=4))
        tone.append(R.uniform(0.8, 1.1))
    # Tail tuft (lion-like).
    for i in range(60):
        a = R.uniform(0, math.tau)
        root = J["tail2"].lerp(J["tail3"], R.uniform(0.2, 1.0)) + V((math.cos(a) * 0.045, 0, math.sin(a) * 0.045))
        out = V((math.cos(a) * 0.5, 0.5, -1.0 + math.sin(a) * 0.3))
        parts.append(fur_clump(f"tuft{i}", root, out, R.uniform(0.18, 0.32), 0.03, droop=0.15, sides=4))
        tone.append(R.uniform(0.8, 1.2))
    # Short locks over shoulders, back and haunches break up the smooth silhouette.
    for i in range(220):
        y = R.uniform(-0.45, 0.55)
        ang = R.uniform(-1.35, 1.35)
        centre = J["chest"].lerp(J["pelvis"], (y + 0.45) / 1.0)
        rad = 0.33 if y < 0 else 0.29
        root = centre + V((math.sin(ang) * rad * 0.95, 0, math.cos(ang) * rad * 0.92))
        out = V((math.sin(ang) * 0.35, 1.0, math.cos(ang) * 0.15 - 0.2)).normalized()
        parts.append(fur_clump(f"coat{i}", root, out, R.uniform(0.07, 0.13), 0.018, droop=0.6, sides=4, segs=4))
        tone.append(R.uniform(0.25, 0.45))
    # Per-lock colour variation is written before joining.
    root_c, tip_c = V((0.006, 0.008, 0.007)), V((0.045, 0.065, 0.035))
    for obj, k in zip(parts, tone):
        pts = [v.co for v in obj.data.vertices]
        base = pts[0]
        far = max((p - base).length for p in pts) or 1.0
        colorize(obj, lambda p, base=base, far=far, k=k: root_c.lerp(tip_c * k, ((p - base).length / far) ** 1.2))
    return C.join(parts, "ThalorixMane")


def build_horns():
    horns, tips = [], []
    for s in (-1, 1):
        p0 = V((0.075 * s, -0.94, 1.5))
        pts = C.bezier_points(p0, p0 + V((0.05 * s, 0.02, 0.2)), p0 + V((0.2 * s, 0.38, 0.22)), p0 + V((0.26 * s, 0.56, 0.02)), 9)
        radii = [0.055 * (1 - i / 9) ** 0.7 + 0.008 for i in range(9)]
        horn = C.tube_from_points(f"horn{s}", pts, radii, sides=8, cap=False)
        horns.append(horn)
        d = (pts[-1] - pts[-2]).normalized()
        tip = C.tube_from_points(f"tip{s}", [pts[-1] - d * 0.01, pts[-1] + d * 0.05, pts[-1] + d * 0.13], [radii[-1] * 1.05, radii[-1] * 0.8, 0.003], sides=6)
        tips.append(tip)
    horn = C.join(horns, "ThalorixHorns")
    base, end = V((0.02, 0.018, 0.016)), V((0.3, 0.28, 0.24))
    colorize(horn, lambda p: base.lerp(end, max(0.0, min(1.0, (p.z - 1.5) / 0.22 + abs(p.y + 0.94) * 1.1)) ** 0.8))
    tip = C.join(tips, "ThalorixCrystal")
    return horn, tip


def build_ears():
    ears = []
    for s in (-1, 1):
        base = V((0.17 * s, -0.8, 1.45))
        pts = [base, base + V((0.025 * s, 0.03, 0.1)), base + V((0.045 * s, 0.06, 0.2)), base + V((0.06 * s, 0.08, 0.27))]
        ear = C.tube_from_points(f"ear{s}", pts, [0.07, 0.055, 0.03, 0.006], sides=6)
        for v in ear.data.vertices:  # flatten front-to-back
            v.co.y = base.y + 0.03 + (v.co.y - base.y - 0.03) * 0.45
        dark, inner = V((0.03, 0.035, 0.04)), V((0.16, 0.1, 0.09))
        colorize(ear, lambda p, s=s: inner.lerp(dark, 0.6) if p.y < base.y + 0.02 else dark)
        ears.append(ear)
    return ears


def build_eyes():
    return [C.uv_sphere(f"eye{s}", (0.088 * s, -1.05, 1.43), (0.034, 0.022, 0.024), 12, 8) for s in (-1, 1)]


def build_claws():
    claws = []
    for name, pts in LEGS.items():
        toe = pts[4]
        for k in (-1, 0, 1):
            root = toe + V((k * 0.035, -0.02, 0.0))
            claws.append(C.tube_from_points(f"claw{name}{k}", [root, root + V((0, -0.05, -0.01)), root + V((0, -0.07, -0.04))], [0.014, 0.01, 0.002], sides=5))
    c = C.join(claws, "ThalorixClaws")
    colorize(c, lambda p: V((0.05, 0.045, 0.04)))
    return c


# ---------------------------------------------------------------------------- materials

def fur_material():
    mat = bpy.data.materials.new("ThalorixFurProc")
    N = C.Nodes(mat)
    co = N.coords("Object")
    nrm = N.coords("Normal")
    sep = N.add("ShaderNodeSeparateXYZ")
    N.link(co, sep.inputs[0])
    sepn = N.add("ShaderNodeSeparateXYZ")
    N.link(nrm, sepn.inputs[0])
    z = sep.outputs["Z"]
    # Leg mask (fur runs vertically below the body line).
    legs = N.math("SUBTRACT", 1.0, N.math("MULTIPLY", N.math("SUBTRACT", z, 0.6), 4.0, clamp=True), clamp=True)
    # Directional fur streaks: stretched noise along Y on the body, along Z on the legs.
    sb = N.add("ShaderNodeVectorMath", operation="MULTIPLY")
    N.link(co, sb.inputs[0]); sb.inputs[1].default_value = (70, 7, 70)
    sl = N.add("ShaderNodeVectorMath", operation="MULTIPLY")
    N.link(co, sl.inputs[0]); sl.inputs[1].default_value = (70, 70, 7)
    nb = N.noise(1.0, 8, 0.6, sb.outputs[0])
    nl = N.noise(1.0, 8, 0.6, sl.outputs[0])
    streak = N.add("ShaderNodeMix", data_type="FLOAT")
    N.link(legs, streak.inputs[0]); N.link(nb.outputs["Fac"], streak.inputs[2]); N.link(nl.outputs["Fac"], streak.inputs[3])
    streak = streak.outputs[0]
    # Colour zones: dark slate back, blue-grey flanks, ivory throat and belly, darker legs.
    back = (0.008, 0.011, 0.014)
    flank = (0.03, 0.04, 0.05)
    belly = (0.2, 0.18, 0.14)
    under = N.math("MULTIPLY", N.math("SUBTRACT", -0.4, sepn.outputs["Z"]), 2.5, clamp=True)
    up = N.math("MULTIPLY", N.math("SUBTRACT", sepn.outputs["Z"], 0.35), 2.0, clamp=True)
    patch = N.noise(2.2, 4, 0.5, co)
    base = N.mix(up, flank, back)
    base = N.mix(N.math("MULTIPLY", under, N.math("ADD", 0.6, patch.outputs["Fac"]), clamp=True), base, belly)
    base = N.mix(N.math("MULTIPLY", legs, 0.5), base, (0.012, 0.014, 0.016))
    # Muzzle lighter.
    muz = N.math("MULTIPLY", N.math("SUBTRACT", -1.08, sep.outputs["Y"]), 6.0, clamp=True)
    base = N.mix(N.math("MULTIPLY", muz, 0.5), base, (0.12, 0.115, 0.1))
    nose = N.math("MULTIPLY", N.math("SUBTRACT", -1.2, sep.outputs["Y"]), 12.0, clamp=True)
    base = N.mix(nose, base, (0.004, 0.004, 0.005))
    shade = N.ramp(streak, [(0.3, (0.55, 0.55, 0.55)), (0.7, (1.25, 1.25, 1.25))])
    albedo = N.add("ShaderNodeMix", data_type="RGBA", blend_type="MULTIPLY")
    albedo.inputs[0].default_value = 1.0
    N.link(base, albedo.inputs[6]); N.link(shade, albedo.inputs[7])
    albedo = albedo.outputs[2]
    # Glowing veins: thin Voronoi edges, warped, masked to legs, shoulders, flanks and face.
    warp = N.noise(1.6, 3, 0.5, co)
    wv = N.add("ShaderNodeVectorMath", operation="ADD")
    N.link(co, wv.inputs[0]); N.link(warp.outputs["Color"], wv.inputs[1])
    vor = N.voronoi(3.4, "DISTANCE_TO_EDGE", wv.outputs[0])
    line = N.math("SUBTRACT", 1.0, N.math("DIVIDE", vor.outputs["Distance"], 0.02), clamp=True)
    line = N.math("POWER", line, 1.6)
    region = N.noise(1.3, 2, 0.5, co)
    regmask = N.math("MULTIPLY", N.math("SUBTRACT", region.outputs["Fac"], 0.52), 5.0, clamp=True)
    regmask = N.math("MAXIMUM", regmask, N.math("MULTIPLY", legs, 0.55))
    regmask = N.math("MULTIPLY", regmask, N.math("SUBTRACT", 1.0, under), clamp=True)
    vein = N.math("MULTIPLY", line, regmask, clamp=True)
    glow_col = (0.18, 0.62, 1.0)
    emit = N.mix(vein, (0, 0, 0), glow_col)
    albedo = N.mix(N.math("MULTIPLY", vein, 0.7), albedo, (0.25, 0.6, 0.9))
    # Shading graph used for the normal bake (fur streak bump + vein grooves).
    bsdf = N.add("ShaderNodeBsdfPrincipled", (500, 0))
    bump = N.add("ShaderNodeBump")
    bump.inputs["Strength"].default_value = 0.8
    bump.inputs["Distance"].default_value = 0.004
    hgt = N.math("ADD", streak, N.math("MULTIPLY", vein, -0.3))
    N.link(hgt, bump.inputs["Height"])
    N.link(bump.outputs[0], bsdf.inputs["Normal"])
    N.link(albedo, bsdf.inputs["Base Color"])
    N.link(bsdf.outputs[0], N.out.inputs["Surface"])
    return mat, (lambda: albedo), (lambda: emit)


def vcol_material(name, roughness, sheen=0.0):
    mat = bpy.data.materials.new(name)
    N = C.Nodes(mat)
    bsdf = N.add("ShaderNodeBsdfPrincipled", (500, 0))
    a = N.add("ShaderNodeVertexColor")
    a.layer_name = "Col"
    N.link(a.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = roughness
    N.link(bsdf.outputs[0], N.out.inputs["Surface"])
    return mat


# ---------------------------------------------------------------------------- rig

def bones():
    B = [("root", V((0, 0, 0)), V((0, 0, 0.3)), None, False),
         ("hips", J["pelvis"], J["mid"], "root", True),
         ("spine", J["mid"], J["chest"], "hips", True),
         ("chest", J["chest"], J["neck"], "spine", True),
         ("neck", J["neck"], J["skullB"], "chest", True),
         ("head", J["skullB"], J["nose"], "neck", True),
         ("earL", V((0.17, -0.8, 1.45)), V((0.23, -0.72, 1.72)), "head", True),
         ("earR", V((-0.17, -0.8, 1.45)), V((-0.23, -0.72, 1.72)), "head", True),
         ("eyeL", V((0.088, -1.03, 1.43)), V((0.088, -1.09, 1.43)), "head", True),
         ("eyeR", V((-0.088, -1.03, 1.43)), V((-0.088, -1.09, 1.43)), "head", True),
         ("tail1", J["tail0"], J["tail1"], "hips", True),
         ("tail2", J["tail1"], J["tail2"], "tail1", True),
         ("tail3", J["tail2"], J["tail3"] + (J["tail3"] - J["tail2"]) * 0.6, "tail2", True)]
    for name, pts in LEGS.items():
        parent = "chest" if name[0] == "F" else "hips"
        B += [(f"leg{name}1", pts[0], pts[1], parent, True),
              (f"leg{name}2", pts[1], pts[2], f"leg{name}1", True),
              (f"leg{name}3", pts[2], pts[4], f"leg{name}2", True)]
    return B


def main():
    C.reset_scene()
    body = build_body()
    C.smart_uv(body, margin=0.006)
    proc, albedo_get, emit_get = fur_material()
    body.data.materials.append(proc)
    imgs = C.bake_channels(body, proc, {"albedo": (albedo_get, False), "emit": (emit_get, False), "normal": ("NORMAL", True)}, size=1024)
    body.data.materials.clear()
    body.data.materials.append(C.baked_material("ThalorixFur", imgs, roughness=0.72, emissive_strength=3.0))
    colorize(body, lambda p: V((1, 1, 1)))

    mane = build_mane()
    mane.data.materials.append(vcol_material("ThalorixMane", 0.8))
    horns, crystal = build_horns()
    horns.data.materials.append(vcol_material("ThalorixHorn", 0.42))
    crystal.data.materials.append(C.flat_material("ThalorixCrystal", (0.25, 0.7, 1.0), 0.15, emission=(0.3, 0.75, 1.0), strength=6.0))
    colorize(crystal, lambda p: V((1, 1, 1)))
    ears = build_ears()
    for e in ears:
        e.data.materials.append(vcol_material("ThalorixEar", 0.8))
    eyes = build_eyes()
    eye_mat = C.flat_material("ThalorixEye", (0.4, 0.85, 1.0), 0.1, emission=(0.35, 0.8, 1.0), strength=8.0)
    for e in eyes:
        e.data.materials.append(eye_mat)
        colorize(e, lambda p: V((1, 1, 1)))
    claws = build_claws()
    claws.data.materials.append(vcol_material("ThalorixClaw", 0.35))

    arm = C.build_armature("ThalorixRig", bones())
    unweighted = C.skin_to_armature(body, arm)
    print("THALORIX unweighted body verts:", unweighted)
    C.transfer_weights(mane, body)
    C.transfer_weights(claws, body)
    C.rigid_to_bone(horns, "head")
    C.rigid_to_bone(crystal, "head")
    C.rigid_to_bone(ears[0], "earR")   # -X is the creature's right side (it faces -Y)
    C.rigid_to_bone(ears[1], "earL")
    C.rigid_to_bone(eyes[0], "eyeR")
    C.rigid_to_bone(eyes[1], "eyeL")
    model = C.join([body, mane, horns, crystal, *ears, *eyes, claws], "Thalorix")
    tris = C.count_tris([model])
    print("THALORIX tris:", tris)

    out = os.path.join(C.EXPORT_DIR, "thalorix.glb")
    size = C.export_glb(out, [model, arm])
    print("THALORIX glb bytes:", size)
    if "render" in sys.argv:
        C.preview_render(os.path.join(C.RENDER_DIR, "thalorix_blender_render.png"), (0, -0.15, 0.85), distance=6.2, height=0.35, yaw_deg=-55)


main()
