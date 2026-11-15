# Development setup

Use Unity 6000.6.9f1 for the Editor, Neovim for C#, and Codex with the official
Unity CLI for automation. The project includes Unity Pipeline 1.0.0 and a pinned
[Neovim IDE integration](https://github.com/walcht/com.walcht.ide.neovim).

## Daily workflow

From this repository:

```bash
unity open .
unity status --format json
nvim Assets/Scripts/MandalaWelcome.cs
```

In Unity, open `Assets/Scenes/Main.unity` and press Play. Double-clicking a script
or a Console source location opens Neovim and jumps to the requested line. The
integration starts a GNOME Terminal window and reuses its Neovim instance for
later requests. Terminal settings are available under **Neovim > Settings**.

The local Neovim configuration uses Mason's OmniSharp server with the existing
Blink completion plugin. Existing mappings include `gd` for definition, `gr`
for references, `K` for diagnostics/hover, `<Space>rn` for rename, `<Space>ca`
for code actions, and `<Space>f` for formatting. Open a C# file and run
`:checkhealth vim.lsp` to inspect the connection or `:Mason` to manage servers.
Language-server startup can take a few seconds while it loads the solution.

### Breakpoint debugging

Neovim uses `nvim-dap` and the MIT-licensed
[Unity DAP adapter](https://github.com/walcht/unity-dap), installed locally at
`~/.local/share/unity-dap/v0.1.0`. It runs through the Mono runtime bundled with
Unity 6000.6.9f1. This supports the Unity Editor and Mono players; IL2CPP players
require a different debugger.

Open a C# file in Neovim, press **F9** to toggle a breakpoint, then **F5** to
attach to the running Unity Editor. Enter Play Mode in Unity to exercise the
code. Use **F10** to step over, **F11** to step into, **F12** to step out,
`<Space>dr` for the debugger REPL, and `<Space>dq` to detach. Unity must be in
Debug code optimization mode; the Configure Neovim menu sets this locally.
Restart an existing Neovim session after the initial setup to load the mappings.

The configuration discovers the current project's Editor through Unity CLI and
uses its default Mono debugger port. Set `UNITY_DEBUG_PORT` before starting
Neovim to override that port. After removing or replacing Unity 6000.6.9f1,
update `~/.local/bin/unity-debug-adapter` or set `UNITY_MONO` to the replacement
Editor's `Editor/Data/MonoBleedingEdge/bin/mono` executable.

Unity generates the `.csproj` and `.sln` files used by OmniSharp. These files
are intentionally ignored by Git. After moving the checkout or installing a
different Editor, regenerate them with **Tools > Development > Regenerate C#
Projects**. **Tools > Development > Configure Neovim (Linux)** selects Neovim,
sets up the terminal launcher, and regenerates projects in one operation.

## Codex and Unity CLI

Unity's official Codex plugin is installed in the local Codex configuration.
Its Unity skills become available in a new session. CLI access works directly
through the shell; a separate MCP server is unnecessary for this setup.

Useful commands with the Editor open:

```bash
unity status --format json
unity command --query scene
unity command console get --level error
unity command script eval 'return UnityEngine.Application.unityVersion;'
```

When several projects are open, pass `--project-path /absolute/path/to/project`.
Agents using the Unity skill also pass `--caller plugin --skill unity-cli` on
Editor command invocations.

Unity Personal licensing is active on this machine. CLI account operations
require a separate browser sign-in using `unity auth login`; local Pipeline
commands can work without that sign-in.

## Other machines

Install Unity through Hub and install Neovim, .NET SDK, GNOME Terminal (Linux),
and the official [Unity CLI](https://docs.unity.com/en-us/unity-cli/use-unity-cli).
On X11, `wmctrl` enables focusing the existing Neovim terminal from Unity.
Install OmniSharp through Mason and enable the `omnisharp` Neovim LSP config.
The Neovim settings in this machine's home directory are not part of the repo.
Breakpoint debugging also requires installing `nvim-dap`, the Unity DAP adapter,
and the adapter launcher on that machine.

Install Unity's Codex plugin:

```bash
codex plugin marketplace add Unity-Technologies/unity-agent-plugin
codex plugin add unity@unity-agent-plugin
```

Open the project, allow package import, review any first-launch Unity software
terms, and choose **Tools > Development > Configure Neovim (Linux)**. This menu
uses common Linux Neovim locations; for another OS or terminal, configure the
integration through **Neovim > Settings** instead.

## Builds and tests

Use Unity's **File > Build Profiles** to make a player. Install target-specific
build modules only when needed. For automation, `unity build --help` and
`unity test --help` describe the CLI interfaces. Batch runs require the project
to be closed in the interactive Editor or checked out separately.

The repository currently contains a welcome scene, without a gameplay test
suite. Add EditMode tests for painting logic and PlayMode tests for interaction
as those features are implemented.
