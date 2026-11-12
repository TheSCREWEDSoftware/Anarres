# Themes and options

**View > Options** (Ctrl+,) holds how Anarres looks and behaves; the project's folders and database are under
**File > Project settings**.

## Options

- **Appearance**: the look (System, Dark or Light), the colours, and the text size.
- **Spell Editor**: the language of names, ranks and the tooltip preview (enUS when a spell has no text in it),
  ranks in the spell list, the preview itself, and opening the last spell again on start.
- **DBC Editor**: the grid's text size, hiding columns that are zero or empty in every row, and showing Flags,
  Mask and Attributes columns in hex.
- **General**: which editor opens first, asking before deleting, how many undo steps each table keeps, and
  remembering the window's size and position.

Options are saved as `options.json` beside `settings.json` (`%APPDATA%\Anarres` on Windows).

## Colours

Colours offers the built-in colours of the look, the ready-made themes (Urras, Frost, Fel, Horde, Alliance and
Parchment), and your own.

1. Choose **New theme...** and give it a name. It starts as a copy of the colours chosen above.
2. Change the colours in the editor that opens. Each row shows a swatch, what the colour is used for, and its
   value as `#RRGGBB`, or `#AARRGGBB` for a see-through colour.
3. Pick the theme under **Colours** and save the options.

Editing a ready-made theme makes a copy to change.

## Theme files

Each theme is a JSON file in the `themes` folder beside the options. **Open themes folder** goes there. The format
is [Urras](https://github.com/TheSCREWEDSoftware/Urras)'s, so a theme file works in both apps; colours one app doesn't know are left alone by the other.

```json
{
  "Name": "Sunset",
  "Base": "Dark",
  "Colours": {
    "Text": "#F0E6DC",
    "MutedText": "#A08C7D",
    "Accent": "#FF9E64"
  }
}
```

`Base` is `Dark` or `Light`: it sets the look of buttons and lists, and the colour of anything the file leaves
out. A theme's base wins over the look chosen in the options. When a newer Anarres adds colours, it writes them
into every theme file it reads, with the base's values.
