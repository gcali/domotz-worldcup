# Workspace worldcup

This is a JIRA-keyed workspace managed by `domotz-worktree`.

## Structure

- `wt.toml` — workspace manifest (source of truth for components, branches, metadata)
- `checkouts/` — git worktrees of the components checked out for this story
- `notes/` — task and progress tracking (managed by you, Claude)

## Notes conventions

Use the `notes/` directory to track tasks, progress, and context for this workspace.

- Create markdown files for distinct tasks or topics (e.g., `notes/backend-refactor.md`)
- Update existing files as progress is made
- Include enough context that work can be resumed after interruption
- Only persist notes after user confirmation

## Manual tests checklist

When the `manual_tests` optional plugin is enabled the scaffold also creates
`notes/manual_tests.md` with the team's house template. While implementing
the story you MUST keep that checklist aligned with the code you are
writing:

- Add a new step whenever you introduce behavior that needs a human-eyes
  verification (UI flow, log signature, metric, end-to-end staging check).
- Update or remove a step when the corresponding code path changes meaning
  or is dropped, so the file stays an accurate pre-merge gate rather than
  drifting into noise.
- Tick the checkboxes as you actually exercise each step on staging — the
  file is meant to be the reviewer's confidence signal that the fix was
  validated, not just claimed.

## Key commands

- `domotz-worktree show worldcup` — view workspace status
- `domotz-worktree add worldcup <repo>` — add a component
- `domotz-worktree refresh worldcup` — pull all components
