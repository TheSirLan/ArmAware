# Security policy

## Reporting a vulnerability

Please report security problems privately through [GitHub private vulnerability reporting](https://github.com/TheSirLan/ArmAware/security/advisories/new), not in a public issue.

Include the ArmAware version, Windows version, steps to reproduce and the impact you expect. Redact personal paths and do not attach a full application inventory.

Reports are acknowledged as soon as possible. Fixes are released through the Microsoft Store and this repository.

## Supported versions

Only the latest release receives security fixes.

## Scope

ArmAware inspects program files without loading or running them, and only runs Winget commands after the user confirms them in a visible terminal. Issues where crafted registry entries, package manifests, program files or catalog data could cause code execution, command injection, path traversal or a misleading switch command are in scope.
