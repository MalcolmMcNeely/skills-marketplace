# Ticket state as a guardrail

Researched 2026-09-08. Anything labelled **measured** was run against github.com from this machine on that date, with `gh` 2.92.0 and a token carrying `admin:ssh_signing_key, gist, read:org, repo, workflow`. Anything else was read from the source cited beside it.

Two guardrails motivate this: refuse to start a ticket whose blockers are still open, and refuse to finish without updating the ticket. Both need GitHub's dependency graph to be machine-readable. It is, with three traps in the way.

## Native dependencies are generally available

GitHub Issues gained first-class blocking relationships on 21 August 2025 ([changelog](https://github.blog/changelog/2025-08-21-dependencies-on-issues/)). Not a preview. An issue can declare which issues it is **blocked by** and which it is **blocking**, "up to 50 issues for each relationship type". The GraphQL schema landed slightly earlier: `blockedBy`, `blocking`, `issueDependenciesSummary` and the `addBlockedBy` / `removeBlockedBy` mutations on 30 July 2025 ([GraphQL changelog](https://docs.github.com/en/graphql/overview/changelog/2025)).

Nothing about it is enforced. GitHub will not stop you closing a blocked issue, assigning it, or working on it. The relationship draws a "Blocked" icon and nothing else. **The enforcement is yours to build.** That is the whole point of the guardrail.

Cross-repository edges are not documented either way, though three things suggest they work: the `issue_dependencies` webhook payload carries a `blocking_issue_repo` object, `gh issue edit --add-blocked-by` accepts an issue "number or URL", and the REST blocker list returns full issue objects carrying `repository.full_name`.

## The whole of guardrail one is a single field

`GET /repos/{owner}/{repo}/issues/{n}` returns an `issue_dependencies_summary` object, and `blocked_by` inside it **counts only open blockers**. GraphQL's own field descriptions draw the line: `blockedBy` is "Count of issues this issue is blocked by", `totalBlockedBy` is "Total count... (open and closed)".

**Measured on this machine**, with no `Accept` header and no API version header:

```
$ gh api repos/cli/cli/issues/10000 --jq '.issue_dependencies_summary'
{"blocked_by":0,"blocking":0,"total_blocked_by":0,"total_blocking":0}
```

So the predicate is one call, no pagination, no state filtering:

```bash
blocked=$(gh api "repos/$REPO/issues/$N" --jq '.issue_dependencies_summary.blocked_by')
[ "$blocked" -eq 0 ] || exit 1
```

The same response also carries `sub_issues_summary`, `parent_issue_url` and `type`.

## Three traps

### 1. This machine's `gh` cannot do it

The `gh` flags and JSON fields for dependencies arrived in **gh 2.94.0** ([changelog, 10 June 2026](https://github.blog/changelog/2026-06-10-manage-sub-issues-types-and-dependencies-from-github-cli/)).

**Measured:** this machine runs gh 2.92.0 (2026-04-28). Its `gh issue view --json` field list contains no `blockedBy`, no `blocking`, no `subIssues` and no `parent`, and `gh issue edit` has no `--add-blocked-by`.

A guardrail written against `gh issue view --json blockedBy` fails here. A guardrail written against `gh api` works here and on 2.94.0. **Write it against `gh api`**, or check `gh --version` at the top of the script and refuse to run below 2.94.0. Per the [v2.94.0 release notes](https://github.com/cli/cli/releases/tag/v2.94.0), the new relationship commands also need GHES 3.19+ if you are not on github.com.

### 2. The blocker list includes closed blockers

`GET /repos/{o}/{r}/issues/{n}/dependencies/blocked_by` returns every blocker, open or closed. Counting the array length is wrong. Filter on state, or use the summary field, which already does.

### 3. `is:blocked` silently lies without `advanced_search=true`

This is the one that will cost an afternoon. The four search qualifiers announced in the changelog (`is:blocked`, `is:blocking`, `blocked-by:`, `blocking:`) are only parsed as dependency qualifiers by advanced search. On the legacy path they degrade to a free-text match on the word "blocked". No error, no warning, plausible results.

**Measured on this machine**, same query, same repository:

| Call | `total_count` |
|---|---|
| `search/issues?q=repo:cli/cli is:issue is:blocked` | **107** |
| the same, plus `-f advanced_search=true` | **0** |

Every sampled item in the 107 had `blocked_by: 0`. The GraphQL `search` connection behaves the same way.

Always pass `advanced_search=true`, or use `gh issue list --search`, which parses correctly. Or skip search entirely and read `issue_dependencies_summary` per issue.

Those qualifiers are, incidentally, absent from GitHub's own search-syntax documentation pages. A documentation gap, not a functionality gap.

## Writing an edge, and the id that catches everyone

To say "A blocks B", POST to **B's** `blocked_by` with **A's** id. There is no `POST .../dependencies/blocking`, and GraphQL has only `addBlockedBy`. The asymmetry is deliberate.

| Method | Path | Body |
|---|---|---|
| `GET` | `/repos/{o}/{r}/issues/{n}/dependencies/blocked_by` | none |
| `POST` | `/repos/{o}/{r}/issues/{n}/dependencies/blocked_by` | `{"issue_id": <int>}` |
| `DELETE` | `/repos/{o}/{r}/issues/{n}/dependencies/blocked_by/{issue_id}` | none |
| `GET` | `/repos/{o}/{r}/issues/{n}/dependencies/blocking` | none |

`issue_id` is the **numeric database id**. Not the issue number, and not the node id. For `cli/cli#10000` those are three different values: `number` 10000, `id` 2716143027, `node_id` `I_kwDODKw3uc6h5Q2z`. Worse, `gh issue view --json id` hands you the node id, which the REST endpoint rejects. Fetch the numeric one with `gh api repos/O/R/issues/N --jq .id`.

## Sub-issues are containment, not ordering

Sub-issues went GA on 9 April 2025 ([changelog](https://github.blog/changelog/2025-04-09-evolving-github-issues-and-projects/)), with a REST API from December 2024. Limits, quoted from [the docs](https://docs.github.com/en/issues/tracking-your-work-with-issues/using-issues/adding-sub-issues): "You can add up to 100 sub-issues per parent issue" and "You can create up to eight levels of nested sub-issues."

They carry no blocking semantics. A parent with open children is not reported as blocked, and `issue_dependencies_summary.blocked_by` ignores children entirely. If a spec issue should not close until its tickets are done, either declare that as dependencies too, or check `sub_issues_summary.completed < total` yourself. The summary is on the plain issue payload, so it costs nothing extra.

One API wart: the DELETE path is singular, `/issues/{n}/sub_issue`, while POST and GET are plural.

## What not to build on

**Tasklist blocks are gone.** The ` ```[tasklist] ` syntax was retired on 30 April 2025, and "the `Tracked` and `Tracked by` fields on projects will no longer be available" ([changelog](https://github.blog/changelog/2025-02-18-github-issues-projects-february-18th-update/)). No API now reports `tracked_in`.

**Plain `- [ ] #123` checklists** still render and still create a cross-reference in the target's timeline, but no API field exposes them as a relationship. Reading them means fetching `.body` and running a regex over free text a human can reformat at any time.

**Parsing a `## Blocked by` heading from the body** is the current `/to-tickets` fallback for trackers without native edges. It works everywhere, and nothing validates it. If you keep it, make the guardrail *write* the native edge whenever it parses one, so the convention decays into the real thing.

## Claiming a ticket: the race you cannot close

There is no compare-and-swap anywhere in the Issues API. No `If-Match`, no ETag preconditions, no conditional writes on labels or assignees ([REST: labels](https://docs.github.com/en/rest/issues/labels)).

`PUT /issues/{n}/labels` replaces the whole label set in one request, which is the closest thing to atomic on offer, but it is last-write-wins rather than test-and-set. Two agents that both read `ready-for-agent` will both succeed at setting `in-progress`, and neither is told.

Assignment is no better as a claim. `POST .../assignees` "Adds up to 10 assignees to an issue" and is additive, so a second agent assigning itself succeeds rather than colliding. It also carries a nasty failure mode: "Only users with push access can add assignees to an issue. Assignees are silently ignored otherwise" ([REST: assignees](https://docs.github.com/en/rest/issues/assignees)). A bot without push access appears to claim the ticket and does not. Always read back.

`gh issue lock` locks the conversation. It reserves nothing.

Three things do help.

1. **In GitHub Actions, `concurrency` is a real mutex.** Group on the issue number and claims serialise ([workflow syntax](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#concurrency)). Useless for a script on a laptop.
2. **Claim then verify.** Write the claim, pause, read back, and settle ties by a deterministic rule such as the earliest `AssignedEvent` in the timeline. This detects the race rather than preventing it, which is usually enough: a wasted start is cheap, two agents pushing the same branch is not.
3. **Prefer the assignee over a label as the claim**, because the timeline gives you `AssignedEvent` with an actor and a timestamp, so "who claimed first" is answerable afterwards.

## There is no "unblocked" event

Nothing fires when an issue's last open blocker closes. The `issue_dependencies` webhook has actions for the edge only: `blocked_by_added`, `blocked_by_removed`, `blocking_added`, `blocking_removed`. The GraphQL timeline union carries `BlockedByAddedEvent` and `BlockedByRemovedEvent` and their blocking counterparts, and no unblocked or resolved event of any kind.

Worse for automation: **`issue_dependencies` and `sub_issues` are not on the list of events that trigger workflows** ([events that trigger workflows](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows)), and neither are `projects_v2` or `projects_v2_item`. A GitHub Actions workflow cannot react to a dependency edge changing at all. Only a GitHub App or a webhook receiver can, and it would have to bounce the event into Actions via `repository_dispatch`.

Two approximations remain.

**Derive it from a close.** `on: issues: types: [closed]` is a supported trigger. When ticket N closes, ask what it was blocking and re-check each downstream ticket:

```bash
gh api "repos/$REPO/issues/$N/dependencies/blocking" --jq '.[].number' |
while read -r downstream; do
  open=$(gh api "repos/$REPO/issues/$downstream" --jq '.issue_dependencies_summary.blocked_by')
  [ "$open" -eq 0 ] && gh issue edit "$downstream" --add-label ready-for-agent
done
```

Catches the common case. Misses a blocker being removed as a dependency, deleted, or transferred.

**Or poll.** Budget from [the rate limit docs](https://docs.github.com/en/rest/using-the-rest-api/rate-limits-for-the-rest-api): 5,000 requests an hour for a PAT, 1,000 an hour per repository for `GITHUB_TOKEN` inside Actions, 100 concurrent requests shared across REST and GraphQL, and for writes "no more than 80 content-generating requests per minute and no more than 500 content-generating requests per hour". Scheduled workflows have a floor: "The shortest interval you can run scheduled workflows is once every 5 minutes", and runs can be delayed under load.

## Scripting notes

**Exit codes.** `gh` documents 0 success, 1 "failed for any reason", 2 cancelled, 4 requires authentication ([exit codes](https://cli.github.com/manual/gh_help_exit-codes)). You get auth failure for free, but **"issue not found" and "the network blipped" are both exit 1.** A guardrail that must tell those apart should drive `gh api` and read the HTTP status:

```bash
if ! body=$(gh api "repos/$REPO/issues/$N" 2>err.txt); then
  grep -q "HTTP 404" err.txt && echo "not found" || echo "other failure"
fi
```

**Non-interactive operation.** `GH_TOKEN` then `GITHUB_TOKEN`, "in order of precedence", with `GH_ENTERPRISE_TOKEN` for Enterprise Server. Set **`GH_PROMPT_DISABLED`** so a missing argument fails loudly instead of hanging a headless run ([environment](https://cli.github.com/manual/gh_help_environment)).

**Closing atomically.** `gh issue close N --comment "..." --reason completed` does the close and its justification in one call, so guardrail two cannot leave a half-applied state.

## Projects v2, if labels are not enough

GraphQL only. There is no REST API for Projects v2 ([using the API to manage projects](https://docs.github.com/en/issues/planning-and-tracking-with-projects/automating-your-project/using-the-api-to-manage-projects)). `gh project` wraps it without hand-written GraphQL, and `gh project item-edit --field "Status" --value "In Progress"` is the ergonomic write.

The argument for it: a single-select field makes the states mutually exclusive by construction, so a ticket cannot be `in-progress` and `done` at once, which labels permit. Capacity is 50,000 items per project.

The arguments against, and there are four. It still has no compare-and-swap, so the race above is unchanged. It needs the `project` token scope, and **measured**, this machine's token has neither `project` nor `read:project`, so it would need `gh auth refresh -s project` first. It is a second API to learn, with node ids and an item-id lookup step, on top of the issue calls the dependency check already needs. And `projects_v2` webhooks cannot trigger Actions, so status changes must be polled.

Built-in project automations cover only three transitions: item added to Todo, issue closed to Done, PR merged to Done. Nothing dependency-aware. The middle one is worth knowing, because it means closing the issue already moves the card, so guardrail two may need to do nothing more than close.

## Both guardrails, end to end

**Refuse to start a blocked ticket.** Works on stock gh 2.92.0, no extra scopes, no headers.

```bash
blocked=$(gh api "repos/$REPO/issues/$N" --jq '.issue_dependencies_summary.blocked_by')
if [ "$blocked" -ne 0 ]; then
  gh api "repos/$REPO/issues/$N/dependencies/blocked_by" \
    --jq '.[] | select(.state=="open") | "  blocked by \(.repository.full_name)#\(.number): \(.title)"' >&2
  exit 2
fi
```

The `select(.state=="open")` is not optional. Exit 2 is what a `UserPromptSubmit` hook needs to block the request, with stderr becoming the reason Claude is shown. See [Hooks as guardrails](hooks-as-guardrails.md).

**Enforce the ticket is updated on finish.** Nothing native enforces this; there is no "must be closed" gate anywhere on the platform. A `Stop` hook reads the ticket's `state` and blocks if it is still open, with the reason naming the omission. That mechanism is measured to work in the hooks report.

## What was not tested

- Writing a dependency edge. Every POST and DELETE above is read from the source; only reads were exercised here.
- Cross-repository blocking edges. Fifteen genuinely-blocked issues were sampled and every blocker was in the same repository, so no cross-repo edge was observed in the wild.
- Anything on Projects v2, because this machine's token lacks the scope.
- Whether the plan tiers named in secondary summaries (Free, Pro, Team, Enterprise Cloud) are accurate. The changelog body does not state them and neither do the docs.
