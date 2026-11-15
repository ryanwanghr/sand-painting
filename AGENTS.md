# Development

- Use Unity 6000.6.9f1, pinned in ProjectSettings/ProjectVersion.txt.
- This is a Unity C# project. There is no npm application or web build.
- Neovim is the preferred script editor. Do not install or configure VS Code.
- Use the official Unity CLI and the installed unity-cli skill for Editor work.
- Start with `unity status --format json`. Open this project with `unity open .`
  if it is closed. Use `--project-path` to target this checkout when multiple
  Editors are open.
- Discover Editor commands with `unity command --query <term>`. When an Editor
  is connected, use its commands for scenes, prefabs, and assets.
- Inspect errors with `unity command console get --level error`. Wait for
  recompilation after changing C# before evaluating the result.
- Commit .meta files with their assets and keep Packages/packages-lock.json.
  Generated .csproj/.sln files, Library, Temp, and UserSettings stay untracked.
- Regenerate language-server projects through Tools > Development > Regenerate
  C# Projects in Unity, rather than editing generated project files.
- Use Unity for builds and runtime validation. A successful .NET language-server
  load does not establish that a Unity player builds or plays correctly.
