# Fixed default-skin base

`Default.dsf` is the same PotPlayer default resource package against which the existing skin was compared. Its SHA-256 is recorded in `manifest.json`; it contains 46 entries. Original skin resources retain their original ownership and terms.

`../overrides/` contains the eight changed definitions and one new small-window icon. `build/skin.ps1` verifies the base, extracts it into a temporary build directory, applies overrides, then writes a complete DSF. Do not silently use the default skin from whichever player happens to be installed on a build machine.
