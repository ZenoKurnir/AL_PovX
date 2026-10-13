# AL_PovX

First-person (PoV) camera for H scenes in **AmanatsuLocation**, as a BepInEx 6 (IL2CPP) plugin.

It is a port of **AC_PovX** for Aicomi by [y0soro](https://github.com/y0soro) ([ILL_Plugins](https://github.com/y0soro/ILL_Plugins), [binary release](https://github.com/y0soro/ILL_Plugins/releases/tag/bin-AC_PovX)), which itself builds on **SVS_PovX** for Summer Vacation Scramble by its original author(s). All the core PoV logic comes from their work; this port adapts it to AmanatsuLocation's H code and adds a camera stabilizer.

## Features

- See the H scene through any character's eyes. Starts on the male; cycle between everyone in the scene.
- Look around and zoom with the mouse (or touch).
- The PoV character's head is hidden so it doesn't block the view (also on the silhouette body).
- **Stabilizer** (new): keeps the camera steady instead of following every head movement of the animation, with adjustable strength.
- Hotkeys are ignored while typing in a text box.

## Requirements

- AmanatsuLocation
- [BepInEx 6 (IL2CPP, bleeding edge)](https://builds.bepinex.dev/projects/bepinex_be), as included in the usual modpacks

## Install

1. Download `AL_PovX-vX.Y.Z.zip` from [Releases](../../releases).
2. Extract it into the game folder (the one with `AmanatsuLocation.exe`), so you end up with `BepInEx/plugins/AL_PovX.dll`.
3. Start the game. The settings file `BepInEx/config/AL_PovX.cfg` is created on first launch.

## Controls (during H)

| Key | Action |
| --- | --- |
| `N` | Toggle first-person view |
| `M` | Switch to the next character |
| Hold left mouse | Look around |
| Hold right mouse + move | Zoom |

All keys can be changed in the settings (F1 with ConfigurationManager, or the .cfg file).

## Settings

**General:** Hide Head, Camera Sensitivity, Max/Min Field of View, Offset X/Y/Z (camera position relative to the eyes).

**Scene PoV:**
- Min/Max Camera Angle X, Camera Angle Span Y: how far you can look around.
- Normalize Camera Z-Axis: stops the camera from tilting sideways.
- Camera Smoothness (0-90): light position smoothing.
- Rotate Head to Camera: the head turns along with where you look.
- **Stabilizer** (off by default): damps head bobbing and shaking from the animation. Mouse look still responds immediately, and posture changes snap instantly.
- **Stabilizer Strength** (0-100, default 85): higher is steadier but trails big head movements a little more.

## Building

Requires the .NET SDK (6.0 or newer) and a game install where BepInEx has run at least once (it generates `BepInEx/interop`).

```
cd src
dotnet build -c Release -p:GameDir="C:\path\to\AmanatsuLocation\BepInEx"
```

The plugin is written to `src/bin/Release/net6.0/AL_PovX.dll`.

## Credits

- **y0soro**: AC_PovX, which this plugin is ported from.
- **The original author(s) of SVS_PovX**: the PoV mod that AC_PovX is based on.

## License

MIT, see [LICENSE](LICENSE).
