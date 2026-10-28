Pixel UI sprites (1 pixel = 4 canvas units; set Image > Pixels Per Unit Multiplier to 0.25 for sliced use):
  Keycap_<Colour>_Face / _Lip: 12 x 12, 9-slice border 3. Panel: 16 x 16, border 3 (bottom 5: the thick edge).
Digits.png: "0123456789:" from Neo둥근모 at 16 px (8 px per character), rows cropped to the ink, 1 pixel = 1 block in
VoxelText. Re-render with:
  magick -background white -fill black -font NeoDunggeunmoPro-Regular.ttf -pointsize 16 -density 72 +antialias \
    label:"0123456789:" -alpha off -type bilevel raw.png   (then crop rows to the ink, keeping the full width)
