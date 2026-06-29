# Installer Maintenance

This runbook keeps KingdomMod MSI behavior consistent across releases. Read it
before changing `installer/Product.wxs`, either MSI-time PowerShell script, or
the MSI packaging flow.

## Lifecycle

Fresh install:

1. The MSI resolves the Kingdom Two Crowns folder and installs the support
   payload into `.kingdommod-installer`.
2. If MelonLoader is not already present, the MSI extracts the bundled
   MelonLoader archive and records `OwnsMelonLoader=1`.
3. If the installer adds a new Defender exclusion, it records
   `DefenderExclusionAdded=1`.
4. The installer downloads setup dependencies into `.kingdommod-cache`, builds
   patched Cpp2IL and KingdomMod locally, and copies `KingdomMod*.dll` files
   into `Mods`.

Upgrade or same-version reinstall:

1. The MSI reads previous state from `HKLM\Software\KingdomMod`.
2. Legacy installs are also checked for
   `.kingdommod-installer\owns-melonloader` before the old payload is removed.
3. Existing MSI-owned MelonLoader remains MSI-owned; preexisting user
   MelonLoader remains user-owned.
4. Cached setup downloads in `.kingdommod-cache` survive the replaced support
   payload.

Uninstall:

1. The uninstall custom action stops the game process if it is still running.
2. It removes `KingdomMod*.dll`, `.kingdommod-installer`,
   `.kingdommod-cache`, and installer registry state.
3. It removes the Defender exclusion only when KingdomMod recorded that it
   added that exclusion.
4. It removes MSI-owned MelonLoader only when no foreign content remains under
   `Mods`, `Plugins`, or `UserLibs`.

## Ownership Matrix

| Item | Fresh MSI install | Preexisting user install | Uninstall behavior |
|---|---|---|---|
| `Mods\KingdomMod*.dll` | MSI-owned | MSI-owned | Always removed |
| `.kingdommod-installer` | MSI-owned | MSI-owned | Always removed |
| `.kingdommod-cache` | MSI-owned cache | MSI-owned cache | Always removed on uninstall, kept across upgrades |
| `MelonLoader` and archive root files | MSI-owned only if absent before install | User-owned | Removed only if MSI-owned and no foreign content exists |
| `Mods`, `Plugins`, `UserLibs` foreign content | User-owned | User-owned | Preserved; also protects MelonLoader from removal |
| Defender exclusion | MSI-owned only if added by KingdomMod | User-owned if already present | Removed only if `DefenderExclusionAdded=1` |
| `UserData`, MelonPreferences, logs, dumps, backups | User-generated | User-generated | Preserved |
| Generated interop and UnityDependencies | MelonLoader-generated | MelonLoader-generated | Removed only as part of owned MelonLoader removal |

## Release Checklist

Before publishing a release:

1. Run parser checks for installer scripts:

   ```powershell
   $files = @(
     'installer/scripts/BuildAndInstallKingdomMod.ps1',
     'installer/scripts/UninstallMelonLoader.ps1',
     'tools/build-msi.ps1',
     'tools/test-installer-cleanup.ps1'
   )
   foreach ($file in $files) {
     $tokens = $null
     $errors = $null
     [System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path $file), [ref]$tokens, [ref]$errors) | Out-Null
     if ($errors.Count) { throw "$file has PowerShell parse errors: $($errors | ForEach-Object Message)" }
   }
   ```

2. Run the cleanup harness:

   ```powershell
   powershell -ExecutionPolicy Bypass -File tools\test-installer-cleanup.ps1
   ```

3. Build the MSI locally:

   ```powershell
   powershell -ExecutionPolicy Bypass -File tools\build-msi.ps1 -Version <next-version>
   ```

4. Smoke test on a disposable Kingdom Two Crowns install or VM:
   - Fresh install, launch once, then uninstall.
   - Upgrade from the latest public release, then uninstall.
   - Reinstall the same MSI version and confirm Apps & Features shows one
     KingdomMod entry.
   - Install with preexisting MelonLoader plus a foreign mod, then uninstall
     and confirm MelonLoader and the foreign mod remain.

5. Check logs when anything fails:
   - `%TEMP%\KingdomModMsi-BuildAndInstall.log`
   - `%TEMP%\KingdomModMsi-Uninstall.log`
   - `<KTC>\MelonLoader\Latest.log`

6. Before sharing the GitHub Release, write release notes that describe the
   actual installer/runtime/user-visible changes. Do not publish a body that
   only repeats the version number.

## Troubleshooting Notes

- Install failures should name the failed stage in
  `KingdomModMsi-BuildAndInstall.log`.
- Uninstall should not fail just because Defender cannot remove an exclusion;
  it logs the failure and continues cleanup.
- If a user reports MelonLoader left behind, first check whether `Mods`,
  `Plugins`, or `UserLibs` contained foreign content.
- If a user reports MelonLoader removed unexpectedly, check whether
  `OwnsMelonLoader=1` was carried forward from a previous release or legacy
  `owns-melonloader` marker.
- If same-version reinstalls create duplicate Apps & Features entries, verify
  `AllowSameVersionUpgrades="yes"` is still present in `Product.wxs`.
