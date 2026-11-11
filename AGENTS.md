# AGENTS.md

Anarres is a spell and DBC editor for AzerothCore (WoW 3.3.5a), written in C# on .NET 10 with an Avalonia user
interface. It reads and writes .dbc and MPQ files and keeps imported data in SQLite or MySQL.

## Agent rules

- Follow `CODING_STANDARDS.md`. Formatting follows `.editorconfig`: UTF-8, LF, max 120 columns, trailing newline,
  no trailing whitespace; 4-space indent (tabs forbidden), 2-space for JSON/YAML/sh/XML/csproj/axaml.
- Comments are one line, and only where the code isn't self-explanatory.
- Keep code portable: no OS-specific APIs outside `OperatingSystem` checks, `System.IO` for paths, exact file-name
  case.
- Do not add dependencies, commit or push unless explicitly asked.
- `Anarres.Core` must not depend on Avalonia or any UI code. Every change to DBC, MPQ or store behaviour needs a test
  in `tests/Anarres.Core.Tests`.
- Never add an operation that can lose data without a confirmation in the UI.
- Commit messages follow `.git_commit_template.txt`. Co-authors go in `Co-authored-by:` lines; no session links.
- Anarres is licensed under the AGPL-3.0-or-later (`LICENSE`). Code taken from another project must credit its
  author and have a compatible license; ask first. Stoneharry's WoW Spell Editor and WDBX Editor have no license:
  study their layout and behaviour, never copy their code. The 3.3.5a client decompilation may be read for how the
  client sees the data and for DBC layouts; never copy its code.
- Generated files (`src/Anarres.Core/Definitions/*.json`) are rebuilt with the scripts in `tools/`, not edited.
- Planning docs go in `.agents/plans/<task-slug>/` (gitignored), named `<task-slug>.<TYPE>.md`
  (`PLAN`, `REQUIREMENTS`, `ANALYSIS`, …).

## Mandatory reading per task

- Writing or modifying C# → `CODING_STANDARDS.md`
- Reusing outside code → `LICENSE` and `CONTRIBUTING.md`

## Persisting lessons

When a user correction reveals a rule that generalizes, offer to add it to this file or `CODING_STANDARDS.md`.
