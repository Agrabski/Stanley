// Release notes and version numbers from the issues each merged PR closes.
//
// Run from CI with actions/github-script (see .github/workflows/ci.yml):
//   draftRelease  - on every push to main: keeps ONE draft GitHub Release
//                   ("vX.Y.Z") listing every PR merged into develop or main since
//                   the last release that main now contains. Publishing that
//                   draft is how a release is made.
//   checkPullRequest - on pull requests: says which issues the PR closes and what
//                   that means for the next version (a notice, never a failure).
//
// The next version comes from the closed issues' labels (or GitHub issue types):
//   "breaking"                       -> major (minor while still on 0.x)
//   "enhancement" / "feature", type Feature -> minor
//   anything else ("bug", no label, a PR closing no issue) -> patch
// The first release is always 0.1.0.

const MARKER = '<!-- stanley-release-draft -->';

/** Where feature PRs land. PRs *between* these (develop -> main, back-merges) are release plumbing, not changes. */
const BRANCHES = ['develop', 'main'];

const BREAKING_LABELS = ['breaking', 'breaking change'];
const FEATURE_LABELS = ['enhancement', 'feature'];
const BUG_LABELS = ['bug'];

/** 'major' | 'minor' | 'patch' for one closed issue. */
function issueKind(issue) {
  const labels = (issue.labels ?? []).map((l) => l.toLowerCase());
  const type = (issue.type ?? '').toLowerCase();
  if (labels.some((l) => BREAKING_LABELS.includes(l))) return 'breaking';
  if (type === 'feature' || labels.some((l) => FEATURE_LABELS.includes(l))) return 'feature';
  if (type === 'bug' || labels.some((l) => BUG_LABELS.includes(l))) return 'fix';
  return 'other';
}

/** The largest bump any merged PR's issues ask for. */
function bumpFor(prs) {
  const kinds = prs.flatMap((pr) => pr.issues.map(issueKind));
  if (kinds.includes('breaking')) return 'major';
  if (kinds.includes('feature')) return 'minor';
  return 'patch';
}

function parseVersion(tag) {
  const m = /^v?(\d+)\.(\d+)\.(\d+)$/.exec(tag ?? '');
  return m ? m.slice(1, 4).map(Number) : null;
}

/** Next version after `previous` ([major, minor, patch] or null for none yet). */
function nextVersion(previous, bump) {
  if (!previous) return '0.1.0';
  const [major, minor, patch] = previous;
  if (bump === 'major' && major > 0) return `${major + 1}.0.0`;
  if (bump === 'major' || bump === 'minor') return `${major}.${minor + 1}.0`;
  return `${major}.${minor}.${patch + 1}`;
}

/** Markdown body: one line per closed issue, grouped; PRs closing no issue under "Other changes". */
function renderNotes(prs, previousTag) {
  const sections = { breaking: [], feature: [], fix: [], other: [] };
  const seen = new Set();
  for (const pr of prs) {
    if (pr.issues.length === 0) {
      sections.other.push(`- ${pr.title} (PR #${pr.number})`);
      continue;
    }
    for (const issue of pr.issues) {
      if (seen.has(issue.number)) continue; // one issue closed by several PRs: list it once
      seen.add(issue.number);
      sections[issueKind(issue)].push(`- ${issue.title} (#${issue.number}, PR #${pr.number})`);
    }
  }
  const titles = { breaking: 'Breaking changes', feature: 'New features', fix: 'Bug fixes', other: 'Other changes' };
  const parts = [MARKER];
  for (const key of ['breaking', 'feature', 'fix', 'other']) {
    if (sections[key].length) parts.push(`## ${titles[key]}\n\n${sections[key].join('\n')}`);
  }
  parts.push(previousTag ? `_Changes since ${previousTag}._` : '_First release._');
  return parts.join('\n\n') + '\n';
}

// ------------------------------------------------------------------ GitHub queries

const PR_FIELDS = `
  number title mergedAt headRefName
  mergeCommit { oid }
  closingIssuesReferences(first: 25) {
    nodes { number title labels(first: 20) { nodes { name } } issueType { name } }
  }`;

function toPr(node) {
  return {
    number: node.number,
    title: node.title,
    mergedAt: node.mergedAt,
    headRefName: node.headRefName,
    mergeCommit: node.mergeCommit?.oid ?? null,
    issues: node.closingIssuesReferences.nodes.map((i) => ({
      number: i.number,
      title: i.title,
      labels: i.labels.nodes.map((l) => l.name),
      type: i.issueType?.name ?? null,
    })),
  };
}

/** PRs merged into `branch` after `since` (ISO date, or null for all), oldest first. */
async function mergedPrsSince(github, owner, repo, branch, since) {
  const prs = [];
  let cursor = null;
  for (;;) {
    const data = await github.graphql(
      `query($owner: String!, $repo: String!, $branch: String!, $cursor: String) {
        repository(owner: $owner, name: $repo) {
          pullRequests(baseRefName: $branch, states: MERGED, first: 50, after: $cursor,
                       orderBy: { field: UPDATED_AT, direction: DESC }) {
            pageInfo { hasNextPage endCursor }
            nodes { updatedAt ${PR_FIELDS} }
          }
        }
      }`,
      { owner, repo, branch, cursor },
    );
    const page = data.repository.pullRequests;
    for (const node of page.nodes) {
      if (!since || node.mergedAt > since) prs.push(toPr(node));
    }
    // Ordered by last update; a PR merged after `since` was also updated after it.
    const oldest = page.nodes.at(-1);
    if (!page.pageInfo.hasNextPage || (since && oldest && oldest.updatedAt <= since)) break;
    cursor = page.pageInfo.endCursor;
  }
  return prs;
}

/** True if `sha` is `head` or one of its ancestors. */
async function contains(github, owner, repo, head, sha) {
  const { data } = await github.rest.repos.compareCommitsWithBasehead({ owner, repo, basehead: `${sha}...${head}`, per_page: 1 });
  return data.status === 'ahead' || data.status === 'identical';
}

/** Change PRs (into develop, or hotfixes straight into main) merged since `since` whose merge commit `head` contains, oldest first. */
async function changesSince(github, owner, repo, head, since) {
  const byNumber = new Map();
  for (const branch of BRANCHES) {
    for (const pr of await mergedPrsSince(github, owner, repo, branch, since)) {
      if (BRANCHES.includes(pr.headRefName) || !pr.mergeCommit) continue;
      if (await contains(github, owner, repo, head, pr.mergeCommit)) byNumber.set(pr.number, pr);
    }
  }
  return [...byNumber.values()].sort((a, b) => a.mergedAt.localeCompare(b.mergedAt));
}

/** The newest published, non-prerelease vX.Y.Z release, or null. */
async function latestRelease(github, owner, repo) {
  const releases = await github.paginate(github.rest.repos.listReleases, { owner, repo, per_page: 100 });
  const published = releases
    .filter((r) => !r.draft && !r.prerelease && parseVersion(r.tag_name))
    .sort((a, b) => b.published_at.localeCompare(a.published_at));
  return { latest: published[0] ?? null, releases };
}

// ------------------------------------------------------------------ entry points

async function draftRelease({ github, context, core }) {
  const { owner, repo } = context.repo;
  const { latest, releases } = await latestRelease(github, owner, repo);
  const drafts = releases.filter((r) => r.draft && (r.body ?? '').includes(MARKER));

  const prs = await changesSince(github, owner, repo, context.sha, latest?.published_at ?? null);
  if (prs.length === 0) {
    for (const d of drafts) await github.rest.repos.deleteRelease({ owner, repo, release_id: d.id });
    core.notice('Nothing merged since the last release; no draft release.');
    return;
  }

  const bump = bumpFor(prs);
  const version = nextVersion(parseVersion(latest?.tag_name), bump);
  const tag = `v${version}`;
  const release = {
    owner, repo,
    tag_name: tag,
    name: tag,
    body: renderNotes(prs, latest?.tag_name ?? null),
    draft: true,
    target_commitish: context.sha, // publishing tags exactly the commit these notes describe
  };

  const [keep, ...stale] = drafts;
  for (const d of stale) await github.rest.repos.deleteRelease({ owner, repo, release_id: d.id });
  if (keep) await github.rest.repos.updateRelease({ ...release, release_id: keep.id });
  else await github.rest.repos.createRelease(release);

  core.notice(`Draft release ${tag} (${bump} bump, ${prs.length} PR(s)). Publish it under Releases to ship it.`);
  await core.summary.addHeading(`Draft release ${tag}`).addRaw(release.body.replace(MARKER, '')).write();
}

async function checkPullRequest({ github, context, core }) {
  const { owner, repo } = context.repo;
  const data = await github.graphql(
    `query($owner: String!, $repo: String!, $number: Int!) {
      repository(owner: $owner, name: $repo) { pullRequest(number: $number) { ${PR_FIELDS} } }
    }`,
    { owner, repo, number: context.payload.pull_request.number },
  );
  const pr = toPr({ ...data.repository.pullRequest, mergedAt: '' });
  if (BRANCHES.includes(pr.headRefName)) {
    core.notice(`${pr.headRefName} -> ${context.payload.pull_request.base.ref}: release plumbing, its changes are counted from the PRs it carries.`);
    return;
  }
  if (pr.issues.length === 0) {
    core.warning('This PR closes no issue, so it will be listed under "Other changes" and only bump the patch version. '
      + 'Link the issue it resolves ("Closes #123" in the description, or the Development box).');
    return;
  }
  const lines = pr.issues.map((i) => `#${i.number} ${i.title} -> ${issueKind(i)}`);
  core.notice(`Closes ${lines.join('; ')}. Next version bump from this PR: ${bumpFor([pr])}.`);
}

module.exports = { draftRelease, checkPullRequest, issueKind, bumpFor, nextVersion, parseVersion, renderNotes, MARKER };
