# Learnings

## 2026-03-18
Always read `Talk.md` again before concluding current repository state in a shared workspace. A collaborator may have added or changed large parts of the project after an earlier scan, and stale assumptions will produce wrong findings.
When an imported upstream codebase is built with `StyleCop.Analyzers` and `TreatWarningsAsErrors`, first separate true compile failures from analyzer-policy failures. Large error counts can be mostly policy noise rather than functional breakage.
When CS0246 appears for a Jellyfin event-args type, verify namespace imports before blaming package references. A working sibling project that resolves the same type with an extra `using MediaBrowser.Controller.Library;` is strong evidence that the issue is a missing using directive.
When packaging Jellyfin plugins, zip the full `dotnet publish` output rather than only `*.dll`. SQLite and other runtime assets can be required at runtime even when the main plugin assembly builds cleanly.
