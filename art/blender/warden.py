"""The Warden — Beastbound's player character (original design).

A grounded traveller: wide-brimmed woven hat, layered long coat with a short shoulder cape, rust
scarf, satchel on a cross-body strap, leather boots and gloves, and a gnarled staff topped with a
glowing leaf-crystal. Skin-modifier body, cloth layers skinned by weight transfer, procedural fabric
baked to textures, humanoid rig with named bones, exported as .glb.

Usage (from the repository root):
    blender -b -P art/blender/warden.py            # model + bake + export
    blender -b -P art/blender/warden.py -- render  # also writes a labelled Blender preview render
"""
import math
import os
import sys

import bmesh
import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(__file__))
import common as C  # noqa: E402

V = Vector
R = C.rng(4242)

# Blender space: Z up, the Warden faces -Y. Right side is -X.
J = {
    "pelvis": V((0, 0.0, 0.98)), "spine": V((0, 0.0, 1.17)), "chest": V((0, 0.0, 1.36)),
    "neck": V((0, 0.005, 1.53)), "head": V((0, -0.005, 1.66)), "crown": V((0, 0.0, 1.76)), "chin": V((0, -0.045, 1.6)),
}
SIDE = {}
for s, tag in ((1, "L"), (-1, "R")):
    SIDE[tag] = {
        "shoulder": V((0.19 * s, 0.0, 1.45)), "elbow": V((0.25 * s, 0.03, 1.18)), "wrist": V((0.28 * s, -0.01, 0.94)), "hand": V((0.29 * s, -0.03, 0.85)),
        "hip": V((0.095 * s, 0.0, 0.94)), "knee": V((0.1 * s, -0.025, 0.52)), "ankle": V((0.1 * s, 0.02, 0.1)), "toe": V((0.1 * s, -0.13, 0.04)),
    }

COL = {
    "skin": V((0.62, 0.42, 0.3)), "trousers": V((0.07, 0.065, 0.05)), "boots": V((0.06, 0.035, 0.02)), "gloves": V((0.12, 0.07, 0.035)),
    "coat": V((0.1, 0.13, 0.11)), "coatDark": V((0.05, 0.065, 0.06)), "cape": V((0.13, 0.11, 0.08)), "scarf": V((0.42, 0.11, 0.04)),
    "leather": V((0.16, 0.08, 0.035)), "hat": V((0.36, 0.27, 0.15)), "hatBand": V((0.12, 0.05, 0.03)), "hair": V((0.04, 0.025, 0.015)),
    "wood": V((0.2, 0.12, 0.06)), "metal": V((0.5, 0.42, 0.25)),
}


def colorize(obj, fn):
    me = obj.data
    attr = me.color_attributes.get("Col") or me.color_attributes.new("Col", "FLOAT_COLOR", "POINT")
    for v in me.vertices:
        attr.data[v.index].color = (*fn(v.co), 1.0)


def ring_tube(name, rings, sides=20):
    """rings: list of (center Vector, rx, ry) stacked top to bottom -> open tube (coat, cape, sleeves)."""
    bm = bmesh.new()
    loops = []
    for c, rx, ry in rings:
        loops.append([bm.verts.new(c + V((math.cos(a) * rx, math.sin(a) * ry, 0))) for a in (i / sides * math.tau for i in range(sides))])
    for a, b in zip(loops, loops[1:]):
        for i in range(sides):
            bm.faces.new((a[i], a[(i + 1) % sides], b[(i + 1) % sides], b[i]))
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = C.link(bpy.data.objects.new(name, me))
    sol = obj.modifiers.new("Thick", "SOLIDIFY")
    sol.thickness = 0.012
    sol.offset = 1.0
    sub = obj.modifiers.new("Sub", "SUBSURF")
    sub.levels = 1
    C.apply_all_modifiers(obj)
    C.shade_smooth(obj)
    return obj


# ---------------------------------------------------------------------------- parts

def build_body():
    joints, edges = [], []

    def add(p, r):
        joints.append((p, (r, r)))
        return len(joints) - 1

    sp = [add(J["pelvis"], 0.13), add(J["spine"], 0.12), add(J["chest"], 0.145), add(J["neck"], 0.055),
          add(J["chin"], 0.075), add(J["head"], 0.1), add(J["crown"], 0.085)]
    edges += [(sp[0], sp[1]), (sp[1], sp[2]), (sp[2], sp[3]), (sp[3], sp[4]), (sp[4], sp[5]), (sp[5], sp[6])]
    for tag, S in SIDE.items():
        arm = [add(S["shoulder"], 0.055), add(S["elbow"], 0.042), add(S["wrist"], 0.032), add(S["hand"], 0.038)]
        leg = [add(S["hip"], 0.085), add(S["knee"], 0.06), add(S["ankle"], 0.045), add(S["toe"], 0.048)]
        edges += [(sp[2], arm[0])] + list(zip(arm, arm[1:])) + [(sp[0], leg[0])] + list(zip(leg, leg[1:]))
    body = C.skin_mesh("WardenBody", joints, edges, subdiv=2)
    for v in body.data.vertices:  # nose and brow on the face
        p = v.co
        if p.z > 1.6 and p.y < -0.07 and abs(p.x) < 0.03 and p.z < 1.69:
            p.y -= 0.018 * (1 - abs(p.x) / 0.03)

    def col(p):
        if p.z > 1.56:
            return COL["skin"]
        if p.z < 0.2:
            return COL["boots"]
        if abs(p.x) > 0.2 and p.z < 1.0:
            return COL["gloves"]
        if p.z < 0.95:
            return COL["trousers"]
        return COL["coat"]
    colorize(body, col)
    return body


def build_coat():
    rings = []
    for i, (z, rx, ry) in enumerate([(1.47, 0.12, 0.1), (1.42, 0.2, 0.14), (1.3, 0.19, 0.135), (1.12, 0.165, 0.125), (1.0, 0.175, 0.135),
                                     (0.88, 0.2, 0.155), (0.74, 0.235, 0.18), (0.6, 0.265, 0.205)]):
        rings.append((V((0, 0.005, z)), rx, ry))
    coat = ring_tube("WardenCoat", rings, sides=24)
    # Front opening below the belt: pull the front centre of the hem slightly apart and back.
    for v in coat.data.vertices:
        if v.co.z < 0.92 and v.co.y < -0.08 and abs(v.co.x) < 0.07:
            v.co.x += math.copysign(0.035, v.co.x if abs(v.co.x) > 1e-4 else 1) * (0.92 - v.co.z) / 0.3
    colorize(coat, lambda p: COL["coatDark"] if p.z < 0.66 or p.z > 1.44 else COL["coat"])
    return coat


def build_cape():
    rings = [(V((0, 0.01, 1.56)), 0.085, 0.075), (V((0, 0.01, 1.5)), 0.2, 0.15), (V((0, 0.015, 1.42)), 0.29, 0.18), (V((0, 0.02, 1.3)), 0.32, 0.2), (V((0, 0.03, 1.2)), 0.33, 0.21)]
    cape = ring_tube("WardenCape", rings, sides=28)
    for v in cape.data.vertices:  # ragged, uneven hem
        if v.co.z < 1.25:
            a = math.atan2(v.co.y, v.co.x)
            v.co.z += 0.03 * math.sin(a * 7) + 0.015 * math.sin(a * 13)
    colorize(cape, lambda p: COL["cape"] * (0.8 if p.z < 1.24 else 1.0))
    return cape


def build_sleeves():
    out = []
    for tag, S in SIDE.items():
        pts = [S["shoulder"] + V((0, 0, 0.02)), S["shoulder"].lerp(S["elbow"], 0.5), S["elbow"], S["elbow"].lerp(S["wrist"], 0.6), S["wrist"] + (S["wrist"] - S["elbow"]).normalized() * 0.02]
        out.append(C.tube_from_points(f"sleeve{tag}", pts, [0.068, 0.058, 0.052, 0.046, 0.05], sides=10, cap=False))
    for o in out:
        colorize(o, lambda p: COL["coat"])
    return out


def build_boots():
    out = []
    for tag, S in SIDE.items():
        pts = [S["ankle"] + V((0, 0, 0.2)), S["ankle"] + V((0, 0, 0.06)), S["ankle"] + V((0, -0.04, -0.03)), S["toe"] + V((0, -0.03, -0.0))]
        b = C.tube_from_points(f"boot{tag}", pts, [0.065, 0.062, 0.06, 0.05], sides=12)
        for v in b.data.vertices:
            v.co.z = max(v.co.z, 0.005)
        colorize(b, lambda p: COL["boots"] * (1.4 if p.z > 0.27 else 1.0))
        out.append(b)
    return out


def build_hat():
    bm = bmesh.new()
    segs = 40
    # Brim: concentric rings, drooping slightly toward the edge.
    rings = []
    for k, r in enumerate([0.112, 0.16, 0.21, 0.255, 0.29]):
        droop = -0.012 * k * k * 0.6
        rings.append([bm.verts.new(V((math.cos(a) * r, math.sin(a) * r * 0.96, droop))) for a in (i / segs * math.tau for i in range(segs))])
    for a, b in zip(rings, rings[1:]):
        for i in range(segs):
            bm.faces.new((a[i], a[(i + 1) % segs], b[(i + 1) % segs], b[i]))
    # Crown: tapered dome.
    prev = rings[0]
    for z, r in [(0.045, 0.11), (0.085, 0.104), (0.115, 0.088), (0.13, 0.055)]:
        ring = [bm.verts.new(V((math.cos(a) * r, math.sin(a) * r * 0.96, z))) for a in (i / segs * math.tau for i in range(segs))]
        for i in range(segs):
            bm.faces.new((prev[i], ring[i], ring[(i + 1) % segs], prev[(i + 1) % segs]))
        prev = ring
    top = bm.verts.new(V((0, 0, 0.138)))
    for i in range(segs):
        bm.faces.new((prev[i], top, prev[(i + 1) % segs]))
    me = bpy.data.meshes.new("WardenHat")
    bm.to_mesh(me)
    bm.free()
    hat = C.link(bpy.data.objects.new("WardenHat", me))
    sol = hat.modifiers.new("Thick", "SOLIDIFY")
    sol.thickness = 0.012
    C.apply_all_modifiers(hat)
    C.shade_smooth(hat)
    for v in hat.data.vertices:
        v.co = V((v.co.x, v.co.y, v.co.z)) + V((0, 0.005, 1.735))
    # Woven straw pattern + dark band, in vertex colour.
    def col(p):
        q = p - V((0, 0.005, 1.735))
        r = math.hypot(q.x, q.y)
        a = math.atan2(q.y, q.x)
        weave = 0.82 + 0.18 * (math.sin(r * 160) * math.sin(a * 48 + r * 30) > 0)
        if 0.03 < q.z < 0.065 and r > 0.09:
            return COL["hatBand"]
        return COL["hat"] * weave * (0.85 + 0.15 * math.sin(a * 5 + 1.3))
    colorize(hat, col)
    return hat


def build_hair():
    parts = []
    for i in range(26):
        a = R.uniform(0.6, math.pi - 0.6)  # back half of the head
        root = J["head"] + V((math.cos(a) * 0.095, math.sin(a) * 0.09, 0.03))
        d = V((math.cos(a) * 0.4, math.sin(a) * 0.6 + 0.3, -1.0))
        pts = C.bezier_points(root, root + d * 0.03, root + d * 0.07, root + d * 0.11 + V((0, 0.02, 0)), 4)
        parts.append(C.tube_from_points(f"hair{i}", pts, [0.02, 0.016, 0.01, 0.002], sides=4))
    cap = C.uv_sphere("hairCap", J["head"] + V((0, 0.012, 0.012)), (0.104, 0.104, 0.1), 20, 12)
    for v in cap.data.vertices:  # keep the face clear: pull front vertices back behind the hairline
        if v.co.y < J["head"].y - 0.03 and v.co.z < J["head"].z + 0.06:
            v.co.y = J["head"].y + 0.02
            v.co.z = max(v.co.z, J["head"].z + 0.05)
    parts.append(cap)
    hair = C.join(parts, "WardenHair")
    colorize(hair, lambda p: COL["hair"])
    return hair


def build_face():
    eyes = [C.uv_sphere(f"eye{s}", (0.035 * s, -0.088, 1.675), (0.012, 0.006, 0.009), 8, 6) for s in (-1, 1)]
    brows = [C.tube_from_points(f"brow{s}", [V((0.015 * s, -0.093, 1.695)), V((0.05 * s, -0.087, 1.698))], [0.006, 0.005], sides=4) for s in (-1, 1)]
    for e in eyes:
        colorize(e, lambda p: V((0.02, 0.015, 0.012)))
    for b in brows:
        colorize(b, lambda p: COL["hair"])
    return C.join(eyes + brows, "WardenFace")


def build_scarf():
    bm_parts = []
    neck = C.tube_from_points("scarfRing", [J["neck"] + V((0, 0, -0.02)), J["neck"] + V((0, 0, 0.05))], [0.095, 0.085], sides=16, cap=False)
    sol = neck.modifiers.new("Thick", "SOLIDIFY"); sol.thickness = 0.03
    C.apply_all_modifiers(neck)
    bm_parts.append(neck)
    tail = C.tube_from_points("scarfTail", C.bezier_points(V((0.05, 0.08, 1.53)), V((0.08, 0.14, 1.45)), V((0.07, 0.16, 1.32)), V((0.09, 0.15, 1.18)), 6), [0.045, 0.042, 0.04, 0.038, 0.036, 0.03], sides=6)
    for v in tail.data.vertices:  # flatten into a cloth strip
        v.co.y = 0.13 + (v.co.y - 0.13) * 0.35 if v.co.z < 1.5 else v.co.y
    bm_parts.append(tail)
    scarf = C.join(bm_parts, "WardenScarf")
    colorize(scarf, lambda p: COL["scarf"] * (0.75 if math.sin(p.z * 90) > 0.6 else 1.0))
    return scarf


def build_gear():
    parts = []
    # Belt.
    belt = C.tube_from_points("belt", [V((0, 0.005, 0.99)), V((0, 0.005, 1.035))], [0.185, 0.185], sides=24, cap=False)
    for v in belt.data.vertices:
        v.co.y = 0.005 + (v.co.y - 0.005) * 0.75
    parts.append(belt)
    # Cross-body strap from the right shoulder to the left hip.
    strap = C.tube_from_points("strap", C.bezier_points(V((-0.15, -0.1, 1.44)), V((-0.05, -0.17, 1.3)), V((0.12, -0.16, 1.1)), V((0.2, -0.05, 0.98)), 7), [0.018] * 7, sides=4, cap=False)
    parts.append(strap)
    strap_back = C.tube_from_points("strapBack", C.bezier_points(V((-0.15, 0.08, 1.45)), V((-0.05, 0.16, 1.3)), V((0.12, 0.16, 1.1)), V((0.2, 0.05, 0.98)), 7), [0.018] * 7, sides=4, cap=False)
    parts.append(strap_back)
    # Satchel on the left hip.
    bpy.ops.mesh.primitive_cube_add(size=1, location=(0.24, 0.0, 0.9))
    bag = bpy.context.active_object
    bag.scale = (0.08, 0.17, 0.15)
    bev = bag.modifiers.new("Bevel", "BEVEL"); bev.width = 0.02; bev.segments = 3
    C.apply_all_modifiers(bag)
    bpy.ops.object.transform_apply(scale=True)
    C.select_only(bag)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    C.shade_smooth(bag)
    parts.append(bag)
    flap = C.tube_from_points("flap", [V((0.285, -0.08, 0.98)), V((0.29, 0.0, 0.99)), V((0.285, 0.08, 0.98))], [0.03, 0.03, 0.03], sides=4)
    parts.append(flap)
    gear = C.join(parts, "WardenGear")
    colorize(gear, lambda p: COL["leather"] * (1.25 if p.x > 0.2 else 1.0))
    return gear


def build_staff():
    hand = SIDE["R"]["hand"]
    base = V((hand.x, hand.y - 0.02, 0.04))
    pts = [base + V((0.004 * math.sin(i * 1.7), 0.004 * math.cos(i * 2.3), i * 0.12)) for i in range(15)]
    radii = [0.022 + 0.004 * math.sin(i * 2.1) for i in range(15)]
    radii[-1] = 0.026
    staff = C.tube_from_points("staff", pts, radii, sides=8)
    top = pts[-1]
    # Curling head: a few gnarled branches cradling the crystal.
    parts = [staff]
    for k in range(3):
        a = k / 3 * math.tau
        d = V((math.cos(a), math.sin(a), 0))
        parts.append(C.tube_from_points(f"curl{k}", C.bezier_points(top, top + d * 0.05 + V((0, 0, 0.04)), top + d * 0.07 + V((0, 0, 0.13)), top + d * 0.02 + V((0, 0, 0.19)), 6), [0.016, 0.013, 0.011, 0.009, 0.006, 0.003], sides=5))
    wood = C.join(parts, "WardenStaff")
    colorize(wood, lambda p: COL["wood"] * (0.75 + 0.25 * math.sin(p.z * 37)))
    crystal = C.tube_from_points("crystal", [top + V((0, 0, 0.05)), top + V((0, 0, 0.1)), top + V((0, 0, 0.16)), top + V((0, 0, 0.21))], [0.012, 0.03, 0.026, 0.002], sides=6)
    colorize(crystal, lambda p: V((1, 1, 1)))
    leaves = []
    for k in range(4):
        a = k / 4 * math.tau + 0.4
        d = V((math.cos(a), math.sin(a), 0.4)).normalized()
        root = top + V((0, 0, 0.03))
        leaves.append(C.tube_from_points(f"leaf{k}", [root, root + d * 0.06, root + d * 0.11], [0.006, 0.022, 0.002], sides=4))
    leaf = C.join(leaves, "WardenLeaves")
    colorize(leaf, lambda p: V((0.08, 0.2, 0.05)))
    return wood, crystal, leaf


# ---------------------------------------------------------------------------- materials

def cloth_material(name):
    """Vertex-colour base with procedural weave, stitching and wear; baked to albedo + normal."""
    mat = bpy.data.materials.new(name)
    N = C.Nodes(mat)
    co = N.coords("Object")
    vc = N.add("ShaderNodeVertexColor")
    vc.layer_name = "Col"
    sep = N.add("ShaderNodeSeparateXYZ")
    N.link(co, sep.inputs[0])
    # Fine weave: two crossing sine gratings in object space.
    wx = N.add("ShaderNodeTexWave", wave_type="BANDS", bands_direction="X")
    wx.inputs["Scale"].default_value = 320
    wx.inputs["Distortion"].default_value = 0.6
    N.link(co, wx.inputs["Vector"])
    wz = N.add("ShaderNodeTexWave", wave_type="BANDS", bands_direction="Z")
    wz.inputs["Scale"].default_value = 320
    wz.inputs["Distortion"].default_value = 0.6
    N.link(co, wz.inputs["Vector"])
    weave = N.math("MULTIPLY", N.math("ADD", wx.outputs["Fac"], wz.outputs["Fac"]), 0.5)
    grime = N.noise(6.0, 6, 0.6, co)
    low = N.math("SUBTRACT", 1.0, N.math("MULTIPLY", sep.outputs["Z"], 1.2), clamp=True)  # dust toward the hem
    wear = N.math("MULTIPLY", grime.outputs["Fac"], N.math("ADD", 0.4, low))
    shade = N.ramp(N.math("ADD", N.math("MULTIPLY", weave, 0.15), N.math("MULTIPLY", wear, 0.85)), [(0.2, (0.72, 0.72, 0.7)), (0.8, (1.12, 1.08, 1.03))])
    albedo = N.add("ShaderNodeMix", data_type="RGBA", blend_type="MULTIPLY")
    albedo.inputs[0].default_value = 1.0
    N.link(vc.outputs["Color"], albedo.inputs[6])
    N.link(shade, albedo.inputs[7])
    albedo = albedo.outputs[2]
    dust = N.math("MULTIPLY", N.math("MULTIPLY", low, low), 0.35)
    albedo = N.mix(dust, albedo, (0.25, 0.2, 0.14))
    bsdf = N.add("ShaderNodeBsdfPrincipled", (500, 0))
    bump = N.add("ShaderNodeBump")
    bump.inputs["Strength"].default_value = 0.35
    bump.inputs["Distance"].default_value = 0.002
    N.link(N.math("ADD", weave, N.math("MULTIPLY", grime.outputs["Fac"], 0.5)), bump.inputs["Height"])
    N.link(bump.outputs[0], bsdf.inputs["Normal"])
    N.link(albedo, bsdf.inputs["Base Color"])
    N.link(bsdf.outputs[0], N.out.inputs["Surface"])
    return mat, (lambda: albedo)


def vcol_material(name, roughness, metallic=0.0):
    mat = bpy.data.materials.new(name)
    N = C.Nodes(mat)
    bsdf = N.add("ShaderNodeBsdfPrincipled", (500, 0))
    a = N.add("ShaderNodeVertexColor")
    a.layer_name = "Col"
    N.link(a.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = roughness
    bsdf.inputs["Metallic"].default_value = metallic
    N.link(bsdf.outputs[0], N.out.inputs["Surface"])
    return mat


# ---------------------------------------------------------------------------- rig

def strip_groups(obj, prefixes):
    """Removes weights of the given bone groups and renormalises the rest."""
    for g in list(obj.vertex_groups):
        if g.name.startswith(prefixes):
            obj.vertex_groups.remove(g)
    C.select_only(obj)
    bpy.ops.object.vertex_group_normalize_all(lock_active=False)


def bones():
    B = [("root", V((0, 0, 0)), V((0, 0, 0.3)), None, False),
         ("hips", J["pelvis"], J["spine"], "root", True),
         ("torso", J["spine"], J["neck"], "hips", True),
         ("head", J["neck"], J["crown"] + V((0, 0, 0.06)), "torso", True)]
    for tag, S in SIDE.items():
        B += [(f"arm{tag}", S["shoulder"], S["elbow"], "torso", True),
              (f"forearm{tag}", S["elbow"], S["wrist"], f"arm{tag}", True),
              (f"hand{tag}", S["wrist"], S["hand"] + (S["hand"] - S["wrist"]) * 0.5, f"forearm{tag}", True),
              (f"leg{tag}", S["hip"], S["knee"], "hips", True),
              (f"shin{tag}", S["knee"], S["ankle"], f"leg{tag}", True),
              (f"foot{tag}", S["ankle"], S["toe"], f"shin{tag}", True)]
    return B


def main():
    C.reset_scene()
    arm = C.build_armature("WardenRig", bones())
    body = build_body()
    unweighted = C.skin_to_armature(body, arm)
    print("WARDEN unweighted body verts:", unweighted)
    sleeves, boots = build_sleeves(), build_boots()
    coat, cape, scarf, gear = build_coat(), build_cape(), build_scarf(), build_gear()
    layers = [coat, cape, *sleeves, *boots, scarf]
    for o in layers:
        C.transfer_weights(o, body)
    for o in (coat, cape, scarf):  # drape from the trunk; arms must not drag the coat
        strip_groups(o, ("arm", "forearm", "hand"))
    C.rigid_to_bone(gear, "hips")
    layers.append(gear)
    cloth = C.join([body, *layers], "WardenCloth")
    C.smart_uv(cloth, margin=0.004)
    proc, albedo_get = cloth_material("WardenClothProc")
    cloth.data.materials.append(proc)
    imgs = C.bake_channels(cloth, proc, {"albedo": (albedo_get, False), "normal": ("NORMAL", True)}, size=1024)
    cloth.data.materials.clear()
    cloth.data.materials.append(C.baked_material("WardenCloth", imgs, roughness=0.85))
    colorize(cloth, lambda p: V((1, 1, 1)))  # colour now lives in the baked texture

    hat = build_hat()
    hat.data.materials.append(vcol_material("WardenStraw", 0.8))
    hair = build_hair()
    hair.data.materials.append(vcol_material("WardenHair", 0.6))
    face = build_face()
    face.data.materials.append(vcol_material("WardenFace", 0.3))
    wood, crystal, leaf = build_staff()
    wood.data.materials.append(vcol_material("WardenWood", 0.75))
    crystal.data.materials.append(C.flat_material("WardenCrystal", (0.4, 1.0, 0.85), 0.15, emission=(0.35, 1.0, 0.85), strength=5.0))
    leaf.data.materials.append(vcol_material("WardenLeaf", 0.6))
    for o, b in ((hat, "head"), (hair, "head"), (face, "head"), (wood, "handR"), (crystal, "handR"), (leaf, "handR")):
        C.rigid_to_bone(o, b)
    model = C.join([cloth, hat, hair, face, wood, crystal, leaf], "Warden")
    print("WARDEN tris:", C.count_tris([model]))
    out = os.path.join(C.EXPORT_DIR, "warden.glb")
    print("WARDEN glb bytes:", C.export_glb(out, [model, arm]))
    if "render" in sys.argv:
        C.preview_render(os.path.join(C.RENDER_DIR, "warden_blender_render.png"), (0, 0, 1.0), distance=4.4, height=0.35, yaw_deg=-30, sun_deg=(60, 0, -35))
        C.preview_render(os.path.join(C.RENDER_DIR, "warden_blender_render_back.png"), (0, 0, 1.0), distance=4.4, height=0.5, yaw_deg=150, sun_deg=(60, 0, 160))

main()
