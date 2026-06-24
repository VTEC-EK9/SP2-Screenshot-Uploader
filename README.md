# SP2 Screenshot Uploader

SP2 Screenshot Uploader is a BepInEx interface extension for **SimplePlanes 2**. It adds an **Upload Screenshot** button to the craft-upload dialog, allowing an existing PNG or JPEG image to be selected from disk and included with a craft upload.

The added button uses the game's native styling and appears directly below **Take Screenshot**.

## Features

- Select an existing PNG, JPG, or JPEG image with a native Windows file picker.
- Add the selected image to the game's normal craft-upload screenshot list.
- Preserve the game's three-screenshot limit.
- Use the same screenshot attachment path as the built-in screenshot button.
- Match the native upload-dialog button style and layout.
- Avoid a separate uploader, account flow, or external network service.

## How it works

When the craft-upload dialog opens, the plugin locates the native **Take Screenshot** button and creates a matching **Upload Screenshot** button underneath it.

After an image is selected, the plugin decodes it into a Unity texture and inserts a standard screenshot-list item into the dialog. It then refreshes the upload dialog's internal screenshot list using the same game methods used by the native screenshot workflow. The image is therefore submitted as a normal `UserView` attachment when the craft is uploaded.

The plugin does not upload files independently and does not communicate with any service outside the game's existing craft-upload process.

## Usage

1. Start SimplePlanes 2.
2. Open a craft in the designer.
3. Open the game's craft-upload dialog.
4. Click **Upload Screenshot**, directly below **Take Screenshot**.
5. Select a PNG, JPG, or JPEG image.
6. Confirm that the image appears in the screenshot list.
7. Upload the craft normally.

The game permits up to three screenshots. The upload button is hidden when that limit is reached.

## Usage example

![Selecting and adding an existing screenshot to a craft upload](assets/usage-example.gif)

## Installation

1. Install BepInEx 5.x for the 64-bit Windows version of SimplePlanes 2.
2. Run the game once so BepInEx creates its folders.
3. Copy `SP2ScreenshotUploader.dll` into:

```text
SimplePlanes 2\BepInEx\plugins\
```

4. Restart SimplePlanes 2.
5. Check `BepInEx\LogOutput.log` if the button does not appear.

## Building from source

The project targets .NET Framework 4.8 and references assemblies from the installed game and BepInEx.

By default, the project expects SimplePlanes 2 at:

```text
C:\Program Files (x86)\Steam\steamapps\common\SimplePlanes 2
```

Build a release DLL with:

```powershell
dotnet build .\SP2ScreenshotUploader.csproj -c Release
```

The compiled plugin is written to:

```text
bin\Release\SP2ScreenshotUploader.dll
```

To build against a different installation directory:

```powershell
dotnet build .\SP2ScreenshotUploader.csproj -c Release -p:GameDir="D:\Path\To\SimplePlanes 2"
```

## Compatibility notes

- Designed for the Windows version of SimplePlanes 2.
- Requires the game's current craft-upload dialog and widget names.
- Uses the Windows file picker bundled with the game.
- Future game updates may change private upload-dialog methods or UI structure.

## Uninstalling

Delete `SP2ScreenshotUploader.dll` from `BepInEx\plugins`, then restart the game.

## Project status

This is an experimental, unofficial community tool. It is not affiliated with or endorsed by Jundroo.
