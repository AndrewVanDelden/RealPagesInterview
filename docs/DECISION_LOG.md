# Decision log

One paragraph per decision, in the recording form of `~/.agent-rules/ARCHITECTURE.md`: the
question, the options, the recommendation, what it scopes, the evidence, the numbered assumption
it depends on (assumptions are the table in [DESIGN.md](DESIGN.md) section 7). A task that
cannot cite a paragraph here is not scheduled. Dates are when the decision was taken; D1 to D8
were first written in the retrospective of 2026-09-06 and are restated here under the frame D9
sets.

## Starting decisions (2026-09-07)

**S1. What runs.** Question: the shape of the deployable. Options: the default, a library plus
a thin command-line entry point; an API or a worker. Recommendation: the default. Scopes: the
entry point parses arguments, wires the composition root, runs the per-record loop, and holds
no rules. Evidence: the statement's verbs, "reads the input record" and "produces output"; a
file arrives, a file leaves. Assumption: none.

**S2. What code decides and what is delegated.** Question: the boundary between deterministic
code and the model. Options: the default, code owns every reproducible decision and the model
writes prose and picks from a code-owned catalog under constrained decoding; the alternative,
the model decides actions. Recommendation: the default. Scopes: the policy catalog, the
composer prompt, the evaluator's semantic checks. Evidence: the statement asks for an
autonomous agent whose behavior is auditable against thresholds; a model choosing actions
cannot be replayed or proven. Assumption: A18.

**S3. Where the seams are.** Question: where interfaces exist. Options: the default, one seam
per external or non-deterministic dependency with a real and an offline implementation on the
day it is created; the alternative, an interface per component. Recommendation: the default.
Scopes: three interfaces stay (composer, completion client, safety validator); every other
interface goes as the sprint that touches it lands (D7); time is a value passed in (D10).
Evidence: at the retrospective eleven interfaces existed and eight had one implementation and
no test substitute. Assumption: none.

**S4. How done is measured.** Question: whether evaluation is part of the deliverable.
Options: the default, an evaluator built before the product and proven able to fail; the
alternative, none. Recommendation: the default. Scopes: Sprint 2 precedes every decision-code
sprint; the definition of complete in D6. Evidence: the retrospective's finding 5, a scorer
that could not fail on two of its own fields. Assumption: none.

## Decisions

**D1. Input contract (revised 2026-09-07).** Question: what the reader accepts, reports, and
refuses. Options: a hand list of required fields fitted to the files seen; or three required
members and everything else optional with a named default. Recommendation: the second.
`task_id`, `consent`, and `channel_preferences` are required and a line missing one is an
error row naming the member; every other member is optional, its absence gets a default and a
diagnostics line naming the field; members the record types do not declare are kept as
extension data, listed in diagnostics, and logged per record; a value-type property on an input
record is either required or nullable, enforced by a reflection test over every record type.
Scopes: D2, D4, D5, Sprint 3. Evidence: DESIGN.md section 2; the statement promises cases the
samples do not show; the retrospective's finding 4 (silent year-0001 dates). Assumptions: A16,
A17.

**D2. Decision model (revised 2026-09-07).** Question: how `next_action` is chosen. Options: a
policy table with one row per stage observed in the twelve; or a catalog with the rows the
two samples justify plus one generic row, and a semantic score for what the catalog cannot
name. Recommendation: the second, under D9. Consent first: not contactable gives `no_op` with
reason `no_contact_consent` and nothing else runs. The catalog is one data file keyed on
persona and stage, carrying the action template and the default call to action; the rows are
prospect `new` and prospect `open` from the samples and the generic row; the horizon rule of A7
picks the template's branch; a persona or stage with no row uses the generic row and the
diagnostics name the fallback; the planner returns a `Result` and never applies the nearest
rule to a record it cannot classify. `NextAction` is one record with `Type` and nullable
`Name`, `Value`, `Reason`, nulls omitted on the wire. Scopes: Sprint 4, the orchestrator order.
Evidence: section 3 of DESIGN.md; playbook steps 41 to 43. Assumptions: A7, A8.

**D3. Output contract.** Question: what suppression looks like and what diagnostics carry.
Options: a null `next_message`; or an object with channel `none` and null fields.
Recommendation: the object, since the output always has both members; the evaluator treats
both spellings and a null channel as one value. Diagnostics carry `suppression_reason`
(`none`, `no_contact_consent`, `composition_failed`, `safety_violation`), the decision inputs,
every defaulted field, every fallback, and the earned states: `consent_verified` only when the
selector ran, `fair_housing_check_passed` as the validator's verdict, `brand_style_applied` as a
check that can fail, any other name as not earned. Scopes: Sprint 3, Sprint 7. Evidence: the
statement's "(or not sent)"; A2, A14.

**D4. Scheduling (revised 2026-09-07).** Question: what `send_at` is a function of. Options:
per-stage day offsets and minutes fitted to the twelve; or the channel slot on the first day at
or after max(reference time, `last_interaction`) in the record's timezone, minutes not modeled.
Recommendation: the second, under D9. Scored to the day and to the hour. Unknown timezone is
UTC plus a diagnostic; daylight-saving transitions get property tests. Scopes: Sprint 5, D6's
`send_at` rule. Evidence: section 3; the two samples fix the hour by channel and nothing else.
Assumptions: A4, A5, A6.

**D5. Composition (revised 2026-09-07).** Question: what the message is a function of.
Options: templates per stage fitted to the twelve, Spanish included; or templates keyed on
persona and channel built from the facts the record carries, in English, with any other
language delegated to the model composer and reported in template mode. Recommendation: the
second, under D9. The call-to-action type comes from `primary_cta` through the vocabulary
table, unknown values pass through, absent means the generic `reply`; sms carries numbered
options, email carries a link built from the property slug; the model prompt carries persona,
stage, language, every date the record has, the stated interests, and the catalog's call to
action, with structured output constraining the type. Body semantics are scored by the judge,
not asserted. Scopes: Sprint 6. Evidence: section 3; A9 to A13.

**D6. Evaluation contract (revised 2026-09-07).** Question: what is scored and against which
data. Options: score the twelve as a training set; or score both the twelve and a synthetic
set as evaluation sets, never fitted to. Recommendation: the second, under D9. Fields and
granularity are DESIGN.md section 6. The scorer proof runs in the suite. Definition of
complete: every record in both sets produces a valid output row with diagnostics that explain
it; the deterministic checks the input can decide (channel, consent, language, opt-out, safety,
payload shape) pass on every record; the honest numbers for `send_at` and `next_action.type`
are reported in the README and never targeted. Scopes: Sprint 2, the README. Evidence: S4; the
requester's answer, D9. Assumption: A19.

**D7. Structure.** Question: what stays, what goes, what the orchestrator reads like. Options:
collapse interfaces up front; or let each sprint delete the interface it touches.
Recommendation: the second. Kept: composer, completion client, safety validator. Removed as
touched: consent gate, channel selector (merged into one `Select` returning an option),
scheduler, planner, agent, evaluator, record writer; the record reader is already concrete.
The orchestrator has six numbered steps, one comment each, matching the DESIGN.md diagram.
Scopes: Sprint 8. Evidence: S3. Assumption: none.

**D8. Gates.** Question: what runs without a person remembering. Options: the default,
`.github/workflows/test.yml` running `dotnet build` and `.\test.ps1` on every push and pull
request with branch protection on `dev` requiring the `test` check; or none. Recommendation:
the default, taken 2026-09-07. `test.ps1` exits with the `dotnet test` exit code so the check
can go red. Applied 2026-09-07 after the first green run on PR #16: `dev` requires the `test`
context, refuses force pushes and deletion, and does not enforce on admins, so the owner can
override in an emergency. Scopes: Sprint 1; every later PR. Evidence: the retrospective's finding that
nothing governed truth against data between sprints. Assumption: none.

**D9. What the twelve-record file is (2026-09-07).** Question: whether rules may be fitted to
`holdout_12.jsonl`. Options: treat it as the training set and fit a per-stage table; or treat
it as unknown, build from the two-record file, and use its curveball categories only to shape
the risk register. Recommendation: the second, on the requester's answer that the twelve are
unknown to the design and future sets may be harder. Scopes: D2, D4, D5, D6, the definition of
complete. Evidence: the requester, 2026-09-07; the statement, "learns what to do only from
input data." Assumption: A19.

**D10. Reference time (2026-09-07).** Question: how a run learns the date its send times are
relative to. Options: a `--now` flag with the current UTC time as default; or a default derived
from the latest `last_interaction` in the batch. Recommendation: the flag, on the requester's
answer. The `--now` flag does not exist yet; it lands with the CLI contract work in Sprint 3.
The documented run against the twelve will pass `--now 2025-12-09T00:00:00-06:00`, and the
README will record that command once the flag ships. Scopes: D4, the CLI, Sprint 3. Evidence:
section 3, the send-day row; the field is absent from most records. Assumption: A4.

**D11. Where the twelve-record file lives (2026-09-07).** Question: whether CI can read the
evaluation set. Options: in the repo beside `sample.jsonl`, linked into the test output; or
outside the repo, scored by hand. Recommendation: in the repo, on the requester's answer.
Scopes: Sprint 2's fixtures, D8's check. Evidence: a file only two session scratchpads held
would have been lost. Assumption: none.

**D12. Phase 0 restart (2026-09-07).** Question: whether Phase 0 restarts at step 1 or
continues at step 4. Options: either. Recommendation: restart, on the requester's answer;
DESIGN.md is rewritten from step 1 and the retrospective's section 7 plan is superseded by
DESIGN.md section 9. Scopes: everything after. Evidence: the retrospective's section 9.
Assumption: none.
