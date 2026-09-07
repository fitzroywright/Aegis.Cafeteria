# FFP Integration

## Read model

The cafeteria platform reads FFP employee eligibility and holiday data for customer setup and monthly allocation. Eligible employees are synchronized into the active cafeteria customer set.

## Write model

Completed cafeteria financial activity is published through the service layer to FFP ledger and balance projection data. The POS terminals do not write directly to the central FFP database.

## Product ownership

Saleable cafeteria products and current prices are owned by POS administration. Completed order lines snapshot description, unit price and quantity so historical reports remain stable.

## Funding policy

Funding-policy rates are stored as management policy values. Monthly credit and subsidy allocations snapshot the policy used at opening time.

## Table impact

The current integration does not require a new FFP table.
