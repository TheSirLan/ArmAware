# Microsoft Store source support

Store-installed MSIX/AppX inventory remains local, using Windows PackageManager and executable inspection. Optional online identity resolution now additionally uses the Windows Package Manager COM API's `msstore` catalog and a composite installed-package catalog. It correlates Store products to existing inventory using exact package family names or ARP product codes; ambiguous matches are left unresolved. Store product IDs are recorded separately from community Winget IDs and appear in source evidence and JSON/CSV exports.

Community Winget export and Store API resolution are independent stages. A resilient source boundary retains previously successful enrichment and adds a source-specific warning for failures. Store connection, query and COM failures are nonfatal. The Store stage has a 20-second timeout and caller cancellation propagates. Already-started native activation cannot always be interrupted immediately; it may finish in the background, without committing a timed-out result. No source agreements are accepted automatically, no authentication prompt is requested, and no package installation is performed.

Online mode can contact the configured Microsoft Store source, normally `storeedgefd.dsx.mp.microsoft.com`, in addition to the community Winget source normally hosted at `cdn.winget.microsoft.com`. Installed-package correlation can send package family names/product codes and normal request/market information to that source. Offline mode does not activate either Winget online source. The Store correlation results are not currently cached between scans. The separate CLI manifest diagnostic can contact GitHub.

Store identity alone does not verify a same-version ARM64 distribution. Store-only ARM64 availability remains explicitly unconfirmed; no architecture upgrade command is generated from Store identity alone. Community manifest recommendations remain available independently for applications that also have a verified community identity.

## Validation and limitations

The integration compiles against Microsoft's `Microsoft.WindowsPackageManager.ComInterop` 1.29.380 SDK. Its ARM64 activation shim is copied transitively to the desktop/CLI output and publish folders. The implementation follows Microsoft's [minimal COM samples](https://github.com/microsoft/winget-cli/tree/master/samples/MinimalCallers) and [API contract](https://github.com/microsoft/winget-cli/blob/master/src/Microsoft.Management.Deployment/PackageManager.idl).

Automated tests cover exact/ambiguous family matches, ARP product codes, offline behaviour, either source failing, source timeouts, user cancellation, separate export IDs and avoiding unsupported ARM64 claims.

Earlier builds failed activation with `0x80073D54` ("The process has no package identity", sometimes shown as "Interface not registered"). The cause was a missing `Microsoft.Management.Deployment.winmd` beside the executable: without it, WinRT falls back to system activation of the packaged Windows Package Manager server, which requires package identity. `ArmAware.Windows` now copies the winmd with the activation shim, and live Store correlation has been confirmed with an installed Store app. If activation still fails with a registration error (`0x80040154`, `0x80040155` or `0x80073D54`), the Store stage is skipped and logged rather than shown as a scan note; other Store failures still produce a note.

Only installed packages that Windows Package Manager links to the `msstore` catalog are correlated. This usually matches the output of `winget list --source msstore`; preinstalled Microsoft and OEM Store apps are generally not linked. Their architecture is still determined locally from package and executable metadata.

Before release, verify successful Store correlation on a machine with working COM registration and accepted Store source agreements. The UI also needs its normal interaction checks.
