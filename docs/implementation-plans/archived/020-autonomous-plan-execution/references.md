# References

## GitHub Copilot CLI Documentation

- [Adding agent skills for GitHub Copilot CLI](https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/add-skills) — skill format, `SKILL.md` structure, `allowed-tools` frontmatter, skill discovery from `.github/skills/`
- [Quickstart for automating with GitHub Copilot CLI](https://docs.github.com/en/copilot/how-tos/copilot-cli/automate-copilot-cli/quickstart) — `-p` flag for non-interactive prompts, scripting examples
- [Running GitHub Copilot CLI programmatically](https://docs.github.com/en/copilot/how-tos/copilot-cli/automate-copilot-cli/run-cli-programmatically) — piped input, CI/CD integration, `--no-ask-user`, shell scripting patterns
- [GitHub Copilot CLI programmatic reference](https://docs.github.com/en/copilot/reference/copilot-cli-reference/cli-programmatic-reference) — full flag reference (`--allow-all`, `--model`, `--share`, `--agent`, `--secret-env-vars`), environment variables (`COPILOT_ALLOW_ALL`, `COPILOT_MODEL`, `COPILOT_HOME`), tool filters, model precedence
- [Authenticating GitHub Copilot CLI](https://docs.github.com/en/copilot/how-tos/copilot-cli/set-up-copilot-cli/authenticate-copilot-cli) — auth methods (OAuth device flow, fine-grained PAT, GitHub App user-to-server), env var precedence (`COPILOT_GITHUB_TOKEN` > `GH_TOKEN` > `GITHUB_TOKEN`), credential storage (keychain/Credential Manager), BYOK/offline mode, supported token types

- [Creating and using custom agents for GitHub Copilot CLI](https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/create-custom-agents-for-cli) — `.agent.md` file creation, project vs user agents, programmatic invocation with `--agent`
- [Custom agents configuration](https://docs.github.com/en/copilot/reference/custom-agents-configuration) — YAML frontmatter (`name`, `description`, `model`, `tools`, `disable-model-invocation`), tool aliases, MCP server config, model pinning in agent definition
- [Azure DevOps REST API — Git](https://learn.microsoft.com/en-us/rest/api/azure/devops/git/) — PR creation via `az repos pr create`, token scopes for ADO

## Key Decisions Derived from Docs

- **Non-interactive invocation**: `copilot -p "prompt" --agent autopilot --no-ask-user --share=transcript.md`
- **No `-s` flag**: omitting silent mode enables live output streaming to host terminal
- **Auth for containers (GitHub)**: fine-grained PAT with Copilot Requests + Contents + PR permissions. `gh auth setup-git` configures git credential helper.
- **Auth for containers (ADO)**: `az login --use-device-code` on host → mount `~/.azure/` → `az account get-access-token` for git credential helper
- **Auth for corporate (GitHub)**: OAuth device flow pre-auth on host → token in Credential Manager → extracted and passed to container
- **Token auto-redaction**: `COPILOT_GITHUB_TOKEN` values are redacted by default in CLI output
- **Permissions via env var**: `COPILOT_ALLOW_ALL=true` as alternative to `--allow-all` flag
- **Custom agent**: `.github/agents/autopilot.agent.md` pins model + instructions. Invoked with `--agent autopilot`. Runs as isolated subagent with own context window.
- **Token security**: use `--env-file` instead of `-e TOKEN=value` to avoid process table exposure
- **Credential Manager**: use `CredentialManager` PowerShell module (`Get-StoredCredential`), NOT `cmdkey` (can't read passwords)
