"""Shared helpers for Beastbound's scripted Blender assets (Blender 4.0+, runs headless).

Every asset script builds an original model from scratch, bakes its procedural materials to
textures, rigs it with named bones and exports a .glb for the web build (and Unity via glTFast).

Run from the repository root, for example:
    blender -b -P art/blender/thalorix.py
"""
import math
import os
import random

import bmesh
import bpy
from mathutils import Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
EXPORT_DIR = os.path.join(ROOT, "web", "assets", "models")
RENDER_DIR = os.path.join(ROOT, "art", "renders")
TEX_DIR = os.path.join(ROOT, "art", "textures")


# ---------------------------------------------------------------------------- scene

def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.unit_settings.system = "METRIC"
    return scene


def link(obj):
    bpy.context.scene.collection.objects.link(obj)
    return obj


def select_only(*objs):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]


def apply_all_modifiers(obj):
    select_only(obj)
    for m in list(obj.modifiers):
        bpy.ops.object.modifier_apply(modifier=m.name)


def shade_smooth(obj, angle=None):
    for p in obj.data.polygons:
        p.use_smooth = True


# ---------------------------------------------------------------------------- geometry builders

def skin_mesh(name, joints, edges, subdiv=2):
    """Builds a smooth organic body from a joint graph using the Skin modifier.

    joints: list of (position Vector, (radius_x, radius_y)); edges: list of (i, j).
    Returns the applied mesh object.
    """
    me = bpy.data.meshes.new(name)
    me.from_pydata([j[0] for j in joints], edges, [])
    obj = link(bpy.data.objects.new(name, me))
    skin = obj.modifiers.new("Skin", "SKIN")
    skin.use_smooth_shade = True
    skin.branch_smoothing = 0.6
    for v, (_, r) in zip(me.skin_vertices[0].data, joints):
        v.radius = r
    me.skin_vertices[0].data[0].use_root = True
    sub = obj.modifiers.new("Sub", "SUBSURF")
    sub.levels = subdiv
    sub.render_levels = subdiv
    apply_all_modifiers(obj)
    shade_smooth(obj)
    return obj


def tube_from_points(name, points, radii, sides=6, cap=True):
    """A tapered tube (horn, fur clump, claw) along a polyline. Returns a mesh object."""
    bm = bmesh.new()
    rings = []
    n = len(points)
    for i, (p, r) in enumerate(zip(points, radii)):
        a = points[min(i + 1, n - 1)] - points[max(i - 1, 0)]
        t = a.normalized() if a.length > 1e-6 else Vector((0, 0, 1))
        up = Vector((0, 0, 1)) if abs(t.z) < 0.95 else Vector((1, 0, 0))
        u = t.cross(up).normalized()
        v = t.cross(u).normalized()
        ring = []
        for s in range(sides):
            ang = s / sides * math.tau
            ring.append(bm.verts.new(p + (u * math.cos(ang) + v * math.sin(ang)) * r))
        rings.append(ring)
    for a, b in zip(rings, rings[1:]):
        for s in range(sides):
            bm.faces.new((a[s], a[(s + 1) % sides], b[(s + 1) % sides], b[s]))
    if cap:
        bm.faces.new(list(reversed(rings[0])))
        tip = bm.verts.new(points[-1] + (points[-1] - points[-2]).normalized() * radii[-1] * 0.5)
        last = rings[-1]
        for s in range(sides):
            bm.faces.new((last[s], last[(s + 1) % sides], tip))
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = link(bpy.data.objects.new(name, me))
    shade_smooth(obj)
    return obj


def bezier_points(p0, p1, p2, p3, count):
    out = []
    for i in range(count):
        t = i / (count - 1)
        it = 1 - t
        out.append(p0 * it ** 3 + p1 * 3 * it * it * t + p2 * 3 * it * t * t + p3 * t ** 3)
    return out


def uv_sphere(name, loc, scale, segments=16, rings=10):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=segments, v_segments=rings, radius=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * scale[0], v.co.y * scale[1], v.co.z * scale[2])) + Vector(loc)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = link(bpy.data.objects.new(name, me))
    shade_smooth(obj)
    return obj


def join(objs, name):
    select_only(*objs)
    bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active
    o.name = name
    o.data.name = name
    return o


def smart_uv(obj, margin=0.01, angle=66):
    select_only(obj)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(angle), island_margin=margin)
    bpy.ops.object.mode_set(mode="OBJECT")


# ---------------------------------------------------------------------------- materials + baking

class Nodes:
    """Tiny helper to write shader node graphs compactly."""

    def __init__(self, mat):
        mat.use_nodes = True
        self.mat = mat
        self.nt = mat.node_tree
        self.nt.nodes.clear()
        self.out = self.add("ShaderNodeOutputMaterial", (900, 0))

    def add(self, kind, loc=(0, 0), **props):
        n = self.nt.nodes.new(kind)
        n.location = loc
        for k, v in props.items():
            setattr(n, k, v)
        return n

    def link(self, a, b):
        self.nt.links.new(a, b)

    def val(self, v):
        n = self.add("ShaderNodeValue")
        n.outputs[0].default_value = v
        return n.outputs[0]

    def math(self, op, a, b=None, clamp=False):
        n = self.add("ShaderNodeMath", operation=op, use_clamp=clamp)
        for i, x in enumerate((a, b)):
            if x is None:
                continue
            if isinstance(x, (int, float)):
                n.inputs[i].default_value = x
            else:
                self.link(x, n.inputs[i])
        return n.outputs[0]

    def mix(self, fac, a, b):
        n = self.add("ShaderNodeMix", data_type="RGBA")
        for sock, x in ((n.inputs[0], fac), (n.inputs[6], a), (n.inputs[7], b)):
            if isinstance(x, (int, float)):
                sock.default_value = x
            elif isinstance(x, tuple):
                sock.default_value = x if len(x) == 4 else (*x, 1)
            else:
                self.link(x, sock)
        return n.outputs[2]

    def ramp(self, src, stops):
        n = self.add("ShaderNodeValToRGB")
        cr = n.color_ramp
        while len(cr.elements) < len(stops):
            cr.elements.new(0.5)
        for el, (pos, col) in zip(cr.elements, stops):
            el.position = pos
            el.color = col if len(col) == 4 else (*col, 1)
        self.link(src, n.inputs[0])
        return n.outputs[0]

    def noise(self, scale, detail=6, rough=0.55, vector=None, dim="3D"):
        n = self.add("ShaderNodeTexNoise", noise_dimensions=dim)
        n.inputs["Scale"].default_value = scale
        n.inputs["Detail"].default_value = detail
        n.inputs["Roughness"].default_value = rough
        if vector is not None:
            self.link(vector, n.inputs["Vector"])
        return n

    def voronoi(self, scale, feature="F1", vector=None, randomness=1.0):
        n = self.add("ShaderNodeTexVoronoi", feature=feature)
        n.inputs["Scale"].default_value = scale
        n.inputs["Randomness"].default_value = randomness
        if vector is not None:
            self.link(vector, n.inputs["Vector"])
        return n

    def coords(self, kind="Object"):
        return self.add("ShaderNodeTexCoord").outputs[kind]

    def attribute(self, name):
        return self.add("ShaderNodeAttribute", attribute_name=name)


def new_image(name, size, non_color=False):
    img = bpy.data.images.new(name, size, size, alpha=False, float_buffer=False)
    if non_color:
        img.colorspace_settings.name = "Non-Color"
    return img


def bake_channels(obj, mat, channels, size=1024, margin=8):
    """Bakes procedural channels of `mat` on `obj` to images.

    channels: dict name -> (socket_getter, non_color) where socket_getter(nodes) returns the colour
    socket to bake (routed through an Emission shader), or the special value 'NORMAL'.
    Returns dict name -> image.
    """
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 1
    scene.cycles.use_denoising = False
    scene.render.bake.margin = margin
    scene.render.bake.use_selected_to_active = False
    nt = mat.node_tree
    out = next(n for n in nt.nodes if n.type == "OUTPUT_MATERIAL")
    original = out.inputs["Surface"].links[0].from_socket if out.inputs["Surface"].links else None
    images = {}
    select_only(obj)
    for name, (getter, non_color) in channels.items():
        img = new_image(f"{obj.name}_{name}", size, non_color)
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = img
        nt.nodes.active = tex
        if getter == "NORMAL":
            if original is not None:
                nt.links.new(original, out.inputs["Surface"])
            bpy.ops.object.bake(type="NORMAL", normal_space="TANGENT")
        else:
            emit = nt.nodes.new("ShaderNodeEmission")
            nt.links.new(getter(), emit.inputs["Color"])
            nt.links.new(emit.outputs[0], out.inputs["Surface"])
            bpy.ops.object.bake(type="EMIT")
            nt.nodes.remove(emit)
        nt.nodes.remove(tex)
        images[name] = img
    if original is not None:
        nt.links.new(original, out.inputs["Surface"])
    os.makedirs(TEX_DIR, exist_ok=True)
    for name, img in images.items():
        img.filepath_raw = os.path.join(TEX_DIR, f"{img.name}.png")
        img.file_format = "PNG"
        img.save()
    return images


def baked_material(name, images, roughness=0.6, emissive_strength=0.0, alpha_clip=None):
    """Final export material: Principled BSDF fed by baked textures (glTF friendly)."""
    mat = bpy.data.materials.new(name)
    N = Nodes(mat)
    bsdf = N.add("ShaderNodeBsdfPrincipled", (500, 0))
    N.link(bsdf.outputs[0], N.out.inputs["Surface"])
    y = 300
    if "albedo" in images:
        t = N.add("ShaderNodeTexImage", (0, y), image=images["albedo"])
        N.link(t.outputs["Color"], bsdf.inputs["Base Color"])
        if alpha_clip is not None:
            N.link(t.outputs["Alpha"], bsdf.inputs["Alpha"])
            mat.blend_method = "CLIP"
            mat.alpha_threshold = alpha_clip
        y -= 300
    if "rough" in images:
        t = N.add("ShaderNodeTexImage", (0, y), image=images["rough"])
        N.link(t.outputs["Color"], bsdf.inputs["Roughness"])
        y -= 300
    else:
        bsdf.inputs["Roughness"].default_value = roughness
    if "normal" in images:
        t = N.add("ShaderNodeTexImage", (-300, y), image=images["normal"])
        nm = N.add("ShaderNodeNormalMap", (100, y))
        N.link(t.outputs["Color"], nm.inputs["Color"])
        N.link(nm.outputs[0], bsdf.inputs["Normal"])
        y -= 300
    if "emit" in images and emissive_strength > 0:
        t = N.add("ShaderNodeTexImage", (0, y), image=images["emit"])
        N.link(t.outputs["Color"], bsdf.inputs["Emission Color"])
        bsdf.inputs["Emission Strength"].default_value = emissive_strength
    bsdf.inputs["Specular IOR Level"].default_value = 0.35
    return mat


def flat_material(name, color, roughness=0.5, emission=None, strength=0.0, metallic=0.0):
    mat = bpy.data.materials.new(name)
    N = Nodes(mat)
    bsdf = N.add("ShaderNodeBsdfPrincipled", (500, 0))
    bsdf.inputs["Base Color"].default_value = (*color, 1)
    bsdf.inputs["Roughness"].default_value = roughness
    bsdf.inputs["Metallic"].default_value = metallic
    if emission is not None:
        bsdf.inputs["Emission Color"].default_value = (*emission, 1)
        bsdf.inputs["Emission Strength"].default_value = strength
    N.link(bsdf.outputs[0], N.out.inputs["Surface"])
    return mat


# ---------------------------------------------------------------------------- rigging

def build_armature(name, bones):
    """bones: list of (name, head, tail, parent_name or None, deform). Returns armature object."""
    arm = bpy.data.armatures.new(name)
    obj = link(bpy.data.objects.new(name, arm))
    select_only(obj)
    bpy.ops.object.mode_set(mode="EDIT")
    for bname, head, tail, parent, deform in bones:
        b = arm.edit_bones.new(bname)
        b.head = head
        b.tail = tail
        b.use_deform = deform
        b.roll = 0
        if parent:
            b.parent = arm.edit_bones[parent]
            b.use_connect = False
    bpy.ops.object.mode_set(mode="OBJECT")
    return obj


def skin_to_armature(mesh, arm):
    """Automatic (heat) weights; falls back to envelope weights if heat weighting fails."""
    select_only(mesh, arm)
    bpy.context.view_layer.objects.active = arm
    try:
        bpy.ops.object.parent_set(type="ARMATURE_AUTO")
    except RuntimeError:
        bpy.ops.object.parent_set(type="ARMATURE_ENVELOPE")
    unweighted = sum(1 for v in mesh.data.vertices if not v.groups or max(g.weight for g in v.groups) < 1e-3)
    return unweighted


def rigid_to_bone(obj, bone_name):
    """Binds every vertex of obj to one bone (horns, eyes, claws)."""
    vg = obj.vertex_groups.new(name=bone_name)
    vg.add([v.index for v in obj.data.vertices], 1.0, "REPLACE")


def transfer_weights(target, source):
    """Copies skin weights from the nearest point on `source` (the body) to `target` (mane, fur)."""
    for g in source.vertex_groups:
        if g.name not in target.vertex_groups:
            target.vertex_groups.new(name=g.name)
    dt = target.modifiers.new("WeightXfer", "DATA_TRANSFER")
    dt.object = source
    dt.use_vert_data = True
    dt.data_types_verts = {"VGROUP_WEIGHTS"}
    dt.vert_mapping = "POLYINTERP_NEAREST"
    dt.layers_vgroup_select_src = "ALL"
    dt.layers_vgroup_select_dst = "NAME"
    select_only(target)
    bpy.ops.object.modifier_apply(modifier=dt.name)


# ---------------------------------------------------------------------------- export + preview

def export_glb(path, objects):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    select_only(*objects)
    bpy.ops.export_scene.gltf(
        filepath=path,
        export_format="GLB",
        use_selection=True,
        export_yup=True,
        export_apply=False,
        export_skins=True,
        export_animations=False,
        export_image_format="JPEG",
        export_jpeg_quality=88,
        export_tangents=False,
        export_materials="EXPORT",
    )
    return os.path.getsize(path)


def count_tris(objs):
    total = 0
    for o in objs:
        if o.type == "MESH":
            total += sum(len(p.vertices) - 2 for p in o.data.polygons)
    return total


def preview_render(path, target, distance=4.0, height=1.2, yaw_deg=35, size=(1280, 800), samples=96, sun_deg=(52, 0, 140)):
    """Offline Cycles preview of the asset (labelled 'Blender render' wherever it is shown)."""
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = samples
    scene.cycles.use_denoising = False
    scene.render.resolution_x, scene.render.resolution_y = size
    scene.view_settings.view_transform = "AgX"
    scene.view_settings.look = "AgX - Medium High Contrast"
    world = bpy.data.worlds.new("PreviewWorld")
    scene.world = world
    W = Nodes(world)
    W.nt.nodes.remove(W.out)
    out = W.add("ShaderNodeOutputWorld", (600, 0))
    bg = W.add("ShaderNodeBackground", (300, 0))
    # Moody dusk-forest backdrop: cool teal ambient, warm low key light, blue rim light.
    grad = W.add("ShaderNodeTexGradient", (-300, 0))
    W.link(W.coords("Generated"), grad.inputs[0])
    sepz = W.add("ShaderNodeSeparateXYZ", (-500, -200))
    W.link(W.coords("Generated"), sepz.inputs[0])
    W.link(W.ramp(sepz.outputs["Z"], [(0.45, (0.015, 0.02, 0.022)), (0.75, (0.05, 0.09, 0.12))]), bg.inputs[0])
    bg.inputs[1].default_value = 1.0
    W.link(bg.outputs[0], out.inputs[0])
    sun = link(bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN")))
    sun.data.energy = 4.0
    sun.data.color = (1.0, 0.86, 0.68)
    sun.data.angle = math.radians(3)
    sun.rotation_euler = [math.radians(a) for a in sun_deg]
    rim = link(bpy.data.objects.new("Rim", bpy.data.lights.new("Rim", "AREA")))
    rim.data.energy = 220
    rim.data.size = 2.5
    rim.data.color = (0.55, 0.75, 1.0)
    rim.location = (target[0] - 2.5, target[1] + 3.5, target[2] + 2.0)
    rim.rotation_euler = (math.radians(-60), 0, math.radians(200))
    bpy.ops.mesh.primitive_plane_add(size=40, location=(0, 0, 0))
    ground = bpy.context.active_object
    gm = bpy.data.materials.new("Ground")
    G = Nodes(gm)
    gb = G.add("ShaderNodeBsdfPrincipled", (500, 0))
    n = G.noise(3.0, 8)
    G.link(G.ramp(n.outputs["Fac"], [(0.3, (0.015, 0.022, 0.01)), (0.7, (0.045, 0.045, 0.025))]), gb.inputs["Base Color"])
    gb.inputs["Roughness"].default_value = 0.95
    G.link(gb.outputs[0], G.out.inputs["Surface"])
    ground.data.materials.append(gm)
    cam = link(bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")))
    cam.data.lens = 55
    yaw = math.radians(yaw_deg)
    cam.location = (target[0] + math.sin(yaw) * -distance, target[1] - math.cos(yaw) * distance, target[2] + height)
    direction = Vector(target) - cam.location
    cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam
    scene.render.filepath = path
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.render.render(write_still=True)
    for o in (sun, rim, ground, cam):
        bpy.data.objects.remove(o, do_unlink=True)


def rng(seed):
    return random.Random(seed)
