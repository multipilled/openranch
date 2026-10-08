# Slime Rancher sound

Notes on how the game stores its sounds and how openranch turns them into files Godot can load.
The reader is `src/OpenRanch.Formats/Audio`; `openranch-import sounds` converts every clip in
memory and checks the result. Nothing is ever played by the importer or the tests.

## AudioClip objects

Each sound is a Unity AudioClip (class 83). In Unity 2019.4 the object holds, in order:

| Field | Type | Notes |
| --- | --- | --- |
| Name | string | not unique: the same name can appear in more than one file |
| Load type | int32 | 0 decompress on load, 1 compressed in memory, 2 streaming |
| Channels | int32 | |
| Frequency | int32 | sample rate in Hz |
| Bits per sample | int32 | |
| Length | float | seconds |
| Tracker format, ambisonic | bool, bool | then 4-byte alignment |
| Subsound index | int32 | which sound in the bank (always 0 here) |
| Preload, load in background, legacy 3D | bool x3 | then alignment |
| Resource | string, uint64, uint64 | file name, offset and size of the sound data |
| Compression format | int32 | the codec chosen at import: 0 PCM, 1 Vorbis, 2 ADPCM, ... |

The resource file is one of the `.resource` files next to the serialized files
(`resources.resource`, `sharedassets2.resource`, `sharedassets3.resource`); its name is stored as
`archive:/...` or a bare file name, and only the file name matters.

## The sound data: FMOD sound banks

At the given offset sits one FMOD 5 sound bank (it starts with `FSB5`) per clip. A bank has a
header with the codec, one entry per sound with its rate, channel count and sample count, a name
table and the encoded data. Unity's banks hold a single sound each.

openranch reads banks with [Fmod5Sharp](https://github.com/SamboyCoding/Fmod5Sharp) (MIT):

- **Vorbis** banks keep raw Vorbis packets without the three Vorbis header packets. The bank only
  stores a checksum of the setup header; Fmod5Sharp carries a table of the setup headers FMOD's
  encoder uses and rebuilds a normal Ogg Vorbis file from it. Nothing is re-encoded, so the Ogg file
  is exactly the original sound.
- **PCM** banks become WAV files directly; **ADPCM** (IMA, GameCube, FMOD's own FADPCM) is decoded
  to 16-bit PCM WAV.

Godot loads either form from memory (`AudioStreamOggVorbis.LoadFromBuffer`,
`AudioStreamWav.LoadFromBuffer`).

## Checking a conversion without playing it

`SoundFileInfo` walks the converted file:

- Ogg: every page must start with `OggS`, belong to one stream, follow the page sequence and match
  its CRC-32 checksum (polynomial `0x04C11DB7`, computed with the checksum field set to zero). The
  first packet must be a Vorbis identification header, which gives the channel count and rate; the
  last page's granule position gives the length in samples.
- WAV: the `fmt ` chunk gives channels and rate, the `data` chunk the length.

The importer then compares channels, rate and length with the AudioClip's own fields.
