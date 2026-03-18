# Learnings

## 2026-03-18
Always read `Talk.md` again before concluding current repository state in a shared workspace. A collaborator may have added or changed large parts of the project after an earlier scan, and stale assumptions will produce wrong findings.
When an imported upstream codebase is built with `StyleCop.Analyzers` and `TreatWarningsAsErrors`, first separate true compile failures from analyzer-policy failures. Large error counts can be mostly policy noise rather than functional breakage.
When CS0246 appears for a Jellyfin event-args type, verify namespace imports before blaming package references. A working sibling project that resolves the same type with an extra `using MediaBrowser.Controller.Library;` is strong evidence that the issue is a missing using directive.
When packaging Jellyfin plugins, zip the full `dotnet publish` output rather than only `*.dll`. SQLite and other runtime assets can be required at runtime even when the main plugin assembly builds cleanly.
When a repo contains multiple plugin projects, normalize analyzer policy per project before assuming one successful fix will generalize. A new plugin can still inherit strict global analyzer settings and fail CI long after the imported sibling project has been quieted.
When a Jellyfin API compile error names a missing permission enum or helper, verify the extension namespace before replacing the whole approach. The current admin check comes from `Jellyfin.Data` plus `Jellyfin.Database.Implementations.Enums.PermissionKind`, not from `MediaBrowser.Model.Users`.
