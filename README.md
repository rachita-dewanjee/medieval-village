# Exploring the Medieval Village (Unity, Android VR)

MM 806 Virtual Reality and Tele-Presence, Assignment 2, by Rachita Dewanjee.

A mobile VR app for Android. The user explores a low-poly medieval village either on the
phone screen (**Mono Mode**, touch gestures) or in stereo inside a **Google Cardboard** viewer
(**VR Mode**, head tracking plus the Cardboard thumb hole).

## Features
- **Start menu:** Mono Mode, VR Mode (Cardboard), Instructions, Exit
- **Mono Mode touch gestures:** drag to look, hold to walk (keep holding to run), tap to walk to a spot,
  pinch to zoom (FOV 30°–90°), double tap to reset, two-finger slide to walk in any direction
- **VR Mode:** stereo rendering with Google Cardboard XR, gyroscope head tracking,
  thumb-hole tap / hold / swipe navigation, look-down 3D Menu and Quit buttons
- **Adjustable field of view** in VR (FOV −/+, 5° steps, 30°–60°)
- **Adjustable inter-ocular distance** (IOD −/+, 2 mm steps, 50–80 mm, default 64 mm), applied live by
  generating Cardboard v2 viewer parameters as if read from the viewer's QR code
- **Collisions and gravity:** buildings, walls, fences, trees and rocks block the player; eye height 1.6 m
- **Ambient sound:** wind, leaves and birds generated in code (no audio files)
- **Better-looking village:** improved lighting and colour, extra trees and props (editor tool)

## Scripts (`Assets/Scripts`, `Assets/Editor`)
| Script | What it does |
|---|---|
| `GameMenu.cs` | Start menu, mode switching, Menu / Quit in both modes |
| `MobileTouchMovement.cs` | All Mono Mode touch gestures, walking, running, zoom |
| `CardboardVRController.cs` | VR Mode: Cardboard setup, thumb-hole navigation, FOV and IOD controls |
| `AmbientSound.cs` | Procedural ambient soundscape |
| `Editor/VillageBeautifier.cs` | Editor menu (Tools > Village) for lighting, props and cleanup steps |

## Open the project
1. Install **Unity 6000.3.22f1** (Unity 6) with **Android Build Support**.
2. Unity Hub → **Add project from disk** → select this folder.
3. **Import the village models:** the 3D village comes from the free Unity Asset Store pack
   **"Slavic World Free" by EmaceArt**. It is not included here because Asset Store licences do not
   allow re-sharing it. Add it from the Asset Store (Package Manager → My Assets) so it lands in
   `Assets/EmaceArt`; the scenes then open complete.
4. Open `Assets/Scenes/Assignment2_MainScene.unity` (menu and Mono Mode) or
   `Assets/Scenes/Assignment2_VRScene.unity`.
5. To run on a phone: File → Build Profiles → Android → Build And Run (minimum API 26).

## Built with
Unity 6000.3.22f1, Universal Render Pipeline (URP), Google Cardboard XR Plugin 1.35, Android (API 26+).
