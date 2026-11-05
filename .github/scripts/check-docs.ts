// Checks the repository's Markdown documentation:
// - relative links point to existing files, and #anchors to existing headings;
// - every tracked Markdown file is reachable from the index (README.md or docs/README.md);
// - repository paths named in inline code exist.
// Usage: node .github/scripts/check-docs.ts [repo-root]   (Node 24+, no dependencies)
import { execFileSync } from "node:child_process";
import { existsSync, readFileSync, statSync } from "node:fs";
import { dirname, join, normalize, relative, resolve, sep } from "node:path";

export interface Problem {
  file: string;
  line: number;
  message: string;
}

export const INDEX_FILES = ["README.md", "docs/README.md"];
const PATH_ROOTS = ["src/", "tests/", "docs/", ".github/", "benchmarks/"];
/** Dated records kept unchanged: their links are checked, but the paths they name describe the code of their time. */
export const HISTORICAL = [/^CHANGELOG\.md$/, /^docs\/adr\//, /^docs\/audit\//, /^docs\/native-evidence-audit-/, /\/results\/[^/]+\.md$/];

/** GitHub-style heading slug. */
export function slugify(heading: string): string {
  return heading
    .trim()
    .toLowerCase()
    .replace(/<[^>]+>/g, "")
    .replace(/\[([^\]]*)\]\([^)]*\)/g, "$1")
    .replace(/(^|[^\p{L}\p{N}])_+|_+(?=[^\p{L}\p{N}]|$)/gu, "$1")
    .replace(/[^\p{L}\p{N}\s_-]/gu, "")
    .replace(/\s/g, "-");
}

interface Parsed {
  anchors: Set<string>;
  links: { target: string; line: number }[];
  codePaths: { path: string; line: number }[];
}

/** Extracts headings, links and inline-code paths, ignoring fenced code blocks. */
export function parseMarkdown(text: string): Parsed {
  const anchors = new Set<string>();
  const counts = new Map<string, number>();
  const links: Parsed["links"] = [];
  const codePaths: Parsed["codePaths"] = [];
  let fence: string | null = null;
  let previousBlank = true;
  text.split(/\r?\n/).forEach((raw, index) => {
    const line = index + 1;
    const fenceMatch = raw.match(/^ {0,3}(`{3,}|~{3,})(.*)$/);
    if (fence === null && fenceMatch) {
      fence = fenceMatch[1];
      return;
    }
    if (fence !== null) {
      // A closing fence uses the same character, is at least as long, and has no info string.
      if (fenceMatch && fenceMatch[1][0] === fence[0] && fenceMatch[1].length >= fence.length && fenceMatch[2].trim() === "") {
        fence = null;
      }
      return;
    }
    const blank = raw.trim() === "";
    const indentedCode = previousBlank && /^( {4}|\t)/.test(raw) && !/^\s*([-*+]|\d+[.)])\s/.test(raw);
    previousBlank = blank || indentedCode;
    if (indentedCode) return;
    const heading = raw.match(/^#{1,6}\s+(.*?)\s*#*\s*$/);
    if (heading) {
      const base = slugify(heading[1]);
      const seen = counts.get(base) ?? 0;
      counts.set(base, seen + 1);
      anchors.add(seen === 0 ? base : `${base}-${seen}`);
    }
    for (const html of raw.matchAll(/<a\s+(?:name|id)="([^"]+)"/g)) anchors.add(html[1]);
    const codeSpans: string[] = [];
    const withoutCode = raw.replace(/`([^`]+)`/g, (_, code: string) => {
      codeSpans.push(code);
      return "``";
    });
    for (const code of codeSpans) {
      // Check each path-like token, including paths inside inline commands (`dotnet test --project tests/X`).
      for (const token of code.trim().split(/[\s=:"']+/)) {
        if (PATH_ROOTS.some((root) => token.startsWith(root)) && /^[\w./-]+$/.test(token)) {
          codePaths.push({ path: token.replace(/[.,;]+$/, ""), line });
        }
      }
    }
    // Every "](target)" closes a link, including the outer link of a badge such as [![b](img)](docs/x.md).
    for (const link of withoutCode.matchAll(/\]\(\s*<?([^)\s>]+)>?(?:\s+"[^"]*")?\s*\)/g)) {
      links.push({ target: link[1], line });
    }
    for (const ref of withoutCode.matchAll(/^\s*\[[^\]]+\]:\s*(\S+)/g)) links.push({ target: ref[1], line });
  });
  return { anchors, links, codePaths };
}

function safeDecode(value: string): string {
  try {
    return decodeURIComponent(value);
  } catch {
    return value;
  }
}

function isExternal(target: string): boolean {
  return /^[a-z][a-z0-9+.-]*:/i.test(target) || target.startsWith("//");
}

export function listMarkdown(root: string): string[] {
  const output = execFileSync("git", ["ls-files", "-z", "--cached", "--others", "--exclude-standard", "*.md"], {
    cwd: root,
    encoding: "utf8",
  });
  return output
    .split("\0")
    .filter((file) => file.length > 0 && existsSync(join(root, file)))
    .filter((file) => !/(^|\/)(node_modules|bin|obj)\//.test(file))
    .sort();
}

export function checkDocs(root: string, files: string[] = listMarkdown(root)): Problem[] {
  const problems: Problem[] = [];
  const parsed = new Map<string, Parsed>();
  const load = (file: string): Parsed => {
    let result = parsed.get(file);
    if (!result) {
      result = parseMarkdown(readFileSync(join(root, file), "utf8"));
      parsed.set(file, result);
    }
    return result;
  };
  const toRepoPath = (absolute: string) => relative(root, absolute).split(sep).join("/");
  const indexed = new Set<string>();

  for (const file of files) {
    const doc = load(file);
    for (const { target, line } of doc.links) {
      if (isExternal(target)) continue;
      const [rawPath, anchor] = target.split("#", 2) as [string, string | undefined];
      const decoded = safeDecode(rawPath);
      const absolute = decoded === "" ? join(root, file) : resolve(root, dirname(file), decoded);
      if (absolute !== root && !normalize(absolute).startsWith(normalize(root) + sep)) {
        problems.push({ file, line, message: `link leaves the repository: ${target}` });
        continue;
      }
      if (!existsSync(absolute)) {
        problems.push({ file, line, message: `broken link: ${target}` });
        continue;
      }
      const repoPath = toRepoPath(absolute);
      if (INDEX_FILES.includes(file)) indexed.add(repoPath);
      if (anchor && repoPath.endsWith(".md") && statSync(absolute).isFile()) {
        if (!load(repoPath).anchors.has(safeDecode(anchor).toLowerCase())) {
          problems.push({ file, line, message: `missing anchor: ${target}` });
        }
      }
    }
    const historical = HISTORICAL.some((pattern) => pattern.test(file));
    for (const { path, line } of historical ? [] : doc.codePaths) {
      const fromRoot = join(root, path.replace(/\/$/, ""));
      const fromDoc = resolve(root, dirname(file), path.replace(/\/$/, ""));
      if (!existsSync(fromRoot) && !existsSync(fromDoc)) {
        problems.push({ file, line, message: `named path does not exist: ${path}` });
      }
    }
  }

  for (const file of files) {
    if (INDEX_FILES.includes(file) || indexed.has(file)) continue;
    problems.push({ file, line: 1, message: "not linked from README.md or docs/README.md" });
  }
  return problems;
}

if (import.meta.main) {
  const root = resolve(process.argv[2] ?? process.cwd());
  const problems = checkDocs(root);
  for (const p of problems) console.error(`${p.file}:${p.line}: ${p.message}`);
  if (problems.length > 0) {
    console.error(`check-docs: ${problems.length} problem(s)`);
    process.exit(1);
  }
  console.log("check-docs: OK");
}
