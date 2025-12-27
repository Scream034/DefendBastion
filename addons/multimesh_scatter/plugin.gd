@tool
extends EditorPlugin

var undo_redo := get_undo_redo()
var dialog: AcceptDialog
var remove_checkbox: CheckBox
var current_selection: Array

func _enter_tree() -> void:
	add_tool_menu_item("Convert Selection to ScatterMultiMesh", Callable(self, "_open_convert_dialog"))
	add_tool_menu_item("Convert Selection to MultiMesh", Callable(self, "_convert_to_plain_multimesh"))
	add_tool_menu_item("Extract MultiMesh to Instances", Callable(self, "_extract_multimesh"))

	# Dialog for Scatter conversion
	dialog = AcceptDialog.new()
	dialog.title = "Convert to ScatterMultiMesh"
	dialog.dialog_text = "Combine selected MeshInstance3D nodes into a ScatterMultiMesh?"

	var vbox := VBoxContainer.new()
	vbox.set_anchors_preset(Control.PRESET_FULL_RECT)

	remove_checkbox = CheckBox.new()
	remove_checkbox.text = "Remove MeshInstance3D nodes after conversion (otherwise just hide)"
	remove_checkbox.size_flags_vertical = Control.SIZE_EXPAND_FILL
	remove_checkbox.button_pressed = true

	dialog.confirmed.connect(_do_convert_to_scatter)

	vbox.add_child(remove_checkbox)
	dialog.add_child(vbox)
	add_child(dialog)


func _exit_tree() -> void:
	remove_tool_menu_item("Convert Selection to ScatterMultiMesh")
	remove_tool_menu_item("Convert Selection to MultiMesh")
	remove_tool_menu_item("Extract MultiMesh to Instances")
	if dialog:
		dialog.queue_free()


# =================== Dialog Open ===================

func _open_convert_dialog() -> void:
	current_selection = get_editor_interface().get_selection().get_selected_nodes()
	if current_selection.is_empty():
		push_warning("No nodes selected")
		return
	dialog.popup_centered(Vector2(400, 120))


# =================== Scatter Conversion ===================

func _do_convert_to_scatter() -> void:
	if current_selection.is_empty():
		return

	var mesh_nodes: Array = []
	for node in current_selection:
		if node is MeshInstance3D and node.mesh != null:
			mesh_nodes.append(node)
	if mesh_nodes.is_empty():
		push_warning("No valid MeshInstance3D found")
		return

	# Group by Mesh resource
	var mesh_groups := {}
	for node in mesh_nodes:
		var m: Mesh = node.mesh
		if not mesh_groups.has(m):
			mesh_groups[m] = []
		mesh_groups[m].append(node)

	undo_redo.create_action("Convert Selection to ScatterMultiMesh")

	for mesh in mesh_groups.keys():
		var group_nodes: Array = mesh_groups[mesh]
		if group_nodes.is_empty(): continue

		var parent: Node = group_nodes[0].get_parent()
		var scene_owner: Node = group_nodes[0].owner

		# Build Scatter node
		var scatter = preload("res://addons/multimesh_scatter/scatter_multimesh.gd").new()
		scatter.name = "Scatter_" + (mesh.resource_name if mesh.resource_name != "" else "Mesh")
		scatter._suppress_scatter = true

		var mm := MultiMesh.new()
		mm.mesh = mesh
		mm.transform_format = MultiMesh.TRANSFORM_3D
		mm.instance_count = group_nodes.size()
		for i in range(group_nodes.size()):
			mm.set_instance_transform(i, group_nodes[i].transform)
		scatter.multimesh = mm
		scatter.count = group_nodes.size()

		# Disable collision snapping and warn
		if scatter.snap_to_surface:
			scatter.snap_to_surface = false
			push_warning("Collision snapping disabled during conversion to preserve transforms. You can re-enable it later.")

		# Lock as baked
		scatter.scatter_mode = ScatterMultiMesh.ScatterMode.BAKED
		scatter._suppress_scatter = false

		# Register for Undo/Redo
		undo_redo.add_do_method(parent, "add_child", scatter)
		undo_redo.add_do_method(scatter, "set_owner", scene_owner)
		undo_redo.add_undo_method(parent, "remove_child", scatter)
		undo_redo.add_undo_reference(scatter)

		# Originals
		for node in group_nodes:
			if remove_checkbox.button_pressed:
				undo_redo.add_do_method(parent, "remove_child", node)
				undo_redo.add_undo_method(parent, "add_child", node)
				undo_redo.add_undo_method(node, "set_owner", scene_owner)
			else:
				undo_redo.add_do_method(node, "set_visible", false)
				undo_redo.add_undo_method(node, "set_visible", true)

	undo_redo.commit_action()
	dialog.hide()


# =================== Plain MultiMesh Conversion ===================

func _convert_to_plain_multimesh() -> void:
	current_selection = get_editor_interface().get_selection().get_selected_nodes()
	if current_selection.is_empty():
		push_warning("No nodes selected")
		return

	var mesh_nodes: Array = []
	for node in current_selection:
		if node is MeshInstance3D and node.mesh != null:
			mesh_nodes.append(node)
	if mesh_nodes.is_empty():
		push_warning("No valid MeshInstance3D found")
		return

	var mesh_groups := {}
	for node in mesh_nodes:
		var m: Mesh = node.mesh
		if not mesh_groups.has(m):
			mesh_groups[m] = []
		mesh_groups[m].append(node)

	undo_redo.create_action("Convert Selection to MultiMesh")

	for mesh in mesh_groups.keys():
		var group_nodes: Array = mesh_groups[mesh]
		if group_nodes.is_empty(): continue

		var parent: Node = group_nodes[0].get_parent()
		var scene_owner: Node = group_nodes[0].owner

		var mm := MultiMesh.new()
		mm.mesh = mesh
		mm.transform_format = MultiMesh.TRANSFORM_3D
		mm.instance_count = group_nodes.size()
		for i in range(group_nodes.size()):
			mm.set_instance_transform(i, group_nodes[i].transform)

		var mminst := MultiMeshInstance3D.new()
		mminst.name = "Combined_" + (mesh.resource_name if mesh.resource_name != "" else "Mesh")
		mminst.multimesh = mm

		undo_redo.add_do_method(parent, "add_child", mminst)
		undo_redo.add_do_method(mminst, "set_owner", scene_owner)
		undo_redo.add_undo_method(parent, "remove_child", mminst)
		undo_redo.add_undo_reference(mminst)

		for node in group_nodes:
			undo_redo.add_do_method(parent, "remove_child", node)
			undo_redo.add_undo_method(parent, "add_child", node)
			undo_redo.add_undo_method(node, "set_owner", scene_owner)

	undo_redo.commit_action()


# =================== Extraction ===================

func _extract_multimesh() -> void:
	var selection := get_editor_interface().get_selection().get_selected_nodes()
	if selection.is_empty():
		push_warning("No MultiMeshInstance3D selected")
		return

	var extracted: Array = []
	undo_redo.create_action("Extract MultiMesh to Instances")

	for node in selection:
		if node is MultiMeshInstance3D and node.multimesh:
			var mm: MultiMesh = node.multimesh
			var mesh: Mesh = mm.mesh
			if mesh == null: continue

			var parent: Node = node.get_parent()
			var scene_owner: Node = node.owner

			for i in range(mm.instance_count):
				var trans: Transform3D = mm.get_instance_transform(i)
				var inst := MeshInstance3D.new()
				inst.mesh = mesh
				inst.transform = trans
				inst.name = node.name + "_inst" + str(i)

				undo_redo.add_do_method(parent, "add_child", inst)
				undo_redo.add_do_method(inst, "set_owner", scene_owner)
				undo_redo.add_undo_method(parent, "remove_child", inst)
				undo_redo.add_undo_reference(inst)

				extracted.append(inst)

			# Remove MultiMesh container
			undo_redo.add_do_method(parent, "remove_child", node)
			undo_redo.add_undo_method(parent, "add_child", node)
			undo_redo.add_undo_method(node, "set_owner", scene_owner)

	undo_redo.commit_action()
	if extracted.size() > 0:
		print("Extracted %d MeshInstance3D nodes" % extracted.size())
