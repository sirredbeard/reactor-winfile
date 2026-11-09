# Testing

Four suites, four levels. All four target `net11.0` and use xUnit v3 on the Microsoft Testing Platform. They exercise WinFile.Core directly, so they run on any OS. The Windows-only paths in Core (hard links, junctions, registry associations, ACL writes) sit behind OS checks, and the suites cover their portable behavior and their contracts.

## Unit tests: tests/WinFile.UnitTests

Pure logic, no file system writes beyond temp settings files.

- Formatting: byte sizes, grouped numbers, the status line.
- Natural sort: `file2` before `file10`, case-insensitive, stable on equal numbers.
- Entry sorting: all five sort modes, directories first.
- File type filter: wildcards, semicolon pattern lists, hidden filtering.
- INI: parse, round trip, overwrite, fallbacks.
- Settings: defaults on a missing file, full round trip of every field.
- Navigation history: back, forward, forward-stack clearing.
- Goto index: word splitting with default and custom punctuation, duplicate handling.
- Window layout: cascade, both tiles, icon arrangement, the zero-window case.
- Associations: command expansion, the in-memory store, case handling.
- Duplicate names: the `- Copy` rule. MDI manager state.

## Functional tests: tests/WinFile.FunctionalTests

Real operations on real temp trees.

- Copy, move, delete, rename, create: files and recursive directories.
- Conflicts: fail by default, overwrite, skip.
- Same-directory copy produces `- Copy`.
- Progress reporting on copy.
- Scanner: filtering, sorting, drives, subtree queries, recursive size.
- Search: recursion toggle, pattern, modified-after filter, newest-first order.
- Tree model: expand branch, collapse, find.
- Links and attributes: file and directory symlinks, read-only round trip, attribute letters.
- Clipboard model: copy keeps the source list, cut clears after paste, same-directory paste duplicates.
- Selection by pattern. Directory watcher change signal.

## Integration tests: tests/WinFile.IntegrationTests

Services composed over one realistic fixture tree, the way the app composes them.

- Scan, search, and Goto over the same tree.
- Settings loaded from a written Winfile.ini driving scanner filtering.
- Copy, then search finds the copies.
- Rename, then a rebuilt Goto index finds the new name.
- Clipboard cut and paste restructuring the tree.
- Association store and expansion together.
- Per-window navigation inside the MDI manager.
- Tree expansion agreeing with the scanner. Status line composition.
- Search date cutoffs against files the copy pass just wrote.

## End-to-end tests: tests/WinFile.E2ETests

Full runs through `WinFileCli`, the console mode of the shipped binary.

- Help and version exit 0. Unknown verbs exit 2 with a usage hint.
- One scripted scenario: mkdir, mkfile, ls, copy, rename, search, move, delete, with file system checks at each step.
- tree output, goto by words, attrib show and set, a symlink created through the CLI.
- Missing-file copy reports an error and a nonzero exit code.
- Verb detection: the boundary between console mode and GUI launch.

## On Windows, one more level

CI publishes the real `winfile.exe` for x64 and ARM64 and runs it: `--self-test`, `version`, and `help` against the shipped binary itself. That is the check that the AOT build works end to end on the target architecture, not just that the libraries pass on a test agent.
