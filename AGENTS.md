# Repository UI Guardrails

- Never use Tailwind's `text-xs` utility for visible interface copy.
- Never use an arbitrary text-size utility below 14px (for example, `text-[10px]`).
- Use `text-sm` as the minimum size for labels and supporting copy; use `text-base` or larger for primary numeric values.
- Use the shared custom dropdown presentation at every responsive breakpoint. A hidden native select may remain only as a programmatic backing control.
- Run `npm run setup:git-hooks` once per clone. The versioned pre-commit hook runs `npm run check:ui-text` and rejects commits containing tiny-text utilities.
- GitHub Actions enforces the same check for every push and pull request.
