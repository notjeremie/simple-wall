# Configuration

Everything SimpleWall remembers lives in one JSON file. Most of it is editable
in the app; this is the reference for what the fields mean and for the cases
where hand-editing is easier.

## Where the file lives

`config.json`, in the first of these SimpleWall can actually write to:

1. the folder containing `SimpleWall.exe`
2. `%LOCALAPPDATA%\simple-wall\`
3. the Desktop

The resolved folder is shown in the app's title bar and in the first line of the
log, so you never have to guess which one won.

Writes go to a temp file and are then atomically swapped in, so a power cut
mid-save cannot leave a half-written config. A file that fails to parse is moved
aside to `config.json.bad-<timestamp>` and replaced with defaults, rather than
being silently overwritten — the broken one is always recoverable.

**Edit it with the app closed.** SimpleWall writes the whole file on change and
will overwrite edits made underneath it.

## Output geometry

| Field | Default | Meaning |
|---|---|---|
| `OutputX` | `0` | Left edge of the output window in virtual-desktop coordinates |
| `OutputY` | `0` | Top edge |
| `OutputWidth` | `0` | Width in pixels |
| `OutputHeight` | `0` | Height in pixels |

These are the coordinates of your wall *as Windows sees it* — with the display
extended, a wall to the right of a 1920-wide primary monitor starts at
`OutputX: 1920`.

Zero means "never configured". There is deliberately no plausible default: a
default like 1920×256 at 0,0 is a perfectly valid window on the operator's own
desktop, it passes validation because it really does overlap a real screen, and
the wall just stays dark. Being forced to set it once means the first run lands
on the wall.

Values are validated against the displays Windows reports at startup; a geometry
that no longer matches any screen (a display unplugged, a resolution changed) is
corrected rather than leaving the output invisible. Hand-edited values are not
range-checked — `999999` is your business.

## Clips

`Clips` is an array. Slots are 1-based, up to 50.

```json
{
  "Slot": 3,
  "Path": "D:\\wall\\morning-loop.mp4",
  "Brightness": 1.15,
  "Contrast": 1.0
}
```

| Field | Default | Meaning |
|---|---|---|
| `Slot` | — | The slot number. This is the stable identity — what OSC and the scheduler address |
| `Path` | — | Absolute path to the video file |
| `Brightness` | `1.0` | 0–2, where 1.0 leaves the picture alone |
| `Contrast` | `1.0` | 0–2 |

The look belongs to the **clip**, not to the wall: a clip comes up at its own
brightness and contrast from every trigger, applied to the incoming layer before
the swap so the outgoing frame never flashes at the wrong setting. Replacing a
clip's file resets its look to neutral — a new file gets a fresh start rather
than inheriting tuning meant for different footage.

A config from before per-clip looks existed deserializes to neutral, so nothing
breaks on upgrade.

| Field | Default | Meaning |
|---|---|---|
| `DefaultSlot` | `0` | Slot played once at launch. `0` = start dark |

`DefaultSlot` is honoured only if that slot still holds a clip whose file exists;
otherwise the wall is left dark and the reason is logged, never guessed. Combined
with autostart, it means an unattended wall comes back from a power cut with a
picture on it instead of waiting for the next scheduled event.

## OSC

| Field | Default | Meaning |
|---|---|---|
| `OscPort` | `7000` | UDP port to listen on |
| `OscReplyHost` | `""` | Where to send state feedback. Empty = replies off |
| `OscReplyPort` | `9000` | Port for feedback |

Addresses accepted:

| Address | Argument | Effect |
|---|---|---|
| `/clip/N` | — | Play slot N (1–50) |
| `/play` `/pause` `/toggle` `/stop` | — | Transport |
| `/brightness` | float 0–2 | Set the current clip's brightness |
| `/contrast` | float 0–2 | Set the current clip's contrast |

Out-of-range values are clamped; `NaN` is rejected. A trigger arriving with a
leading `0` argument is treated as a button *release* and ignored, so a single
Stream Deck press doesn't fire the clip twice.

Feedback, sent on every state change to `OscReplyHost`:

| Address | Payload |
|---|---|
| `/state/slot` | Slot currently on the wall |
| `/state/playing` | Playing or not |
| `/state/brightness` | Current clip's brightness |
| `/state/contrast` | Current clip's contrast |

Because feedback fires on *every* change, a controller's faders stay correct even
when someone changes the wall by mouse or the scheduler moves it.

> **Windows Firewall drops inbound UDP silently.** If OSC triggers never arrive,
> add an inbound rule for your `OscPort` before debugging anything else. Nothing
> in the log will tell you — the packets simply never reach the app.

## Scheduler

| Field | Default | Meaning |
|---|---|---|
| `SchedulerEnabled` | `true` | Master on/off for all tasks |

`Tasks` is an array:

```json
{
  "Id": "b2c3…",
  "Enabled": true,
  "Days": ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"],
  "OneOffDate": null,
  "Time": "06:00:00",
  "Command": { "Kind": "PlayClip", "Slot": 7, "Value": 0 },
  "Spent": false
}
```

| Field | Meaning |
|---|---|
| `Enabled` | Per-task on/off, independent of the master switch |
| `Days` | Weekdays to fire on. Any subset — all seven is "every day" |
| `OneOffDate` | A single date instead of a recurrence. Mutually exclusive with `Days` |
| `Time` | Time of day, `HH:MM:SS` |
| `Command` | What to do — see below |
| `Spent` | Set after a one-off fires, so it never repeats. Leave it alone |

`Command.Kind` is one of `PlayClip`, `Play`, `Pause`, `Toggle`, `Stop`,
`Brightness`, `Contrast`. `PlayClip` uses `Slot`; `Brightness` and `Contrast`
use `Value`; the rest use neither.

Each due task fires once. Windows spanning midnight are handled, and a clock
jump — NTP correction, DST, someone changing the system time — cannot cause a
storm of catch-up firings.

If a scheduled clip is already on the wall, the event is a no-op rather than a
restart, so the picture doesn't jump.

## Legacy

| Field | Meaning |
|---|---|
| `Brightness` `Contrast` | Vestigial. A pre-1.0 global wall look, migrated onto individual clips on first launch and no longer read |

There is deliberately **no** autostart field. Autostart is an `HKCU\...\Run`
registry value, and the registry is the only thing that decides whether Windows
launches the app. A boolean here could only ever be a second opinion, and it
would disagree with reality the first time anyone touched msconfig — showing a
ticked box for a machine that never comes back after a reboot.

## Logging

Not configurable. The log sits beside `config.json`, appends every trigger, load,
swap (with timing) and scheduler firing, and rolls at ~5 MB keeping two files.
Roll failures still write the line — an oversized log is recoverable, a missing
line is the evidence you needed.
