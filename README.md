# RepoAtlas

[![.NET](https://github.com/avarnon/RepoAtlas/actions/workflows/dotnet.yml/badge.svg)](https://github.com/avarnon/RepoAtlas/actions/workflows/dotnet.yml)

RepoAtlas is a local [Model Context Protocol](https://modelcontextprotocol.io) (MCP) server that catalogs the git repositories on your machine. It scans configured root directories for git repos, then lets an MCP client (like Claude Code) read, describe, tag, and link them as a durable, queryable catalog — so an AI agent can answer "what repos do I have, what do they do, and how do they depend on each other?" without you re-explaining it every session.

## Why

If you work across dozens of repos, an agent has no persistent notion of what they are or how they relate. RepoAtlas gives it one: a small YAML-backed catalog that survives across sessions, merging curated metadata (descriptions, tags, dependency links) with live facts read straight from git (remotes, current branch, HEAD, fork lineage) and each repo's README.

## Features

- **Discovery** — recursively scans configured root directories for git repositories — normal repos (a `.git` folder), submodules and bare repos, and linked worktrees (a `.git` *file*) — instead of losing them or recursing into their internals, and derives a stable id (`owner/repo`) from each repo's `origin` remote, falling back to the folder name when there's no `origin` remote or its URL doesn't parse into an `owner/repo` pair (two origin-less repos sharing a folder name will collide, same as any other id collision, below). A repo reached via a symlink/junction is cataloged, but the scanner won't descend *through* one into further subdirectories (to avoid an infinite loop if it points back at an ancestor), and recursion is capped at 64 levels below each root; well-known dependency/build directories (`node_modules`, `bin`, `obj`, `.venv`, `venv`) are never descended into either, to keep scans fast. A linked worktree shares its main repo's `origin` (and so its derived id) — the catalog keeps one entry for the pair, same as any other id collision.
- **Catalog** — a durable YAML store of curated repo metadata: description, tags, and directed dependency links (`fromId` depends on `toId`).
- **Live git facts** — remotes, current branch, HEAD commit, and fork lineage are read from disk on demand, not cached.
- **MCP resources** — `repo://` lists every known repo; `repo://{id}` returns full detail for one, merging catalog data with live git facts and README contents.
- **MCP tools** — `AddRoot`, `RemoveRoot`, and `ListRoots` to curate the scanned directories; `UpdateDescription`, `AddTag`, `RemoveTag`, `AddDependency`, and `RemoveDependency` to curate the catalog; `Rescan` to refresh it against disk. `AddRoot` refuses a filesystem root (e.g. `C:\` or `/`) outright, and — if `REPOATLAS_ALLOWED_ROOT_BASES` is configured — refuses any path outside that allowlist.
- **OpenTelemetry** — tracing and metrics for every tool/resource invocation, exportable via OTLP.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Git repositories on disk to scan

## Getting started

Build and run the server:

```bash
dotnet build
dotnet run --project src/RepoAtlas.McpServer
```

RepoAtlas speaks MCP over stdio, so it's meant to be launched by an MCP client rather than run interactively. For example, in Claude Code's `.mcp.json`:

```json
{
  "mcpServers": {
    "repoatlas": {
      "command": "dotnet",
      "args": ["run", "--project", "path/to/RepoAtlas/src/RepoAtlas.McpServer"]
    }
  }
}
```

On startup, RepoAtlas rescans its configured root directories and loads its catalog before accepting MCP requests. Root directories are stored in the catalog itself (not a startup argument) — curate them with the `AddRoot`/`RemoveRoot`/`ListRoots` tools, then call `Rescan` to pick up the change. (The YAML data file can still be hand-edited directly if you prefer: every tool that *mutates* the catalog — not just `Rescan` — re-reads the file from disk immediately before applying its change, so a hand edit is picked up, and never clobbered, by whichever mutating tool call happens next. Read-only resources/tools like `repo://` and `ListRoots` serve from an in-memory copy, so they won't reflect a hand edit until some mutating call — `Rescan` included — has run since. A hand-edited *root*, specifically, only takes effect on repos once `Rescan` is actually called, since only `Rescan` re-scans the filesystem.)

## Configuration

| Environment variable | Purpose | Default |
| --- | --- | --- |
| `REPOATLAS_DATA_PATH` | Path to the YAML catalog file | Windows: `%APPDATA%\RepoAtlas\data.yaml` · Linux: `~/.config/RepoAtlas/data.yaml` · macOS: `~/Library/Application Support/RepoAtlas/data.yaml` |
| `REPOATLAS_ALLOWED_ROOT_BASES` | Allowlist of directories `AddRoot` may add roots under, delimited like `PATH` (`;` on Windows, `:` elsewhere) | unset (unrestricted) |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | OTLP endpoint for traces/metrics; when unset, telemetry is collected but not exported | unset |

## Project layout

```text
src/RepoAtlas.McpServer/
  Discovery/   repo scanning and id derivation
  Git/         live git fact reading (LibGit2Sharp)
  Mcp/         MCP resource and tool definitions
  Models/      persisted catalog document types (RepoEntry, DependencyLink, ...)
  Storage/     atomic, durable YAML persistence
tests/RepoAtlas.McpServer.Tests/
```

## Testing

```bash
dotnet test
```

## License

MIT — see [LICENSE](LICENSE). Third-party dependency licenses are listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
