Place optional high-resolution texture overrides in this folder.

Partial sets are fine — only files you add will replace the originals.

Supported formats: .png, .jpg, .jpeg
(PNG is preferred if more than one format exists for the same name.)

Static textures
  floor00.png … floor51.png
  door00.png … door12.png
  wall000.png … wall209.png

Animated wall/floor textures (palette-cycle replacements)
  Use consecutive lettered frames starting at 'a', e.g.:
    floor00a.png, floor00b.png, floor00c.png, floor00d.png
    wall000a.jpg … wall000d.jpg
  Frames advance at the same rate as the original palette animation.
  If 'a' exists, lettered frames are used (not the static file).
  Extra letters beyond 'd' are allowed if present consecutively.
  All frames in a sequence must share the same extension as the 'a' frame.

Doors are static only (door00 … door12).

UI, inventory, and paperdoll overrides
  panel00.png … panel03.png (inventory, magic, and stats panel backgrounds)
  statspanel.png (stats panel wood background)
  body_m0.png … body_m4.png (male paperdoll bodies)
  body_f0.png … body_f4.png (female paperdoll bodies)
  armor_m00.png … armor_m75.png (male paperdoll armor)
  armor_f00.png … armor_f75.png (female paperdoll armor)
  A higher-resolution file keeps the original on-screen size: the panel is laid out as 83×114
  and a paperdoll body as 34×50.
