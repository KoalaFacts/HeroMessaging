// Run: node --test .github/scripts/check-docs.test.ts
import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { afterEach, describe, it } from "node:test";
import { checkDocs, parseMarkdown, slugify } from "./check-docs.ts";

const roots: string[] = [];

function repo(files: Record<string, string>): string {
  const root = mkdtempSync(join(tmpdir(), "check-docs-"));
  roots.push(root);
  for (const [path, content] of Object.entries(files)) {
    mkdirSync(dirname(join(root, path)), { recursive: true });
    writeFileSync(join(root, path), content);
  }
  execFileSync("git", ["init", "-q"], { cwd: root });
  return root;
}

function messages(root: string): string[] {
  return checkDocs(root).map((p) => `${p.file}:${p.line}: ${p.message}`);
}

afterEach(() => {
  for (const root of roots.splice(0)) rmSync(root, { recursive: true, force: true });
});

describe("slugify", () => {
  it("matches GitHub heading anchors", () => {
    assert.equal(slugify("Integration Tests"), "integration-tests");
    assert.equal(slugify("F09 — Consumer retries/redelivery!"), "f09--consumer-retriesredelivery");
    assert.equal(slugify("`WithIdempotency` API"), "withidempotency-api");
  });

  it("numbers duplicate headings", () => {
    const parsed = parseMarkdown("# Setup\n## Setup\n");
    assert.deepEqual([...parsed.anchors], ["setup", "setup-1"]);
  });
});

describe("checkDocs", () => {
  it("accepts an indexed, consistent doc set", () => {
    const root = repo({
      "README.md": "See [docs](docs/README.md).\n",
      "docs/README.md": "- [Guide](guide.md#usage)\n",
      "docs/guide.md": "# Guide\n## Usage\nRun `docs/guide.md`.\n",
    });
    assert.deepEqual(messages(root), []);
  });

  it("reports a broken relative link", () => {
    const root = repo({ "README.md": "[gone](docs/missing.md)\n" });
    assert.deepEqual(messages(root), ["README.md:1: broken link: docs/missing.md"]);
  });

  it("reports a missing anchor", () => {
    const root = repo({
      "README.md": "[a](docs/guide.md#nope)\n",
      "docs/guide.md": "# Guide\n",
    });
    assert.deepEqual(messages(root), ["README.md:1: missing anchor: docs/guide.md#nope"]);
  });

  it("reports a doc missing from the index", () => {
    const root = repo({ "README.md": "# Readme\n", "docs/orphan.md": "# Orphan\n" });
    assert.deepEqual(messages(root), ["docs/orphan.md:1: not linked from README.md or docs/README.md"]);
  });

  it("does not count links from non-index docs as indexing", () => {
    const root = repo({
      "README.md": "[a](docs/a.md)\n",
      "docs/a.md": "[b](b.md)\n",
      "docs/b.md": "# B\n",
    });
    assert.deepEqual(messages(root), ["docs/b.md:1: not linked from README.md or docs/README.md"]);
  });

  it("reports a named repository path that does not exist", () => {
    const root = repo({ "README.md": "Run `tests/validate.ps1` first.\n" });
    assert.deepEqual(messages(root), ["README.md:1: named path does not exist: tests/validate.ps1"]);
  });

  it("ignores links and paths inside fenced code, and external links", () => {
    const root = repo({
      "README.md": "```md\n[x](missing.md) `src/missing.cs`\n```\n[site](https://example.com/x.md)\n",
    });
    assert.deepEqual(messages(root), []);
  });

  it("still checks links but not named paths in historical records", () => {
    const root = repo({
      "README.md": "[log](CHANGELOG.md)\n",
      "CHANGELOG.md": "Removed `src/Old.cs`. [gone](docs/gone.md)\n",
    });
    assert.deepEqual(messages(root), ["CHANGELOG.md:1: broken link: docs/gone.md"]);
  });
});
