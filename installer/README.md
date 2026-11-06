# Mabi Auto Windows setup and recovery

The Windows Setup is built from the existing **SHA256-verified Windows Lite GitHub Release ZIP**. It does not alter the running macro engine, auto-update policy or ZIP release.

## Before formatting Windows

Close Mabi Auto and all active jobs. Run BACKUP_SETTINGS.cmd from the installed folder. Copy the generated MabiAuto_Backup_*.zip from Documents to an external drive. Keep it private; it contains character, account and work information.

## After formatting Windows

1. Install Windows and Mabinogi Mobile. Run MabiAuto_Setup_V*.exe, allowing administrative installation.
2. The Setup contains the existing self-contained .NET 8 app, OpenCV dependencies, Watchdog, screenshots/templates, update and rollback scripts.
3. Run the optional Windows prerequisite check. The game CLI must separately exist at C:\Nexon\MabinogiMobile\MabinogiMobile_CLI.exe; the Mabi Auto GitHub release does not ship that executable. Install/check Korean Windows OCR if necessary.
4. If input driver is missing, explicitly opt in to InstallInterception.ps1, type YES, and reboot. This installs a kernel input driver only with user permission.
5. Run RESTORE_SETTINGS.cmd, enter the previously saved ZIP path, and confirm by typing RESTORE. The restore first creates a safety backup of any existing settings.
6. Enter Telegram Bot Token/Chat ID and GitHub error-upload PAT in the app again. Verify 800x1000 game window geometry, keyboard/mouse device selection, OCR and game connections before starting unattended automation.

Restoration does not bypass crafting/altering restart or duplicate-input guards. Unfinished jobs must be revalidated in-game.

## Privacy and scope

The backup includes config, mutable images and local crafting/altering restart journals; it excludes credentials, the DPAPI-encrypted GitHub PAT, executable code, logs and diagnostic screenshots. The ZIP is **not encrypted**. Do not upload it to a public repository.

Support scripts are placed in the dedicated support directory outside the ZIP updater's list of replaceable paths, so subsequent normal updates should not remove the backup and restore tools. The setup is separate from and does not replace the normal ZIP updater.

## CI release

A Windows GitHub Actions workflow downloads and verifies the existing release ZIP's SHA256, builds the Inno Setup installer, performs a silent-install smoke test and backup/restore secret-redaction check, and uploads the result as a workflow artifact. On main it then attaches the EXE to that existing GitHub Release.

Python, Git, Inno Setup and the .NET SDK are builder tools, not requirements for a user running the published executable.
