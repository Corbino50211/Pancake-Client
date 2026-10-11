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

The first companion includes four views: first person, right-hand-mounted, stationary, and follow. A physical grabbable camera prop, its own in-world screen, collision-aware follow movement, and high-resolution output are not implemented yet.

1. In the desktop **Camera & OBS** section, select a view and FOV.
2. Click **Start / update camera**. This opens a window named **Pancake Camera**.
3. In OBS, add **Window Capture** and choose **Pancake Camera**. Enable client-area capture or crop borders if your capture method includes window chrome.
4. Keep that window open and unminimized. Add it to the scenes you want.
5. Start recording from the desktop or the in-game OBS menu.

The output is **640×360, JPEG, up to 10 FPS**. It is an initial bridge implementation, not a 60-FPS production camera. Rendering/readback can affect VR frame time. Turn it off when unused. For full-quality normal gameplay recording, capture BONELAB's regular desktop mirror directly in OBS; the in-game OBS controls still work.

The capture window contains the camera image only. Objects that the in-game camera sees, potentially including world-space menus or your avatar, can still appear in its view. The initial first-person camera may need per-avatar framing adjustments. Scene unloading stops the experimental camera; restart it in the new level.

Use **Save screenshot** to write the most recent camera frame as PNG. This preserves the preview resolution; it is not a high-resolution render.

## Planned camera improvements

- High-quality GPU-based capture instead of JPEG/readback.
- Physical camera body, grabbable handle, and in-world monitor.
- Camera presets paired with OBS scenes.
- Follow collision avoidance and configurable damping/distance.
- Hide-menu/avatar capture options and per-avatar framing.
- Optional OBS scene/source creation after the capture backend is stable.
