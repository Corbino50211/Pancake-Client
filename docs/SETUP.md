# Pancake Client — first-run setup

## 1. Run the Windows app

Download the `PancakeClient-win-x64` artifact from the latest successful **Windows build** in this repository's **Actions** tab. Extract the whole ZIP into a folder and run `PancakeClient.exe`. Keep all the extracted files together. The portable build includes its .NET runtime.

The first release is an unsigned development build for Windows x64. There is no installer or automatic client updater yet.

## 2. Connect your BONELAB installation

1. Open **Settings**.
2. Click **Detect Steam install**, or **Browse game folder** for a custom/Meta PC installation.
3. Select the folder containing `BONELAB_Steam_Windows64.exe` (Steam) or `BONELAB.exe`.
4. Confirm the **Content Mods folder**, usually:
   `%USERPROFILE%\AppData\LocalLow\Stress Level Zero\BONELAB\Mods`
5. Click **Save settings**.

Code mods live in `Mods` in the game installation; loader plugins live in `Plugins` next to it. Content mods live in the separate LocalLow folder. Do not select the same directory for code and content mods.

## 3. Manage existing mods

**Installed Mods** shows code DLLs, plugins, and content folders. Select an entry, then **Enable / disable** while BONELAB is closed.

- Code DLLs are renamed to `name.dll.pancake-disabled`; enabling restores the original name.
- Content folders are moved into the sibling `Mods.pancake-disabled` folder; enabling moves them back.
- No mod is deleted by a toggle.
- Dependency relationships are not yet enforced. Keep libraries required by your enabled mods enabled, and check each mod's supported game patch.
- If enabled and disabled copies conflict, the app refuses to overwrite either copy.

**Browse Mods → Refresh catalog** loads Thunderstore. Select a mod to read its description and listed dependencies; **Download selected ZIP** saves its archive. Code mod installation is manual in this first version: follow the author's mod page and install its dependencies.

**Import content ZIP** accepts a single content mod containing one `pallet.json`. It rejects executable files, archive traversal, multiple pallets, and existing destination folders. Download a Windows/PCVR-compatible content package. It does not convert Quest assets to PC assets.

## 4. Profiles and save backups

Profiles save enabled/disabled states of currently installed mods. Applying one never downloads missing mods or changes installed versions. Missing mods are counted, and mods not mentioned in a profile stay as they were. Before applying, the app saves the current state under its `profile-backups` folder. Import that JSON to recover a prior setup.

To back up saves, close BONELAB, open **Levels & Saves**, choose the save folder (normally the `Saves` folder under BONELAB's LocalLow directory), and choose a ZIP destination outside that folder. Restore manually by extracting the desired backup while the game is closed.

## 5. OBS without the companion

The desktop OBS controls work even if BONELAB or the companion is not installed. See [OBS.md](OBS.md).

## 6. Build the in-game companion

The companion is source-only until its game-specific build and headset behavior are validated. You need:

- Your PC copy of BONELAB.
- A MelonLoader version compatible with that BONELAB patch and exposing `MelonLoader/net6/MelonLoader.dll`.
- A compatible BoneLib with its current `BoneLib.BoneMenu.Page` API. The initial source was written against the BoneLib 3.2.x API.
- The .NET 8 SDK (it can target the companion's .NET 6 runtime).
- The full Pancake Client source checkout or repository ZIP.

Run BONELAB once with MelonLoader and BoneLib to generate the assemblies, then close it. From the source folder in PowerShell:

```powershell
.\scripts\Build-Companion.ps1 -GameDir 'C:\Program Files (x86)\Steam\steamapps\common\BONELAB'
```

If PowerShell's script policy prevents running that script, use the equivalent command directly:

```powershell
dotnet build src/Pancake.Companion -c Release "-p:GameDir=C:\Program Files (x86)\Steam\steamapps\common\BONELAB"
```

The project checks for the loader, BoneLib, and generated Unity assemblies before compiling. Missing references require installing/running the matching loader; do not copy random game assemblies from elsewhere.

The output should be `src/Pancake.Companion/bin/Release/net6.0/Pancake.Companion.dll`. In the desktop app, choose **Settings → Install companion DLL** and select that file. Existing companion DLLs are backed up before replacement. Start Pancake Client and BONELAB under the same Windows account and privilege level.

In VR, open **BoneMenu → Pancake Client**. The app's lower-left status should change to **Companion connected**. See the OBS guide for in-game controls.

## Data and troubleshooting

Pancake Client stores settings and profile data in `%LOCALAPPDATA%\PancakeClient`. OBS passwords are not saved. Screenshots default to `Pictures\Pancake Client`.

- **No mods:** check both game and content paths. Click Refresh.
- **Cannot toggle:** close BONELAB; check for an existing enabled/disabled copy.
- **No console output:** start BONELAB with MelonLoader; the app reads `MelonLoader/Latest.log`.
- **Companion offline:** verify its DLL and BoneLib are enabled, inspect `Latest.log`, and run both apps under the same user/elevation. Only one desktop instance may run.
- **Companion build error:** record the BONELAB patch, MelonLoader/BoneLib versions, and the complete compiler error. Game API changes may require an adapter update.
- **Black camera:** the experimental camera path is not yet verified for every game patch/render setup. Stop that camera and use BONELAB's regular desktop mirror in OBS while reporting the log.

## Validation boundary

Automated checks cover inventory toggles, profile rollback, archive safety, content import, OBS authentication/response correlation/errors, and the companion named-pipe handshake. Windows CI compiles the desktop and launches a WPF smoke check. A human PCVR test is still required for the companion's game API compatibility, menu appearance, camera framing, capture performance, and interaction with other installed mods.

## Desktop updates

The client checks public GitHub Releases at startup. Settings → Client updates lets you check manually or disable startup checks. Choose Install update & restart while BONELAB is closed. The download is checked against its SHA-256 manifest before extraction. Settings and profiles stay in LocalAppData/PancakeClient; replaced application files are backed up in its updates/backups folder. Update failures are logged in update-errors.log. The companion and installed game mods are not updated by the desktop updater.
