# Slime Rancher text

Notes on how the game stores its translated text, written so openranch can show the same words
from the player's own copy. The reader is `src/OpenRanch.Formats/Text`; `openranch-import text`
lists what it found and `--key` looks one message up.

## Where the text lives

All text is in Unity TextAssets inside `resources.assets`. A TextAsset is just a name and the
bytes of the original text file (UTF-8, some with a byte-order mark).

The TextAsset's own name is only the bundle name (`actor`, `ui`, `pedia` and so on), and every
language has a TextAsset with the same name. The language is known only from the asset's path in
the game's `Resources` folder. Unity keeps those paths in the ResourceManager object of
`globalgamemanagers` (class 147), whose first field is a list of (lower-case path, object
reference) pairs. Text bundles sit under:

```
i18n/<language>/<bundle>
```

`<language>` is a lower-case two-letter code (`en`, `de`, `fr`, ...). The game's lookup would also
accept a longer tag for a regional variant, but this release has none. The counts are listed under
"What is there".

## Bundle files

A bundle is a plain text file of messages, one per line:

```
# comment
l.pink_slime = Pink Slime
m.desc.pink_slime = A long description that goes on \
    over two lines.
```

- Lines are split at line feeds. A backslash at the very end of a line joins it to the next line;
  the backslash, the line break and the spaces or tabs that start the next line all disappear.
- A line is skipped when it is shorter than two characters or starts with `#`.
- Every other line must hold exactly one `=` that isn't preceded by a backslash. The text before it
  is the key, the text after it the value. Lines with no such `=`, or more than one, are ignored
  (the reader keeps them in `MalformedLines` so they can be counted).
- Escapes in keys and values: `\=` is an equals sign, `\n` a line break, `­` a soft hyphen
  and `&bsol;` a backslash. After unescaping, the key and the value are trimmed of white space.
- When a key appears twice, the later line wins.
- Values can hold .NET format placeholders such as `{0}`, filled in by the code that shows them.

Key prefixes follow a naming habit rather than a rule: `l.` for labels and names, `m.` for longer
messages, `t.` for titles, and so on.

## Finding a message

The game asks for a message by bundle and key in the current language:

1. The bundle file is chosen by language: first the full language tag, then its two-letter
   language, then English. A missing file is replaced as a whole, not key by key.
2. If the bundle doesn't have the key, the game looks in the bundle named by the bundle's own
   `__parent` key, or in the `global` bundle when it names none, and so on up the chain.

`GameText.Get(language, bundle, key)` follows the same order. The game can also name the bundle
inside the key, written `%bundle:key`; resolving that form is left to the code that shows the text.

## What is there

Read from the Steam release (1.4.x) with `openranch-import text`:

- 10 languages: `de`, `en`, `es`, `fr`, `ja`, `ko`, `pt`, `ru`, `sv`, `zh` (Simplified Chinese
  is stored under plain `zh`).
- English has 11 bundles: `achieve`, `actor`, `build`, `exchange`, `global`, `keys`, `mail`,
  `pedia`, `range`, `tutorial`, `ui`, with 2,816 messages. Every other language has the same
  bundles except `build`, 2,813 messages each, and gets `build` from English by the whole-file
  fallback.
- No line in any bundle is malformed.

## How this was checked

Read from the game's data with `openranch-import text`: every TextAsset reads to its exact end,
every English line parses, and known keys come back with the expected English text
(`tests/OpenRanch.Formats.Tests/TextTests.cs`).
