<!-- Keep PRs to a single focused vertical slice. -->

## Summary

<!-- What does this change and why? -->

Closes #

## Milestone

<!-- Which milestone does this belong to? -->

## Checklist

- [ ] Written test-first (red → green → refactor)
- [ ] `dotnet build Daybook.slnx` passes (warnings are errors)
- [ ] `dotnet test Daybook.slnx` is green
- [ ] `dotnet format Daybook.slnx --verify-no-changes` is clean
- [ ] No mutation/deletion of posted journal entries
- [ ] `Daybook.Accounting.Core` took no new external dependency
- [ ] Errors carry a stable `code`, message, `recovery`, and `correlationId` (if applicable)
- [ ] Does not build ahead of the current milestone

## Notes for reviewers

<!-- Anything tricky, trade-offs, follow-ups. -->
