"""Create an original, tiny square-glyph font for ASS attachment regression tests.

Requires fonttools==4.61.1 (development only). Contains no third-party glyph data.
"""
from pathlib import Path
from fontTools.fontBuilder import FontBuilder
from fontTools.pens.ttGlyphPen import TTGlyphPen

font = FontBuilder(1000, isTTF=True)
font.setupGlyphOrder([".notdef", "A"])
font.setupCharacterMap({65: "A"})
glyphs = {}
for name in [".notdef", "A"]:
    pen = TTGlyphPen(None)
    if name == "A":
        pen.moveTo((0, 0))
        pen.lineTo((600, 0))
        pen.lineTo((600, 900))
        pen.lineTo((0, 900))
        pen.closePath()
    glyphs[name] = pen.glyph()
font.setupGlyf(glyphs)
font.setupHorizontalMetrics({".notdef": (600, 0), "A": (600, 0)})
font.setupHorizontalHeader(ascent=1000, descent=0)
font.setupOS2(sTypoAscender=1000, sTypoDescender=0, usWinAscent=1000, usWinDescent=0)
font.setupNameTable({"familyName": "AssFixtureFont", "styleName": "Regular",
                    "uniqueFontIdentifier": "BilibiliAssFixture1", "fullName": "AssFixtureFont",
                    "psName": "AssFixtureFont", "version": "Version 1.0",
                    "copyright": "Original test glyph. MIT license; see project LICENSE."})
font.setupPost()
font.setupMaxp()
font.font["head"].created = font.font["head"].modified = 3406620153
font.save(Path(__file__).resolve().parents[1] / "tests/fixtures/ass-fixture.ttf")
