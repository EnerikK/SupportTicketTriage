# Working agreement — Support Ticket Triage

The full project specification is in `docs/BUILD-SPEC.md`. Read it before doing anything.
This file holds the rules that apply to **every** session, whatever we're working on.

## Who I am

A .NET developer with ~2 years of experience (ASP.NET Core, EF Core, PostgreSQL, React/TypeScript),
building this as a portfolio project for mid-level .NET + React roles.

**The overriding constraint:** I must be able to explain every architectural decision and every
non-obvious line in a technical interview. Code I cannot defend is worse than no code.

## How to work with me

1. **Before writing code, propose a plan** — solution structure, project layout, data model, and the phase order. Wait for my approval.
2. **Work in phases.** At the end of each, stop. Show me what you built, explain the decisions in terms I could repeat to an interviewer, and flag anything you did that I might not be able to defend. Wait for my go-ahead before continuing.
3. **Ask when ambiguous.** Don't assume and don't silently pick. If a requirement has two reasonable readings, ask me.
4. **Commit as you go, to the rules in *Commit and branch discipline* below.** The git history is part of the deliverable — a reviewer reads it before they read the code.
5. **Tell me when I'm wrong.** If something in this spec is a bad idea, say so before building it.
6. **Flag anything I should be able to explain but probably can't yet**, so I can go and learn it rather than discovering the gap in an interview.

## Explicit anti-goals

- **Do not over-architect.** No MediatR, no CQRS, no event sourcing, no microservices, no repository interface with a single implementation added "for testability." A clean layered structure is correct for an app this size. If you genuinely think a pattern earns its place, argue for it first and record the reasoning in the README — an interviewer asking "why is MediatR here?" is a trap I will not survive by saying "it's best practice."
- **Do not build an agent framework.** This is a pipeline with one model call per stage, not an autonomous loop. Autonomy adds failure modes and subtracts credibility.
- No secrets in the repo — user-secrets locally, environment variables in Compose, and document what's needed.
- No abandoned scaffolding, commented-out code, or TODOs in the final state.
- Don't gold-plate the React UI. It needs a ticket queue, a detail view showing the draft with its citations and confidence, and approve/edit/reject controls. That's all.

## Commit and branch discipline

The git history is a reviewed artifact, not a byproduct. Treat it as part of the deliverable.

- **One logical change per commit.** A phase is not a commit — a phase is typically 5–15 commits. If a commit message needs the word "and", it should probably be two commits.
- **Tests ship in the same commit as the code they cover.** Never a `feat:` commit followed by a separate `add tests` commit; that history says I wrote tests as an afterthought.
- **Conventional commit format** (`feat:`, `fix:`, `test:`, `refactor:`, `docs:`, `chore:`). Subject line under ~70 characters, imperative mood.
- **Bodies where the reasoning isn't obvious.** For any commit involving a trade-off, add two or three lines explaining *why* — what alternative was rejected and on what grounds. These are the commits an interviewer quotes back at me, so they're worth the keystrokes.
- **Never commit broken states.** Every commit builds and passes tests. No `WIP`, no "fix previous commit" chains, no commented-out code.
- **Feature branches and pull requests, even though I'm working solo.** One branch per phase or sub-feature, a PR with a real description of what changed and why, then merge. This mirrors how I work professionally and gives a reviewer something to read besides raw diffs.
- **Stop and let me review before each commit.** Show me the diff and your proposed message. I'll approve, amend, or ask for a split. This is the mechanism that keeps me able to explain the code — if I'm approving diffs I don't understand, we're building the wrong thing and I want you to tell me so.
- **Configure git to commit under my identity.** Do not add tool-attribution trailers or co-author lines to commit messages.