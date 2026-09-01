# Run with NVIDIA GPU

Restores the old [**"Run with graphics processor"**](https://nvidia.custhelp.com/app/answers/detail/a_id/5035/~/run-with-graphics-processor-missing-from-context-menu:-change-in-process-of) context-menu entry that NVIDIA removed from hybrid-GPU (Optimus) laptops. Right-click any `.exe`, choose **"Run with NVIDIA GPU"**, and only that one launch uses the dedicated GPU. Everything else keeps running on the integrated GPU as usual. No permanent setting is changed.

## Components

- **[`GpuLauncher/`](GpuLauncher/GpuLauncher.cs)**: standalone CLI tool. `GpuLauncher.exe nvidia|integrated <program.exe> [args...]` starts the target with `SHIM_MCCOMPAT`/`SHIM_RENDERING_MODE` set only in its own environment.
- **Classic menu entry** (registered by default): a plain `HKCU\Software\Classes\exefile\shell` verb that calls `GpuLauncher.exe`. Shows under "Show more options" for `.exe` files and, thanks to Explorer's shortcut-verb merging, for shortcuts pointing at one too.
- **[`GpuContextMenu/`](GpuContextMenu/GpuContextMenuHandler.cs)** (opt-in, `-Win11Menu`): a COM server (`GpuContextMenuHandler.exe`) implementing `IExplorerCommand`, registered through a sparse-package manifest ([`AppxManifest.xml`](GpuContextMenu/AppxManifest.xml)) for `.exe` files and shortcuts. Shows the same entry directly in the new Windows 11 top-level context menu.

The COM interfaces (`IShellItem`, `IShellItemArray`, `IExplorerCommand`) are taken verbatim from the real .NET Framework source and the well-tested [Vanara P/Invoke library](https://github.com/dahall/Vanara) to rule out COM-vtable-ordering bugs.

## Requirements

- Windows 10/11 laptop with an NVIDIA Optimus hybrid GPU.
- Windows Developer Mode, only needed for the Windows 11 top-level menu entry (`-Win11Menu`). It lets the sparse package register without a code-signing certificate. The classic menu entry needs nothing extra.

## Build & install

```powershell
.\build.ps1 build                  # compile both executables
.\build.ps1 register               # register the classic menu entry (no Developer Mode needed)
.\build.ps1 register -Win11Menu    # also register the Windows 11 top-level menu entry (needs Developer Mode)
.\build.ps1 unregister             # remove both entries again
.\build.ps1 clean                  # delete compiled .exe files
.\build.ps1 all -Win11Menu         # build + register both entries
```

## Sources

- [NVIDIA KB: "Run with graphics processor" missing from context menu](https://nvidia.custhelp.com/app/answers/detail/a_id/5035/~/run-with-graphics-processor-missing-from-context-menu:-change-in-process-of)
- [NVIDIA Optimus: rendering decision hierarchy](https://archive.docs.nvidia.com/gameworks/content/technologies/desktop/optimus.htm)
- [NVIDIA/nvapi: NvApiDriverSettings.h](https://github.com/NVIDIA/nvapi/blob/main/NvApiDriverSettings.h)
- [Steam report confirming SHIM_MCCOMPAT child-process behavior](https://steamcommunity.com/groups/SteamClientBeta/discussions/0/154644787621730542/)
- [microsoft/vscode issue #127365](https://github.com/microsoft/vscode/issues/127365): official Microsoft confirmation on the new context menu's requirements
- [Grant package identity by packaging with external location manually](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/grant-identity-to-nonpackaged-apps) and [Add-AppxPackage reference](https://learn.microsoft.com/en-us/powershell/module/appx/add-appxpackage)
- [desktop4:FileExplorerContextMenus](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-desktop4-fileexplorercontextmenus), [desktop4:ItemType](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-desktop4-itemtype), [desktop4:Verb](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-desktop4-verb), [com:ExeServer](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-com-exeserver), [com:Class](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-com-exeserver-class)
- [dahall/Vanara: ShObjIdl.IExplorerCommand.cs](https://github.com/dahall/Vanara/blob/master/PInvoke/Shell32/ShObjIdl.IExplorerCommand.cs) and [.NET Framework source: FileDialog_Vista_Interop.cs](https://www.dotnetframework.org/default.aspx/4@0/4@0/DEVDIV_TFS/Dev10/Releases/RTMRel/ndp/fx/src/WinForms/Managed/System/WinForms/FileDialog_Vista_Interop@cs/1305376/FileDialog_Vista_Interop@cs)
