# Installation

## Play in the Unity editor

1. **Install** Unity Hub and **Unity 6000.6.4f1** (a later 6000.6.x should work). Add the **Android Build Support**
   module only if you want to build the APK.
2. **Install Git LFS** before cloning (models, textures, audio and large generated meshes are LFS objects):
   ```bash
   git lfs install
   git clone https://github.com/soyebmohammad03-dev/NEON-RIFT.git
   ```
   Without LFS the binary assets arrive as small text pointers and the scenes render without models and textures.
3. **Open** the cloned folder from Unity Hub. The first import takes a while; Unity's `Library/` cache for this
   project grows to roughly 30 GB. Packages come from the Unity registry via `Packages/manifest.json` (URP 17.6.0,
   Input System 1.19.0, Cinemachine 6.6.0, Timeline 6.6.0, glTFast 6.20.0, Splines 2.9.1, ProBuilder 6.1.2,
   AI Navigation 2.0.12, Test Framework 1.8.0).
4. **Play**:

   | To play | Scene |
   |---|---|
   | The connected campaign | `Assets/_NeonRift/Levels/Campaign/Scenes/CampaignBootstrap.unity` |
   | The original standalone Night Run | `Assets/_NeonRift/Scenes/Bootstrap.unity` |
   | Night Run directly (development) | `Assets/_NeonRift/Scenes/NightRun.unity` (the standalone flow adopts it) |
   | Harbor Rain directly (development) | `Assets/_NeonRift/Levels/HarborRain/Scenes/HarborRain.unity` (opens its own garage) |

   Pressing Play in any scene loads the matching bootstrap first (`BootstrapLoader`), so the game always runs
   through its normal flow.
5. Click the Game view once so it receives keyboard input. If the console warns that no keyboard or gamepad is
   connected, the editor's Input System lost its devices: restart the editor.

### What a public checkout does not contain

Kept on the owner's machine for licensing reasons (see [ThirdParty.md](ThirdParty.md)):

- the two Harbor Rain cars: the selections show the three Night Run cars, and everything else works;
- one Harbor Rain work-boat model: the boat is simply absent from the bay;
- the voice recordings (macOS System Voices): every line is still shown as a subtitle. On a Mac you can generate
  your own local copies with `Neon Rift ▸ Campaign ▸ Build Campaign` and `Neon Rift ▸ Harbor Rain ▸ Build Scene`.

## Android

No APK is published (see [BUILDING.md § Distribution](BUILDING.md#distribution)). To run it on your own device:

1. Build it ([BUILDING.md](BUILDING.md)): the output is `Builds/Android/NEON_RIFT_COMPLETE_CAMPAIGN.apk`
   (ARM64, about 526 MB).
2. Enable *Install unknown apps* (or USB debugging) on the device, then copy and open the APK, or install with
   `adb install -r Builds/Android/NEON_RIFT_COMPLETE_CAMPAIGN.apk`.
3. It installs as its own app (`com.soyebmohammad.neonrift.campaign`), beside the standalone Night Run build
   (`com.soyebmohammad.neonrift`).

Requirements and caveats: a 64-bit ARM device on Android 8.0 (API 26) or later, with Vulkan (OpenGL ES 3 as the fallback, per the project settings), and a few GB free.
The APK has **not** been tested on a physical device; performance, memory, touch layout and scaling on phones are
unverified. Landscape only; touch controls appear automatically, and tilt steering can be enabled in Settings
([CONTROLS.md](CONTROLS.md)).

## Troubleshooting

| Symptom | Cause / fix |
|---|---|
| Pink materials, missing models | Git LFS was not installed when cloning: run `git lfs install && git lfs pull`. |
| "WASD does nothing" | Click the Game view; if the console says no keyboard is connected, restart the editor. |
| Opening plays every launch / never | It plays on the first launch per bootstrap. Title ▸ WATCH INTRO replays it. |
| Harbor Rain cars missing from the selection | Expected in a public checkout (licensing). |
| Validator: "garage lists 5 cars" fails | That Harbor Rain check expects the two local-only cars; it fails in a public checkout. |
