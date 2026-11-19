# Zek PC Sayaç — Claude Development Plan

**Audit first. No code changes until the audit and roadmap are approved.**
Repository: nicewo/ZekPcSayac
## Mission
Make this repository reproducible, stable, maintainable and accurately documented. Preserve existing useful behavior; avoid speculative rewrites.
## Phase 0: Audit only
Read README/docs/source/project files/configuration/history. Verify README claims. Classify features implemented/partial/placeholder/broken/unverified. Run available build/tests and record exact commands/results. Check secrets, .gitignore, dependency/runtime versions, TODOs, duplicated/dead code and unsafe defaults. Report evidence with paths, blockers, risks and unknowns. Do not edit files.## Specific audit focus
Auth/Firestore rules, data model, usage measurement, session lifecycle, multi-device behavior, offline/error handling, privacy, rule tests and deployment.
## Phase 1: Hygiene and baseline
After approval: fix confirmed documentation/config hygiene, dependency pinning, ignore rules, reproducible build/test baseline; CI if justified.
## Phase 2: Prioritized implementation
Derive tasks from findings. For each task specify priority, rationale, dependencies, scope, risk/effort and acceptance criteria. Correctness/security/reliability before polish.## Phase 3: Tests and verification
Add regression/unit/integration tests. Cover normal/failure paths, edge cases and persistence. Label anything not verified in target environment/hardware.## Phase 4: Documentation and release readiness
Setup/build/run/test, troubleshooting, known limitations, release checklist and deployment steps. Never include secrets.## Claude workflow
1. Audit only, no edits. 2. Report evidence and roadmap. 3. Wait for approval. 4. Implement one milestone at a time, scoped commits. 5. Run checks after each milestone and report actual results. 6. Ask before destructive changes, migrations, deployments, hardware actions or breaking changes. 7. Update this plan only with verified progress.## Acceptance
Documented reproducible build/tests; clear scope; critical risks fixed or tracked; core behavior verified; limitations honest
## Checklist
- [ ] Audit - [ ] Roadmap approved - [ ] Hygiene baseline - [ ] Milestones verified - [ ] Release docs
## First task for Claude
Read this plan and the entire repo. Return evidence-based audit and roadmap only. Make no edits.