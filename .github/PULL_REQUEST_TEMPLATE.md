<!-- Thanks for contributing. Keep changes focused; a small, clearly-correct change beats a clever one.
     See CONTRIBUTING.md. -->

## What and why

<!-- What does this change do, and why? Link any related issue (#123). -->

## Type of change

- [ ] Bug fix (non-breaking)
- [ ] New feature (non-breaking)
- [ ] Breaking change (API or behaviour)
- [ ] Docs / build / tooling only

## Checklist

- [ ] The full solution builds and all tests pass (`dotnet test`), with no new warnings.
- [ ] New behaviour has tests; the requirement is tested, not just the mechanism.
- [ ] Public API changes have XML doc comments.
- [ ] I have **not** weakened the transaction-recovery or payment-outcome contracts (the `Unknown`
      handling, the pre-POST sentinel gate, the transport `Delivery` classification). If this change
      touches them, explain why below.
- [ ] Version bumped in the affected package's csproj if this ships a change (packages version
      independently).

## Notes for reviewers

<!-- Anything non-obvious: a trade-off, a deferred follow-up, a thing you're unsure about. -->
