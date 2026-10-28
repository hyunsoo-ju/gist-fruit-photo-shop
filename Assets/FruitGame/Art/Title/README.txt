Title glyph bitmaps (1 px = 1 block), rendered from Neo둥근모 Pro at its native 16 px with ImageMagick:
  magick -background white -fill black -font NeoDunggeunmoPro-Regular.ttf -pointsize 16 -density 72 +antialias \
    label:"<text>" -alpha off -trim +repage -type bilevel <name>.png
Black = letter. Re-render after changing the title or subtitle text, then run FruitGame > Title > Build Title.

GIST_Logo_Pixel.png: the 지스트 mark (Art/Logos/GIST_Logo.png) pixelated to 20 blocks tall: trimmed, box-resized to
height 20, alpha thresholded at 50 %, and every pixel snapped to the logo's red #DF1B14 or dark green #3F584F (the tiny
white "GIST" letters are too small at this size and take their surrounding colour). It replaces 지스트 on the first line,
so TitleLine1.png is just "의".
