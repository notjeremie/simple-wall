# simple-wall Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** A minimal Resolume alternative that loops mp4 clips on a strip LED wall, driven by mouse, OSC from a Stream Deck, or a built-in scheduler, running on Windows 7 SP1 x64 and later.

**Architecture:** WinForms control window + borderless always-on-top output window, with a single VLC media player instance rendering into the output window's handle. Three input sources — mouse, OSC, scheduler — converge on one `IWallEngine` command path. All state persists to one JSON file next to the EXE. See `docs/plans/2026-07-16-simple-wall-design.md` for the full design and the reasoning behind what was cut.

**Tech Stack:** C# / .NET Framework 4.8 (SDK-style csproj, `net48`), WinForms, LibVLCSharp + VideoLAN.LibVLC.Windows, xUnit, built with the .NET SDK CLI on a Windows 11 ARM VM in Parallels.

---

## Read this before Task 1

**The dev machine is not the target machine.** Code is written on a Mac (Apple Silicon), built and unit-tested in a Windows 11 **ARM** VM, and verified for real on a Windows 7 SP1 **x64** PC driving the actual LED wall. Three environments, and the one that matters is the one that's hardest to reach.

**Consequences that shape the whole build:**

1. **Pin `PlatformTarget` to x64. Never AnyCPU.** .NET Framework has no ARM64 story. Pinning x64 means the app runs emulated in the VM (fine) and natively on the wall PC, and — more importantly — the process bitness always matches the x64 native VLC DLLs. AnyCPU risks a process/native mismatch that surfaces as an unexplainable DLL-load failure.
2. **The VM proves logic; only the Win7 box proves playback.** A green test suite in the VM says nothing about whether VLC decodes on Win7. That's why Task 2 exists.
3. **Task 2 is a risk spike and comes before almost everything.** If VLC can't do what we need on Win7, every later task is wasted work. Find out in day one, not week six.

**Toolchain: verified 2026-07-16, not assumed.** The earlier uncertainty about `net48` + `UseWindowsForms` is resolved — it builds, and net48 xUnit tests run on ARM64 (both AnyCPU and x64). Probed before any task was dispatched.

How the environment actually ended up, and why (none of this was the first plan):

| Thing | Resolution |
|---|---|
| Running commands in the VM | **SSH**, not `prlctl exec` — that needs Parallels Pro. Host alias `wallvm` on the Mac (key `~/.ssh/simple-wall-vm`). OpenSSH Server was installed from the GitHub zip because Feature-on-Demand *and* winget both failed on this ARM VM |
| .NET SDK | **8.0.423 ARM64, per-user** at `C:\Users\notjeremie\.dotnet` via `dotnet-install.ps1`. An SSH session has a non-elevated token, so `winget install` gives `Accès refusé`; the per-user script needs no admin |
| .NET Framework 4.8 targeting pack | **Not installed — not needed.** `Microsoft.NETFramework.ReferenceAssemblies` (PackageReference) supplies the reference assemblies. This is required in every project targeting net48; without it the build fails on a machine with no targeting pack |
| Repo path in the VM | `\\Mac\Home\Documents\Coding\simple-wall` (Parallels shared folder). Builds run over UNC |
| Windows language | **French.** `icacls`/`net` group names are localized — use SIDs (`*S-1-5-32-544`) not `"Administrators"` |

Known-broken in that VM, worked around, don't retry: Feature-on-Demand downloads (`0x800f0950`) and `winget install` of anything machine-wide. Ordinary HTTPS downloads from Microsoft's CDN and GitHub work fine — it's the update channel that's sick, not the network.

---

## Task 0: Dev environment — ✅ DONE 2026-07-16

Complete. The build loop works from the Mac with no human in it:

```bash
ssh wallvm "dotnet --version"          # 8.0.423
ssh wallvm 'cmd /c "cd /d \\Mac\Home\Documents\Coding\simple-wall && dotnet test"'
```

Verified by probe: `net48` builds with `UseWindowsForms`, and net48 xUnit tests execute on ARM64. See the toolchain table above for how it's wired and which routes are dead ends.

**Still outstanding — needed for Task 2, not before:** how files reach the Win7 wall PC (network share, USB stick?), and physical access to that machine and the wall. Nothing between here and Task 2 needs it.

---

## Task 1: Solution scaffold

**Files:**
- Create: `simple-wall.sln`
- Create: `src/SimpleWall/SimpleWall.csproj`
- Create: `src/SimpleWall/Program.cs`
- Create: `tests/SimpleWall.Tests/SimpleWall.Tests.csproj`
- Create: `tests/SimpleWall.Tests/ScaffoldTests.cs`
- Create: `.gitignore`

**Step 1: Create the .gitignore**

```gitignore
bin/
obj/
.vs/
*.user
*.log
config.json
```
`config.json` is ignored deliberately — it's runtime state, and committing a dev machine's window geometry would be actively harmful on the wall PC.

**Step 2: Create the app project**

`src/SimpleWall/SimpleWall.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net48</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <PlatformTarget>x64</PlatformTarget>
    <LangVersion>latest</LangVersion>
    <AssemblyName>SimpleWall</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="all" />
  </ItemGroup>
</Project>
```
Both are load-bearing. `PlatformTarget` x64 — see "Read this before Task 1". `Microsoft.NETFramework.ReferenceAssemblies` — the VM has no targeting pack (installing one needs admin, which an SSH session doesn't have), so this package supplies the reference assemblies instead. Verified working. It goes in **every** net48 project, app and tests alike.

**Step 3: Minimal Program.cs**

```csharp
using System;
using System.Windows.Forms;

namespace SimpleWall
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            MessageBox.Show("scaffold ok");
        }
    }
}
```

**Step 4: Verify it builds**

```bash
ssh wallvm 'cmd /c "cd /d \\Mac\Home\Documents\Coding\simple-wall && dotnet build src\SimpleWall\SimpleWall.csproj"'
```
Expected: `La génération a réussi` (French VM) — 0 errors. This combination was probed on 2026-07-16 and works; a failure here means the csproj differs from the probe, not that the toolchain is broken.

**Step 5: Create the test project**

`tests/SimpleWall.Tests/SimpleWall.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <PlatformTarget>x64</PlatformTarget>
    <IsPackable>false</IsPackable>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="all" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/SimpleWall/SimpleWall.csproj" />
  </ItemGroup>
</Project>
```

**Step 6: Write a scaffold test**

`tests/SimpleWall.Tests/ScaffoldTests.cs`:
```csharp
using Xunit;

namespace SimpleWall.Tests
{
    public class ScaffoldTests
    {
        [Fact]
        public void TestHarnessRuns()
        {
            Assert.True(true);
        }
    }
}
```
This asserts nothing about the product. Its job is to prove `dotnet test` works before any real test depends on it — so a red test later means broken code, not a broken harness.

**Step 7: Run the tests**

```bash
ssh wallvm 'cmd /c "cd /d \\Mac\Home\Documents\Coding\simple-wall && dotnet test"'
```
Expected: `Réussi! - échec : 0, réussite : 1`. (The VM is French — "échec" is failures, "réussite" is passes. Read them the right way round.)

**Step 8: Commit**

```bash
git add .gitignore simple-wall.sln src tests
git commit -m "chore: scaffold solution, app and test projects"
```

---

## Task 2: RISK SPIKE — VLC on the real Win7 wall

**This is the task the plan exists to reach early.** Everything after it is ordinary code that will work anywhere. This is the part that could kill the whole approach, and it must be answered on the actual Win7 machine driving the actual wall — not in the VM, which proves nothing about Win7.

Throwaway code. No tests. No polish. It answers five questions and then most of it gets deleted.

**Questions this spike must answer:**
1. Does VLC 3.x initialize at all on this Win7 SP1 box?
2. Does it decode and loop a real 1964x256 mp4 without stutter?
3. Does a borderless always-on-top window land pixel-accurately on the LED strip?
4. Do the brightness/contrast adjust filters actually apply, live, with no restart?
5. Does clip switching look acceptable, and how bad is the black frame really?

**Files:**
- Modify: `src/SimpleWall/SimpleWall.csproj` (add packages)
- Create: `src/SimpleWall/Spike/SpikeForm.cs`
- Modify: `src/SimpleWall/Program.cs`

**Step 1: Add the VLC packages**

```xml
<ItemGroup>
  <PackageReference Include="LibVLCSharp" Version="3.*" />
  <PackageReference Include="LibVLCSharp.WinForms" Version="3.*" />
  <PackageReference Include="VideoLAN.LibVLC.Windows" Version="3.*" />
</ItemGroup>
```
`VideoLAN.LibVLC.Windows` ships the native VLC binaries into the output folder — this is what makes the app self-contained, with no VLC install required on the wall PC. Pin the 3.x line: **VLC 4.x drops Windows 7.**

**Step 2: Write the spike form**

`src/SimpleWall/Spike/SpikeForm.cs` — a borderless top-most form, hardcoded geometry, playing a hardcoded clip on loop, with two trackbars for brightness/contrast and a key to switch clips.

```csharp
using System;
using System.Windows.Forms;
using LibVLCSharp.Shared;
using LibVLCSharp.WinForms;

namespace SimpleWall.Spike
{
    public class SpikeForm : Form
    {
        private readonly LibVLC _libVlc;
        private readonly MediaPlayer _player;
        private readonly VideoView _videoView;

        public SpikeForm(string clipPath)
        {
            Core.Initialize();
            _libVlc = new LibVLC();
            _player = new MediaPlayer(_libVlc);
            _videoView = new VideoView { Dock = DockStyle.Fill, MediaPlayer = _player };

            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Location = new System.Drawing.Point(0, 0);
            Size = new System.Drawing.Size(1920, 256);
            Controls.Add(_videoView);

            Load += (s, e) => Play(clipPath);
        }

        private void Play(string path)
        {
            var media = new Media(_libVlc, path, FromType.FromPath);
            media.AddOption(":input-repeat=65535"); // loop
            _player.Play(media);

            _player.SetAdjustInt(VideoAdjustOption.Enable, 1);
            _player.SetAdjustFloat(VideoAdjustOption.Brightness, 1.0f);
            _player.SetAdjustFloat(VideoAdjustOption.Contrast, 1.0f);
        }
    }
}
```

**Step 3: Point Program.cs at the spike**

```csharp
[STAThread]
private static void Main(string[] args)
{
    Application.EnableVisualStyles();
    Application.SetCompatibleTextRenderingDefault(false);
    Application.Run(new SimpleWall.Spike.SpikeForm(args[0]));
}
```

**Step 4: Build and smoke-test in the VM**

```bash
ssh wallvm 'cmd /c "cd /d \\Mac\Home\Documents\Coding\simple-wall && dotnet build src\SimpleWall\SimpleWall.csproj"'
```
Run it in the VM against any mp4. Expect video. Expect it to be slow — VLC x64 is emulating on ARM. **Do not tune performance here; VM performance is meaningless.** This step only proves the code runs before you carry it to the wall.

**Step 5: Deploy to the Win7 PC — the real test**

Copy the entire `bin/x64/Debug/net48/` folder (EXE + VLC natives) to the Win7 machine. Run it with a real clip on the real wall.

**Step 6: Answer the five questions in writing**

Create `docs/plans/2026-07-16-spike-findings.md` and record what actually happened — especially anything surprising. Answer all five questions, and measure the black frame between clips (film it with a phone at 60fps if it's hard to judge by eye).

**Known Win7 failure modes, and what they mean:**

| Symptom | Likely cause | Response |
|---|---|---|
| `Core.Initialize()` throws / native DLL not found | libvlc natives missing or bitness mismatch | Confirm x64 natives are in the output folder and the process is x64 |
| Black window, no error | Hardware decode path unsupported by the old GPU driver | Set `:avcodec-hw=none` to force software decode; a 1964x256 clip is tiny, software is fine |
| Stutter or tearing | Old GPU driver / vsync | Try output modules: `--vout=direct3d9` vs `directdraw` |
| Missing VC++ runtime | Win7 lacks the redistributable | Install VC++ 2015-2022 x64 redist on the wall PC; note it as a deploy prerequisite |

**Step 7: Commit the spike and findings**

```bash
git add src/SimpleWall docs/plans/2026-07-16-spike-findings.md
git commit -m "spike: verify VLC playback on Windows 7 target"
```
Committed even though it's throwaway — if the approach later needs defending or revisiting, the evidence should be in history.

**STOP HERE. Report the findings before continuing.** If VLC can't drive this wall on Win7, the design changes and the rest of this plan is void. If it works, everything below is ordinary work.

---

## Task 3: Config model and persistence

Pure logic. TDD from here on.

**Files:**
- Create: `src/SimpleWall/Model/WallConfig.cs`
- Create: `src/SimpleWall/Model/ClipEntry.cs`
- Create: `src/SimpleWall/Model/ConfigStore.cs`
- Create: `tests/SimpleWall.Tests/ConfigStoreTests.cs`

**Step 1: Write the failing tests**

```csharp
using System.IO;
using Xunit;
using SimpleWall.Model;

namespace SimpleWall.Tests
{
    public class ConfigStoreTests
    {
        private static string TempFile() =>
            Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");

        [Fact]
        public void LoadReturnsDefaultsWhenFileMissing()
        {
            var config = new ConfigStore(TempFile()).Load();

            Assert.Equal(7000, config.OscPort);
            Assert.Equal(1.0f, config.Brightness);
            Assert.Equal(1.0f, config.Contrast);
            Assert.Empty(config.Clips);
        }

        [Fact]
        public void SaveThenLoadRoundTrips()
        {
            var path = TempFile();
            var store = new ConfigStore(path);
            var config = store.Load();
            config.Brightness = 0.6f;
            config.Clips.Add(new ClipEntry { Slot = 3, Path = @"C:\clips\a.mp4" });
            store.Save(config);

            var loaded = new ConfigStore(path).Load();

            Assert.Equal(0.6f, loaded.Brightness);
            Assert.Equal(3, Assert.Single(loaded.Clips).Slot);
        }

        [Fact]
        public void CorruptConfigIsQuarantinedAndDefaultsReturned()
        {
            var path = TempFile();
            File.WriteAllText(path, "{ this is not json");

            var config = new ConfigStore(path).Load();

            Assert.Equal(7000, config.OscPort);
            Assert.True(File.Exists(path + ".bad"), "corrupt config should be kept for diagnosis, not deleted");
        }
    }
}
```
That third test is the one that matters. "Lose the layout, not the evening" is a design promise, and this is where it's kept or broken.

**Step 2: Run the tests, verify they fail**

```bash
ssh wallvm 'cmd /c "cd /d \\Mac\Home\Documents\Coding\simple-wall && dotnet test"'
```
Expected: compile failure — `ConfigStore` does not exist. That's a legitimate red.

**Step 3: Implement**

Add `Newtonsoft.Json` to `SimpleWall.csproj` (`<PackageReference Include="Newtonsoft.Json" Version="13.*" />`) — chosen over `System.Text.Json` because it needs no extra dependency dance on net48.

`ClipEntry.cs`:
```csharp
namespace SimpleWall.Model
{
    public class ClipEntry
    {
        public int Slot { get; set; }
        public string Path { get; set; }
    }
}
```

`WallConfig.cs`:
```csharp
using System.Collections.Generic;

namespace SimpleWall.Model
{
    public class WallConfig
    {
        public List<ClipEntry> Clips { get; set; } = new List<ClipEntry>();
        public int OutputX { get; set; }
        public int OutputY { get; set; }
        public int OutputWidth { get; set; } = 1920;
        public int OutputHeight { get; set; } = 256;
        public float Brightness { get; set; } = 1.0f;
        public float Contrast { get; set; } = 1.0f;
        public int OscPort { get; set; } = 7000;
        public string OscReplyHost { get; set; } = "";
        public int OscReplyPort { get; set; } = 9000;
        public List<ScheduledTask> Tasks { get; set; } = new List<ScheduledTask>();
        public bool SchedulerEnabled { get; set; } = true;
        public bool Autostart { get; set; }
    }
}
```
(`ScheduledTask` arrives in Task 6; stub it as an empty class to keep this compiling, or reorder if preferred.)

`ConfigStore.cs`:
```csharp
using System;
using System.IO;
using Newtonsoft.Json;

namespace SimpleWall.Model
{
    public class ConfigStore
    {
        private readonly string _path;

        public ConfigStore(string path) { _path = path; }

        public WallConfig Load()
        {
            if (!File.Exists(_path)) return new WallConfig();

            try
            {
                return JsonConvert.DeserializeObject<WallConfig>(File.ReadAllText(_path))
                       ?? new WallConfig();
            }
            catch (Exception)
            {
                Quarantine();
                return new WallConfig();
            }
        }

        private void Quarantine()
        {
            try
            {
                var bad = _path + ".bad";
                if (File.Exists(bad)) File.Delete(bad);
                File.Move(_path, bad);
            }
            catch (Exception) { /* refusing to start is worse than losing the layout */ }
        }

        public void Save(WallConfig config)
        {
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(config, Formatting.Indented));
            if (File.Exists(_path)) File.Delete(_path);
            File.Move(tmp, _path);
        }
    }
}
```
`Save` writes to a temp file then moves it. A power cut mid-write leaves the old config intact rather than a half-written one — this is a wall PC that will lose power without warning.

**Step 4: Run the tests, verify they pass**

Expected: `Failed: 0, Passed: 4`.

**Step 5: Commit**

```bash
git add src/SimpleWall/Model tests/SimpleWall.Tests/ConfigStoreTests.cs
git commit -m "feat: config model with atomic save and corrupt-file recovery"
```

---

## Task 4: Clip library with stable slot numbers

The design's central promise: a Stream Deck mapping must never silently drift. This task keeps it.

**Files:**
- Create: `src/SimpleWall/Model/ClipLibrary.cs`
- Create: `tests/SimpleWall.Tests/ClipLibraryTests.cs`

**Step 1: Write the failing tests**

```csharp
using System.Linq;
using Xunit;
using SimpleWall.Model;

namespace SimpleWall.Tests
{
    public class ClipLibraryTests
    {
        [Fact]
        public void FirstClipGetsSlotOne()
        {
            var library = new ClipLibrary();
            Assert.Equal(1, library.Add(@"C:\a.mp4").Slot);
        }

        [Fact]
        public void SlotsAreSequentialForSuccessiveAdds()
        {
            var library = new ClipLibrary();
            library.Add(@"C:\a.mp4");
            library.Add(@"C:\b.mp4");
            Assert.Equal(3, library.Add(@"C:\c.mp4").Slot);
        }

        [Fact]
        public void RemovingAClipDoesNotRenumberTheOthers()
        {
            var library = new ClipLibrary();
            library.Add(@"C:\a.mp4");
            library.Add(@"C:\b.mp4");
            library.Add(@"C:\c.mp4");

            library.Remove(2);

            Assert.Equal(new[] { 1, 3 }, library.Clips.Select(c => c.Slot));
            Assert.Equal(@"C:\c.mp4", library.BySlot(3).Path);
        }

        [Fact]
        public void NewClipReusesTheLowestFreeSlot()
        {
            var library = new ClipLibrary();
            library.Add(@"C:\a.mp4");
            library.Add(@"C:\b.mp4");
            library.Add(@"C:\c.mp4");
            library.Remove(2);

            Assert.Equal(2, library.Add(@"C:\d.mp4").Slot);
        }

        [Fact]
        public void AddIsRefusedAtTheFiftyClipCeiling()
        {
            var library = new ClipLibrary();
            for (var i = 0; i < 50; i++) library.Add($@"C:\{i}.mp4");

            Assert.Null(library.Add(@"C:\overflow.mp4"));
            Assert.Equal(50, library.Clips.Count);
        }

        [Fact]
        public void BySlotReturnsNullForUnknownSlot()
        {
            Assert.Null(new ClipLibrary().BySlot(7));
        }
    }
}
```
`RemovingAClipDoesNotRenumberTheOthers` and `NewClipReusesTheLowestFreeSlot` are the Stream Deck contract in executable form. If either ever goes red, someone's buttons are about to point at the wrong clips.

**Step 2: Run tests, verify they fail**

Expected: compile failure — `ClipLibrary` does not exist.

**Step 3: Implement**

```csharp
using System.Collections.Generic;
using System.Linq;

namespace SimpleWall.Model
{
    public class ClipLibrary
    {
        public const int MaxClips = 50;

        private readonly List<ClipEntry> _clips;

        public ClipLibrary() : this(new List<ClipEntry>()) { }
        public ClipLibrary(List<ClipEntry> clips) { _clips = clips; }

        public IReadOnlyList<ClipEntry> Clips => _clips;

        public ClipEntry Add(string path)
        {
            if (_clips.Count >= MaxClips) return null;

            var entry = new ClipEntry { Slot = LowestFreeSlot(), Path = path };
            _clips.Add(entry);
            return entry;
        }

        private int LowestFreeSlot()
        {
            var used = new HashSet<int>(_clips.Select(c => c.Slot));
            var slot = 1;
            while (used.Contains(slot)) slot++;
            return slot;
        }

        public void Remove(int slot) => _clips.RemoveAll(c => c.Slot == slot);

        public ClipEntry BySlot(int slot) => _clips.FirstOrDefault(c => c.Slot == slot);
    }
}
```

**Step 4: Run tests, verify they pass**

Expected: `Failed: 0, Passed: 10`.

**Step 5: Commit**

```bash
git add src/SimpleWall/Model/ClipLibrary.cs tests/SimpleWall.Tests/ClipLibraryTests.cs
git commit -m "feat: clip library with stable slot numbers"
```

---

## Task 5: The command path

One interface, three callers. This is what keeps the UI honest when the Stream Deck or the clock changes something.

**Files:**
- Create: `src/SimpleWall/Engine/IWallEngine.cs`
- Create: `src/SimpleWall/Engine/WallCommand.cs`
- Create: `tests/SimpleWall.Tests/FakeWallEngine.cs`

**Step 1: Define the command**

```csharp
namespace SimpleWall.Engine
{
    public enum CommandKind { PlayClip, Play, Pause, Toggle, Stop, Brightness, Contrast }

    public class WallCommand
    {
        public CommandKind Kind { get; set; }
        public int Slot { get; set; }
        public float Value { get; set; }

        public static WallCommand PlayClip(int slot) =>
            new WallCommand { Kind = CommandKind.PlayClip, Slot = slot };
        public static WallCommand Simple(CommandKind kind) => new WallCommand { Kind = kind };
        public static WallCommand WithValue(CommandKind kind, float value) =>
            new WallCommand { Kind = kind, Value = value };
    }
}
```

**Step 2: Define the engine interface**

```csharp
using System;

namespace SimpleWall.Engine
{
    public interface IWallEngine
    {
        void Execute(WallCommand command);
        int? CurrentSlot { get; }
        bool IsPlaying { get; }
        event EventHandler StateChanged;
    }
}
```
One entry point, `Execute`. Mouse, OSC and scheduler all call it and nothing else. A second way in would be a second set of bugs.

**Step 3: Write the fake for tests**

```csharp
using System;
using System.Collections.Generic;
using SimpleWall.Engine;

namespace SimpleWall.Tests
{
    public class FakeWallEngine : IWallEngine
    {
        public List<WallCommand> Received { get; } = new List<WallCommand>();
        public int? CurrentSlot { get; set; }
        public bool IsPlaying { get; set; }
        public event EventHandler StateChanged;

        public void Execute(WallCommand command)
        {
            Received.Add(command);
            if (command.Kind == CommandKind.PlayClip)
            {
                CurrentSlot = command.Slot;
                IsPlaying = true;
            }
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
```
This fake is what lets the scheduler and OSC layers be tested without VLC, a window, or a wall.

**Step 4: Build**

Expected: `Build succeeded`. No behaviour to test yet — these are contracts.

**Step 5: Commit**

```bash
git add src/SimpleWall/Engine tests/SimpleWall.Tests/FakeWallEngine.cs
git commit -m "feat: unified command path interface"
```

---

## Task 6: Scheduler due-calculation

**The highest-value tests in the project.** Scheduler bugs surface once a week, at the worst possible moment, and can't be reproduced on demand. So the clock is a *parameter*, never read inside the logic — which turns "is this due?" into a pure function testable in milliseconds instead of a thing you find out about on Sunday.

**Files:**
- Create: `src/SimpleWall/Scheduling/ScheduledTask.cs`
- Create: `src/SimpleWall/Scheduling/Scheduler.cs`
- Create: `tests/SimpleWall.Tests/SchedulerTests.cs`

**Step 1: Write the failing tests**

```csharp
using System;
using System.Collections.Generic;
using Xunit;
using SimpleWall.Engine;
using SimpleWall.Scheduling;

namespace SimpleWall.Tests
{
    public class SchedulerTests
    {
        private static readonly DateTime SundayNoon = new DateTime(2026, 7, 19, 12, 0, 0); // a Sunday

        private static ScheduledTask WeeklyTask(DayOfWeek day, int hour, int minute, int slot) =>
            new ScheduledTask
            {
                Enabled = true,
                Days = new List<DayOfWeek> { day },
                Time = new TimeSpan(hour, minute, 0),
                Command = WallCommand.PlayClip(slot)
            };

        private static Scheduler SchedulerWith(params ScheduledTask[] tasks) =>
            new Scheduler(new List<ScheduledTask>(tasks)) { Enabled = true };

        [Fact]
        public void TaskFiresWhenItsTimeIsCrossed()
        {
            var scheduler = SchedulerWith(WeeklyTask(DayOfWeek.Sunday, 13, 0, 7));

            var due = scheduler.DueBetween(SundayNoon.AddHours(1).AddSeconds(-1), SundayNoon.AddHours(1));

            Assert.Equal(7, Assert.Single(due).Command.Slot);
        }

        [Fact]
        public void TaskDoesNotFireOnTheWrongWeekday()
        {
            var scheduler = SchedulerWith(WeeklyTask(DayOfWeek.Monday, 13, 0, 7));

            var due = scheduler.DueBetween(SundayNoon.AddHours(1).AddSeconds(-1), SundayNoon.AddHours(1));

            Assert.Empty(due);
        }

        [Fact]
        public void TaskFiresOnlyOncePerCrossing()
        {
            var scheduler = SchedulerWith(WeeklyTask(DayOfWeek.Sunday, 13, 0, 7));
            var justBefore = SundayNoon.AddHours(1).AddSeconds(-1);
            var justAfter = SundayNoon.AddHours(1);

            scheduler.DueBetween(justBefore, justAfter);
            var second = scheduler.DueBetween(justAfter, justAfter.AddSeconds(1));

            Assert.Empty(second);
        }

        [Fact]
        public void MissedTaskDoesNotFireOnStartup()
        {
            // no catch-up: app starts at 13:20, the 13:00 task is gone
            var scheduler = SchedulerWith(WeeklyTask(DayOfWeek.Sunday, 13, 0, 7));

            var due = scheduler.DueBetween(SundayNoon.AddHours(1).AddMinutes(20),
                                           SundayNoon.AddHours(1).AddMinutes(20).AddSeconds(1));

            Assert.Empty(due);
        }

        [Fact]
        public void BackwardClockJumpDoesNotReplayATask()
        {
            var scheduler = SchedulerWith(WeeklyTask(DayOfWeek.Sunday, 13, 0, 7));
            var after = SundayNoon.AddHours(1).AddMinutes(30);

            var due = scheduler.DueBetween(after, after.AddHours(-1)); // clock went backwards

            Assert.Empty(due);
        }

        [Fact]
        public void DisabledTaskNeverFires()
        {
            var task = WeeklyTask(DayOfWeek.Sunday, 13, 0, 7);
            task.Enabled = false;
            var scheduler = SchedulerWith(task);

            Assert.Empty(scheduler.DueBetween(SundayNoon.AddHours(1).AddSeconds(-1), SundayNoon.AddHours(1)));
        }

        [Fact]
        public void MasterDisableSuppressesEverything()
        {
            var scheduler = SchedulerWith(WeeklyTask(DayOfWeek.Sunday, 13, 0, 7));
            scheduler.Enabled = false;

            Assert.Empty(scheduler.DueBetween(SundayNoon.AddHours(1).AddSeconds(-1), SundayNoon.AddHours(1)));
        }

        [Fact]
        public void OneOffFiresOnItsDateOnly()
        {
            var task = new ScheduledTask
            {
                Enabled = true,
                OneOffDate = SundayNoon.Date,
                Time = new TimeSpan(13, 0, 0),
                Command = WallCommand.PlayClip(9)
            };
            var scheduler = SchedulerWith(task);

            var due = scheduler.DueBetween(SundayNoon.AddHours(1).AddSeconds(-1), SundayNoon.AddHours(1));
            Assert.Single(due);

            var nextWeek = SundayNoon.AddDays(7).AddHours(1);
            Assert.Empty(scheduler.DueBetween(nextWeek.AddSeconds(-1), nextWeek));
        }

        [Fact]
        public void EveryDayTaskFiresOnAnyWeekday()
        {
            var task = new ScheduledTask
            {
                Enabled = true,
                Days = new List<DayOfWeek>((DayOfWeek[])Enum.GetValues(typeof(DayOfWeek))),
                Time = new TimeSpan(9, 0, 0),
                Command = WallCommand.PlayClip(9)
            };
            var scheduler = SchedulerWith(task);

            var wednesday = new DateTime(2026, 7, 22, 9, 0, 0);
            Assert.Single(scheduler.DueBetween(wednesday.AddSeconds(-1), wednesday));
        }
    }
}
```
`MissedTaskDoesNotFireOnStartup` and `BackwardClockJumpDoesNotReplayATask` encode two explicit design decisions. If someone "fixes" no-catch-up later without meaning to, these go red and say so.

**Step 2: Run tests, verify they fail**

Expected: compile failure — `Scheduler` does not exist.

**Step 3: Implement**

`ScheduledTask.cs`:
```csharp
using System;
using System.Collections.Generic;
using SimpleWall.Engine;

namespace SimpleWall.Scheduling
{
    public class ScheduledTask
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public bool Enabled { get; set; } = true;
        public List<DayOfWeek> Days { get; set; } = new List<DayOfWeek>();
        public DateTime? OneOffDate { get; set; }
        public TimeSpan Time { get; set; }
        public WallCommand Command { get; set; }
        public bool Spent { get; set; }

        public bool FiresOn(DateTime moment)
        {
            if (OneOffDate.HasValue) return OneOffDate.Value.Date == moment.Date;
            return Days.Contains(moment.DayOfWeek);
        }
    }
}
```

`Scheduler.cs`:
```csharp
using System;
using System.Collections.Generic;

namespace SimpleWall.Scheduling
{
    public class Scheduler
    {
        private readonly List<ScheduledTask> _tasks;

        public Scheduler(List<ScheduledTask> tasks) { _tasks = tasks; }

        public bool Enabled { get; set; } = true;
        public IReadOnlyList<ScheduledTask> Tasks => _tasks;

        /// <summary>
        /// Tasks whose scheduled moment falls in (previousTick, now].
        /// Half-open at the start so a task can never fire twice on successive ticks.
        /// Returns nothing if the clock went backwards — no replays.
        /// </summary>
        public List<ScheduledTask> DueBetween(DateTime previousTick, DateTime now)
        {
            var due = new List<ScheduledTask>();
            if (!Enabled || now <= previousTick) return due;

            foreach (var task in _tasks)
            {
                if (!task.Enabled || task.Spent) continue;
                if (!task.FiresOn(now)) continue;

                var moment = now.Date + task.Time;
                if (moment > previousTick && moment <= now)
                {
                    if (task.OneOffDate.HasValue) task.Spent = true;
                    due.Add(task);
                }
            }
            return due;
        }
    }
}
```
The whole no-catch-up rule is `moment > previousTick && moment <= now`. Because `previousTick` is set to "now" at startup, anything earlier than launch simply isn't in the window. The design decision is one line, and the tests hold it there.

**Step 4: Run tests, verify they pass**

Expected: `Failed: 0, Passed: 19`.

**Step 5: Commit**

```bash
git add src/SimpleWall/Scheduling tests/SimpleWall.Tests/SchedulerTests.cs
git commit -m "feat: scheduler due-calculation with no catch-up and clock-jump safety"
```

---

## Task 7: OSC message parsing

**Files:**
- Create: `src/SimpleWall/Osc/OscParser.cs`
- Create: `tests/SimpleWall.Tests/OscParserTests.cs`

**Step 1: Write the failing tests**

```csharp
using Xunit;
using SimpleWall.Engine;
using SimpleWall.Osc;

namespace SimpleWall.Tests
{
    public class OscParserTests
    {
        [Theory]
        [InlineData("/play", CommandKind.Play)]
        [InlineData("/pause", CommandKind.Pause)]
        [InlineData("/toggle", CommandKind.Toggle)]
        [InlineData("/stop", CommandKind.Stop)]
        public void BareTransportAddressesParse(string address, CommandKind expected)
        {
            Assert.Equal(expected, OscParser.Parse(address, new object[0]).Kind);
        }

        [Fact]
        public void ClipAddressCarriesItsSlot()
        {
            var command = OscParser.Parse("/clip/7", new object[0]);

            Assert.Equal(CommandKind.PlayClip, command.Kind);
            Assert.Equal(7, command.Slot);
        }

        [Fact]
        public void ButtonPressValueIsAccepted()
        {
            // some Stream Deck OSC plugins send 1 on press
            Assert.NotNull(OscParser.Parse("/clip/7", new object[] { 1f }));
        }

        [Fact]
        public void ButtonReleaseValueIsIgnored()
        {
            // ...and 0 on release — which must not re-trigger
            Assert.Null(OscParser.Parse("/clip/7", new object[] { 0f }));
        }

        [Fact]
        public void BrightnessCarriesItsValue()
        {
            var command = OscParser.Parse("/brightness", new object[] { 0.5f });

            Assert.Equal(CommandKind.Brightness, command.Kind);
            Assert.Equal(0.5f, command.Value);
        }

        [Fact]
        public void BrightnessIsClampedToRange()
        {
            Assert.Equal(2f, OscParser.Parse("/brightness", new object[] { 99f }).Value);
            Assert.Equal(0f, OscParser.Parse("/brightness", new object[] { -5f }).Value);
        }

        [Fact]
        public void UnknownAddressIsIgnored()
        {
            Assert.Null(OscParser.Parse("/nonsense", new object[0]));
        }

        [Fact]
        public void MalformedClipAddressIsIgnored()
        {
            Assert.Null(OscParser.Parse("/clip/abc", new object[0]));
            Assert.Null(OscParser.Parse("/clip/", new object[0]));
        }
    }
}
```
`ButtonReleaseValueIsIgnored` is the double-trigger bug, caught before it exists. `MalformedClipAddressIsIgnored` matters because this parser is fed by the network — a stray packet must never take down the wall.

**Step 2: Run tests, verify they fail**

Expected: compile failure — `OscParser` does not exist.

**Step 3: Implement**

```csharp
using System;
using System.Globalization;
using SimpleWall.Engine;

namespace SimpleWall.Osc
{
    public static class OscParser
    {
        /// <summary>Returns null for anything that should be ignored — unknown, malformed, or a button release.</summary>
        public static WallCommand Parse(string address, object[] arguments)
        {
            if (string.IsNullOrWhiteSpace(address)) return null;
            if (IsButtonRelease(arguments)) return null;

            if (address.StartsWith("/clip/", StringComparison.Ordinal))
            {
                var raw = address.Substring("/clip/".Length);
                if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var slot) || slot < 1)
                    return null;
                return WallCommand.PlayClip(slot);
            }

            switch (address)
            {
                case "/play":   return WallCommand.Simple(CommandKind.Play);
                case "/pause":  return WallCommand.Simple(CommandKind.Pause);
                case "/toggle": return WallCommand.Simple(CommandKind.Toggle);
                case "/stop":   return WallCommand.Simple(CommandKind.Stop);
                case "/brightness": return Adjust(CommandKind.Brightness, arguments);
                case "/contrast":   return Adjust(CommandKind.Contrast, arguments);
                default: return null;
            }
        }

        private static bool IsButtonRelease(object[] arguments)
        {
            if (arguments == null || arguments.Length == 0) return false;
            return TryFloat(arguments[0], out var value) && value == 0f;
        }

        private static WallCommand Adjust(CommandKind kind, object[] arguments)
        {
            if (arguments == null || arguments.Length == 0) return null;
            if (!TryFloat(arguments[0], out var value)) return null;
            return WallCommand.WithValue(kind, Math.Max(0f, Math.Min(2f, value)));
        }

        private static bool TryFloat(object argument, out float value)
        {
            value = 0f;
            switch (argument)
            {
                case float f: value = f; return true;
                case int i: value = i; return true;
                case double d: value = (float)d; return true;
                default: return false;
            }
        }
    }
}
```
Note the conflict this resolves: `/brightness 0` is a legitimate command (blackout), but `IsButtonRelease` would swallow it. It doesn't — `Adjust` is only reached for addresses that take a value, and the release check runs first only to protect *triggers*. **Add a test asserting `/brightness 0` still parses, and if the current structure swallows it, restructure so the release check applies only to trigger addresses.** This is exactly the kind of thing that works in testing and fails on stage.

**Step 4: Run tests, verify they pass**

Expected: `Failed: 0, Passed: 30`.

**Step 5: Commit**

```bash
git add src/SimpleWall/Osc tests/SimpleWall.Tests/OscParserTests.cs
git commit -m "feat: OSC message parsing"
```

---

## Task 8: Output geometry validation

Prevents the output window opening off-screen where it can't be grabbed.

**Files:**
- Create: `src/SimpleWall/Model/GeometryValidator.cs`
- Create: `tests/SimpleWall.Tests/GeometryValidatorTests.cs`

**Step 1: Write the failing tests**

```csharp
using System.Drawing;
using Xunit;
using SimpleWall.Model;

namespace SimpleWall.Tests
{
    public class GeometryValidatorTests
    {
        private static readonly Rectangle[] TwoScreens =
        {
            new Rectangle(0, 0, 1920, 1080),
            new Rectangle(1920, 0, 1920, 1080)
        };

        [Fact]
        public void GeometryOnAConnectedScreenIsKept()
        {
            var saved = new Rectangle(1920, 0, 1920, 256);
            Assert.Equal(saved, GeometryValidator.Validate(saved, TwoScreens));
        }

        [Fact]
        public void GeometryOnAMissingScreenSnapsToPrimary()
        {
            var saved = new Rectangle(3840, 0, 1920, 256); // third monitor, unplugged
            var result = GeometryValidator.Validate(saved, TwoScreens);

            Assert.True(TwoScreens[0].IntersectsWith(result), "must land somewhere reachable");
            Assert.Equal(saved.Size, result.Size);
        }

        [Fact]
        public void PartiallyVisibleGeometryIsKept()
        {
            // overlapping the seam between screens is legitimate — don't "helpfully" move it
            var saved = new Rectangle(1900, 0, 1920, 256);
            Assert.Equal(saved, GeometryValidator.Validate(saved, TwoScreens));
        }

        [Fact]
        public void ZeroSizedGeometryGetsUsableDefaults()
        {
            var result = GeometryValidator.Validate(new Rectangle(0, 0, 0, 0), TwoScreens);

            Assert.True(result.Width > 0);
            Assert.True(result.Height > 0);
        }
    }
}
```
`PartiallyVisibleGeometryIsKept` guards against over-correcting — an output deliberately straddling screens is a legitimate setup, and an app that "helpfully" moves it every launch is worse than one that never moves it.

**Step 2: Run tests, verify they fail**

**Step 3: Implement**

```csharp
using System.Drawing;

namespace SimpleWall.Model
{
    public static class GeometryValidator
    {
        public static Rectangle Validate(Rectangle saved, Rectangle[] screens)
        {
            var size = saved.Size;
            if (size.Width <= 0) size.Width = 1920;
            if (size.Height <= 0) size.Height = 256;

            var candidate = new Rectangle(saved.Location, size);

            foreach (var screen in screens)
                if (screen.IntersectsWith(candidate))
                    return candidate;

            var primary = screens.Length > 0 ? screens[0] : new Rectangle(0, 0, 1920, 1080);
            return new Rectangle(primary.X, primary.Y, size.Width, size.Height);
        }
    }
}
```
Takes screens as a parameter rather than calling `Screen.AllScreens` — same reason the scheduler takes the clock. Testable without a monitor.

**Step 4: Run tests, verify they pass**

**Step 5: Commit**

```bash
git add src/SimpleWall/Model/GeometryValidator.cs tests/SimpleWall.Tests/GeometryValidatorTests.cs
git commit -m "feat: output geometry validation against connected screens"
```

---

## Task 9: The real VLC engine

Where tested logic meets the untestable outside world. Deliberately thin: everything worth testing already lives elsewhere.

**Files:**
- Create: `src/SimpleWall/Engine/VlcWallEngine.cs`
- Create: `src/SimpleWall/UI/OutputWindow.cs`
- Delete: `src/SimpleWall/Spike/SpikeForm.cs`

**Step 1: Write the output window**

Borderless, `TopMost`, `StartPosition = Manual`, black background, hosting a `VideoView` docked fill. Geometry comes from `GeometryValidator.Validate(saved, Screen.AllScreens.Select(s => s.Bounds).ToArray())`. Expose a method to reposition it live from settings.

**Step 2: Implement VlcWallEngine : IWallEngine**

Carry forward whatever Task 2's findings demanded (`:avcodec-hw=none`, a specific `--vout`, etc.) — those are hard-won facts about the target, not preferences.

`Execute` switches on `CommandKind`:
- `PlayClip` — look up `ClipLibrary.BySlot`; null or missing file ⇒ log, raise a clip-unavailable event, **change nothing on the wall**. If the slot is already `CurrentSlot` and playing, return without restarting. Otherwise build the `Media` with `:input-repeat=65535`, `Play`, set `CurrentSlot`.
- `Play` / `Pause` / `Toggle` — straight to the media player. Pause holds the frame; it must not blank.
- `Stop` — `_player.Stop()`, `CurrentSlot = null`.
- `Brightness` / `Contrast` — `SetAdjustFloat`, clamped 0–2, persisted to config.

Raise `StateChanged` after every mutation. That event is the only way the UI learns anything — which is what keeps the grid honest when OSC or the scheduler is what acted.

**Step 3: Delete the spike**

Its findings live in the doc and in this engine. Delete `Spike/SpikeForm.cs`.

**Step 4: Build and test in the VM**

Expected: `Build succeeded`, all existing tests still pass. `VlcWallEngine` has no unit tests — it's the untestable boundary, verified by hand on the wall in Task 15.

**Step 5: Commit**

```bash
git add -A src/SimpleWall
git commit -m "feat: VLC engine and output window, replacing the spike"
```

---

## Task 10: Clip grid UI

**Files:**
- Create: `src/SimpleWall/UI/MainForm.cs`
- Create: `src/SimpleWall/UI/ClipBox.cs`
- Create: `src/SimpleWall/UI/ThumbnailCache.cs`

**Step 1: ThumbnailCache**

First frame per clip, extracted once via VLC's snapshot, cached to `%LOCALAPPDATA%\simple-wall\thumbs\<hash>.png`, keyed by path + last-write-time so a replaced file re-thumbnails. Extraction is async and must never block startup — a wall that takes ten seconds to open because of thumbnails is a worse wall.

**Step 2: ClipBox control**

Fixed size (~160×90). Draws thumbnail, filename, slot number in the corner. Visual states: normal, **playing** (bright border), **missing file** (red). Click raises a trigger event — it does not touch the engine directly; `MainForm` owns that.

**Step 3: MainForm grid**

`FlowLayoutPanel` of `ClipBox` plus a trailing **+** tile that disappears at 50. Drag-and-drop mp4s onto the form adds boxes left-to-right. Right-click ⇒ Remove. Drag box onto box ⇒ reorder. On startup, `File.Exists` every clip and mark the missing ones red.

**Step 4: Wire to the engine via the command path**

Click ⇒ `_engine.Execute(WallCommand.PlayClip(slot))`. Subscribe to `StateChanged` and repaint from `_engine.CurrentSlot` — **never** from the click handler. The grid must show what the wall is doing, not what the mouse asked for; that distinction is the whole reason OSC and the scheduler can't desync the UI.

**Step 5: Manual verification in the VM**

Add clips, trigger, remove, re-add. Confirm slot numbers survive removal (the Task 4 contract, now visible on screen).

**Step 6: Commit**

```bash
git add src/SimpleWall/UI
git commit -m "feat: clip grid with thumbnails and stable slot display"
```

---

## Task 11: Transport and image adjustment UI

**Files:**
- Modify: `src/SimpleWall/UI/MainForm.cs`

**Step 1: Transport**

Play/Pause toggle button (icon swaps on `StateChanged`) and Stop. Both go through `Execute`.

**Step 2: Sliders**

Brightness and contrast, 0–2 (trackbars are integers: 0–200 mapped to 0.0–2.0). Numeric readout beside each. Reset button per slider snapping to 1.0. Fire on scroll, live.

**Step 3: Persist**

Save to config on release, not on every tick — a slider drag would otherwise write the file a hundred times.

**Step 4: Verify in the VM**

Drag brightness, confirm the video responds live with no restart.

**Step 5: Commit**

```bash
git add src/SimpleWall/UI/MainForm.cs
git commit -m "feat: transport and brightness/contrast controls"
```

---

## Task 12: OSC listener

**Files:**
- Create: `src/SimpleWall/Osc/OscListener.cs`
- Create: `src/SimpleWall/Osc/OscReplySender.cs`

**Step 1: Add an OSC library**

`<PackageReference Include="Rug.Osc" Version="1.*" />` — verify it targets net48 before committing to it. If it doesn't, `SharpOSC` is the fallback.

**Step 2: OscListener**

UDP receive on its own thread. Each packet ⇒ `OscParser.Parse` ⇒ if non-null, marshal onto the UI thread (`form.BeginInvoke`) and `Execute`. **Wrap the receive loop in try/catch and never let it die** — a malformed packet must not silently kill remote control for the evening.

Port already in use ⇒ raise an event carrying the message, do not throw. The app runs fine without OSC.

**Step 3: OscReplySender**

If `OscReplyHost` is set, push current slot, play state, brightness and contrast on every `StateChanged`. Blank host ⇒ silent.

**Step 4: Verify from the Mac**

With the app running in the VM, send from the Mac (`oscsend` or a Python one-liner) — confirm `/clip/1` triggers, `/clip/1 0` does not, `/brightness 0.5` dims, `/nonsense` is ignored. **Prove OSC works before the Stream Deck is anywhere near it**; otherwise a failure could be the app, the plugin, the network, or the firewall, and you won't know which.

**Step 5: Commit**

```bash
git add src/SimpleWall/Osc
git commit -m "feat: OSC listener and state reply"
```

---

## Task 13: Scheduler UI and tick

**Files:**
- Create: `src/SimpleWall/UI/SchedulerTab.cs`
- Create: `src/SimpleWall/UI/TaskEditDialog.cs`
- Modify: `src/SimpleWall/UI/MainForm.cs`

**Step 1: The tick**

A `System.Windows.Forms.Timer` at 1000ms in `MainForm`. Each tick:
```csharp
var now = DateTime.Now;
foreach (var task in _scheduler.DueBetween(_previousTick, now))
{
    _log.Info($"scheduler fired: {task.Describe()}");
    _engine.Execute(task.Command);
}
_previousTick = now;
```
Initialise `_previousTick = DateTime.Now` at startup — that single line *is* the no-catch-up rule.

**Step 2: Task list UI**

`ListView` of tasks, each rendered as a sentence via `task.Describe()` ("Every Sun at 13:00 → play clip 7 (intro.mp4)"). Per-task enable checkbox. Add / Edit / Remove. Tasks pointing at a missing or removed clip render red — same convention as the grid.

**Step 3: Master enable**

Prominent checkbox above the list, bound to `_scheduler.Enabled`, persisted. When off, the tab shows an unmissable banner. A silently disabled scheduler is a Sunday-afternoon discovery.

**Step 4: Task edit dialog**

Time picker; radio between *weekly* (Mon–Sun checkboxes, all-ticked = daily) and *one-off* (date picker). Command dropdown; the editor adapts — *play clip* shows a dropdown of real boxes ("7 — intro.mp4"), *brightness*/*contrast* show a value field.

**Step 5: Verify in the VM**

Create a task one minute out. Watch it fire. Then disable the master switch and confirm the next one doesn't.

**Step 6: Commit**

```bash
git add src/SimpleWall/UI
git commit -m "feat: scheduler tab, task editor and one-second tick"
```

---

## Task 14: Settings, autostart, logging

**Files:**
- Create: `src/SimpleWall/UI/SettingsTab.cs`
- Create: `src/SimpleWall/Infrastructure/Autostart.cs`
- Create: `src/SimpleWall/Infrastructure/Log.cs`
- Modify: `src/SimpleWall/Program.cs`

**Step 1: Log**

Append-only text log next to the EXE, timestamped, roll at ~5MB. Logs: startup, clip triggers with their source (mouse/OSC/scheduler), scheduler firings, missing files, OSC errors, unhandled exceptions. This file is the only witness to what happened at 3am on a Sunday.

**Step 2: Crash handler**

In `Program.Main`, hook `Application.ThreadException` and `AppDomain.CurrentDomain.UnhandledException` ⇒ log with full stack ⇒ then let it die. Never show a dialog on the wall PC: an unattended machine sitting on a modal error box is worse than a clean restart.

**Step 3: Autostart**

```csharp
using Microsoft.Win32;

namespace SimpleWall.Infrastructure
{
    public static class Autostart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "SimpleWall";

        public static bool IsEnabled()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
                return key?.GetValue(ValueName) != null;
        }

        public static void Set(bool enabled, string exePath)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true))
            {
                if (key == null) return;
                if (enabled) key.SetValue(ValueName, $"\"{exePath}\"");
                else key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
    }
}
```
`HKCU` deliberately: no admin rights, no service, and unticking removes it cleanly.

**Step 4: Settings tab**

OSC port, reply host/port, **the machine's IP addresses in plain selectable text** (so Stream Deck setup means reading the screen, not running `ipconfig`), output window X/Y/W/H with live apply, **Reset output window** button, autostart checkbox, and the OSC status line ("listening on 7000" / "port 7000 in use, OSC disabled").

**Step 5: Verify in the VM**

Tick autostart, check the registry, reboot the VM, confirm it comes back. Untick, confirm the value is gone.

**Step 6: Commit**

```bash
git add src/SimpleWall
git commit -m "feat: settings tab, autostart, logging and crash handler"
```

---

## Task 15: Packaging and Win7 acceptance

The only task that proves the product works. Everything before it is preparation.

**Step 1: Release build**

```bash
ssh wallvm 'cmd /c "cd /d \\Mac\Home\Documents\Coding\simple-wall && dotnet build -c Release src\SimpleWall\SimpleWall.csproj"'
```
Confirm the output folder contains the EXE plus the VLC natives.

**Step 2: Deploy to the wall PC**

Copy the folder over. Install the VC++ redist if Task 2 said it was needed.

**Step 3: Acceptance checklist — on the real machine, on the real wall**

- [ ] Launches on Win7 SP1 x64 with no missing-runtime error
- [ ] Output window lands exactly on the LED strip; no visible chrome
- [ ] A 1964×256 clip loops seamlessly with no stutter
- [ ] Clip switching is acceptable; black frame matches the spike's measurement
- [ ] Brightness/contrast apply live and look right *on the LED panel*, not on a desktop monitor
- [ ] Missing clip ⇒ red box, wall unaffected
- [ ] Stream Deck triggers clips over OSC; press-and-release fires exactly once
- [ ] Grid highlight follows Stream Deck triggers
- [ ] A scheduled task fires on time
- [ ] Master disable stops the scheduler firing
- [ ] Kill the app mid-playback, relaunch ⇒ layout, geometry, brightness and schedule all restored
- [ ] Autostart survives a real reboot of the wall PC
- [ ] Leave it running overnight ⇒ still playing in the morning, log clean, memory not climbing

That last one is the only test for the slow leaks — and this machine is expected to run for months.

**Step 4: Write up**

Record results in `docs/plans/2026-07-16-acceptance.md`, including anything Win7-specific worth knowing next time.

**Step 5: Commit and tag**

```bash
git add docs/plans/2026-07-16-acceptance.md
git commit -m "docs: Win7 acceptance results"
git tag v1.0
```

---

## Deliberately not built

From the design, restated so nobody adds them by reflex: layers/crossfades, saturation/gamma/hue, audio, clip trimming/speed/effects/BPM sync, cron expressions, scheduled playlists, catch-up on missed tasks.

The black frame between clips is the known cost of no layers. If Task 2 or Task 15 shows it's visible on the wall, that's the one to reconsider — with evidence, not by reflex.
