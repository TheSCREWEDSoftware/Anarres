# Working with Anarres

The spell and other DBC data of WoW 3.3.5a lives in `.dbc` files. The client reads them from its MPQ archives
(`DBFilesClient\Spell.dbc`); the worldserver reads its own copy from its `dbc` folder. A change usually needs both
updated, so Anarres keeps the data in one place while you edit it and writes both copies when you're done.

## Why a database

Anarres keeps imported tables in a database, SQLite or MySQL, and writes `.dbc` files only when you export or
apply. This is the approach of Stoneharry's DBC editing workflow:

- A `.dbc` is a packed binary table: records of fixed size and one shared block of text that every row points
  into. Changing one string moves the others, so tools that edit the file in place are easy to break, and two
  people can't work on one file.
- In SQL the same data can be searched, compared and changed in bulk with any SQL tool (for example every spell
  that costs mana), shared by several people on MySQL, and saved spell by spell instead of rewriting a 49 MB file.
- The clean files stay untouched, so a mistake is undone by importing again, and "what did I change" is a
  comparison with the clean file (the SQL of changed spells, and what Apply puts in `acore_world`).
- The `.dbc` files written from the database are rebuilt in full each time, with a fresh text block, which is how
  the client and the worldserver expect them. An unchanged table comes out byte for byte the same as it went in.

The DBC editor can still open a `.dbc` straight from disk, like WDBX Editor, for a quick look or a one-off fix.

## 1. Set up the project

Open **File > Project settings**.

- **Where the data is kept.** A SQLite project file needs nothing else. For MySQL or MariaDB, give the server,
  user and database; Anarres creates the database when it doesn't exist. The defaults are AzerothCore's own
  (`localhost:3306`, user `acore`, password `acore`). **Test connection** checks it.
  - *DBC names* make tables called `Spell`, `SpellIcon` and so on, in a database of their own:
    `acore_dbc_extracted` by default, created on first use.
  - *AzerothCore names* use `acore_world`'s `spell_dbc` and the other `*_dbc` tables. The worldserver loads those
    rows over the `.dbc` files at start-up, so keep only the rows you change there.
- **Clean DBC folder.** Unmodified 3.3.5a files, such as the `dbc` folder of AzerothCore's client data.
- **Server data folder**: the worldserver's `DataDir` (the folder with `dbc`, `maps` and `vmaps`) or its `dbc`
  folder. The window shows where files will go.
- **WoW client folder**: the folder with `Wow.exe`, or its `Data` folder, and the **Locale** (such as `enUS`): to
  import from the client's MPQs, show icons, and put the patch in.
- **Export folder**: where exported `.dbc` files are written, under `DBFilesClient`.
- **Patch name**: `patch-A.MPQ` by default. The client loads `patch-2` to `patch-9`, then `patch-A` to `patch-Z`;
  later ones win, so pick one after any other custom patch you use.

## 2. Import

**Import** in the toolbar lists the `.dbc` files of a folder, or of the client's MPQs. **Spell editor set** ticks
Spell.dbc and the tables the spell editor shows values from (cast times, durations, ranges, radii, icons,
description variables). Importing replaces the project's copy of each ticked table.

Import once; the project keeps the data from then on.

## 3. Edit

- **Undo and redo** (Edit menu, Ctrl+Z and Ctrl+Y outside a text box) work in both editors, back to when the
  table was loaded.
- **Spell Editor**: search by ID or name, or by field: `effect:6` (Apply Aura), `aura:3`, `target:22`,
  `trigger:12345`, `family:3`, `icon:1`, `visual:`, `misc:`, `category:`, `mechanic:`, `school:`.
  **New** makes an empty spell with the next free ID; **Copy** duplicates the selected one. **Save** (Ctrl+S)
  writes changed spells to the project.
- **DBC Editor**: pick a table, or **Open .dbc file** to edit a file without importing it. Double-click or press
  Enter to edit a cell, type to replace it, Tab and Enter to move on, Ctrl+C to copy. The filter takes an ID, text
  found in any text column, `Column=value` or `Column~part`.

Unsaved changes are marked in the status line; Anarres asks before closing or importing over them.

Shortcuts: Ctrl+S saves, Ctrl+Z and Ctrl+Y undo and redo, Ctrl+F goes to the search box or the filter, and
Ctrl+, opens the options.

## 4. Export and deploy

**Export** writes the ticked tables as `.dbc` files, then, when ticked:

- copies them to the server's `dbc` folder: restart the worldserver;
- packs them into the patch MPQ in the WoW Data folder: restart the client (and clear its `Cache` folder when spell
  text looks out of date).

For a few spells, the spell editor's **SQL...** writes the shown spell, or every spell that differs from the clean
Spell.dbc, for `spell_dbc`; **Send to world DB...** writes a spell straight into `acore_world.spell_dbc`. The client
still needs the patch to know the spells.

With *AzerothCore names*, the world tables hold only the rows that differ, so Export lays them over the clean
file from the Clean DBC folder or the client's MPQs; without one it refuses, rather than write a `.dbc` with only
those rows.

## Apply: one click

**Apply** (F6) saves, then puts the changes where the server and client read them, as chosen under **Apply** in
Project settings:

- the tables ticked in the last export (Spell.dbc to start with) are written and copied to the server's `dbc`
  folder, keeping the server's own copy of each file the first time (in the export folder's `server-backup`);
- the client patch is built in the WoW `Data` folder;
- optionally, the client's `Cache` folder is emptied, so WoW doesn't show old spell text;
- optionally, rows that differ from the clean files go into `acore_world`'s `_dbc` tables.

It lists what it will do first (an option turns that off), and says when the worldserver or WoW is running and
needs a restart.

## Automating it

`anarres-cli` uses the same settings as the app (or the options you give it), so the steps can run from a script
or a build server, like Stoneharry's Jenkins setup:

```
anarres-cli import-client "C:\WoW\Data" --locale enUS --only Spell
anarres-cli export --only Spell,SpellIcon --server --patch
anarres-cli export --mysql localhost:3306/acore_dbc_extracted --user acore --password acore --server --patch
anarres-cli sql Spell --ids 90000 --out spell_90000.sql
anarres-cli sql Spell --changed --source "C:\client-data\dbc" --out changed.sql
anarres-cli apply
anarres-cli list
```

`anarres-cli help` lists every command and option.

## Layouts

Each table's layout (column names and types) comes from, in order:

1. a `.json` file in the **Extra definitions** folder (saved from the DBC editor's **Columns** window);
2. AzerothCore's `*_dbc` tables, for the 110 files the worldserver loads;
3. the client's record layouts, for 130 client-only files such as `SpellIcon.dbc` and `SpellVisualKit.dbc`;
4. a guess from the data, with columns named `Field1`, `Field2`... A guessed layout is kept in the project, so it
   can be renamed later.
