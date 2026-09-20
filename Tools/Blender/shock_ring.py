# Generates a shockwave ring: a thin flat band that spreads out across the
# floor from the point of impact. The spreading itself is done in Unity by the
# effect's Scale Curve, so this only has to supply the band at its final size.
#
# The mesh is a UV-mapped strip for the Slash shader: U runs 0..1 around the
# ring, V runs 0 at the inner edge to 1 at the outer edge, so the shader's
# outer-edge bias makes the leading edge of the wave the brightest part and
# lets the inside trail off.
#
# Run it from Blender's Scripting tab (Text > Open, then Run Script), change
# the numbers below and run again. Each run replaces the previous result.
#
# The ring lies flat on Blender's XY plane, centred on the object origin. In
# Unity give the effect entry a Swing Plane Normal of (0, 1, 0) to lay it on
# the floor, Scale Curve Axes of (1, 1, 0) so the curve only widens it, and
# tick Stay In World so it spreads from where it landed.

import bpy
import math
import random

# ---- Shape --------------------------------------------------------------

NAME = "Shock_Ring"

# Radius of the outer edge at full size, before any scaling in Unity.
RADIUS = 1.0

# Width of the band, measured inwards from the outer edge.
WIDTH = 0.18

# Breaks up the perfect circle so the wave reads as a pressure front rather
# than a drawn line. WOBBLE is how far the edge wanders in and out, in metres
# (0 keeps a clean circle); WOBBLE_WAVES is how many bumps go round the ring.
WOBBLE = 0.0
WOBBLE_WAVES = 7

# Changes the wobble's layout. Keep a value you like; the same seed always
# gives the same ring.
SEED = 7

# Which way round the ring a sweep travels, if the effect entry uses one.
REVERSE_SWEEP = False

# Columns around the ring. 128 keeps it round at any size it is scaled to.
SEGMENTS = 128

# ---- Build --------------------------------------------------------------


def make_wobble():
    """A few sine waves of random phase and strength, summed round the ring."""
    rng = random.Random(SEED)
    waves = []

    for i in range(3):
        count = max(1, WOBBLE_WAVES + i * 3)
        waves.append((count, rng.uniform(0.0, 2.0 * math.pi), rng.uniform(0.4, 1.0)))

    total = sum(strength for _, _, strength in waves)
    return waves, total


def offset_at(angle, waves, total):
    if WOBBLE <= 0.0:
        return 0.0

    value = sum(strength * math.sin(count * angle + phase)
                for count, phase, strength in waves)
    return WOBBLE * value / total


def build():
    if bpy.context.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')

    waves, total = make_wobble()
    verts = []
    faces = []
    uvs = []

    # One extra column closes the ring. It sits on top of the first but carries
    # U = 1, so the unwrap runs cleanly round with no seam through it.
    for j in range(SEGMENTS + 1):
        u = j / SEGMENTS
        angle = u * 2.0 * math.pi
        outer = RADIUS + offset_at(angle, waves, total)
        inner = max(outer - WIDTH, 0.0)
        cos_a = math.cos(angle)
        sin_a = math.sin(angle)
        swept = 1.0 - u if REVERSE_SWEEP else u

        verts.append((inner * cos_a, inner * sin_a, 0.0))
        uvs.append((swept, 0.0))
        verts.append((outer * cos_a, outer * sin_a, 0.0))
        uvs.append((swept, 1.0))

    for j in range(SEGMENTS):
        base = j * 2
        faces.append((base, base + 2, base + 3, base + 1))

    mesh = bpy.data.meshes.new(NAME)
    mesh.from_pydata(verts, [], faces)
    uv_layer = mesh.uv_layers.new(name="UVMap")

    for poly in mesh.polygons:
        for loop_index in poly.loop_indices:
            uv_layer.data[loop_index].uv = uvs[mesh.loops[loop_index].vertex_index]

    for existing in list(bpy.data.objects):
        if existing.name == NAME or existing.name.startswith(NAME + "."):
            bpy.data.objects.remove(existing, do_unlink=True)

    obj = bpy.data.objects.new(NAME, mesh)
    bpy.context.scene.collection.objects.link(obj)

    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    print(NAME, "rebuilt:", len(mesh.vertices), "verts,", len(mesh.polygons), "faces")
    return obj


build()
