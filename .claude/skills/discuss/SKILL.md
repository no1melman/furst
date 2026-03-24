---
name: discuss
description: Start a design discussion thread on a topic, saving the full uncompressed conversation to docs/discussions/
allowed-tools: Read, Write, Edit, Glob, Grep, Bash
---

When the user says "I want to discuss [topic]":

1. Check `docs/discussions/` for an existing file on the topic. If one exists, read it and continue from where it left off.
2. If new, create `docs/discussions/<topic>.md` with a `# Title` header.
3. Explore the codebase for relevant context before responding.
4. Have the discussion naturally. After each response, **copy what you said in the chat verbatim into the discussion file** — no rewording, no restructuring, no editing. The file must be an exact record of what was said.
5. Unresolved questions go immediately after the block they relate to, not collected elsewhere.
6. When the user says to "put a pin" in something or move on, note it inline and stop expanding that subtopic.

## Style

This skill **overrides** the project-level brevity rules from CLAUDE.md. Discussions should be clear, well-structured, and use proper grammar. Do not sacrifice clarity or grammar for concision — these are exploratory records, not commit messages.

## Format

```markdown
# Topic Title

## Subtopic

[Full Claude output as written]

### Unresolved
- question 1
- question 2

## Next Subtopic
...
```

## Rules

- The discussion file must be a verbatim copy of what was said in the chat — never reword, restructure, or edit your output when writing it to the file
- Preserve all code blocks exactly as outputted
- Keep unresolved questions next to the conversation block they came from
- If continuing a previous discussion, append — don't rewrite what's there
- The file is a record of exploration, not a decision document (that's what ADRs are for)

## Finalising a discussion

When the user says to finalise, wrap up, or close out a discussion:

1. **Build an implementation order** — extract all decisions from the discussion. Order them by dependency (what must exist before what). Flag anything that depends on unfinished work (e.g., a pending epic).
2. **Analyse for consistency** — check the ordered list against existing ADRs, epics, and earlier discussion decisions. Call out contradictions or gaps.
3. **Identify ADRs** — each major design decision that will constrain future work should become an ADR. Group related decisions into a single ADR where they form a coherent topic. Use the project's existing ADR format (see `docs/adr/`).
4. **Integrate with epics** — map the implementation items into new or existing epics in `tasks.md`. Respect the dependency order. Each epic should be a shippable chunk.
5. **Write it all out** — generate the ADR files, update `tasks.md`, and append a `## Finalisation` section to the discussion file summarising what was produced.
