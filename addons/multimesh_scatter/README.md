# 🌱 Godot MultiMesh Scatter Tools

A powerful scatter tool for **Godot 4.x** to quickly populate your 3D scenes
with meshes like trees, rocks, grass, etc.  
Originally inspired by [arcaneenergy/godot-multimesh-scatter](https://github.com/arcaneenergy/godot-multimesh-scatter),  
but **heavily extended and improved**.

---

## ✨ Features

- ✅ Convert selected `MeshInstance3D` nodes into a single `MultiMesh`
- ✅ Convert into a custom `ScatterMultiMesh` node with:
  - Box, Sphere and *NEW* Grid placement
  - Procedural or Baked scatter modes
  - Random rotation, scale, and offsets
  - Snap to surfaces option
- ✅ Extract an existing `MultiMeshInstance3D` back into individual meshes  
- ✅ Undo/Redo friendly  
- ✅ Debug display (placement volume visualization in editor)  

---

## 🎥 Demo

Here are some examples of the scatter tool in action:

![random_rotation](https://arcaneenergy.github.io/assets/multimesh_scatter/random_rotation.jpg)

https://user-images.githubusercontent.com/52855634/205499151-2fed5529-d116-400e-817d-a37fefeb8989.mp4

https://user-images.githubusercontent.com/52855634/205499155-1d9bd480-21a9-4b51-9225-40db23342474.mp4

https://user-images.githubusercontent.com/52855634/205499157-723e4ab5-bd87-441a-98ba-3b5a482bf655.mp4

---

## 🚀 Installation

1. Copy the `addons/multimesh_scatter` folder into your project or download the addon from the asset library inside Godot.
    - Import the addons folder into your project (if it already isn't present).
2. In Godot Editor: **Project → Project Settings → Plugins → Enable "MultiMesh Scatter Tools"**  

## ⚠️ Notes

- The sphere placement type takes `placement_size.x` for the radius. The y and z values are not used.
- The sphere placement type behaves more like a capsule shape. This means that only the horizontal radius is taken into account when scattering meshes.
- Scattering occurs automatically in the editor whenever you change a parameter or move the MultiMeshScatter node. In game mode, the scatter occurs once at the beginning of the game.

---

## 🛠 Usage

1. Select one or multiple `MeshInstance3D` nodes.  
2. Use the top editor menu → **Tools**:  
   - **Convert Selection to MultiMesh**  
   - **Convert Selection to ScatterMultiMesh**  
   - **Extract MultiMesh to Instances**  
3. Configure `ScatterMultiMesh` parameters directly in the Inspector.  

---

## 📜 License

This project is under the MIT License.  
Based on **"godot-multimesh-scatter" by arcaneenergy (MIT License)**.  
Extended and maintained by **paralax034**.