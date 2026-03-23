---
name: discuss
description: Start a design discussion thread on a topic, saving the full uncompressed conversation to docs/discussions/
allowed-tools: Read, Write, Edit, Glob, Grep, Bash
---

When the user says "I want to discuss [topic]":

1. Check `docs/discussions/` for an existing file on the topic. If one exists, read it and continue from where it left off.
2. If new, create `docs/discussions/<topic>.md` with a `# Title` header.
3. Explore the codebase for relevant context before responding.
4. Have the discussion naturally. After each response, **append your full output** to the discussion file — every code block, explanation, and reasoning. Do not summarise or compress.
5. Unresolved questions go immediately after the block they relate to, not collected elsewhere.
6. When the user says to "put a pin" in something or move on, note it inline and stop expanding that subtopic.

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

- Never summarise or compress the conversation
- Preserve all code blocks exactly as outputted
- Keep unresolved questions next to the conversation block they came from
- If continuing a previous discussion, append — don't rewrite what's there
- The file is a record of exploration, not a decision document (that's what ADRs are for)
