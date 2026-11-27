# GitSync

English | [简体中文](README.zh-CN.md)

A small Windows desktop tool that syncs **all branches and tags** from a **source
repository** (the upstream, maintained by someone else) to a **target repository**
(the one on your side that receives the sync). Double-click to run — no .NET
installation required.

Typical use cases: keeping a mirror/backup repository in sync with an upstream,
following vendor code, org migrations, cross-platform mirroring. The terminology
matches Gitee / Gitea's "repository sync / fork sync": source (upstream) →
target (receiver).

## Features

- **Multi-project dashboard**: one card per project showing sync status
  (up to date / pending / failed), with automatic background checks
- **Read-only inspection**: compares every branch (fast-forward / new /
  diverged / target-ahead) before anything is pushed
- **Safe sync**:
  - Branches that exist **only in the target repository are never deleted or
    overwritten**
  - Force strategy uses protective `--force-with-lease`, never a blind push
  - Tags are only added; existing same-name tags on the target are never overwritten
  - After pushing, the real server state is verified independently
- **Zero credential handling**: no account or password is ever entered or stored.
  HTTPS credentials go through the local Git Credential Manager; SSH private keys
  stay in `~/.ssh`. The config file only contains paths and remote names
- **SSH wizard**: generate an ed25519 key → copy public key → test connection,
  four guided steps
- **Bilingual UI**: English and Simplified Chinese, switchable in one click
- **Portable**: the config file lives next to the exe (falls back to
  `%LOCALAPPDATA%` when the directory is not writable) — copy it and you're done

## Quick start

### Prerequisites

- Windows 10/11
- [Git for Windows](https://git-scm.com/download/win)
  (close all windows and reopen this tool after installing, otherwise git
  won't be found)

### Usage

1. Double-click `GitSync.exe` (put it in a fixed folder such as `D:\Tools\GitSync`)
2. Click "Add project":
   - Already have a local clone: just pick the repository folder
   - No clone yet: enter the source repository URL, the tool clones it and can
     attach the target remote at the same time
3. Confirm the two remotes (source is usually `origin`; rename or re-point them
   any time via "Remotes…" on the detail page)
4. Click "Check" — read-only, shows the state of every branch
5. Click "Sync" — reviews differences and risky branches once more, then pushes
   and verifies independently

### Divergence strategy

| Strategy | Behavior |
| --- | --- |
| Source wins (default) | Diverged branches are overwritten on the target with the source version, after a confirmation dialog |
| Keep both | A merge commit is created, both sides are preserved; on conflict the merge is abandoned safely and the target stays untouched |

Branches that exist on the target but not on the source are **kept untouched
under every strategy**.

## Command line

```
GitSync.exe --selftest    full logic self-test (temp repositories, no real data)
GitSync.exe --inspect     read-only branch report for the current project, never pushes
GitSync.exe --version     show version
```

The self-test report is written to `%TEMP%\GitSync\selftest-report.txt`.

## Building from source

Requires the .NET 8 SDK (`net8.0-windows`, WPF):

```
dotnet build
dotnet run -- --selftest
```

Portable single-folder package: `publish.bat`.

## Project layout

```
Core/   engine layer, UI-free: GitClient (git wrapper), SyncEngine
        (compare / execute / verify), ProjectRunner (orchestration),
        ConfigStore, SshKeyManager, HttpsCredentialManager
UI/     WPF UI: main window (card wall + detail page + log drawer),
        add-project / clone / remote-settings / credentials dialogs
```

## Security notes

- The program never stores or enters any account, password, token or private key
- HTTPS credentials are written by Git Credential Manager into Windows
  Credential Manager
- SSH private keys always stay in `~/.ssh`; only the public key is read,
  for display and copying
- The config file `vendorsync.config.json` only records paths and remote names

## License

[MIT](LICENSE)
