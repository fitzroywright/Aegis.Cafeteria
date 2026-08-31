# Aegis.Cafeteria v67

- Greenfield SQLite schema 67; delete/archive the previous local database before first run.
- Credit Limit rate now comes from the configured credit-limit LunchItem **Price**; default code is `FFP00`.
- Subsidy rate now comes from the configured subsidy LunchItem **Cost**; default code is `FFP00`.
- Credit-limit and subsidy policy codes may be the same row.
- Monthly Credit Limit = Eligible Working Days x configured credit-limit row Price.
- Monthly Subsidy = min(Monthly Credit Limit, Eligible Working Days x configured subsidy row Cost).
- FFP LunchItems import now reads both `Price` and `Cost`; saleable product pricing remains based on `Price`.
- Preserves the identifier normalization null/empty guard before any SQLite identifier lookup, including TRN.
- Development launch profile remains included.
