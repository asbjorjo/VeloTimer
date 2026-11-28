@AGENTS.md

## Claude Code

- `AGENTS.md` (imported above) is the source of truth. The specialist definitions live in [.github/agents/](.github/agents/); the thin wrappers in [.claude/agents/](.claude/agents/) load them as Claude Code subagents.
- Claude Code subagents cannot start other subagents, so there is no Coordinator subagent. For substantial work, the main session follows [coordinator.agent.md](.github/agents/coordinator.agent.md) and delegates to the specialist subagents.
- Use `/code-review` for change review and `/security-review` for a dedicated security assessment.
