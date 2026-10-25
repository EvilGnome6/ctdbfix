# ctdbfix

Mac-native CTDB verify/repair for CD rips. Open a `.cue`, check against [CUETools Database](http://cue.tools/wiki/CUETools_Database), and write a repaired **FLAC** when parity allows.

Built on libraries from [cuetools.net](https://github.com/gchudov/cuetools.net) (GPL-2.0-or-later).

## Layout

- `engine/` — .NET CLI helper (`ctdbfix-engine`); JSON on stdout, logs on stderr
- `app/` — Mac app (drop / Open With a `.cue`). AppKit shell today; SwiftUI sources ready once full Xcode is installed
- `scripts/sync-engine.sh` — publish engine + copy into app Resources
- `scripts/build-app.sh` — build `app/build/CtdbFix.app`

## Quick start

Requires [.NET 10](https://dotnet.microsoft.com/download) (`brew install dotnet`) and Apple Command Line Tools (or Xcode).

```bash
./scripts/sync-engine.sh
./scripts/build-app.sh
open app/build/CtdbFix.app
```

Engine only:

```bash
cd engine && ./setup.sh
dotnet run --project CtdbFix.Engine -c Release -- verify /path/to/album.cue
dotnet run --project CtdbFix.Engine -c Release -- repair /path/to/album.cue -o /path/to/out.flac
dotnet run --project CtdbFix.Engine -c Release -- submit /path/to/album.cue          # dry-run
dotnet run --project CtdbFix.Engine -c Release -- submit /path/to/album.cue --confirm
```

Repaired audio is written as **FLAC**. After opening the app once, use Finder → right-click a `.cue` → Open With → ctdbfix.

**Submit** is enabled when verify finds a sibling XLD `.log` that passes the quality gate (`No errors occurred`, no hard errors) and CTDB status is `clean` (confirm) or `not_found` (new disc, also requires AccurateRip all-tracks OK with confidence ≥ 2).

## License

GPL-2.0-or-later (same family as CUETools). Third-party components retain their own licenses; see `engine/external/cuetools.net/License.txt` after setup.
