# Batchwork

Batchwork is a local-first Windows batch renamer. It previews every filename change, blocks collisions before execution, and supports undoing the last rename plan in the current session.

Version 1.0 executes each batch as a two-phase transaction. File swaps and case-only renames are supported, every source and destination is preflighted, and a failed operation rolls staged files back to their original names instead of leaving a half-renamed batch.

Version 1.1 persists the last rename transaction in an atomic local journal. Undo survives an application restart, prepared-but-unapplied operations are ignored safely, and mixed or damaged journal states disable automatic undo instead of guessing.

Version 1.2 clears stale rename actions whenever a new preview is invalid, preventing a failed preview from leaving an older executable plan available to run. Regression tests cover a valid preview followed by an invalid one. Windows release builds run the engine tests, publish a self-contained executable, and smoke-test its launch before attaching the ZIP and SHA-256 file to the tagged release.

MIT licensed.

## Name change

Batchwork replaces OpenRename. New builds use Batchwork.exe and Batchwork.csproj. Existing releases remain available under their original names.

Existing settings and data locations retain the OpenRename folder name for compatibility. Keep these folders when upgrading; no automatic move or data format change is required.
