# Copilot Knappen

A smart command bar for the **Copilot key** on Windows 11. Press the key, a text field appears in the middle of
the screen, type what you want, and Copilot Knappen works out what you mean:

| You type | What happens |
| --- | --- |
| `how do magnets work` | Searches the web in your default browser (Google by default, configurable) |
| `notepad`, `chrome`, `vscode`, `discord` | Opens the application (fuzzy match on Start-menu names, incl. Store apps) |
| `play bohemian rhapsody`, `spotify daft punk`, `queen on spotify` | Searches Spotify (desktop app if installed, otherwise the web player) |
| `play`, `pause`, `skip`, `next`, `previous`, `spotify pause` | Controls Spotify playback (falls back to the current media player) |
| `volume 40`, `vol up`, `vol down 5`, `louder`, `mute`, `unmute` | Sets the PC master volume |
| `youtube cat videos`, `yt lofi`, `watch speedruns`, `x on youtube` | Searches YouTube |
| `github.com`, `C:\Projects`, `%APPDATA%`, `ms-settings:display` | Opens the website, folder or settings page |
| `open …`, `search …`, `google …` | Forces "open" or "web search" |
| `settings` | Opens the settings panel |

The line under the text field always shows what Enter will do. **Shift+Enter** forces a web search, **Tab**
completes an app name, **↑/↓** walk through your history, **Esc** closes.

## Custom commands

Type `settings` to open the settings panel. On the **Custom commands** tab you can add your own commands:

* **Keywords** – comma separated, e.g. `wiki, wikipedia`.
* **Match** – `StartsWith` (the rest of the prompt becomes `{query}`), `Exact`, or `Regex` (a named group
  `(?<query>…)` becomes `{query}`).
* **Action** – one of:

  | Action | Target | Arguments |
  | --- | --- | --- |
  | Open URL | URL with `{query}`, e.g. `https://en.wikipedia.org/wiki/Special:Search?search={query}` | – |
  | Web search | Search text (empty = `{query}`) | – |
  | Open application | App name, e.g. `Visual Studio Code` | – |
  | Run program / file / folder | `notepad.exe`, `C:\Projects`, `%USERPROFILE%\Downloads` | Program arguments |
  | Shell command (cmd) | Command line | `show` to keep the console open |
  | Spotify search / YouTube search | Search text (empty = `{query}`) | – |
  | Media control | `toggle`, `play`, `pause`, `next`, `previous` | `spotify` (default) or `any` |
  | PC volume | `set`, `up`, `down`, `mute`, `unmute`, `togglemute` | Percent, e.g. `30` or `{query}` |
  | Open settings / Exit | – | – |

* **Placeholders** – `{query}` (URL-encoded in *Open URL*), `{query:raw}`, `{query:url}`, `{input}` (whole prompt).

Custom commands are checked before the built-in rules, so they can override them. A **Try a prompt** box shows
what any text would do with your unsaved changes. A few examples (Wikipedia, Maps, GitHub, Translate, Lock PC,
Downloads, a "quiet" volume preset) are created on first start.

The **General** tab has the search engine, volume step, an optional fallback shortcut (default
`Ctrl+Alt+Space`, for PCs without a Copilot key), app detection, and whether to keep running in the tray.
Settings are stored as JSON in `%LOCALAPPDATA%\CopilotKnappen\settings.json`.

## How the Copilot key integration works

Windows 11 lets a packaged app register as a **Microsoft Copilot hardware key provider** with the
`com.microsoft.windows.copilotkeyprovider` app extension. See
[`packaging/AppxManifest.xml`](packaging/AppxManifest.xml):

```xml
<uap3:Extension Category="windows.appExtension">
  <uap3:AppExtension Name="com.microsoft.windows.copilotkeyprovider" Id="CopilotKnappen"
                     DisplayName="Copilot Knappen" PublicFolder="Public">
    <uap3:Properties>
      <SingleTap>copilotknappen://?state=Tap</SingleTap>
      <PressAndHoldStart>copilotknappen://?state=Down</PressAndHoldStart>
      <PressAndHoldStop>copilotknappen://?state=Up</PressAndHoldStop>
    </uap3:Properties>
  </uap3:AppExtension>
</uap3:Extension>
```

Key presses activate the app through its `copilotknappen:` protocol. The first launch keeps a small
background instance (tray icon). Later presses are handed to it over a named pipe, so the field appears
straight away. A tap toggles the field, and press-and-hold opens it.

The app has to be **MSIX packaged and signed** to show up in the Copilot key settings, so the build produces
a signed `.msix`.

## Install

### From a CI build

Every push builds `x64` and `arm64` packages (GitHub Actions → *Build* → artifacts). Download
`CopilotKnappen-x64`, unzip it, and run in PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

This trusts the signing certificate (one UAC prompt), installs the package and opens the right Settings page.
Then choose **Settings → Personalization → Text input → Customize Copilot key on keyboard → Copilot Knappen**.

> CI builds are signed with a throw-away self-signed certificate, so each build's `.cer` has to be trusted
> once (`install.ps1` does that). To sign with your own certificate, add the repository secrets
> `SIGNING_PFX_BASE64` and `SIGNING_PFX_PASSWORD`, plus a variable `MSIX_PUBLISHER` that equals the
> certificate subject (e.g. `CN=Contoso`).

### Build it yourself

Requirements: Windows 10 2004+ / Windows 11, the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0),
and the Windows SDK (for `makeappx.exe` and `signtool.exe`).

```powershell
.\scripts\build-msix.ps1            # -> artifacts\CopilotKnappen_1.0.0.0_x64.msix + CopilotKnappen.cer
.\scripts\install.ps1
```

For quick development without signing, turn on Developer Mode and run `.\scripts\register-dev.ps1`. That
registers the unpacked layout so you can test with `start copilotknappen://?state=Tap`. Windows only offers
signed packages for the Copilot key, so use the signed build to try the real key.

You can also run it unpackaged with `dotnet run --project src/CopilotKnappen` and use the `Ctrl+Alt+Space`
shortcut.

Uninstall with `Get-AppxPackage CopilotKnappen | Remove-AppxPackage`.

## Project layout

```
src/CopilotKnappen.Core/     Cross-platform logic: intent parser, app matching, settings (unit tested)
src/CopilotKnappen/          WPF app: launcher + settings windows, Copilot key activation, single instance,
                             app index (shell:AppsFolder), Spotify/media sessions, Core Audio volume, tray
tests/                       xUnit tests for the intent detection (`dotnet test` runs on any OS)
packaging/                   AppxManifest.xml (Copilot key provider extension) and logos
scripts/                     build-msix.ps1, install.ps1, register-dev.ps1, generate-assets.py
```
