# Anarres Coding Standards

These standards keep the code consistent, readable and portable across Windows and Linux. They follow the same rules
as the other TheSCREWEDSoftware projects, adapted to C#.

Pull requests that don't follow these standards will be asked to change before they are merged.

## 1. File format (all files)

| Rule | Value |
|---|---|
| Encoding | UTF-8 (no BOM) |
| Line endings | LF (`\n`) |
| Final newline | Required |
| Trailing whitespace | Not allowed |
| Indentation | 4 spaces; tabs are forbidden |
| Indentation for JSON, YAML, shell scripts, XML, `.csproj` and `.axaml` | 2 spaces |
| Maximum line length | 120 columns |

Exceptions: `.bat` and `.cmd` files use CRLF, because Windows requires it. `.sln` files are written by Visual Studio
with a BOM, CRLF and tabs; leave them that way.

These rules are enforced by `.editorconfig` (formatting in your editor) and `.gitattributes` (line endings in Git).

## 2. C# formatting

### Whitespace
No spaces just inside parentheses: `if (ready)`, not `if( ready )` or `if ( ready )`.

### Braces
Opening and closing braces go on their own lines. A single-line body may omit the braces.

```csharp
if (status.HasChanges)
{
    StageAll();
    Refresh();
}
else
    SetStatus("Nothing to commit.");
```

### Namespaces
Use file-scoped namespaces: `namespace Anarres.Core.Dbc;`.

### Switch statements
Always include a `default` case, even if it only contains `break`. A case with more than one statement before its
`break` or `return` gets its own braces:

```csharp
switch (action)
{
    case "Remove":
    {
        commands.Remove(command);
        _settings.Save();
        break;
    }
    default:
        break;
}
```

C# doesn't fall through from one case to the next. If a case should continue into another, use `goto case` with a
comment saying why.

### Declarations
- One declaration per line: `int added = 0;` then `int removed = 0;`, not `int added = 0, removed = 0;`.
- Modifiers in this order: access, `static`, then `readonly` or `const`: `private static readonly HttpClient Http`.

### Types
Write the type instead of `var`: `List<FileChange> changes = new List<FileChange>();`.

## 3. Naming

| What | Style | Example |
|---|---|---|
| Classes, structs, enums, interfaces | UpperCamelCase | `DbcTable`, `DbcColumnType` |
| Methods, properties, events | UpperCamelCase | `GetStatusAsync()`, `IsUnborn` |
| Private fields | `_` + lowerCamelCase | `_repository`, `_isBusy` |
| Private static readonly fields | UpperCamelCase | `QuietCommands` |
| Parameters and local variables | lowerCamelCase | `targetFolder`, `result` |
| Constants | UPPER_SNAKE_CASE | `MAX_LOG_CHARACTERS` |
| Async methods | `Async` suffix | `PushAsync()` |
| Files | Match the main type | `DbcTable.cs` |

- No Hungarian notation (`iCount`, `strName`). The `I` prefix on interfaces (`ITokenStore`) is the .NET convention
  and is kept.
- Float literals use the `f` suffix: `0.25f`.

## 4. Constants

- **No magic numbers.** Give every meaningful number a name.
- Use `const`, `static readonly` or `enum`.
- Every enum has a name, and each enum holds one kind of value; don't mix unrelated values in one enum.
- Give an enum an explicit underlying type only when its values are stored or sent somewhere as numbers.

## 5. Usings

- One block at the top of the file, no blank lines inside it.
- `System` namespaces first, then the rest in alphabetical order.
- Only what the file uses.

## 6. Data files and databases

- Reading and writing .dbc and MPQ files stays in `Anarres.Core` (`Dbc`, `Mpq`), with a test for every format change.
- SQL goes through `SqlDbcStore`: values are always parameters, identifiers always quoted by the store's dialect.
- Never overwrite user data without asking first: a table import replaces the project's copy, so the UI says so; a
  patch MPQ is written to a temporary file and moved into place only when it's complete.
- DBC layouts come from AzerothCore (`tools/generate-definitions.py`) or from client record layouts
  (`tools/generate-client-definitions.py`); don't hand-edit the generated JSON.

## 7. Layers

- `Anarres.Core` has no UI code and no UI package references. Everything about DBC, MPQ and SQL is testable there.
- `Anarres.App` holds the Avalonia UI and calls `Anarres.Core`.
- `Anarres.Cli` is the same work without a window, for scripts and build servers.

## 8. Portability

The app must build and run on Windows and Linux.

- Use `Path.Combine` and `System.IO` APIs for paths. Never hardcode paths or use `\` as a separator.
- File names are case-sensitive on Linux. Always match the exact case.
- Keep OS-specific code behind `OperatingSystem.IsWindows()` checks, and only where unavoidable.

## 9. Comments

- Comments are one line, and only where the code isn't routine or self-explanatory. Explain *why*, not *what*.
- Put a comment on the line above the code, or at the end of the same line.
- Don't put links in code comments. Put them in the pull request description, where Git history keeps them.

## 10. Text shown to the user

- Write it as plain sentences that say what happened and what to do next.
- No emoji. Use words, or plain markers such as `[P]` for pinned.
- Keep user-facing text in the App layer (or returned from Core as messages), so it can be collected for
  translation later.

## 11. Tests

- Core features come with tests in `tests/Anarres.Core.Tests`, using temporary folders and files.
- Tests that need real client files read them from the folder in `ANARRES_TEST_DBC`, and pass without it.
- Tests never reach a network or a real database server.

## 12. Checking your work

`check-standards.bat` (Windows) and `check-standards.sh` (Linux) check files and commit messages against these
rules and the commit template. Run one without options for a menu, or pass an option:

| Option | Checks |
|---|---|
| `--uncommitted` | Changed and new files that aren't committed yet (the default) |
| `--staged` | What is about to be committed |
| `--last` | The last commit: its message and the files it changed |
| `--unpushed` | Commits not pushed yet |
| `--history` | Every commit |
| `--since-commit HASH` | Commits after a commit |
| `--since-date DATE` | Commits since a date, such as `2026-11-01` or `"2 weeks ago"` |
| `--all` | Every tracked file as it is now |
| `--install-hooks` | Checks every commit before it is made (skip once with `git commit --no-verify`) |

The tests also check that every file in the repository follows these rules. Rules that need judgement, such as
naming constants instead of using magic numbers, are left to review.
