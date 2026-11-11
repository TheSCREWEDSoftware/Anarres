# Working with Anarres

The spell and other DBC data of WoW 3.3.5a lives in `.dbc` files. The client reads them from its MPQ archives
(`DBFilesClient\Spell.dbc`); the worldserver reads its own copy from its `dbc` folder. A change usually needs both
updated, so Anarres keeps the data in one place while you edit it and writes both copies when you're done.

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
- **WoW Data folder** and **Locale** (such as `enUS`): to import from the client's MPQs and to put the patch in.
- **Export folder**: where exported `.dbc` files are written, under `DBFilesClient`.
- **Server dbc folder**: the worldserver's `DataDir` `dbc` folder.
- **Patch name**: `patch-A.MPQ` by default. The client loads `patch-2` to `patch-9`, then `patch-A` to `patch-Z`;
  later ones win, so pick one after any other custom patch you use.

## 2. Import

**Import** in the toolbar lists the `.dbc` files of a folder, or of the client's MPQs. **Spell editor set** ticks
Spell.dbc and the tables the spell editor shows values from (cast times, durations, ranges, radii, icons,
description variables). Importing replaces the project's copy of each ticked table.

Import once; the project keeps the data from then on.

## 3. Edit

- **Spell Editor**: search by ID or name, or by field: `effect:6` (Apply Aura), `aura:3`, `target:22`,
  `trigger:12345`, `family:3`, `icon:1`, `visual:`, `misc:`, `category:`, `mechanic:`, `school:`.
  **New** makes an empty spell with the next free ID; **Copy** duplicates the selected one. **Save** (Ctrl+S)
  writes changed spells to the project.
- **DBC Editor**: pick a table, or **Open .dbc file** to edit a file without importing it. Double-click or press
  Enter to edit a cell, type to replace it, Tab and Enter to move on, Ctrl+C to copy. The filter takes an ID, text
  found in any text column, `Column=value` or `Column~part`.

Unsaved changes are marked in the status line; Anarres asks before closing or importing over them.

## 4. Export and deploy

**Export** writes the ticked tables as `.dbc` files, then, when ticked:

- copies them to the server's `dbc` folder: restart the worldserver;
- packs them into the patch MPQ in the WoW Data folder: restart the client (and clear its `Cache` folder when spell
  text looks out of date).

For a few spells, the spell editor's **SQL...** writes them for `spell_dbc`, and **Send to world DB...** writes
them straight into `acore_world.spell_dbc`. The client still needs the patch to know the spells.

## Automating it

`anarres-cli` uses the same settings as the app (or the options you give it), so the steps can run from a script
or a build server, like Stoneharry's Jenkins setup:

```
anarres-cli import-client "C:\WoW\Data" --locale enUS --only Spell
anarres-cli export --only Spell,SpellIcon --server --patch
anarres-cli export --mysql localhost:3306/acore_dbc_extracted --user acore --password acore --server --patch
anarres-cli sql Spell --ids 90000 --out spell_90000.sql
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
