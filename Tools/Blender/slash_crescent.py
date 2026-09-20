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

# Which end the slash sweeps from in the game. False starts at the START end
# (the one START_RADIUS, TAPER_START and START_TIP describe); True starts at
# the END end instead. Only the UVs change - the shape stays exactly the same.
REVERSE_SWEEP = True

# Keeps only one side of the belly, cut straight across at its thickest point,
# which gives a shark-fin shape that suits a downward chop. None keeps the
# whole crescent; "START" keeps the START end's side, "END" the END end's side.
# The kept half sits exactly where it would in the whole crescent, so shape the
# full crescent first and then pick a half. The belly is WIDTH_AT along the
# cut, or the middle in SYMMETRIC mode.
HALF = None

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
            math.pow(1.0 - belly_distance(s), TAPER_START), START_TIP)

    # Each side of the belly tapers on its own and keeps its own tip. Letting
    # one tip's width blend along the whole cut made START_TIP leak into the
    # tail and fattened it. WIDTH_AT 0 means the band never widens: it starts
    # at its thickest and only tapers, which is the brush-stroke shape.
    if s < WIDTH_AT:
        return taper(side_profile(s / WIDTH_AT, TAPER_START), START_TIP)

    return taper(
        side_profile((1.0 - s) / max(1.0 - WIDTH_AT, 1e-4), TAPER_END),
        END_TIP)


def clamp01(value):
    return max(0.0, min(1.0, value))


# How widely the belly is rounded off, as the exponent of the correction term
# in side_profile. build() picks it: the highest value (tightest rounding,
# slimmest tails) that still keeps the inner edge bending one way only.
belly_rounding = 6.0

BELLY_ROUNDING_CANDIDATES = (8.0, 6.0, 4.0, 3.0, 2.0, 1.5, 1.0)


def side_profile(t, power):
    """0 at the tip, 1 at the belly, arriving at the belly with no slope."""
    t = clamp01(t)

    # t^power alone is the taper, but it reaches the belly still climbing, so
    # the two sides met at a point and left a kink in the edge there. The
    # second term cancels that slope right at the belly. A high exponent keeps
    # it close to the belly so the tips thin exactly as t^power; a low one
    # spreads the rounding wider, which a band this thick needs - rounded too
    # tightly, the inner edge dips toward the centre and bulges inwards there.
    return (math.pow(t, power)
            + power * (1.0 - t) * math.pow(t, belly_rounding))


def taper(fraction, tip):
    """Blends a 0 (tip) .. 1 (belly) profile with the width the tip keeps."""
    # A tip above 1 is allowed and makes that end wider than the belly, like a
    # brush pressed down at the start of a stroke.
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


def kept_range():
    """The part of the cut, 0..1, that ends up in the mesh."""
    belly = 0.5 if SYMMETRIC else clamp01(WIDTH_AT)

    if HALF == "START":
        return 0.0, belly

    if HALF == "END":
        return belly, 1.0

    return 0.0, 1.0


def edge_points(inner, whole=False):
    """The inner or outer edge of the band, one point per segment."""
    span = math.radians(SPAN)
    start = math.radians(CENTER_ANGLE) - span * 0.5
    first, last = (0.0, 1.0) if whole else kept_range()
    points = []

    # All SEGMENTS are spent on the kept part, so a half is as smooth as the
    # whole crescent rather than getting only half the segments.
    for i in range(SEGMENTS + 1):
        s = first + (last - first) * i / SEGMENTS
        angle = start + span * s
        half = width_at(s) * 0.5
        radius = radius_at(s) + (-half if inner else half)
        points.append((radius * math.cos(angle), radius * math.sin(angle)))

    return points


# Share of the inner edge's average bend every step must keep. Only forbidding
# a backward bend still let the belly go nearly straight, which reads as a flat
# spot just as much as a bulge does.
MIN_BEND_SHARE = 0.3


def inner_edge_flattens():
    """True when the inner edge goes flat or bends back anywhere."""
    # Judged on the whole crescent even when only a half is built, so a half
    # comes out exactly as that part of the whole would.
    points = edge_points(inner=True, whole=True)
    turns = []

    for i in range(1, len(points) - 1):
        ax = points[i][0] - points[i - 1][0]
        ay = points[i][1] - points[i - 1][1]
        bx = points[i + 1][0] - points[i][0]
        by = points[i + 1][1] - points[i][1]

        # The sweep runs counter-clockwise, so every step should turn left:
        # a positive angle.
        turns.append(math.atan2(ax * by - ay * bx, ax * bx + ay * by))

    average = sum(turns) / len(turns)
    return min(turns) < average * MIN_BEND_SHARE


def choose_belly_rounding():
    global belly_rounding

    for candidate in BELLY_ROUNDING_CANDIDATES:
        belly_rounding = candidate

        if not inner_edge_flattens():
            return True

    return False


def build():
    # Running from edit mode would fail on the selection calls below, and
    # leaving the user's mode changed is worse than switching back.
    if bpy.context.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')

    first, last = kept_range()

    if last - first < 1e-3:
        print("ERROR: HALF =", repr(HALF), "keeps nothing - the belly sits at "
              "that end (WIDTH_AT is", WIDTH_AT, "). Move WIDTH_AT or pick "
              "the other half.")
        return None

    if not choose_belly_rounding():
        print("WARNING: the band is too wide for its radius - the inner edge "
              "still bulges inwards at the belly. Lower WIDTH or raise "
              "START_RADIUS / END_RADIUS.")

    inner = edge_points(inner=True)
    outer = edge_points(inner=False)
    verts = []
    faces = []

    for i in range(SEGMENTS + 1):
        verts.append((inner[i][0], inner[i][1], 0.0))
        verts.append((outer[i][0], outer[i][1], 0.0))

        if i < SEGMENTS:
            base = i * 2
            faces.append((base, base + 2, base + 3, base + 1))

    mesh = bpy.data.meshes.new(NAME)
    mesh.from_pydata(verts, [], faces)
    uv_layer = mesh.uv_layers.new(name="UVMap")

    # Inner vertices are even, outer odd, so V falls out of the index parity and
    # U out of which rung of the ladder the vertex belongs to.
    for poly in mesh.polygons:
        for loop_index in poly.loop_indices:
            index = mesh.loops[loop_index].vertex_index
            u = (index // 2) / SEGMENTS

            # The shader sweeps from U 0 to U 1, so flipping U is all it
            # takes to start the cut from the other end.
            if REVERSE_SWEEP:
                u = 1.0 - u

            uv_layer.data[loop_index].uv = (u, float(index % 2))

    for existing in list(bpy.data.objects):
        if existing.name == NAME or existing.name.startswith(NAME + "."):
            bpy.data.objects.remove(existing, do_unlink=True)

    obj = bpy.data.objects.new(NAME, mesh)
    bpy.context.scene.collection.objects.link(obj)

    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    print(NAME, "rebuilt:", len(mesh.vertices), "verts,", len(mesh.polygons),
          "faces, belly rounding", belly_rounding)
    return obj


build()
