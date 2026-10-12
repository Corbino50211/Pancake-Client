# Camera & OBS

## Connect OBS Studio

1. Open OBS and configure your recording output or streaming destination as usual.
2. In OBS, open **Tools → WebSocket Server Settings**, enable the server, and copy its password. The default port is 4455.
3. In Pancake Client, open **Camera & OBS**.
4. Use `ws://127.0.0.1:4455`, enter the password, and click **Connect OBS**.
5. Choose a program scene and use the recording/microphone controls.

This client uses OBS WebSocket **5.x / RPC 1**. It supports OBS on the same PC only. Your WebSocket password stays in memory until the app closes. The app does not modify OBS's streaming destination, profiles, or encoder settings.

Use the exact OBS audio input source name in the microphone field (for example `Mic/Aux`). An unknown source produces an OBS error rather than muting a different source.

## Controls in VR

Install the companion, keep Pancake Client open and connected to OBS, then open:

**BoneMenu → Pancake Client → OBS**

Available actions:

- Start/stop recording.
- Mute/unmute the configured audio source.
- Select an OBS program scene.
- Start/stop streaming after opting in with **Allow in-game start/stop streaming controls** in the desktop app.

The Pancake menu's top status row shows recording state/time, streaming state, or the last command error. Recording state is polled approximately every two seconds. Use explicit start/stop commands so an accidental repeated input does not toggle a recording back off.

## Experimental camera output

The revised companion source adds a runtime-built handheld camera with two native BoxGrip handles, a live screen, and an OBS streaming indicator. It spawns in front of your player when the rig is ready. Grab a side handle; release it to leave the camera floating. BoneMenu → Pancake Client → Camera → Spawn / recall handheld camera brings it back when not held. Views include handheld, first person, right hand, stationary, behind-you follow, and out front. This companion revision still requires compilation against your local game and headset testing.

1. In BONELAB settings, set spectator mode to **Fish Eye** so the native monitor camera is enabled.
2. Keep Pancake Client open and choose a view under **Camera & OBS**, then click **Start camera**. Active view and FOV changes apply to BONELAB's native spectator camera.
3. In OBS, capture the **BONELAB game window**, using Game Capture or Window Capture. Set your game window and OBS canvas/output resolution as desired. Monitor output uses the game's native render path and frame rate; it does not pass through JPEG or IPC.
4. The handheld screen uses a **1920×1080** render texture. The optional desktop preview uses JPEG at up to **5 FPS** and is separate from the native monitor output. No extra window opens automatically.
5. Stopping the camera restores the native spectator follower target and FOV. After a level change, the companion finds the new spectator camera.

The camera source uses BONELAB's SLZ shaders and Interactable layer for visible geometry, replacing the generic materials/default layer that could produce a shadow-only prop. This revision still requires local compilation and headset testing.

The app's **Save screenshot** saves its most recent 1080p preview frame. For video, record BONELAB directly in OBS. Enable Fish Eye if the companion reports that the spectator camera is unavailable.

## Planned camera improvements

- Reduce overhead of the optional app preview readback. Native spectator recording already avoids JPEG/IPC.
- Further physical-camera polish and headset validation.
- Camera presets paired with OBS scenes.
- Follow collision avoidance and configurable damping/distance.
- Hide-menu/avatar capture options and per-avatar framing.
- Optional OBS scene/source creation after the capture backend is stable.
