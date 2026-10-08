# Project rules

## GitHub release documentation

- Keep release notes short and user-facing. Use exactly these two sections, in this order, with lowercase headings and bullet points:

```markdown
## bug fix
- ...

## features
- ...
```

- Do not include detailed debugging history, implementation walkthroughs, test tables or long explanations in release notes.
- Mention relevant issue numbers and credit reporters using their exact GitHub `@username` in the relevant bullet.
- Describe only implemented changes. Do not claim an issue is fully resolved without evidence; request reporter confirmation when their exact scenario is not verified.
- For the 1.2 and 1.3 publications, the user approved the release drafts and authorized commit, push, tagging, release creation and asset upload. The user will reply to issue #1 personally; do not post an issue comment or close it.

## README preservation

- Keep `README.md` and `README.en.md` exactly as the user's concise versions on GitHub. Do not modify either README during implementation, merging or publication unless the user explicitly requests a README change.
- Preserve the user's remote edits and deletions; do not restore removed sections or add release notes, usage guides or debugging history to either README. Publish approved release notes in GitHub Releases instead.
