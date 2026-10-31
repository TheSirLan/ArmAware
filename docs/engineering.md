# Technical decisions

Decisions favour explicit uncertainty over unsupported inference.

## Inventory and privileges

HKLM/HKCU uninstall keys are read in both RegistryViews. Registry view is provenance, never executable architecture. DisplayIcon parsing only yields absolute `.exe` candidates; no registry command string is executed. UninstallString is retained as metadata, not parsed into an execution command. At most 64 top-level file names and 12 executable candidates are considered per installation root, and directory junction roots and broad drive/Windows roots are rejected. A lone candidate is treated as the primary executable only when its ProductName matches the display name after version numbers, bracketed labels, punctuation and case are normalized (the product name may also be the start or end of the display name). Windows Installer products without an install folder fall back to their component records. If the primary is still ambiguous, a one-off snapshot of running program paths inside the install folder can identify it. When no primary is known, two or more high-confidence candidates with the same architecture give an inferred result at medium confidence. These heuristics intentionally miss some valid executables.

MSIX/AppX inventory uses [PackageManager.FindPackagesForUser](https://learn.microsoft.com/en-us/uwp/api/windows.management.deployment.packagemanager.findpackagesforuser). Framework/resource packages are excluded. The current user's manifest Application/Executable attributes identify the declared app entries; resolved paths must stay beneath the package root. Package architecture describes packaging, while executable PE metadata describes an image. Framework dependencies and all users' protected inventories are outside scope. Ordinary reads require no administrator request; access failures are isolated.

Deduplication merges matching registry subkey identities only when scope, installation location and version also agree, or matching package-family/version records. It retains all source strings. Equal display names alone never merge records. Correlation between separate registry and MSIX records without a shared identity is intentionally left unresolved.

## PE and managed code

The [Microsoft PE specification](https://learn.microsoft.com/en-us/windows/win32/debug/pe-format) defines COFF machine values. We use `System.Reflection.PortableExecutable.PEReader` without loading an image into the runtime. x86, AMD64, ARM64, ARM64EC and ARM64X machine values are recognized. Unknown, corrupt and inaccessible files return explicit uncertainty.

Final linked hybrid images may preserve legacy-looking machine fields. Microsoft describes the x64/ARM64EC interoperability in [ARM64EC](https://learn.microsoft.com/en-us/windows/arm/arm64ec) and dual-image format in [ARM64X](https://learn.microsoft.com/en-us/windows/arm/arm64x-build). The inspector checks the 64-bit load configuration's CHPEMetadataPointer at byte offset 200 when the directory and declared structure size are large enough. Nonzero CHPE metadata with an AMD64 header indicates ARM64EC; with ARM64 it indicates ARM64X. This matches the hybrid metadata mechanisms described in [LLVM's COFF implementation](https://llvm.org/doxygen/COFFObjectFile_8cpp_source.html). Directory-to-file offset conversion is bounded to raw section data. Hybrid contents are not fully decoded and both formats receive MixedArchitecture treatment, never a blanket emulation recommendation.

For managed I386 images, ILOnly without Requires32Bit is reported as AnyCPU, regardless of Prefer32Bit. Mixed-mode CLR images are Unknown; IL-only required-32-bit images remain x86. Runtime/launcher selection, ReadyToRun complexities, DLL dependencies and live process architecture are not inferred from the package or installer. Running processes are never inspected beyond the path snapshot described above.

## Winget integration

Online scans use its COM/WinRT API ([IDL](https://github.com/microsoft/winget-cli/blob/master/src/Microsoft.Management.Deployment/PackageManager.idl)) through `Microsoft.WindowsPackageManager.ComInterop` 1.29.380, the same data behind `winget list` and `winget upgrade`. Human-oriented `winget search`/`list` tables are never parsed.

`WingetCatalogResolver` connects once per scan to a composite catalog of local installed packages plus the community `winget` source (`Microsoft.Winget.Source_8wekyb3d8bbwe`) and enumerates installed matches (indexed access; the projected collections cannot be enumerated). For each installed package that winget links to that source it records the package ID, installed version, whether that exact version is listed, the installer `PackageVersionInfo.GetApplicableInstaller` would choose on this machine for the installed and latest versions (architecture and scope), and `IsUpdateAvailable`. Versions are compared exactly after trimming trailing ".0" segments. Records are matched to ArmAware inventory only by exact package family name (MSIX) or uninstall key/product code (ARP); ambiguous matches are left unresolved. Results are cached for seven days in `winget-catalog-v1.json` and reused by offline scans, which never contact a source. Connection failures or a 60-second timeout produce one scan-wide note.

`Arm64AvailabilityService` makes no network requests. A recommendation requires the applicable installer for the *exact installed version* to be ARM64, a compatible scope (`System` maps to machine installs), a confirmed primary x86/x64 executable and an ARM64 OS. A newer version with an ARM64 installer is reported as evidence but is never treated as a same-version recommendation. Applicable-installer selection does not prove every payload binary is native; dependency, elevation and migration details still need review. The UI links only the fixed official winget-pkgs repository page for the exact version and generates `winget uninstall`, `winget install --architecture arm64` and `winget upgrade` commands from validated IDs and versions. They can be copied, or run in a visible terminal after the user confirms the exact script.

Activation needs `Microsoft.Management.Deployment.winmd` beside the executable; without it WinRT falls back to system activation, which fails with `0x80073D54` ("no package identity") in an unpackaged app. `ArmAware.Windows` copies the winmd and activation shim. `ManifestRepository` (GitHub manifest reads) is used only by the `package` CLI diagnostic.

## Presentation and deployment

CommunityToolkit ObservableObject and RelayCommand implement MVVM. Explicit observable property setters avoid this SDK/compiler combination's partial-property generator incompatibility without suppressing warnings. View code is limited to native window, dialog, clipboard, file-picker, terminal launch, animation and URI operations. Scan options are snapshotted operationally by disabling settings changes during scans; cancellation leaves already-completed findings in view. Only completed reports can be exported through the desktop picker.

See [packaging](packaging.md). No performance improvement for a specific app is asserted; the report explains architecture evidence only.

