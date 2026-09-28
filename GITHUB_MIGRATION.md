# GitHub migration to skwlrna

This repository-ready bundle is based on Astra V0.1.75 Stability Source.

## Migration build
- App version: V0.1.76
- Auto-update repository: `skwlrna/Mbi_auto_update`
- Runtime log repository to create separately: `skwlrna/Mbi_Auto_log`

## Important
Existing V0.1.75 clients still point to the suspended `insubi/Mbi_auto_update` repository.
Install V0.1.76 manually once. After that, future versions can update automatically from the new repository.

The runtime error uploader stores its target repository/token in the local app settings. After creating the new log repository, set it to `skwlrna/Mbi_Auto_log` and create a fine-grained PAT from the new GitHub account.
