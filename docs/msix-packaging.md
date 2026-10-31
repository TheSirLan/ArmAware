# ARM64 MSIX packaging

The package uses the reserved Partner Center identity in `packaging/StoreIdentity.json` and `packaging/AppxManifest.xml`:

- Name: `JoshuaClarkeDevelopment.ArmAware`
- Publisher: `CN=90CEA9B2-A97C-4061-82A2-FD188C5281A4`
- Publisher display name: `Joshua Clarke Development`
- Expected family: `JoshuaClarkeDevelopment.ArmAware_2k8rpk13m07pr`
- Store ID: `9N84PT3981Z1`

## Build

From the repository root, on Windows with PowerShell 7 and the SDK specified by `global.json`:

```powershell
./scripts/Build-Msix.ps1 -Version 1.0.1.0
```

The script publishes only `win-arm64`, verifies the ARM64 PE entry point and required WinUI/Winget resources, generates package logo sizes from copies of the existing artwork, indexes package resources, and packs with MakeAppx semantic validation enabled. Each invocation gets a fresh directory under `artifacts/msix/`; previous builds are retained. It prints the resulting MSIX path and SHA256. It discovers host-native SDK tools from the restored Windows SDK build-tools package; `-SdkToolDirectory` can override the tool directory.

Keep a monotonically increasing four-part version; revision must be zero for Store submissions. The default Store candidate is `1.0.1.0`. Check Partner Center for previously uploaded versions before uploading. CI also builds an unsigned ARM64 MSIX; manual runs accept a package version. CI does not submit or publish it.

The application remains a self-contained WinUI desktop program. The package runs it at medium integrity as a full-trust desktop app with only `runFullTrust` declared. This preserves desktop inventory and user-confirmed terminal behavior. The `dotnet run` and folder-publish workflows remain available.

## Signing and installation

The output is **unsigned for Microsoft Store submission**. Store distribution signs accepted packages; Azure signing is not required for this route. Do not distribute this unsigned build as a normal sideload installer.

Local installation with the real publisher requires an appropriately signed package and certificate trust, or a developer registration of the staged manifest on a machine already configured for development. The build does not install certificates, change Developer Mode, register an app, or submit anything. `Add-AppxPackage -AllowUnsigned` is not a workaround for this Store identity: Windows requires a special development publisher OID for that mode, which changes the package identity.

On a development test machine configured for loose registration, register the printed **staged** manifest using Windows PowerShell, then launch its app identity from Start:

```powershell
Add-AppxPackage -Register '<staged-payload-path>\AppxManifest.xml'
```

Keep that payload directory in place while registered. This checks packaged identity, but does not replace testing installation of a signed MSIX or a private Store flight. Windows rejects unsigned registration with `0x80073CFF` unless the machine allows it; do not change trust settings just to make it pass.

## Before Store submission

1. Install the signed package or a private Store flight on clean ARM64 Windows 11; confirm the installed family matches the expected value above. Run Windows App Certification Kit against that exact package.
2. Exercise startup, offline scan, MSIX/registry inventory, evidence selection, filters, cancellation, preferences, export, clipboard, manifest/Store links and user-confirmed terminal flows. In a disposable test environment, verify missing Winget, offline catalogs, declined agreements and a Store-source failure still leave the other scan results usable. Do not switch a personal application's architecture as a smoke test.
3. Verify Package Manager COM activation and online identity resolution inside the package. Use a disposable fixture app to test each switch failure stage. Test upgrade to a higher MSIX version and uninstall/reinstall, including settings/cache behavior.
4. Provide public privacy and support URLs; align listing/privacy text with optional catalog queries and explicit install/uninstall actions. Explain `runFullTrust` to reviewers: the app reads desktop registry/package inventory and executable metadata, and opens reviewed commands only after user confirmation.
5. Review the listing, age rating, markets, screenshots, package version and certification results before uploading the `.msix` in Partner Center. The package is built, not certified or released.

## Microsoft documentation

- [Manual desktop MSIX manifest, resource indexing and packaging](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-manual-conversion)
- [Store package version requirements](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/package-version-numbering)
- [MSIX signing and Store signing](https://learn.microsoft.com/en-us/windows/msix/package/sign-msix-package-guide)
- [Unsigned development-package identity requirements](https://learn.microsoft.com/en-us/windows/msix/package/unsigned-package)

The build imports the compiled app PRI (including WinUI theme resources) and adds package logos in a separate indexing input, avoiding duplicate XBFs. `scripts/Test-Msix.ps1` validates the resources inside a built MSIX.
