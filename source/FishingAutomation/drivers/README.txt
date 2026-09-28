Interception is REQUIRED by default in v9.

Run the package-root file:
  1_INSTALL_INTERCEPTION.cmd

It downloads the official Interception v1.0.1 package, runs the official command-line driver installer as Administrator, and copies library\x64\interception.dll into the application project.

REBOOT Windows after installation.

v9 does NOT silently fall back to SendInput in Interception mode.
InterceptionKeyboardDevice=0 means auto-detect the first valid keyboard exposed by the driver.
If multiple keyboards are present, fishing.log lists device numbers and hardware IDs so the config can be pinned to 1..10.
