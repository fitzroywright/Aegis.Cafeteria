# Aegis.Cafeteria v70

## FFP reference refresh SQL fix

- Fixed the v69 employee reference refresh query introduced by the department join.
- `Employees` and `Departments` both expose `ID`/`Code`-style columns, so the query now aliases the tables (`e`, `d`) and fully qualifies every selected employee column.
- Department remains sourced from `d.Name`.
- No SQLite schema change: v70 continues to use greenfield schema version 69.
- The v67 funding rules remain unchanged: Credit Limit uses configured policy-row `Price`; Subsidy uses configured policy-row `Cost` (defaults both FFP00).
- The identifier-normalization null/empty guard remains unchanged.
