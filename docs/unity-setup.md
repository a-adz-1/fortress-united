# Installing Unity for Fortress United

For CachyOS / Arch Linux. Takes about 20–30 minutes, mostly downloading.
You need about 10 GB of free disk space.

## 1. Install Unity Hub

Unity Hub manages Unity editors and projects. The AUR package wraps Unity's
official Linux release:

```bash
paru -S unityhub
```

It asks for your password to install. Then start **Unity Hub** from your
application menu (or run `unityhub`).

> If paru isn't wanted, `yay -S unityhub` does the same. The Flatpak version
> on Flathub is unofficial and sandboxed; avoid it, since it has trouble
> opening external code editors and project folders.

## 2. Sign in and get a licence

1. In Unity Hub, click **Sign in** and log in with your Unity account (create
   one at unity.com if you don't have one).
2. When asked about a licence, choose **Unity Personal** (free). If it
   doesn't ask: **Settings (gear) > Licenses > Add > Get a free personal
   licence**.

## 3. Install the Unity 6 editor

1. Go to **Installs > Install Editor**.
2. Under **Official releases**, pick the newest **Unity 6** version marked
   **LTS** (version number `6000.x...`).
3. On the modules page tick:
   - **Linux Build Support (Mono)**: build the game for Linux.
   - **Windows Build Support (Mono)**: optional, to build for Windows
     players.
   - Leave **Documentation** unticked if you want to save space.
4. Keep the default install location (`~/Unity/Hub/Editor`) and click
   **Install**.

## 4. Open Fortress United

1. Go to **Projects > Add > Add project from disk**.
2. Choose the `fortress-united` folder in your home directory.
3. Click the project to open it. If Unity asks which editor version to use,
   pick the Unity 6 you just installed.
4. The first open takes a few minutes while Unity imports packages (URP,
   Input System) and creates `ProjectSettings/` and `Library/`.

When it's open, tell Claude: the generated settings get committed and the
greybox scene and character controller are next.

## 5. Optional: a code editor

For editing C# with autocomplete:

- **JetBrains Rider** (free for non-commercial use), or
- **Visual Studio Code** with the C# Dev Kit and Unity extensions.

Then in Unity: **Edit > Preferences > External Tools > External Script
Editor**, pick it, and click **Regenerate project files**.

## Troubleshooting

- **Hub opens a blank window or crashes on start:** try
  `unityhub --disable-gpu-sandbox`. On Wayland, also try running it with
  `--ozone-platform=x11`.
- **Sign-in page doesn't come back to the Hub:** make sure your browser is
  allowed to open `unityhub://` links; or sign in from the Hub's own window.
- **"Package resolution failed" on first open:** let Unity update the
  package versions it suggests (the manifest pins versions that may be older
  than your editor), then commit the updated `Packages/` files.
- **Editor won't start, missing libraries:** install `libxcrypt-compat` and
  `gtk3` (`paru -S libxcrypt-compat gtk3`), then try again.
