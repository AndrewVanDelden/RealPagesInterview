---
name: review
description: Cold review of a change against the Review criteria in AGENTS.md. Reads the diff and the code only, never the pull request description, the commit messages, or this conversation. Run before opening or merging a PR.
disable-model-invocation: true
context: fork
agent: code-reviewer
background: false
allowed-tools: Read Grep Glob Bash(git diff *) Bash(git rev-parse *) Bash(git merge-base *)
argument-hint: "[base-ref]"
---

# Cold review

You are reviewing a change you did not write and whose author's account you have not read.
Everything you may use is in the working tree and the diff. Do not run `git log` or `gh`, and
do not read any pull request text: a reviewer that reads the author's explanation inherits the
author's blind spots, and that is the failure this review exists to prevent.

## Scope

The base ref is `$ARGUMENTS` if given, otherwise `dev`. The change under review is the output of
`git diff <base>...HEAD`. If that diff is empty, review the uncommitted work instead with
`git diff HEAD`. Then Read every changed file in full, and every file the changed code calls
into that you need in order to answer the evidence questions below. Do not stop at the hunks:
the removed-guarantee question is answered by the surrounding code, not by the hunk.

## Evidence, before any verdict

Write this section first and completely. It is the review's work shown, and a finding is only
as good as the trace behind it. The bar a finding must meet is in `AGENTS.md`, section
"Review criteria".

1. Removed guarantees. For every line the diff deletes or replaces: what did it guarantee, and
   where does the new code re-establish that guarantee? Cite file and line for both. A
   guarantee with no new home is a finding.
2. Traces. For every counter, accumulator, and branch condition the diff touches: one concrete
   scenario with real values, walked through the new code, with the value at each step and
   the final result. Code that reads correctly and code that computes correctly are different
   claims.
3. Boundaries. For the inputs the changed code handles: what happens on an invalid combination
   of inputs, an empty collection, a null on a path that looks unreachable, or calls made out
   of order? Name the line that handles each case, or the line that does not.

Cost questions (a repeated scan, a re-sort, an allocation or I/O behind a property) belong to
the Bounded Cost benchmark, not to reading. Note one only when the diff itself makes the cost
visible, such as a sort inside a getter that a loop reads.

## Findings

One entry per finding: the rule it violates (correctness, a stated requirement, or a pillar by
acronym), the file and line, the failure scenario in concrete terms, and the evidence item
above that exposed it. Do not report style, hypothetical future needs, or requests for more
abstraction, defensive code, or tests for cases that cannot occur. If no finding meets the
bar, this section is one line: Nothing to report.

## Output

Return exactly two sections, `## Evidence` and `## Findings`, in that order. No preamble, no
summary of the change, no praise.
