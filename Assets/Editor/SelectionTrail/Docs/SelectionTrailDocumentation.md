# SelectionTrail

**The Selection Trail** maintains a stack of your recently selected objects for rapid access. It provides filtering by type (Prefabs, Models, Materials, etc.), favoriting, and quick interactions from the list.

---

## Contents

- [1. Opening the window](#1-opening-the-window)
- [2. Workflow & features](#2-workflow--features)
- [3. Interactions](#3-interactions)
- [4. Options (gear menu)](#4-options-gear-menu)

---

## 1. Opening the window

- Open **Window → The Selection Trail** to show the window. The window keeps the last project and scene selections for quick access.

---

## 2. Workflow & features

- **Filters:** Use the type icons in the top bar to toggle visibility for scene objects, prefabs, models, materials, textures, audio, and folders. Click **All** to clear all filters. The search field filters the list by name or path (multiple words match if any word matches).
- **Scene objects:** The **Keep scene objects** option in the gear menu controls whether objects selected in the Hierarchy are added to the trail. When disabled, only Project Browser selections are recorded.
- **Favourites:** Click the **star icon** on any item to pin it. Favourites stay at the top of the list and survive pruning. You can drag items to reorder them, including between the favourites and non-favourites sections.
- **Pruning:** Use **Prune trail** in the gear menu to reduce the trail to the 20 most recent non-favourite items. Click the **trash icon** on an item to remove it from the list without affecting others.
- **Previews:** When **Show previews** is enabled in options, hovering over a list item shows a tooltip with a preview (e.g. thumbnail, mesh/material info, audio length). For audio clips, the tooltip indicates that you can press **Space** to play.

---

## 3. Interactions

- **Single click:** Focuses and pings the object in the Project or Hierarchy view.
- **Double click:** Fully selects the object (useful for quickly populating the Inspector).
- **Drag & drop:** Click and drag any item to move it into scene fields, folder paths, or directly into the Scene view. You can also drag items within the list to reorder them or move them between favourites and non-favourites.
- **Audio preview:** Hover over an audio clip and press **Space** to play it. Press **Space** again to stop. Volume is controlled by **Audio preview volume** in the gear menu.

---

## 4. Options (gear menu)

- **Prune trail:** Reduces history to the 20 most recent non-favourite items.
- **Keep scene objects:** When enabled, selections made in the Scene Hierarchy are added to the trail; when disabled, only Project Browser selections are recorded.
- **Show previews:** When enabled, hovering list items shows preview tooltips (thumbnails, asset info, and for audio clips the option to press Space to play).
- **Audio preview volume:** Slider (0–1) applied when previewing audio clips via Space.

---

*Click the **Help** button (?) in the toolbar to show this content inside the window.*
