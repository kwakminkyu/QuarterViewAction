# Generates a flat shockwave ring for impacts: a band lying on the floor with
# spikes growing off it, inwards, outwards or both. Every spike is joined to the
# band, so the whole thing is one flat sheet. The spreading is done in Unity by
# the effect's Scale Curve, so this only has to supply the shape at full size.
#
# The mesh is UV-mapped for the Slash shader: U runs 0..1 around the ring, V
# runs with the radius, 0 at the innermost point to 1 at the outermost, so the
# band has the same brightness all the way round and the shader's outer-edge
# bias makes the outside of the wave the brightest part.
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

NAME = "Ground_Burst"

# Outer radius of the band, and its width measured inwards from there.
RADIUS = 1.0
BAND = 0.08

# Which way the spikes grow off the band: "IN" towards the centre, "OUT" away
# from it, or "BOTH". With BOTH the outer set sits halfway between the inner
# spikes so the two sides interlock.
DIRECTION = "IN"

# Total number of spikes on one side, and how many make up one cluster. With
# CLUSTER_SIZE equal to SPIKES there is a single cluster, which with the
# spacing below set to 360 / SPIKES spreads them evenly round the ring.
SPIKES = 16
CLUSTER_SIZE = 16

# Angle between neighbouring spikes inside a cluster, and each spike's width
# where it meets the band, both in degrees. A width above the spacing merges
# neighbouring spikes; below it leaves plain band showing between them.
SPIKE_SPACING = 22.5
SPIKE_WIDTH = 14.0

# Length of a cluster's longest spike, measured from the band. LENGTH_FALLOFF
# shortens spikes towards a cluster's edges (0 all equal, 1 the outermost ones
# almost vanish). LENGTH_JITTER varies each spike on top of that (0 none,
# 1 anywhere between nothing and double).
LENGTH = 0.35
LENGTH_FALLOFF = 0.0
LENGTH_JITTER = 0.35

# Curve of each spike's sides. 1 is straight-sided; higher values scoop the
# sides into thin tips with a wide, concave root where they meet the band.
SHARPNESS = 2.0

# How far each spike's tip is swept round the ring, in degrees, so the spikes
# lean like claws instead of pointing straight at (or away from) the centre.
# 0 keeps them straight; the sign picks the direction.
SKEW = 8.0

# How far each cluster may drift from even spacing round the ring, as a
# fraction of the gap between clusters (0 perfectly even).
CLUSTER_JITTER = 0.0

# Changes the random layout. Keep a value you like; the same seed always gives
# the same ring.
SEED = 7

# Which way round the ring a sweep travels, if the effect entry uses one.
REVERSE_SWEEP = False

# Columns around the ring. Around 360 keeps even narrow tips sharp.
SEGMENTS = 360

# ---- Build --------------------------------------------------------------


def wrap_angle(angle):
    """Signed difference folded into -pi..pi."""
    return math.atan2(math.sin(angle), math.cos(angle))


def cluster_sizes():
    """Spikes per cluster, shared out as evenly as the counts allow."""
    total = max(SPIKES, 1)
    per = max(CLUSTER_SIZE, 1)
    count = math.ceil(total / per)
    base, extra = divmod(total, count)
    return [base + (1 if i < extra else 0) for i in range(count)]


def make_spikes(rng, phase):
    """Each spike as (centre angle, length, half width)."""
    sizes = cluster_sizes()
    gap = 2.0 * math.pi / len(sizes)
    spacing = math.radians(SPIKE_SPACING)
    half_width = math.radians(SPIKE_WIDTH) * 0.5
    spikes = []

    for index, size in enumerate(sizes):
        centre = phase + (index + rng.uniform(-CLUSTER_JITTER, CLUSTER_JITTER)) * gap
        middle = (size - 1) * 0.5

        for j in range(size):
            # 0 at the middle spike, 1 at the outermost, so a cluster peaks in
            # its centre and steps down either side.
            edge = abs(j - middle) / middle if middle > 0 else 0.0
            length = LENGTH * (1.0 - LENGTH_FALLOFF * edge)
            length *= max(0.05, 1.0 + LENGTH_JITTER * rng.uniform(-1.0, 1.0))
            spikes.append((centre + (j - middle) * spacing, length, half_width))

    return spikes


def spike_length(angle, spikes):
    """How far the spikes reach past the band at one angle round the ring."""
    reach = 0.0

    for centre, length, half_width in spikes:
        distance = abs(wrap_angle(angle - centre)) / max(half_width, 1e-4)

        if distance < 1.0:
            # The longest spike covering this angle wins, so overlapping
            # spikes meet in a valley rather than stacking up.
            reach = max(reach, length * math.pow(1.0 - distance, SHARPNESS))

    return reach


def build():
    if bpy.context.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')

    rng = random.Random(SEED)
    inward = DIRECTION in ("IN", "BOTH")
    outward = DIRECTION in ("OUT", "BOTH")
    inner_spikes = make_spikes(rng, 0.0) if inward else []
    # The outer set is offset by half a spike so the two sides interlock.
    outer_spikes = make_spikes(
        rng, math.radians(SPIKE_SPACING) * 0.5) if outward else []

    band_outer = RADIUS
    band_inner = max(RADIUS - BAND, 0.0)
    skew = math.radians(SKEW)

    # Each column runs straight out from the centre: the inner spike tip (if
    # any), the band's two edges, then the outer spike tip (if any). The tips
    # are swept round by their spike's reach, which leans the spikes.
    columns = []

    for j in range(SEGMENTS + 1):
        u = j / SEGMENTS
        angle = u * 2.0 * math.pi
        points = []

        if inward:
            reach = spike_length(angle, inner_spikes)
            points.append((max(band_inner - reach, 0.0),
                           angle + skew * reach / max(LENGTH, 1e-4)))

        points.append((band_inner, angle))
        points.append((band_outer, angle))

        if outward:
            reach = spike_length(angle, outer_spikes)
            points.append((band_outer + reach,
                           angle + skew * reach / max(LENGTH, 1e-4)))

        columns.append((u, points))

    radii = [r for _, points in columns for r, _ in points]
    r_min = min(radii)
    r_span = max(max(radii) - r_min, 1e-4)
    rows = len(columns[0][1])

    verts = []
    uvs = []

    # One extra column closes the ring. It sits on top of the first but carries
    # U = 1, so the unwrap runs cleanly round with no seam through it.
    for u, points in columns:
        swept = 1.0 - u if REVERSE_SWEEP else u

        for radius, angle in points:
            verts.append((radius * math.cos(angle), radius * math.sin(angle), 0.0))
            uvs.append((swept, (radius - r_min) / r_span))

    faces = []

    for j in range(SEGMENTS):
        for k in range(rows - 1):
            a = j * rows + k
            b = (j + 1) * rows + k
            faces.append((a, b, b + 1, a + 1))

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
    print(NAME, "rebuilt:", len(mesh.vertices), "verts,", len(mesh.polygons),
          "faces,", DIRECTION)
    return obj


build()
