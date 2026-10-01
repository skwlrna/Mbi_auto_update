# User ZIP baseline for V2.0.2

Source: C:/Users/insub/Mbi_auto_update.zip
SHA256: 0a509dc61084f9260065a84a01f3be10838d41f4618191c7e516669f8b225b0d
Size: 352714967 bytes
Archive version constants: V0.1.96 / 0.1.96.0

Production C# files and regression harnesses differing from main were restored from this archive. ZIP-only production files were added. Existing fishing, dungeon, abyss, updater runtime and unrelated files were compared and retained. Archive .git, bin, obj and test-build files (including user settings/logs) are excluded from source and releases.

Intentional differences from the ZIP: V2.0.2 version constants, current read-only filtered get_gatherable_items and diagnostic button/tests, current updater regression mock fixes, release and CI workflows, release notes. CLI action commands remain blocked for every mode as in the ZIP. UI facility tabs, facility-filtered recipes, arrowless quantity input, material resolution and saved-session progress remain the ZIP implementation.
Archive test-build/FishingAutomation.exe: FileVersion 0.1.96.0, ProductVersion 0.1.96+3199b51b7b511a6d1c3ce9fb7ed98858a129d5e3. Original executable was inspected only, not repackaged or run.
