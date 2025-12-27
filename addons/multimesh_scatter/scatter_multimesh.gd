@tool
class_name ScatterMultiMesh
extends MultiMeshInstance3D

## ScatterMultiMesh - A tool for procedural mesh scattering in Godot
## This node extends MultiMeshInstance3D to provide easy-to-use scattering functionality
## with various placement patterns and randomization options.

enum PlacementType { 
	BOX,      ## Scatter instances randomly within a box volume
	SPHERE,   ## Scatter instances randomly within a sphere volume
	GRID      ## Place instances in a regular grid pattern
}

enum ScatterMode { 
	PROCEDURAL,  ## Regenerate scatter on every change (editor only)
	BAKED        ## Keep current scatter positions (no auto-update)
}

## Number of instances to scatter
@export_range(0, 10000, 1) var count := 100

## Placement pattern type
@export_enum("Box", "Sphere", "Grid") var placement_type: int = PlacementType.BOX

## Size of the placement volume (for Box and Sphere) or grid bounds
@export var placement_size := Vector3(10, 10, 10)

## Physics collision mask for surface snapping
@export_flags_3d_physics var collision_mask := 0x1

@export_group("Offsets")
## Global position offset applied to all instances
@export var offset_position := Vector3.ZERO
## Global rotation offset applied to all instances (in radians)
@export var offset_rotation := Vector3.ZERO
## Base scale multiplier for all instances
@export var base_scale := Vector3.ONE

@export_group("Random Scale")
## Minimum random scale factor per axis
@export var min_random_size := Vector3(0.75, 0.75, 0.75)
## Maximum random scale factor per axis
@export var max_random_size := Vector3(1.25, 1.25, 1.25)

@export_group("Random Rotation (deg)")
## Maximum random rotation per axis in degrees
@export var random_rotation := Vector3.ZERO

@export_group("Grid")
## Number of rows in grid placement
@export_range(1, 1000, 1) var grid_rows := 1
## Number of columns in grid placement
@export_range(1, 1000, 1) var grid_columns := 1
## Spacing between grid cells
@export var grid_cell_size := Vector3(2, 2, 2)
## Additional Y rotation per column (useful for creating patterns)
@export var rotation_per_column: Array[float] = []

@export_group("Advanced")
## Random seed for reproducible results
@export_range(0, 10000, 1) var seed := 0
## Whether to snap instances to collision surfaces below
@export var snap_to_surface := true

@export_group("Debug")
## Show placement volume in editor
@export var show_debug_area := true

## Current scatter mode (Procedural or Baked)
@export var scatter_mode: ScatterMode = ScatterMode.PROCEDURAL

# Runtime variables
var _debug_draw_instance: MeshInstance3D
var _rng := RandomNumberGenerator.new()
var _space: PhysicsDirectSpaceState3D = null
var _suppress_scatter := false

func _init() -> void:
	if multimesh == null:
		multimesh = MultiMesh.new()
		multimesh.transform_format = MultiMesh.TRANSFORM_3D

func _ready() -> void:
	_rng.seed = seed
	if Engine.is_editor_hint() and show_debug_area:
		_create_debug_area()
	if not _suppress_scatter:
		call_deferred("_update")

func _notification(what: int) -> void:
	if what == NOTIFICATION_TRANSFORM_CHANGED:
		if scatter_mode == ScatterMode.PROCEDURAL:
			_update()

func _update() -> void:
	if _suppress_scatter: return
	if not is_inside_tree(): return
	if get_world_3d() == null: return
	if _space == null:
		_space = get_world_3d().direct_space_state
	if not _space: return
	scatter()
	if Engine.is_editor_hint():
		_update_debug_area_size()

## Main scatter function - regenerates all instance positions
func scatter() -> void:
	_rng.seed = seed
	multimesh.instance_count = 0

	match placement_type:
		PlacementType.SPHERE:
			multimesh.instance_count = count
			for i in count: _place_random_sphere(i)
		PlacementType.BOX:
			multimesh.instance_count = count
			for i in count: _place_random_box(i)
		PlacementType.GRID:
			multimesh.instance_count = grid_rows * grid_columns
			var idx := 0
			for r in range(grid_rows):
				for c in range(grid_columns):
					_place_grid(idx, r, c)
					idx += 1

func _place_random_sphere(i: int) -> void:
	var pos := global_position
	# Use square root for uniform distribution in sphere
	var radius := sqrt(_rng.randf()) * (placement_size.x / 2.0)
	var theta := _rng.randf_range(0.0, TAU)
	pos += Vector3(radius * cos(theta), 0, radius * sin(theta))
	_place_instance(i, pos)

func _place_random_box(i: int) -> void:
	var pos := global_position
	pos += Vector3(
		_rng.randf_range(-placement_size.x/2.0, placement_size.x/2.0),
		0,
		_rng.randf_range(-placement_size.z/2.0, placement_size.z/2.0))
	_place_instance(i, pos)

func _place_grid(i: int, r: int, c: int) -> void:
	var pos := global_position
	pos += Vector3(c * grid_cell_size.x, 0, r * grid_cell_size.z)
	var extra_y = deg_to_rad(rotation_per_column[c]) if c < rotation_per_column.size() else 0.0
	_place_instance(i, pos, extra_y)

func _place_instance(i: int, pos: Vector3, extra_y: float = 0) -> void:
	var final_pos := pos - global_position + offset_position

	# Surface snapping
	if snap_to_surface and _space:
		var ray := PhysicsRayQueryParameters3D.create(
		pos + Vector3.UP * (placement_size.y/2.0),
		pos + Vector3.DOWN * (placement_size.y/2.0),
		collision_mask)

		var hit := _space.intersect_ray(ray)
		if not hit.is_empty():
			final_pos = hit.position - global_position + offset_position

	# Build transform
	var t := Transform3D()
	t.origin = final_pos

	# Apply scale
	t = t.scaled(base_scale * Vector3(
	_rng.randf_range(min_random_size.x, max_random_size.x),
	_rng.randf_range(min_random_size.y, max_random_size.y),
	_rng.randf_range(min_random_size.z, max_random_size.z)))

	# Apply rotation (Y -> X -> Z order)
	t = t.rotated(Vector3.UP, deg_to_rad(_rng.randf_range(-random_rotation.y, random_rotation.y)) + offset_rotation.y + extra_y)
	t = t.rotated(Vector3.RIGHT, deg_to_rad(_rng.randf_range(-random_rotation.x, random_rotation.x)) + offset_rotation.x)
	t = t.rotated(Vector3.FORWARD, deg_to_rad(_rng.randf_range(-random_rotation.z, random_rotation.z)) + offset_rotation.z)

	multimesh.set_instance_transform(i, t)

# =================== Debug Visualization ===================

func _create_debug_area():
	_delete_debug_area()
	_debug_draw_instance = MeshInstance3D.new()
	var mat := StandardMaterial3D.new()
	_debug_draw_instance.material_override = mat
	mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	mat.cull_mode = BaseMaterial3D.CULL_DISABLED
	mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	mat.albedo_color = Color(1, 0, 0, 0.078)
	mat.no_depth_test = true
	
	# Create appropriate mesh based on placement type
	if placement_type == PlacementType.SPHERE:
		_debug_draw_instance.mesh = SphereMesh.new()
	else:
		_debug_draw_instance.mesh = BoxMesh.new()
	
	add_child(_debug_draw_instance)
	_update_debug_area_size()

func _delete_debug_area():
	if _debug_draw_instance and _debug_draw_instance.is_inside_tree():
		_debug_draw_instance.queue_free()
		_debug_draw_instance = null

func _update_debug_area_size():
	if not _debug_draw_instance: return
	
	match placement_type:
		PlacementType.SPHERE:
			if _debug_draw_instance.mesh is SphereMesh:
				_debug_draw_instance.mesh.radius = placement_size.x / 2.0
				_debug_draw_instance.mesh.height = placement_size.x
		_:
			if _debug_draw_instance.mesh is BoxMesh:
				_debug_draw_instance.mesh.size = placement_size

# =================== Property Validation ===================

func _validate_property(property: Dictionary) -> void:
	# Hide grid-specific properties when not in grid mode
	if property.name in ["grid_rows", "grid_columns", "grid_cell_size", "rotation_per_column"]:
		if placement_type != PlacementType.GRID:
			property.usage = PROPERTY_USAGE_NO_EDITOR
	
	# Hide count property when in grid mode (since it's determined by rows*columns)
	if property.name == "count" and placement_type == PlacementType.GRID:
		property.usage = PROPERTY_USAGE_NO_EDITOR

# =================== Editor Integration ===================

func _get_configuration_warnings() -> PackedStringArray:
	var warnings := PackedStringArray()
	
	if multimesh == null:
		warnings.append("MultiMesh resource is not set")
	elif multimesh.mesh == null:
		warnings.append("MultiMesh has no mesh assigned")
	
	if placement_type == PlacementType.GRID:
		if grid_rows * grid_columns > 10000:
			warnings.append("Grid size exceeds 10,000 instances. This may impact performance.")
	
	return warnings
