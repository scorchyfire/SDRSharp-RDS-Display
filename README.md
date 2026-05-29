# SDRSharp RDS Display Plugin

A plugin for [SDRSharp](https://airspy.com/download/) that decodes and displays **RDS (Radio Data System)** data from FM broadcasts in real time — including station callsigns, program type, radio text, and more.

---

## Features

- **Live RDS panel** showing:
  - **PS** – Program Service name (the 8-character station name scrolled on car radios)
  - **PI** – Program Identification code (hex)
  - **Callsign** – Resolved from a built-in PI code database or the NRSC algorithmic decoder
  - **PTY** – Program Type (e.g. *Rock Music*, *News*, *Sports*)
  - **RT** – RadioText (the long scrolling message shown on some radios)
- **Spectrum overlay bar** injected directly onto the SDRSharp waterfall/spectrum display — no extra window needed
- **Stereo indicator** — wraps the PS name in `((( ... )))` when an FM stereo pilot is detected
- **PS underscore mode** — shows raw 8-character PS field with spaces replaced by underscores
- **iHeart market callsign** support — optionally show the market-facing station name instead of the licensed callsign for dual-mapped stations
- **PTY region** — switch between European (RDS) and North American (RBDS) program type tables
- **Custom PI overrides** — manually map any PI code to a callsign of your choice; changes are saved persistently
- **Configurable overlay bar** — font name, size, style, foreground/background color, and horizontal character stretch are all user-adjustable from within the plugin panel

---

## Installation

1. Download the latest **SDRSharp.RdsDisplay.zip** from the [Releases](../../releases) page.
2. Extract both files from the zip into your **SDRSharp installation folder** (the same folder as `SDRSharp.exe`):
   - `SDRSharp.RdsDisplay.dll`
   - `pi_codes.json`
3. Add the plugin entry to your `Plugins.xml` (or `SDRSharp.config`):
   ```xml
   <add key="RDS Display" value="SDRSharp.RdsDisplay.RdsDisplayPlugin,SDRSharp.RdsDisplay" />
   ```
4. Launch SDRSharp — the **RDS Display** panel will appear in the plugin list on the left.

---

## Requirements

- **SDRSharp** (v1912 or newer ONLY!)
- **.NET 9 Windows Runtime** — included with SDRSharp; no separate install needed in most cases
- A **FM-capable SDR receiver** tuned to an FM broadcast station with RDS

---

## Usage

1. Tune to an FM station and select **WFM** (Wide FM) demodulation mode.
2. Enable the RDS decoder in SDRSharp (the built-in decoder is used automatically).
3. Open the **RDS Display** plugin panel from the left-hand plugin list.
4. RDS data will appear in the panel and in the spectrum overlay bar as soon as data is received.

### Settings

| Setting | Description |
|---|---|
| **Show iHeart Market callsign** | Swap the FCC-licensed callsign for the iHeart market name when available (e.g. `KERJ` → `WPAP`) |
| **Show underscores in PS** | Replace spaces in the 8-character PS field with `_` so padding is visible |
| **PTY Region** | Switch between Global/European and North American (RBDS) program type names |
| **Bar Font / Size / Style** | Customize the font used in the spectrum overlay bar |
| **Text / Background Color** | Hex color values for the overlay bar (e.g. `#EFEEEC`, `#000000`) |
| **Horizontal Stretch** | Extra pixels added between each character in the overlay bar (0 = off) |
| **Custom PI Overrides** | Manually assign a callsign to any PI code; saved to `pi_codes.json` |

---

## Building from Source

Requirements: **.NET 9 SDK**, Windows (WinForms target)

```bat
dotnet build sdrplugins/sdrplugins.sln --configuration Release
```

Output files will be in `sdrplugins/Release/net7.0-windows/`.

---

## License

See [LICENSE](LICENSE).

## Credits

https://dxsphere.neocities.org/ Kyle / dxsphere - DB credits

Kita Zaizen / dxfoxes - DB credits
