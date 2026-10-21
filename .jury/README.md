# .jury

Project-local state of the ES Acceptance Jury skill: user goal (append-only), plan and its lock,
progress, open items, the architecture baseline, and receipts of real juror reviews
(packets sent, reviews returned). Commit it with the project. Spec: references/state.md of the skill.

After any new session, compaction or window change, run `jury_state.py resume --project <root>` first.
