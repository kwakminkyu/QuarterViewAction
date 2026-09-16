# Generates a crescent slash band as a UV-mapped strip, ready for the Slash
# shader: U runs 0..1 along the cut, V runs 0 (inner) to 1 (outer), the mesh is
# flat in XY and centred on the object origin.
#
# Run it from Blender's Scripting tab (Text > Open, then Run Script), change
# the numbers below and run again. Each run replaces the previous result.
#
# The centre line is a logarithmic spiral, which is what gives the hooked look:
# the radius shrinks along the cut so the tail curls inwards instead of closing
# into a circle.

import bpy
import math

# ---- Shape --------------------------------------------------------------

NAME = "Slash_Crescent"

# True mirrors the shape about the middle of the cut: both tails curl by the
# same amount and taper the same way, so the crescent is left-right symmetric.
# START_RADIUS then means the radius at the belly and END_RADIUS the radius at
# both tips, while WIDTH_AT and TAPER_END are ignored.
SYMMETRIC = True

# Radius of the centre line where the cut starts and where it ends. Equal
# values give a plain circular arc; a smaller END_RADIUS curls the tail in.
START_RADIUS = 1.60
END_RADIUS = 1.00

# How much of a turn the cut covers, in degrees.
SPAN = 160.0

# Where the arc sits. 0 puts the middle of the cut along +X; positive values
# rotate it counter-clockwise.
CENTER_ANGLE = 0.0

# Thickest point of the band, and where along the cut that falls (0 start,
# 1 end). 0 starts at the thickest point and only tapers from there, which is
# the brush-stroke shape; anything higher widens into a belly first. How the
# ends finish is set by the taper and tip values below.
WIDTH = 0.55
WIDTH_AT = 0.38

# 1 tapers straight to the tips. Higher values thin out sooner, leaving a long
# fine tip; lower values stay thick further along and end more bluntly.
TAPER_START = 1.5
TAPER_END = 0.9

# How wide each tip still is, as a fraction of WIDTH. 0 comes to a point, which
# is the crescent look; raising one cuts that end off square, the way a brush
# stroke starts thick and thins out as it leaves the surface. In SYMMETRIC mode
# only START_TIP is used, for both ends.
START_TIP = 0.0
END_TIP = 0.0

# Segments along the cut. 48 is plenty; more only matters for a very long span.
SEGMENTS = 48

# ---- Build --------------------------------------------------------------


def belly_distance(s):
    """0 at the middle of the cut, 1 at either tip, smooth all the way."""
    d = 2.0 * s - 1.0
    return d * d


def width_at(s):
    """Band width at 0..1 along the cut, WIDTH at the belly."""
    if SYMMETRIC:
        # Squared distance from the belly rather than the plain distance: both
        # halves still thin out the same way, but the curve runs flat through
        # the middle instead of meeting at a corner there.
        return taper(
            math.pow(1.0 - belly_distance(s), TAPER_START),
            START_TIP,
            START_TIP,
            s)

    # One curve for the whole band rather than two halves stitched together at
    # WIDTH_AT: stitching left a kink in the middle of the belly, because the
    # two pieces met at the same width but at different slopes. Multiplying the
    # two power curves peaks at WIDTH_AT just the same and stays smooth.
    # WIDTH_AT 0 means the band never widens: it starts at its thickest and
    # only tapers, which is the brush-stroke shape.
    rise = 1.0 if WIDTH_AT <= 0.0 else math.pow(
        clamp01(s / WIDTH_AT), TAPER_START)
    fall = math.pow(clamp01((1.0 - s) / max(1.0 - WIDTH_AT, 1e-4)), TAPER_END)
    return taper(rise * fall, START_TIP, END_TIP, s)


def clamp01(value):
    return max(0.0, min(1.0, value))


def taper(fraction, start_tip, end_tip, s):
    """Blends the belly profile with the width each tip keeps."""
    # fraction is 0 at the tips and 1 at the belly, so the tip widths lead at
    # the ends and WIDTH leads in the middle. A tip above 1 is allowed and
    # makes that end wider than the belly, like a brush pressed down at the
    # start of a stroke.
    tip = start_tip + (end_tip - start_tip) * clamp01(s)
    return WIDTH * (tip + (1.0 - tip) * fraction)


def radius_at(s):
    """Radius of the centre line at 0..1 along the cut."""
    if SYMMETRIC:
        # Belly radius in the middle falling to the tip radius at both ends,
        # which curls the two tails inwards by the same amount.
        return START_RADIUS * math.pow(
            END_RADIUS / START_RADIUS, belly_distance(s))

    # Logarithmic spiral: the radius moves geometrically from start to end,
    # which keeps the curvature smooth the whole way along.
    return START_RADIUS * math.pow(END_RADIUS / START_RADIUS, s)


def build():
    # Running from edit mode would fail on the selection calls below, and
    # leaving the user's mode changed is worse than switching back.
    if bpy.context.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')

    span = math.radians(SPAN)
    start = math.radians(CENTER_ANGLE) - span * 0.5

    verts = []
    faces = []
    uvs = []

    for i in range(SEGMENTS + 1):
        s = i / SEGMENTS
        angle = start + span * s

        radius = radius_at(s)
        half = width_at(s) * 0.5

        cos_a = math.cos(angle)
        sin_a = math.sin(angle)
        verts.append(((radius - half) * cos_a, (radius - half) * sin_a, 0.0))
        verts.append(((radius + half) * cos_a, (radius + half) * sin_a, 0.0))

        if i < SEGMENTS:
            base = i * 2
            faces.append((base, base + 2, base + 3, base + 1))
            uvs.append((s, (i + 1) / SEGMENTS))

    mesh = bpy.data.meshes.new(NAME)
    mesh.from_pydata(verts, [], faces)
    uv_layer = mesh.uv_layers.new(name="UVMap")

    # Inner vertices are even, outer odd, so V falls out of the index parity and
    # U out of which rung of the ladder the vertex belongs to.
    for poly in mesh.polygons:
        for loop_index in poly.loop_indices:
            index = mesh.loops[loop_index].vertex_index
            uv_layer.data[loop_index].uv = (
                (index // 2) / SEGMENTS,
                float(index % 2),
            )

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
