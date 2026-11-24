## 1. Implementation

- [x] 1.1 `--var name=value` option (repeatable) with validation; error with JSON mode
- [x] 1.2 Pass the variables to `bundle validate` (both refs, with and without `--include-locations`) and `bundle summary`
- [x] 1.3 Missing-variable hint in the validate error
- [x] 1.4 Tests: arguments passed at both refs and to summary, malformed value, JSON mode, the hint

## 2. Docs

- [x] 2.1 README, regenerated agent skill, CHANGELOG

## 3. Verification

- [x] 3.1 All tests green, `openspec validate add-bundle-vars --strict` passing
