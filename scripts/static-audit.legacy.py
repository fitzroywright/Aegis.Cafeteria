#!/usr/bin/env python3
from pathlib import Path
import json, re, sys, xml.etree.ElementTree as ET
root=Path(__file__).resolve().parents[1]; errors=[]

for p in list(root.rglob('*.csproj'))+list(root.rglob('*.slnx')):
    try: ET.parse(p)
    except Exception as e: errors.append(f'{p}: XML: {e}')
for p in root.rglob('*.json'):
    if any(x in {'bin','obj'} for x in p.parts): continue
    try: json.loads(p.read_text(encoding='utf-8-sig'))
    except Exception as e: errors.append(f'{p}: JSON: {e}')
for p in root.rglob('*.cs'):
    t=p.read_text(encoding='utf-8-sig')
    if 'NotImplementedException' in t: errors.append(f'{p}: NotImplementedException present')
    if 'Serilog' in t: errors.append(f'{p}: Serilog must not be reintroduced')

initializer=(root/'src/Aegis.Cafeteria.Infrastructure/Persistence/PersistenceInitializer.cs').read_text()
for forbidden in ('EnsureDeleted', 'File.Delete(', 'ALTER TABLE', 'DROP TABLE', 'DROP DATABASE'):
    if forbidden in initializer: errors.append(f'v69 local initializer must be non-destructive: found {forbidden}')
for required in ('CurrentLocalSchemaVersion = 73', 'CurrentProductVersion = "v73"', 'LocalSchemaInfo', 'LegacyDatabaseException'):
    if required not in initializer: errors.append(f'v69 local schema guard missing: {required}')

for forbidden_path in [
 'src/Aegis.Cafeteria.Infrastructure/Persistence/CentralCafeteriaDbContext.cs',
 'src/Aegis.Cafeteria.Infrastructure/Synchronization',
 'src/Aegis.Cafeteria.Infrastructure/Realtime/SignalRChangeSubscriber.cs',
 'src/Aegis.Cafeteria.TestHost']:
    if (root/forbidden_path).exists(): errors.append(f'obsolete central component still present: {forbidden_path}')

for p in (root/'src').rglob('*.cs'):
    t=p.read_text()
    for token in ('TicketsAssigned','TicketsRemaining'):
        if token in t: errors.append(f'legacy ticket concept leaked into source: {p}: {token}')
    if p.name!='FfpSqlApplicationGateway.cs' and 'EligibleForLunchTicket' in t:
        errors.append(f'legacy eligibility name leaked beyond FFP adapter: {p}')
    for token in ('FFP01','FFP03','FFP04','FFP05','FFP06','FFP07','FFP08','FFP09'):
        if token in t: errors.append(f'unapproved FFP0x code leaked into active source: {p}: {token}')

# FFP employee reference query must not import legacy balance/ticket state.
gateway_path=root/'src/Aegis.Cafeteria.Infrastructure/Ffp/FfpSqlApplicationGateway.cs'
gateway=gateway_path.read_text()
employee_sql=gateway.split('private const string EmployeeSql',1)[1].split('private const string HolidaySql',1)[0]
for forbidden in ('TicketsAssigned','TicketsRemaining','CreditLimit','AvailableBalance'):
    if forbidden in employee_sql: errors.append(f'FFP employee reference query imports forbidden legacy funding field: {forbidden}')

# v98: FFP is not a product-catalog source. Only the FFP00 compatibility policy mirror may touch LunchItems.
if 'WHERE e.EligibleForLunchTicket = 1' not in employee_sql:
    errors.append('v98 FFP employee query must load eligible employees only')
for forbidden in ('GetCatalogAsync', 'GetProductsAsync', 'UpdateProductAsync', 'SELECT ID, Code, Name, Price, Cost'):
    if forbidden in gateway:
        errors.append(f'v98 FFP gateway must not expose saleable product catalog behavior: {forbidden}')
if "WHERE Code = 'FFP00'" not in gateway or 'MirrorFundingPolicyAsync' not in gateway:
    errors.append('v98 FFP00 compatibility mirror missing')

product_maintenance=(root/'src/Aegis.Cafeteria.Infrastructure/Persistence/EfProductCatalogMaintenance.cs').read_text()
for required in ('AddAsync', 'UpdateAsync', 'DeleteAsync', 'db.Products'):
    if required not in product_maintenance:
        errors.append(f'v98 local POS product maintenance missing: {required}')

allocation_source=(root/'src/Aegis.Cafeteria.Domain/Accounts/MonthlyCreditAllocation.cs').read_text()
for required in ('CreditLimitPrice', 'SubsidyCost', 'EligibleWorkingDays * CreditLimitPrice', 'EligibleWorkingDays * SubsidyCost'):
    if required not in allocation_source: errors.append(f'v69 monthly allocation split missing: {required}')
if 'DailyCreditRate' in allocation_source: errors.append('DailyCreditRate remains in monthly allocation domain')

for p in [root/'src/Aegis.Cafeteria.UI/appsettings.json', root/'src/Aegis.Cafeteria.UI.Linux/appsettings.json']:
    data=json.loads(p.read_text())
    if 'CafeteriaPolicy' in data: errors.append(f'local daily-credit policy section still exists: {p}')
    ffp_cfg=data.get('FfpIntegration',{})
    if 'DailyCreditLunchItemCode' in ffp_cfg: errors.append(f'invented FFP rate-row code remains in config: {p}')
    if ffp_cfg.get('CreditLimitLunchItemCode') != 'FFP00': errors.append(f'default CreditLimitLunchItemCode must be FFP00: {p}')
    if ffp_cfg.get('SubsidyLunchItemCode') != 'FFP00': errors.append(f'default SubsidyLunchItemCode must be FFP00: {p}')

core_rows=(root/'src/Aegis.Cafeteria.Infrastructure/Persistence/Models/CoreRows.cs').read_text()
for required in ('CafeteriaRatePolicyRow','CreditLimitLunchItemCode','CreditLimitPrice','SubsidyLunchItemCode','SubsidyCost'):
    if required not in core_rows: errors.append(f'clean FFP funding-policy row missing {required}')
if 'DailyCreditLunchItemCode' in core_rows: errors.append('obsolete daily-credit code remains in persistence model')
if 'LegacyPolicyMirror' in core_rows:
    errors.append('v69 greenfield policy row must not contain LegacyPolicyMirror')
account_rows=(root/'src/Aegis.Cafeteria.Infrastructure/Persistence/Models/AccountRows.cs').read_text()
if 'LegacyPolicyMirror' in account_rows:
    errors.append('v69 greenfield monthly allocation row must not contain LegacyPolicyMirror')
db_model=(root/'src/Aegis.Cafeteria.Infrastructure/Persistence/CafeteriaDbContextBase.cs').read_text()
for forbidden in ('DailyCreditRate','CompanySubsidyDailyRate','LegacyPolicyMirror'):
    if forbidden in db_model: errors.append(f'v69 greenfield SQLite model contains obsolete funding storage name: {forbidden}')
launch_path=root/'src/Aegis.Cafeteria.UI/Properties/launchSettings.json'
if not launch_path.exists():
    errors.append('v69 Windows UI development launchSettings.json is missing')
else:
    launch=json.loads(launch_path.read_text())
    profiles=launch.get('profiles',{})
    if not any(p.get('environmentVariables',{}).get('DOTNET_ENVIRONMENT')=='Development' for p in profiles.values()):
        errors.append('v69 Windows UI launch profile must set DOTNET_ENVIRONMENT=Development')

# Synthetic daily-rate concepts must not be exposed through UI-facing contracts.
ui_contract=(root/'src/Aegis.Cafeteria.Application/Reporting/CustomerAccountView.cs').read_text()
for forbidden in ('DailyCreditRate','CompanySubsidyDailyRate'):
    if forbidden in ui_contract: errors.append(f'UI-facing account view exposes removed funding concept: {forbidden}')
visible_files = list((root/'src/Aegis.Cafeteria.UI').rglob('*.cs')) + list((root/'src/Aegis.Cafeteria.UI.Linux').rglob('*.cs'))
visible_files += [root/'src/Aegis.Cafeteria.Reporting/CafeteriaReportService.cs', root/'src/Aegis.Cafeteria.Infrastructure/Persistence/EfCustomerAccountQuery.cs']
for vf in visible_files:
    text=vf.read_text(encoding='utf-8-sig')
    for forbidden in ('Aegis Cafeteria','AEGIS CAFETERIA','Aegis credit limit','Aegis available','Company rate','Company subsidy rate'):
        if forbidden in text: errors.append(f'forbidden user-visible text remains: {vf}: {forbidden}')

# UI safeguards requested through v50.
main=(root/'src/Aegis.Cafeteria.UI/MainForm.cs').read_text()
for required in ('FormClosing +=', 'Are you sure you want to close Cafeteria?', 'MessageBoxButtons.YesNo', 'MessageBoxDefaultButton.Button2'):
    if required not in main: errors.append(f'main-window close confirmation missing: {required}')

# v50 live employee search, action panel sizing, and configurable inactivity privacy reset.
customer_search=(root/'src/Aegis.Cafeteria.UI/CustomerSearchDialog.cs').read_text()
if 'searchButton' in customer_search: errors.append('employee name search still requires a Search button')
for required in ('this.search.TextChanged +=', 'searchDebounce', 'SearchAsync()', 'CancellationTokenSource'):
    if required not in customer_search: errors.append(f'live employee-name search missing: {required}')
for required in ('desiredTransactionPanel2Min = 420', 'MinimumSize = new Size(520, 62)', 'CheckCustomerInactivity()', 'Application.AddMessageFilter', 'CustomerInactivityTimeoutSeconds'):
    if required not in main: errors.append(f'v50 POS UI behavior missing: {required}')
ui_settings=json.loads((root/'src/Aegis.Cafeteria.UI/appsettings.json').read_text()).get('Ui',{})
if ui_settings.get('CustomerInactivityTimeoutSeconds') != 30:
    errors.append('default Ui:CustomerInactivityTimeoutSeconds must be 30')

login=(root/'src/Aegis.Cafeteria.UI/OperatorLoginForm.cs').read_text()
for required in ('Color.FromArgb(22, 66, 105)', 'Color.FromArgb(235, 139, 40)', 'FOOD FOR THE POOR JAMAICA'):
    if required not in login: errors.append(f'login/splash theme alignment missing: {required}')

customer_source=(root/'src/Aegis.Cafeteria.Domain/Customers/Customer.cs').read_text()
if 'decimal additionalCreditLimit' not in customer_source: errors.append('current Customer constructor contract missing additionalCreditLimit')
infra_csproj=(root/'src/Aegis.Cafeteria.Infrastructure/Aegis.Cafeteria.Infrastructure.csproj').read_text()
if 'PackageReference Include="Microsoft.Extensions.Http"' not in infra_csproj: errors.append('Microsoft.Extensions.Http package reference missing')

ui=(root/'src/Aegis.Cafeteria.UI/CustomerDisplayForm.cs').read_text()
if 'CustomerDisplayPolicy.UseMaximizedWindow' not in ui: errors.append('Customer display maximize policy not wired')


# v51 report-menu/report-center guardrails.
main_form = (root / "src/Aegis.Cafeteria.UI/MainForm.cs").read_text(encoding="utf-8")
report_runner = (root / "src/Aegis.Cafeteria.UI/ReportRunnerDialog.cs").read_text(encoding="utf-8")
report_catalog = (root / "src/Aegis.Cafeteria.Reporting/CafeteriaReportCatalog.cs").read_text(encoding="utf-8")
report_kinds = [
    "ExecutiveSummary", "DailySales", "SalesTrend", "TenderMix", "ProductSales",
    "EmployeeStatement", "MonthlyAllocations", "SubsidyUtilization",
    "CustomerResponsibility", "RefundVoidAudit", "AdditionalCreditAudit",
    "OutstandingDebt", "FfpReconciliation"
]
if 'new ToolStripMenuItem("Reports")' not in main_form: errors.append("v51: Windows POS must expose Reports menu")
if 'ReportRunnerDialog' not in main_form: errors.append("v51: Reports menu must open ReportRunnerDialog")
if 'SaveAsync("HTML")' not in report_runner or 'SaveAsync("CSV")' not in report_runner: errors.append("v51: report runner must save HTML and CSV")
if 'RunPreviewAsync' not in report_runner or 'UseShellExecute = true' not in report_runner: errors.append("v51: report runner must preview HTML")
for kind in report_kinds:
    if f"CafeteriaReportKind.{kind}" not in report_catalog: errors.append(f"v51: report catalog missing {kind}")

# v52 recent transactions and Linux parity guardrails.
recent_contract = (root/'src/Aegis.Cafeteria.Application/Abstractions/IRecentTransactionQuery.cs').read_text()
recent_query = (root/'src/Aegis.Cafeteria.Infrastructure/Persistence/EfRecentTransactionQuery.cs').read_text()
infra_registration = (root/'src/Aegis.Cafeteria.Infrastructure/CafeteriaInfrastructure.cs').read_text()
linux_main = (root/'src/Aegis.Cafeteria.UI.Linux/MainWindow.cs').read_text()
linux_program = (root/'src/Aegis.Cafeteria.UI.Linux/Program.cs').read_text()
linux_csproj = (root/'src/Aegis.Cafeteria.UI.Linux/Aegis.Cafeteria.UI.Linux.csproj').read_text()
ui_options = (root/'src/Aegis.Cafeteria.Infrastructure/Configuration/CafeteriaUiOptions.cs').read_text()
for required in ('RecentTransactionView','IRecentTransactionQuery','GetTodayAsync'):
    if required not in recent_contract: errors.append(f'v52 recent-transaction contract missing: {required}')
for required in ('OrderStatus.Completed','DateOnly.FromDateTime','var tender ='):
    if required not in recent_query: errors.append(f'v52 recent-transaction query missing: {required}')
if not re.search(r'AddSingleton\s*<\s*IRecentTransactionQuery\s*,\s*EfRecentTransactionQuery\s*>\s*\(\s*\)', infra_registration):
    errors.append('v52 recent-transaction query is not registered in DI')
if 'RecentTransactionCount' not in ui_options:
    errors.append('v52 Ui options missing RecentTransactionCount')
for settings_path in [root/'src/Aegis.Cafeteria.UI/appsettings.json', root/'src/Aegis.Cafeteria.UI.Linux/appsettings.json']:
    settings=json.loads(settings_path.read_text()).get('Ui',{})
    if settings.get('RecentTransactionCount') != 10:
        errors.append(f'v52 default Ui:RecentTransactionCount must be 10: {settings_path}')
for required in ('RECENT TRANSACTIONS - TODAY','LoadRecentTransactionsAsync','RecentTransactionCount'):
    if required not in main: errors.append(f'v52 Windows recent-transactions UI missing: {required}')
for required in ('RECENT TRANSACTIONS','OperatorLoginWindow','Void Current Sale','Refund Selected',
                 'CustomerInactivityTimeoutSeconds','LoadRecentTransactionsAsync','PreviewReportAsync',
                 'persistenceOptions.TerminalId','operatorSession.Current','Are you sure you want to close Cafeteria?'):
    if required not in linux_main: errors.append(f'v52 Linux parity wiring missing: {required}')
for required in ('AddCafeteriaReporting()','CommonSecurityOperatorAuthenticator','OperatorSession','PosVoidService','RefundService'):
    if required not in linux_program: errors.append(f'v52 Linux service composition missing: {required}')
if '..\\Aegis.Cafeteria.Reporting\\Aegis.Cafeteria.Reporting.csproj' not in linux_csproj:
    errors.append('v52 Linux UI must reference Reporting project')
if 'RefreshRecentTransactionsAsync' in linux_main:
    errors.append('v52 Linux UI contains stale recent-transaction method name')

# v53-splitter-layout
main_form = (root / "src/Aegis.Cafeteria.UI/MainForm.cs").read_text(encoding="utf-8")
main_split_initializer = main_form[main_form.find("var body = new SplitContainer"):main_form.find("body.Panel1.Padding", main_form.find("var body = new SplitContainer"))]
if "SplitterDistance =" in main_split_initializer or "Panel1MinSize =" in main_split_initializer or "Panel2MinSize =" in main_split_initializer:
    errors.append("v53: main SplitContainer must defer SplitterDistance/minimum sizing until real layout")
if "ApplyMainSplitLayout();" not in main_form or "body.SizeChanged += (_, _) => ApplyMainSplitLayout();" not in main_form:
    errors.append("v53: defensive main split layout hook is missing")

# v55-recent-transaction-idle-overlay
windows_ui = (root/'src/Aegis.Cafeteria.UI/MainForm.cs').read_text(encoding='utf-8-sig')
linux_ui = (root/'src/Aegis.Cafeteria.UI.Linux/MainWindow.cs').read_text(encoding='utf-8-sig')
for label, text in [('Windows', windows_ui), ('Linux', linux_ui)]:
    if 'UpdateRecentTransactionsVisibility()' not in text:
        errors.append(f"v55: {label} UI must centrally control recent-transaction visibility")
    if 'currentCustomer is null' not in text and '_currentCustomer is null' not in text:
        errors.append(f"v55: {label} recent transactions must be restricted to no active customer")
if 'this.transactionWorkspace.Visible = !showRecent;' not in windows_ui or 'RowCount = 5' not in windows_ui:
    errors.append('v55: Windows must switch between an idle recent-transactions overlay and the original five-row transaction workspace')
if 'recentTransactionsRow.Height = show ? new GridLength(165) : new GridLength(0);' not in linux_ui:
    errors.append('v55: Linux recent-transactions row must collapse during an active customer')

# v56-empty-find-and-idle-dashboard
if 'browseCustomers' in windows_ui or 'ActionButton("Browse Customers"' in windows_ui:
    errors.append('v56: Windows must not expose a separate Browse Customers button')
if 'if (value.Length == 0)' not in windows_ui or 'await BrowseCustomersAsync();' not in windows_ui:
    errors.append('v56: empty Windows Find must open the customer browser')
for required in ("TODAY'S SALES DASHBOARD", 'LoadIdleDashboardAsync', 'CafeteriaReportKind.DailySales', 'int desiredLeft = available / 2;'):
    if required not in windows_ui:
        errors.append(f'v56: Windows idle dashboard/50-50 layout missing: {required}')
if 'identification.SearchAsync(string.Empty, 50)' not in linux_ui:
    errors.append('v56: Linux empty Find must open cached employee browse results')

# v57-escape-cancel
win_main = (root/'src/Aegis.Cafeteria.UI/MainForm.cs').read_text(encoding='utf-8-sig')
linux_main = (root/'src/Aegis.Cafeteria.UI.Linux/MainWindow.cs').read_text(encoding='utf-8-sig')
for required in ('KeyPreview = true', 'Keys.Escape', 'CancelActiveTransaction();', 'private void CancelActiveTransaction()'):
    if required not in win_main:
        errors.append(f'v57: Windows Escape-cancel wiring missing: {required}')
for required in ('Key.Escape', 'CancelActiveTransaction();', 'private void CancelActiveTransaction()'):
    if required not in linux_main:
        errors.append(f'v57: Linux Escape-cancel wiring missing: {required}')
for label, source in (('Windows', win_main), ('Linux', linux_main)):
    start = source.find('private void CancelActiveTransaction()')
    if start < 0:
        continue
    end = source.find('private void ClearCustomer()', start)
    body = source[start:end if end >= 0 else start + 1600]
    for forbidden in ('SaveOrderAsync', 'Publish', 'VoidCurrentSaleAsync', 'Refund'):
        if forbidden in body:
            errors.append(f'v57: {label} Escape cancel must remain non-persistent; found {forbidden}')

# v58-universal-identification
resolver = (root/'src/Aegis.Cafeteria.Application/Abstractions/CustomerLookupResolver.cs').read_text(encoding='utf-8-sig')
ffp_gateway = (root/'src/Aegis.Cafeteria.Infrastructure/Ffp/FfpSqlApplicationGateway.cs').read_text(encoding='utf-8-sig')
ffp_cache = (root/'src/Aegis.Cafeteria.Infrastructure/Ffp/EfFfpReferenceCache.cs').read_text(encoding='utf-8-sig')
id_enum = (root/'src/Aegis.Cafeteria.Domain/Identification/CustomerIdentifierType.cs').read_text(encoding='utf-8-sig')
for required in ('CustomerIdentifierType.NfcTag, CustomerIdentifierType.Code, CustomerIdentifierType.Trn', 'SearchAsync(trimmed'):
    if required not in resolver: errors.append(f'v58: universal resolver missing required NFC -> Code -> TRN -> Name flow: {required}')
if 'Barcode = 2' in id_enum or 'Code = 2' not in id_enum:
    errors.append('v58: identifier type 2 must be named Code, not Barcode')
for forbidden in ('RFID', 'TempNFC', 'TempBarCode'):
    if forbidden in ffp_gateway: errors.append(f'v58: unsupported FFP identifier remains selected: {forbidden}')
for forbidden in ('TemporaryNfc', 'TemporaryBarcode', '.Rfid'):
    if forbidden in ffp_cache: errors.append(f'v58: unsupported identifier remains cached: {forbidden}')
for required in ('employee.Nfc', 'employee.Code', 'employee.Trn'):
    if required not in ffp_cache: errors.append(f'v58: required local identifier cache missing: {required}')
for label, source in (('Windows', win_main), ('Linux', linux_main)):
    if 'Tap card or enter Code, TRN, or Name' not in source:
        errors.append(f'v58: {label} universal lookup prompt missing')


# v60 partial-name safety
mainform = (root/'src/Aegis.Cafeteria.UI/MainForm.cs').read_text(encoding='utf-8-sig')
if "AutoCompleteMode" in mainform or "AutoCompleteSource" in mainform or "AutoCompleteCustomSource" in mainform:
    errors.append("v63: universal customer search must not use WinForms autocomplete; Find/Enter must receive exactly the cashier-entered value")
if "NormalizedName.Contains(normalized)" not in (root / "src/Aegis.Cafeteria.Infrastructure/Persistence/EfCustomerIdentificationService.cs").read_text(): errors.append("v60: employee name search must use contains matching like product search")
resolver=(root / "src/Aegis.Cafeteria.Application/Abstractions/CustomerLookupResolver.cs").read_text()
if "candidates.Count == 1" in resolver: errors.append("v60: partial name results must never auto-load a single candidate")
if "result.Candidates.Count > 0" not in mainform: errors.append("v60: Windows must open the chooser for any name candidates")

if errors:
    print('\n'.join(errors)); sys.exit(1)

# v62: name-search chooser must not silently select the first employee.
_v62_dialog = (root / "src" / "Aegis.Cafeteria.UI" / "CustomerSearchDialog.cs").read_text(encoding="utf-8")
assert "this.grid.Rows[0].Selected = true" not in _v62_dialog, "v62: customer chooser must not preselect first row"
assert "this.grid.CurrentCell = null" in _v62_dialog, "v62: customer chooser must clear CurrentCell"
assert "this.grid.SelectedRows.Count == 1" in _v62_dialog, "v62: selected customer must require explicit selected row"


# v69 canteen-summary regression guards
def _v69_text(rel): return (root / rel).read_text(encoding="utf-8-sig")
report_models = _v69_text('src/Aegis.Cafeteria.Reporting/Models/ReportModels.cs')
report_catalog = _v69_text('src/Aegis.Cafeteria.Reporting/CafeteriaReportCatalog.cs')
report_service = _v69_text('src/Aegis.Cafeteria.Reporting/CafeteriaReportService.cs')
html_exporter = _v69_text('src/Aegis.Cafeteria.Reporting/Export/HtmlReportExporter.cs')
workforce = _v69_text('src/Aegis.Cafeteria.Application/Integration/IWorkforceReferenceSource.cs')
ffp_gateway = _v69_text('src/Aegis.Cafeteria.Infrastructure/Ffp/FfpSqlApplicationGateway.cs')
core_rows = _v69_text('src/Aegis.Cafeteria.Infrastructure/Persistence/Models/CoreRows.cs')
for required, source, label in [
    ('CanteenSummary', report_models, 'report enum'),
    ('Canteen summary', report_catalog, 'report catalog'),
    ('CanteenSummaryAsync', report_service, 'report service'),
    ('policy.SubsidyCost', report_service, 'subsidy Cost source'),
    ('CountWorkingDays', report_service, 'date-range work-day calculation'),
    ('BuildCanteenSummary', html_exporter, 'printable canteen layout'),
    ('Department = ""', workforce, 'department snapshot'),
    ('LEFT OUTER JOIN dbo.Departments AS d ON d.ID = e.DepartmentID', ffp_gateway, 'qualified FFP department join'),
    ('public string Department', core_rows, 'local department cache')
]:
    assert required in source, f'v69 canteen summary missing {label}: {required}'

print('Static audit passed: v70 includes the department-grouped Canteen Summary, FFP00 Price/Cost funding policy, identifier normalization guard, Development launch profile, and prior POS safeguards.')


# v71: login keyboard navigation must remain Username -> Password -> Sign In.
_login_form = (root / "src" / "Aegis.Cafeteria.UI" / "OperatorLoginForm.cs").read_text(encoding="utf-8-sig")
assert "TabIndex = 0" in _login_form and "TabIndex = 1" in _login_form and "TabIndex = 2" in _login_form, \
    "Windows login must explicitly tab Username -> Password -> Sign In"


# v72 FFP audit identity + descriptions.
ledger_contract=(root/'src/Aegis.Cafeteria.Application/Integration/IExternalAccountingGateway.cs').read_text()
ledger_projection=(root/'src/Aegis.Cafeteria.Application/Integration/ExternalLedgerProjectionService.cs').read_text()
ffp_gateway=(root/'src/Aegis.Cafeteria.Infrastructure/Ffp/FfpSqlApplicationGateway.cs').read_text()
outbox_model=(root/'src/Aegis.Cafeteria.Infrastructure/Persistence/Models/FfpIntegrationRows.cs').read_text()
for required in ('string UserName = "SYSTEM"','string? IdName = null'):
    if required not in ledger_contract: errors.append(f'v72 ledger contract missing {required}')
for required in ('new Tickets Assigned to','By SYSTEM','IdName: "Tickets Assigned"','BuildPurchaseDescription','BuildRefundDescription','NormalizeUserName(order.CashierUserId)','NormalizeUserName(refund.RefundedBy)'):
    if required not in ledger_projection: errors.append(f'v72 ledger projection missing {required}')
for required in ('@UserName','@IDName','entry.UserName','entry.IdName'):
    if required not in ffp_gateway: errors.append(f'v72 FFP gateway missing {required}')
if "'AEGIS.CAFETERIA'" in ffp_gateway: errors.append('v72 FFP gateway must not hard-code AEGIS.CAFETERIA as UserName')
for required in ('public string UserName','public string? IdName'):
    if required not in outbox_model: errors.append(f'v72 durable outbox missing {required}')

if errors:
    print('\n'.join(errors)); sys.exit(1)
# v73 Services boundary: POS may route all FFP reads/writes through the service while retaining its local durable outbox.
services_project=root/'src/Aegis.Cafeteria.Services/Aegis.Cafeteria.Services.csproj'
if not services_project.exists(): errors.append('v73 Aegis.Cafeteria.Services project is missing')
services_program=(root/'src/Aegis.Cafeteria.Services/Program.cs').read_text() if services_project.exists() else ''
for required in ('/api/management/additional-credit','/api/management/stipends','/api/pos/events','/api/ffp/ledger','/api/month-opening/run'):
    if required not in services_program: errors.append(f'v73 Services endpoint missing: {required}')
services_gateway=(root/'src/Aegis.Cafeteria.Infrastructure/Services/ServicesIntegrationGateway.cs').read_text()
for required in ('IExternalAccountingGateway','IWorkforceReferenceSource','api/ffp/ledger'):
    if required not in services_gateway: errors.append(f'v73 POS-to-Services integration gateway missing: {required}')
sync_worker=(root/'src/Aegis.Cafeteria.Infrastructure/Services/ManagementEventSyncWorker.cs').read_text()
for required in ('AdditionalCreditServiceEvent','StipendServiceEvent','MonthlyAllocationServiceEvent','ManagementEventInbox'):
    if required not in sync_worker: errors.append(f'v73 management event synchronization missing: {required}')
if 'CurrentLocalSchemaVersion = 73' not in initializer: errors.append('v73 local schema version must be 73')
if errors:
    print('\n'.join(errors)); sys.exit(1)
print('Static audit passed: v73 adds Aegis.Cafeteria.Services, centralized FFP integration, management-event sync, service month-opening allocations, and scheduled email support while preserving v72 ledger safeguards.')

# v74 Services hardening: server-derived management identity, per-employee Additional Credit policy,
# and service-owned FFP publication for management/month-opening events.
_v74_program=(root/'src/Aegis.Cafeteria.Services/Program.cs').read_text()
_v74_dtos=(root/'src/Aegis.Cafeteria.Services/ApiDtos.cs').read_text()
_v74_options=(root/'src/Aegis.Cafeteria.Services/ServiceOptions.cs').read_text()
_v74_policy=(root/'src/Aegis.Cafeteria.Services/Persistence/CustomerCreditPolicyStore.cs').read_text()
_v74_rows=(root/'src/Aegis.Cafeteria.Services/Persistence/ServiceRows.cs').read_text()
_v74_publisher=(root/'src/Aegis.Cafeteria.Services/Workers/ServiceFfpPublisherWorker.cs').read_text()
_v74_sync=(root/'src/Aegis.Cafeteria.Infrastructure/Services/ManagementEventSyncWorker.cs').read_text()
_v74_ffp_worker=(root/'src/Aegis.Cafeteria.Infrastructure/Ffp/FfpLedgerPublisherWorker.cs').read_text()
_v74_additional=(root/'src/Aegis.Cafeteria.Application/Accounts/AdditionalCreditService.cs').read_text()
for forbidden in ('string ApprovedBy', 'string ProcessedBy'):
    if forbidden in _v74_dtos: errors.append(f'v74 management request DTO must not accept client-supplied actor identity: {forbidden}')
for required in ('AddNegotiate()', 'RequireAuthorization("CafeteriaManagement")', 'RequireActor(http)', 'AuthorizedGroups'):
    if required not in _v74_program + _v74_options: errors.append(f'v74 Windows/AD management identity hardening missing: {required}')
if 'AdditionalCreditMonthlyLimit' in _v74_program + _v74_options:
    errors.append('v74 must not use a global AdditionalCreditMonthlyLimit')
for required in ('CustomerCreditPolicies', 'AdditionalCreditLimit', 'SetAdditionalCreditLimitAsync', 'GetAdditionalCreditLimitAsync'):
    if required not in _v74_rows + _v74_policy + _v74_program: errors.append(f'v74 per-employee Additional Credit policy missing: {required}')
for required in ('operationId ?? Guid.NewGuid()', 'AdditionalCreditService'):
    if required not in _v74_additional + _v74_program: errors.append(f'v74 shared Additional Credit domain rule/idempotency missing: {required}')
for required in ('ServiceFfpPublisherWorker', 'FfpPublishedAt', 'RecordFfpAttemptAsync', 'ExternalLedgerEntryKind.AdditionalCredit', 'ExternalLedgerEntryKind.StipendCardReduction', 'ExternalLedgerEntryKind.MonthlyAllocation'):
    if required not in _v74_publisher + _v74_rows + (root/'src/Aegis.Cafeteria.Services/Persistence/ServiceEventStore.cs').read_text(): errors.append(f'v74 direct service-to-FFP publication missing: {required}')
if 'PublishAdjustmentAsync' in _v74_sync:
    errors.append('v74 POS management-event sync must not republish management-originated events to FFP')
if 'servicesOptions.Enabled && servicesOptions.UseServiceMonthlyAllocations' not in _v74_ffp_worker:
    errors.append('v74 POS FFP recovery must skip monthly projection when Services owns month opening')
if errors:
    print('\n'.join(errors)); sys.exit(1)
print('Static audit passed: v74 derives management actors from Windows/AD, enforces per-employee Additional Credit policy, and makes Services the durable FFP publisher for management/month-opening events.')

# v75 Windows partial-refund UI regression guard.
_v75_main=(root/'src/Aegis.Cafeteria.UI/MainForm.cs').read_text(encoding='utf-8-sig')
_v75_dialog=(root/'src/Aegis.Cafeteria.UI/RefundDialog.cs').read_text(encoding='utf-8-sig')
if 'new RefundDialog(order)' not in _v75_main: errors.append('v75 Windows refund flow must open RefundDialog')
if 'this.refundService.Refund(' not in _v75_main: errors.append('v75 Windows refund flow must call RefundService.Refund for selected quantities/tenders')
if 'RefundAllRemaining(order' in _v75_main: errors.append('v75 Windows refund flow must not force RefundAllRemaining')
for required in ('SelectedItems','RefundQuantity','RemainingQuantity','CafeteriaAccountAmount','CashAmount','Refund All Remaining'):
    if required not in _v75_dialog: errors.append(f'v75 RefundDialog missing partial-refund capability: {required}')
if errors:
    print('\n'.join(errors)); sys.exit(1)
print('Static audit passed: v75 Windows refund UI supports item/quantity selection, full-refund shortcut, and original-tender-safe split allocation.')

# v76 Windows refund-grid minimum-height regression guards.
_v76_main=(root/'src/Aegis.Cafeteria.UI/MainForm.cs').read_text(encoding='utf-8-sig')
_v76_refund=(root/'src/Aegis.Cafeteria.UI/RefundDialog.cs').read_text(encoding='utf-8-sig')
_v76_recent=(root/'src/Aegis.Cafeteria.UI/RecentOrdersDialog.cs').read_text(encoding='utf-8-sig')
if 'this.transactions.MinimumSize = new Size(0, this.transactions.ColumnHeadersHeight + (5 * this.transactions.RowTemplate.Height) + 4);' not in _v76_main:
    errors.append('v76 customer transaction/refund history must reserve five visible rows')
if 'root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));' not in _v76_main:
    errors.append('v76 customer history row must receive remaining vertical space')
if 'this.items.MinimumSize = new Size(0, this.items.ColumnHeadersHeight + (5 * this.items.RowTemplate.Height) + 4);' not in _v76_refund:
    errors.append('v76 partial-refund item grid must reserve five visible rows')
if 'grid.MinimumSize = new Size(0, grid.ColumnHeadersHeight + (5 * grid.RowTemplate.Height) + 4);' not in _v76_recent:
    errors.append('v76 refundable-order chooser must reserve five visible rows')

if errors:
    raise SystemExit('\n'.join(errors))
print('Static audit passed: v76 refund-related Windows grids reserve at least five visible rows.')

# v77 grouped purchase-report regression guards.
_v77_models=(root/'src/Aegis.Cafeteria.Reporting/Models/ReportModels.cs').read_text()
_v77_catalog=(root/'src/Aegis.Cafeteria.Reporting/CafeteriaReportCatalog.cs').read_text()
_v77_service=(root/'src/Aegis.Cafeteria.Reporting/CafeteriaReportService.cs').read_text()
_v77_runner=(root/'src/Aegis.Cafeteria.UI/ReportRunnerDialog.cs').read_text(encoding='utf-8-sig')
_v77_main=(root/'src/Aegis.Cafeteria.UI/MainForm.cs').read_text(encoding='utf-8-sig')
for required in ('PurchaseItemDetail','PurchaseByPerson','PurchaseGrouping { Day, Week, Month }'):
    if required not in _v77_models + _v77_catalog: errors.append(f'v77 purchase report model/catalog missing: {required}')
for required in ('Itemized purchases','Card / Account','BuildPersonPeriodTotalsTable','BuildDepartmentPeriodTotalsTable','BuildPeriodTotalsTable','AllocateByValue','PurchaseGrouping.Week'):
    if required not in _v77_service: errors.append(f'v77 grouped purchase reporting missing: {required}')
for required in ('Group totals','PurchaseGrouping.Day','PurchaseItemDetail or CafeteriaReportKind.PurchaseByPerson'):
    if required not in _v77_runner: errors.append(f'v77 Windows report grouping UI missing: {required}')
for required in ('Itemized Purchases by Customer','Purchases Summarized by Person'):
    if required not in _v77_main: errors.append(f'v77 Windows report menu missing: {required}')
if errors:
    raise SystemExit('\n'.join(errors))
print('Static audit passed: v77 adds itemized and person-summary purchase reports with separate Cash/Card-Account amounts and Day/Week/Month person/department/period totals.')

# v78 dashboard product pies, explicit customer available-to-spend display, and pay-later tender guidance.
_v78_main=(root/'src/Aegis.Cafeteria.UI/MainForm.cs').read_text(encoding='utf-8-sig')
_v78_customer=(root/'src/Aegis.Cafeteria.UI/CustomerDisplayForm.cs').read_text(encoding='utf-8-sig')
_v78_tender=(root/'src/Aegis.Cafeteria.UI/TenderDialog.cs').read_text(encoding='utf-8-sig')
_v78_pie=(root/'src/Aegis.Cafeteria.UI/PieChartControl.cs').read_text(encoding='utf-8-sig')
for required in ('todayItemsByQuantity','todayItemsByValue','ITEMS SOLD — QUANTITY','ITEMS SOLD — VALUE','CafeteriaReportKind.ProductSales','DashboardPieBuckets'):
    if required not in _v78_main: errors.append(f'v78 daily product dashboard missing: {required}')
for required in ('FillPie','No items sold today','PieSlice'):
    if required not in _v78_pie: errors.append(f'v78 pie chart control missing: {required}')
if 'AVAILABLE TO SPEND' not in _v78_customer:
    errors.append('v78 customer-facing display must explicitly label Available to Spend')
for required in ('Available to Spend','New Customer Responsibility','charge account','later payment/employee amount due','this.subsidyRemaining'):
    if required not in _v78_tender: errors.append(f'v78 pay-later tender explanation missing: {required}')
if errors:
    raise SystemExit('\n'.join(errors))
print('Static audit passed: v78 adds daily product quantity/value pie charts, explicit customer Available to Spend, and visible pay-later/customer-responsibility guidance in checkout.')

# v79 customer-payment workflow and Windows/Linux feature-parity regression guards.
_v79_workflow=(root/'src/Aegis.Cafeteria.Application/Accounts/CustomerPaymentWorkflow.cs').read_text()
_v79_payment=(root/'src/Aegis.Cafeteria.Domain/Accounts/CustomerPayment.cs').read_text()
_v79_windows=(root/'src/Aegis.Cafeteria.UI/MainForm.cs').read_text(encoding='utf-8-sig')
_v79_windows_dialog=(root/'src/Aegis.Cafeteria.UI/CustomerPaymentDialog.cs').read_text(encoding='utf-8-sig')
_v79_linux=(root/'src/Aegis.Cafeteria.UI.Linux/MainWindow.cs').read_text()
_v79_linux_payment=(root/'src/Aegis.Cafeteria.UI.Linux/CustomerPaymentWindow.cs').read_text()
_v79_linux_refund=(root/'src/Aegis.Cafeteria.UI.Linux/RefundWindow.cs').read_text()
_v79_linux_display=(root/'src/Aegis.Cafeteria.UI.Linux/CustomerDisplayWindow.cs').read_text()
_v79_linux_pie=(root/'src/Aegis.Cafeteria.UI.Linux/PieChartView.cs').read_text()
for required in ('SaveCustomerPaymentAsync','ExternalLedgerEntryKind.CustomerPayment','PublishAdjustmentAsync','TotalAvailableToSpend + amount','PaymentMethod','ReceivedByUserId'):
    if required not in _v79_workflow + _v79_payment: errors.append(f'v79 shared customer-payment workflow missing: {required}')
for required in ('Customer Payment...','RecordCustomerPaymentAsync','CustomerPaymentDialog','EmployeeAmountDue','Apply Payment'):
    if required not in _v79_windows + _v79_windows_dialog: errors.append(f'v79 Windows customer-payment UI missing: {required}')
for required in ('Customer Payment...','RecordCustomerPaymentAsync','CustomerPaymentWindow','New Customer Responsibility','charge account','Apply Payment'):
    if required not in _v79_linux + _v79_linux_payment: errors.append(f'v79 Linux customer-payment/pay-later parity missing: {required}')
if 'RefundAllRemaining' in _v79_linux:
    errors.append('v79 Linux refund flow must not force RefundAllRemaining')
for required in ('RefundWindow(order)','refundService.Refund(','SelectedItems','Refund All Remaining'):
    if required not in _v79_linux + _v79_linux_refund: errors.append(f'v79 Linux partial-refund parity missing: {required}')
for required in ('AVAILABLE TO SPEND','Transactions —'):
    if required not in _v79_linux_display: errors.append(f'v79 Linux customer-display parity missing: {required}')
for required in ('DAILY DASHBOARD','ITEMS SOLD — QUANTITY','ITEMS SOLD — VALUE','LoadIdleDashboardAsync'):
    if required not in _v79_linux: errors.append(f'v79 Linux daily-dashboard parity missing: {required}')
for required in ('No items sold today','BuildWedge'):
    if required not in _v79_linux_pie: errors.append(f'v79 Linux pie-chart parity missing: {required}')

# v84 void/payment UX clarity and Windows/Linux parity.
_v84_windows=(root/'src/Aegis.Cafeteria.UI/MainForm.cs').read_text(encoding='utf-8-sig')
_v84_windows_payment=(root/'src/Aegis.Cafeteria.UI/CustomerPaymentDialog.cs').read_text(encoding='utf-8-sig')
_v84_linux=(root/'src/Aegis.Cafeteria.UI.Linux/MainWindow.cs').read_text()
_v84_linux_payment=(root/'src/Aegis.Cafeteria.UI.Linux/CustomerPaymentWindow.cs').read_text()
for required in ('Void Current Sale','VOID AVAILABLE','Manager approval and reason required','Customer Payment...','reduce the Employee Amount Due'):
    if required not in _v84_windows: errors.append(f'v84 Windows void/payment UX missing: {required}')
    if required not in _v84_linux: errors.append(f'v84 Linux void/payment UX missing: {required}')
for required in ('Employee Amount Due before payment','Employee Amount Due after payment','Apply Payment'):
    if required not in _v84_windows_payment: errors.append(f'v84 Windows payment dialog missing: {required}')
    if required not in _v84_linux_payment: errors.append(f'v84 Linux payment dialog missing: {required}')
if errors:
    raise SystemExit('\n'.join(errors))
print('Static audit passed: v79 closes the customer-payment UI gap and aligns Linux with Windows for pay-later visibility, partial refunds, customer-facing Available to Spend, and daily product pie charts.')

# v80 employee amount due summary parity
windows_main = (root / "src/Aegis.Cafeteria.UI/MainForm.cs").read_text(encoding="utf-8")
linux_main = (root / "src/Aegis.Cafeteria.UI.Linux/MainWindow.cs").read_text(encoding="utf-8")
v80_errors=[]
if '"Employee Amount Due", this.employeeAmountDue' not in windows_main: v80_errors.append("Windows customer summary must show Employee Amount Due")
if 'EmployeeAmountDue' not in windows_main: v80_errors.append("Windows Employee Amount Due must use shared financial status")
if '"Employee Amount Due", employeeAmountDue' not in linux_main: v80_errors.append("Linux customer summary must show Employee Amount Due")
if 'EmployeeAmountDue' not in linux_main: v80_errors.append("Linux Employee Amount Due must use shared financial status")
if v80_errors: raise SystemExit('\n'.join(v80_errors))
print("Static audit passed: v80 adds Employee Amount Due to the employee summary on Windows and Linux using the shared calculated value.")

# v81: Excel/PDF export and Windows/Linux reporting parity.
excel_exporter = (root / "src/Aegis.Cafeteria.Reporting/Export/ExcelReportExporter.cs").read_text(encoding="utf-8")
pdf_exporter = (root / "src/Aegis.Cafeteria.Reporting/Export/PdfReportExporter.cs").read_text(encoding="utf-8")
reporting_di = (root / "src/Aegis.Cafeteria.Reporting/CafeteriaReporting.cs").read_text(encoding="utf-8")
windows_reports = (root / "src/Aegis.Cafeteria.UI/ReportRunnerDialog.cs").read_text(encoding="utf-8")
windows_main_v81 = (root / "src/Aegis.Cafeteria.UI/MainForm.cs").read_text(encoding="utf-8")
linux_main = (root / "src/Aegis.Cafeteria.UI.Linux/MainWindow.cs").read_text(encoding="utf-8")
assert 'public string Format => "XLSX"' in excel_exporter
assert 'public string FileExtension => ".xlsx"' in excel_exporter
assert 'public string Format => "PDF"' in pdf_exporter
assert 'public string FileExtension => ".pdf"' in pdf_exporter
assert 'ExcelReportExporter' in reporting_di and 'PdfReportExporter' in reporting_di
assert 'SaveAsync("XLSX")' in windows_reports and 'SaveAsync("PDF")' in windows_reports
assert 'Export to Excel (.xlsx)' in linux_main and 'Export to PDF (.pdf)' in linux_main
assert 'Overview & KPIs' in windows_reports or 'Overview & KPIs' in windows_main_v81
assert 'Sales & Purchases' in linux_main
print("Static audit passed: v81 adds shared XLSX/PDF report export and exposes the formats in both Windows and Linux reporting UIs.")

# v83: customer-facing employee amount due and retryable Windows employee search.
v83_errors = []
windows_display_v83 = (root / "src/Aegis.Cafeteria.UI/CustomerDisplayForm.cs").read_text(encoding="utf-8")
linux_display_v83 = (root / "src/Aegis.Cafeteria.UI.Linux/CustomerDisplayWindow.cs").read_text(encoding="utf-8")
windows_search_v83 = (root / "src/Aegis.Cafeteria.UI/CustomerSearchDialog.cs").read_text(encoding="utf-8")
windows_main_v83 = (root / "src/Aegis.Cafeteria.UI/MainForm.cs").read_text(encoding="utf-8")
linux_main_v83 = (root / "src/Aegis.Cafeteria.UI.Linux/MainWindow.cs").read_text(encoding="utf-8")
if '"EMPLOYEE AMOUNT DUE", this.employeeAmountDue' not in windows_display_v83: v83_errors.append("Windows customer-facing display must show Employee Amount Due")
if '"EMPLOYEE AMOUNT DUE", employeeAmountDue' not in linux_display_v83: v83_errors.append("Linux customer-facing display must show Employee Amount Due")
if 'this.search.ReadOnly = true' in windows_search_v83: v83_errors.append("Windows customer search results must keep the search term editable")
if 'new CustomerSearchDialog(this.identification, candidates, searchText)' not in windows_main_v83: v83_errors.append("Windows candidate dialog must retain the identification service for retry search")
if 'Customer Responsibility (before payments)' not in windows_main_v83 or 'Customer Payments' not in windows_main_v83: v83_errors.append("Windows summary must distinguish gross responsibility from payments and employee amount due")
if 'Customer Responsibility (before payments)' not in linux_main_v83 or 'Customer Payments' not in linux_main_v83: v83_errors.append("Linux summary must distinguish gross responsibility from payments and employee amount due")
if v83_errors:
    raise SystemExit("v83 audit failed: " + "; ".join(v83_errors))
print("Static audit passed: v83 shows Employee Amount Due to the customer, clarifies responsibility versus payments, and makes Windows employee search retryable in-place.")

# v85: suppress report rows whose measurable numeric values are all zero.
v85_reporting = (root / "src/Aegis.Cafeteria.Reporting/CafeteriaReportService.cs").read_text(encoding="utf-8")
for required in ('SuppressZeroOnlyRows(tables)', 'ShouldShowReportRow', 'numericValues.Length == 0 || numericValues.Any(value => value != 0m)'):
    if required not in v85_reporting:
        raise SystemExit(f'v85 zero-row suppression missing: {required}')
print("Static audit passed: v85 suppresses zero-only report rows centrally for preview and all export formats.")

# v86: render report exporters away from UI thread to prevent PDF/export freezes.
v86_windows = (root / "src/Aegis.Cafeteria.UI/ReportRunnerDialog.cs").read_text(encoding="utf-8")
v86_linux = (root / "src/Aegis.Cafeteria.UI.Linux/MainWindow.cs").read_text(encoding="utf-8")
for label, text in (("Windows", v86_windows), ("Linux", v86_linux)):
    if 'RenderExporterAsync' not in text or 'Task.Run(async () =>' not in text:
        raise SystemExit(f'v86 {label} report rendering must run off the UI thread')
    if 'Rendering preview...' not in text:
        raise SystemExit(f'v86 {label} preview should expose rendering status')
print("Static audit passed: v86 renders preview/export payloads off the UI thread on Windows and Linux.")

# v87: balanced POS workspace, grid header alignment, and live tender recalculation.
v87_windows = (root / "src/Aegis.Cafeteria.UI/MainForm.cs").read_text(encoding="utf-8")
v87_tender = (root / "src/Aegis.Cafeteria.UI/TenderDialog.cs").read_text(encoding="utf-8")
v87_linux = (root / "src/Aegis.Cafeteria.UI.Linux/MainWindow.cs").read_text(encoding="utf-8")
if 'int desiredLeft = available / 2;' not in v87_windows:
    raise SystemExit('v87 Windows main workspace must use a 50/50 split')
if 'new GridLength(1, GridUnitType.Star)' not in v87_linux or 'new GridLength(0.9, GridUnitType.Star)' in v87_linux:
    raise SystemExit('v87 Linux main workspace must use a 50/50 split')
if 'SyncGridHeaderAlignment' not in v87_windows or 'column.HeaderCell.Style.Alignment = alignment' not in v87_windows:
    raise SystemExit('v87 Windows grid headers must mirror cell alignment')
for required in ('this.account.TextChanged += (_, _) => UpdatePaymentState();', 'this.cashReceived.TextChanged += (_, _) => UpdateAccountFromCash();', 'TryCurrentEditorValue'):
    if required not in v87_tender:
        raise SystemExit(f'v87 live tender recalculation missing: {required}')
print('Static audit passed: v87 balances the POS workspace 50/50, aligns grid headers with cell content, and recalculates payment state while typing.')

# v88: production-secret hardening, documentation and test coverage.
v88_secret_names = (root / 'src/Aegis.Cafeteria.Application/Abstractions/IApplicationSecretProvider.cs').read_text(encoding='utf-8')
v88_resolver = (root / 'src/Aegis.Cafeteria.Infrastructure/Configuration/CafeteriaRuntimeOptionsResolver.cs').read_text(encoding='utf-8')
v88_services = (root / 'src/Aegis.Cafeteria.Services/Program.cs').read_text(encoding='utf-8')
v88_win_program = (root / 'src/Aegis.Cafeteria.UI/Program.cs').read_text(encoding='utf-8')
v88_linux_program = (root / 'src/Aegis.Cafeteria.UI.Linux/Program.cs').read_text(encoding='utf-8')
if 'CafeteriaServicesApiKey' not in v88_secret_names or 'ApplicationSecretNames.CafeteriaServicesApiKey' not in v88_resolver:
    raise SystemExit('v88 must resolve CafeteriaServicesApiKey through the application secret provider')
for label, text in [('Services', v88_services), ('Windows POS', v88_win_program), ('Linux POS', v88_linux_program)]:
    if 'CafeteriaServicesApiKey' not in text or 'IsDevelopment()' not in text:
        raise SystemExit(f'v88 {label} must fail closed for missing production CafeteriaServicesApiKey')
for rel in ['src/Aegis.Cafeteria.UI/appsettings.json','src/Aegis.Cafeteria.UI.Linux/appsettings.json','src/Aegis.Cafeteria.Services/appsettings.json']:
    text=(root/rel).read_text(encoding='utf-8')
    if 'Data Source=JMDB01' in text or 'Password=' in text or 'Pwd=' in text:
        raise SystemExit(f'v88 production appsettings must not contain plaintext database credentials: {rel}')
    if 'CafeteriaServicesApiKey' not in text:
        raise SystemExit(f'v88 appsettings must expose a Bitwarden secret-ID slot for CafeteriaServicesApiKey: {rel}')
for rel in ['docs/PRODUCTION-DEPLOYMENT.md','docs/FFP-MANAGER-INTEGRATION.md','docs/ARCHITECTURE.md','docs/DATABASE-DEPLOYMENT.md','docs/FFP-INTEGRATION.md','docs/OPERATIONS.md','docs/REPORTING.md','docs/SERVICES.md','docs/TESTING.md','docs/WEEKLY-MESSAGING.md']:
    if not (root/rel).exists() or (root/rel).stat().st_size < 200:
        raise SystemExit(f'v88 documentation missing/incomplete: {rel}')
for rel in ['deployment/examples/windows-pos.appsettings.Production.json','deployment/examples/linux-pos.appsettings.Production.json','deployment/examples/services.appsettings.Production.json']:
    if not (root/rel).exists():
        raise SystemExit(f'v88 production configuration example missing: {rel}')
print('Static audit passed: v88 hardens production secrets, adds Bitwarden-backed service API keys, fail-closed production startup, production examples and complete deployment/FFP Manager documentation.')

# v89 Fitzroy/IoT style and compile-regression guards
v89_linux_main = (root / "src/Aegis.Cafeteria.UI.Linux/MainWindow.cs").read_text(encoding="utf-8")
v89_linux_display = (root / "src/Aegis.Cafeteria.UI.Linux/CustomerDisplayWindow.cs").read_text(encoding="utf-8")
v89_contracts = (root / "src/Aegis.Cafeteria.Domain/Services/CafeteriaServiceContracts.cs").read_text(encoding="utf-8")
v89_sync = (root / "src/Aegis.Cafeteria.Infrastructure/Services/ManagementEventSyncWorker.cs").read_text(encoding="utf-8")
assert "using IOPath = System.IO.Path;" in v89_linux_main
assert "IOPath.Combine" in v89_linux_main and "IOPath.GetInvalidFileNameChars" in v89_linux_main
assert "Avalonia.Controls.WindowDecorations.None" in v89_linux_display
assert "namespace Aegis.Cafeteria.Domain.Services;" in v89_contracts
for required in ("CafeteriaServiceEventKind", "AdditionalCreditServiceEvent", "StipendServiceEvent"):
    assert required in v89_contracts and required in v89_sync
import re as _v89_re
for source_path in (root / "src").rglob("*.cs"):
    source_text = source_path.read_text(encoding="utf-8-sig")
    assert not _v89_re.search(r"\bprivate\s+(?:(?:static|readonly|volatile|const|new)\s+)*[^\n;=(){}]+?\s+_[A-Za-z][A-Za-z0-9_]*", source_text), f"v89 underscore-prefixed private field remains: {source_path}"
print("Static audit passed: v89 applies Fitzroy/IoT private-field naming and compile-ambiguity guards.")


# v90 compile cleanup: Avalonia 12 decoration rename, Path disambiguation, Services contract imports.
v90_linux_display = (root / "src/Aegis.Cafeteria.UI.Linux/CustomerDisplayWindow.cs").read_text(encoding="utf-8")
v90_pie = (root / "src/Aegis.Cafeteria.UI.Linux/PieChartView.cs").read_text(encoding="utf-8")
v90_services_program = (root / "src/Aegis.Cafeteria.Services/Program.cs").read_text(encoding="utf-8")
assert "WindowDecorations = Avalonia.Controls.WindowDecorations.None;" in v90_linux_display
assert "SystemDecorations =" not in v90_linux_display
assert "using ShapePath = Avalonia.Controls.Shapes.Path;" in v90_pie
assert "new ShapePath" in v90_pie
assert "new Path" not in v90_pie
assert "using Aegis.Cafeteria.Domain.Services;" in v90_services_program
for required in ("CafeteriaServiceEventKind", "AdditionalCreditServiceEvent", "StipendServiceEvent"):
    assert required in v90_services_program
print("Static audit passed: v90 uses Avalonia 12 WindowDecorations, disambiguates Linux Path, and imports shared Services event contracts.")


# v91: automatic split-tender allocation and generated refund reasons.
v91_tender_calculator = (root / "src/Aegis.Cafeteria.Application/Orders/TenderAllocationCalculator.cs").read_text(encoding="utf-8")
v91_reason_generator = (root / "src/Aegis.Cafeteria.Application/Orders/RefundReasonGenerator.cs").read_text(encoding="utf-8")
v91_windows_tender = (root / "src/Aegis.Cafeteria.UI/TenderDialog.cs").read_text(encoding="utf-8")
v91_windows_main = (root / "src/Aegis.Cafeteria.UI/MainForm.cs").read_text(encoding="utf-8")
v91_linux_main = (root / "src/Aegis.Cafeteria.UI.Linux/MainWindow.cs").read_text(encoding="utf-8")
assert "CalculateCafeteriaAccountAmount" in v91_tender_calculator
assert "Math.Min(maximumCafeteriaAccountAmount, remainingAfterCash)" in v91_tender_calculator
assert "UpdateAccountFromCash" in v91_windows_tender
assert "TenderAllocationCalculator.CalculateCafeteriaAccountAmount" in v91_windows_tender
assert "UpdateAccountTenderFromCash" in v91_linux_main
assert "TenderAllocationCalculator.CalculateCafeteriaAccountAmount" in v91_linux_main
assert "RefundReasonGenerator.Generate(order, refundDialog.SelectedItems)" in v91_windows_main
assert "RefundReasonGenerator.Generate(order, refundDialog.SelectedItems)" in v91_linux_main
assert "original sale" in v91_reason_generator
for rel in ("tests/Aegis.Cafeteria.UnitTests/Orders/TenderAllocationCalculatorTests.cs", "tests/Aegis.Cafeteria.UnitTests/Orders/RefundReasonGeneratorTests.cs"):
    assert (root / rel).exists(), f"v91 test missing: {rel}"
print("Static audit passed: v91 auto-balances split tender as cash is entered and pre-populates editable factual refund reasons on Windows and Linux.")

# v92: SignalR is a wake-up only; durable event pull remains authoritative.
v92_service_program = (root/'src/Aegis.Cafeteria.Services/Program.cs').read_text()
v92_event_store = (root/'src/Aegis.Cafeteria.Services/Persistence/ServiceEventStore.cs').read_text()
v92_signalr_worker = (root/'src/Aegis.Cafeteria.Infrastructure/Services/ManagementEventSignalRWorker.cs').read_text()
v92_sync_worker = (root/'src/Aegis.Cafeteria.Infrastructure/Services/ManagementEventSyncWorker.cs').read_text()
v92_infra = (root/'src/Aegis.Cafeteria.Infrastructure/CafeteriaInfrastructure.cs').read_text()
v92_windows = (root/'src/Aegis.Cafeteria.UI/MainForm.cs').read_text()
v92_linux = (root/'src/Aegis.Cafeteria.UI.Linux/MainWindow.cs').read_text()
v92_dev_gateway = (root/'src/Aegis.Cafeteria.Services/Development/DevelopmentFfpGateway.cs').read_text()

for required in (
    'AddSignalR()',
    'MapHub<ManagementEventsHub>("/hubs/cafeteria")',
    'StartsWithSegments("/hubs/cafeteria")'
):
    if required not in v92_service_program:
        raise SystemExit(f'v92 service SignalR wiring missing: {required}')

for required in (
    'await this.realtimePublisher.PublishAsync',
    'await database.SaveChangesAsync(cancellationToken);'
):
    if required not in v92_event_store:
        raise SystemExit(f'v92 durable-event/realtime ordering missing: {required}')

for required in (
    'EventsAvailableMethod',
    'this.wakeSignal.Pulse()',
    'WithAutomaticReconnect',
    'X-Cafeteria-Api-Key'
):
    if required not in v92_signalr_worker:
        raise SystemExit(f'v92 POS SignalR wake-up missing: {required}')

for required in (
    'PullAndApplyAsync(stoppingToken)',
    'TimeSpan.FromSeconds(this.options.PollSeconds)',
    'await this.accountChangeNotifier.NotifyAsync(serviceEvent.CustomerId)'
):
    if required not in v92_sync_worker:
        raise SystemExit(f'v92 durable polling/account refresh missing: {required}')

for required in (
    'AddHostedService<ManagementEventSyncWorker>()',
    'AddHostedService<ManagementEventSignalRWorker>()',
    'ICustomerAccountChangeNotifier'
):
    if required not in v92_infra:
        raise SystemExit(f'v92 POS DI wiring missing: {required}')

for ui_name, ui_source in [('Windows', v92_windows), ('Linux', v92_linux)]:
    for required in ('accountChangeNotifier.Subscribe(OnCustomerAccountChangedAsync)', 'accountChangeSubscription.Dispose()'):
        if required not in ui_source:
            raise SystemExit(f'v92 {ui_name} active-account refresh wiring missing: {required}')

for required in (
    'DevelopmentFfpGateway',
    'Test Employee One',
    '900001'
):
    if required not in v92_dev_gateway:
        raise SystemExit(f'v92 isolated laptop test gateway missing: {required}')

if 'developmentDataEnabled && !builder.Environment.IsDevelopment()' not in v92_service_program:
    raise SystemExit('v92 DevelopmentData must fail closed outside Development')

print('Static audit passed: v92 adds SignalR wake-up, preserves durable polling/offline catch-up, refreshes active customers on Windows/Linux, and provides an isolated laptop integration test profile.')


# v93-v98: funding policy remains Services-owned; v98 removes Services/FFP product catalog ownership.
v93_program = (root/'src/Aegis.Cafeteria.Services/Program.cs').read_text()
v93_store = (root/'src/Aegis.Cafeteria.Services/Persistence/CafeteriaFundingPolicyStore.cs').read_text()
v93_windows_admin = (root/'src/Aegis.Cafeteria.UI/CafeteriaAdministrationDialog.cs').read_text()
for required in ('CafeteriaFundingPolicyStore', '/api/management/funding-policy'):
    if required not in v93_program:
        raise SystemExit(f'v98 Services funding-policy wiring missing: {required}')
for forbidden in ('/api/management/products', '/api/ffp/catalog', 'PublishCatalogChangedAsync'):
    if forbidden in v93_program:
        raise SystemExit(f'v98 Services must not own POS products: {forbidden}')
for required in ('DailyCreditLimitRate', 'DailyCompanySubsidyRate', 'FundingPolicyHistory'):
    if required not in v93_store and required not in (root/'src/Aegis.Cafeteria.Services/Persistence/ServiceDbContext.cs').read_text():
        raise SystemExit(f'v98 funding policy persistence missing: {required}')
for required in ('POS Product & Price Maintenance', 'Add Product', 'Save Selected Product', 'Delete Selected Product'):
    if required not in v93_windows_admin:
        raise SystemExit(f'v98 local POS product administration missing: {required}')
print('Static audit passed: v98 keeps funding policy in Services while making products POS-local.')

# v94: funding policy is managed outside the POS and mirrored to FFP00 by Services.
v94_pos_admin = (root / "src/Aegis.Cafeteria.UI/CafeteriaAdministrationDialog.cs").read_text(encoding="utf-8")
v94_main = (root / "src/Aegis.Cafeteria.UI/MainForm.cs").read_text(encoding="utf-8")
v94_services = (root / "src/Aegis.Cafeteria.Services/Program.cs").read_text(encoding="utf-8")
v94_policy_service = (root / "src/Aegis.Cafeteria.Services/FundingPolicyManagementService.cs").read_text(encoding="utf-8")

if "Save Funding Policy" in v94_pos_admin or "BuildFundingPolicyPage" in v94_pos_admin:
    raise SystemExit("v94 POS must not edit funding policy")

if 'new("Price Maintenance...")' not in v94_main:
    raise SystemExit("v94 POS Administration must expose price maintenance only")

for required in (
    'FundingPolicyManagementService',
    'MapPut("/api/management/funding-policy"'
):
    if required not in v94_services:
        raise SystemExit(f"v94 management funding-policy API missing: {required}")

for required in (
    'IFundingPolicyCompatibilityGateway',
    'MirrorFundingPolicyAsync',
    'dailyCreditLimitRate',
    'dailyCompanySubsidyRate'
):
    if required not in v94_policy_service:
        raise SystemExit(f"v98 FFP00 compatibility mirror missing: {required}")

print("Static audit passed: v94 assigns funding-policy maintenance to FFP Manager/Services and mirrors approved rates to FFP00 while POS remains price-maintenance only.")

# v95: Cafeteria owns employee liability, not payroll processing.
v95_root = root / "src"
v95_text = "\n".join(path.read_text(encoding="utf-8") for path in v95_root.rglob("*.cs"))
for forbidden in (
    "class PayrollDeductionService",
    "record PayrollDeductionRecord",
    "record PayrollCollectionResult",
    "class DebtCarryForwardService",
    "PayrollDeductionRequired"
):
    if forbidden in v95_text:
        raise SystemExit(f"v95 payroll-boundary audit failed: forbidden application concept remains: {forbidden}")
for required in (
    '"Employee Amount Due"',
    'EmployeeAmountDue'
):
    if required not in v95_text:
        raise SystemExit(f"v95 employee-liability audit failed: missing {required}")
legacy_payroll_mentions = []
for path in v95_root.rglob("*.cs"):
    if "Aegis.Cafeteria.Services" in str(path):
        continue
    text = path.read_text(encoding="utf-8")
    if "Payroll" in text:
        for line_number, line in enumerate(text.splitlines(), start=1):
            if "Payroll" in line and "FinalPayrollDeduction" not in line:
                legacy_payroll_mentions.append(f"{path.relative_to(root)}:{line_number}: {line.strip()}")
if legacy_payroll_mentions:
    raise SystemExit("v95 payroll-boundary audit failed: non-legacy payroll terminology remains: " + "; ".join(legacy_payroll_mentions[:10]))
print("Static audit passed: v95 makes Employee Amount Due the POS liability and removes payroll-processing/tracking components while preserving the legacy SQLite column name for upgrade compatibility.")


# v96: Cafeteria reports Employee Amount Due to Payroll by email without tracking payroll collection.
v96_program = (root / "src/Aegis.Cafeteria.Services/Program.cs").read_text(encoding="utf-8")
v96_worker = (root / "src/Aegis.Cafeteria.Services/Workers/ScheduledEmailWorker.cs").read_text(encoding="utf-8")
v96_report = (root / "src/Aegis.Cafeteria.Services/Email/PayrollDeductionReportSource.cs").read_text(encoding="utf-8")
v96_projection = (root / "src/Aegis.Cafeteria.Application/Integration/ExternalLedgerProjectionService.cs").read_text(encoding="utf-8")
for required in (
    'MapGet("/api/management/payroll-report"',
    'MapPost("/api/management/payroll-report/send"',
    'PayrollDeductionReportSource'
):
    if required not in v96_program:
        raise SystemExit(f"v96 payroll notification API missing: {required}")
for required in (
    'PayrollRecipients',
    'PayrollReportHourLocal',
    'Cafeteria-Payroll-Detail-',
    'Cafeteria-Payroll-Summary-',
    'Employee Amount Due'
):
    if required not in v96_worker:
        raise SystemExit(f"v96 scheduled payroll email missing: {required}")
for required in (
    'CompanyResponsibility',
    'CustomerPayments',
    'EmployeeAmountDue',
    'Cafeteria Refund'
):
    if required not in v96_report:
        raise SystemExit(f"v96 payroll report calculation missing: {required}")
if 'Cafeteria Prior Period Refund' not in v96_projection:
    raise SystemExit("v96 must distinguish prior-period refunds from same-period payroll calculations")
print("Static audit passed: v96 emails detailed and summary Employee Amount Due reports to Payroll while leaving payroll collection/reconciliation outside Cafeteria.")

# v97: Development uses real JMDB01/FFP as a full read/write integration-test database.
v97_options = (root / "src/Aegis.Cafeteria.Infrastructure/Configuration/FfpIntegrationOptions.cs").read_text(encoding="utf-8")
v97_gateway = (root / "src/Aegis.Cafeteria.Infrastructure/Ffp/FfpSqlApplicationGateway.cs").read_text(encoding="utf-8")
v97_development = (root / "src/Aegis.Cafeteria.Services/appsettings.Development.json").read_text(encoding="utf-8")
v97_launcher = (root / "scripts/windows/Start-LaptopIntegrationTest.ps1").read_text(encoding="utf-8")
if "public bool AllowWrites { get; init; } = true;" not in v97_options:
    raise SystemExit("v97 FFP write-safety option missing")
for required in (
    "EnsureWritesEnabled();",
    "if (!options.AllowWrites)",
    "FFP writes are disabled for this environment"
):
    if required not in v97_gateway:
        raise SystemExit(f"v97 FFP write guard missing: {required}")
for required in (
    '"Enabled": false',
    'Data Source=JMDB01;Initial Catalog=FFP;Integrated Security=true;Pooling=false;Encrypt=false',
    '"AllowWrites": true'
):
    if required not in v97_development:
        raise SystemExit(f"v97 Development real-FFP configuration missing: {required}")
for required in (
    "FfpIntegration__AllowWrites='true'",
    "DevelopmentData__Enabled='false'",
    "Initial Catalog=FFP"
):
    if required not in v97_launcher:
        raise SystemExit(f"v97 laptop launcher safety setting missing: {required}")
print("Static audit passed: v97 Development uses JMDB01/FFP through Windows Integrated Security for full read/write integration testing while retaining isolated local Services/POS SQLite databases.")

# v98: eligible-only workforce, local POS products, immutable transaction prices.
v98_gateway = (root / "src/Aegis.Cafeteria.Infrastructure/Ffp/FfpSqlApplicationGateway.cs").read_text(encoding="utf-8")
v98_identification = (root / "src/Aegis.Cafeteria.Infrastructure/Persistence/EfCustomerIdentificationService.cs").read_text(encoding="utf-8")
v98_di = (root / "src/Aegis.Cafeteria.Infrastructure/CafeteriaInfrastructure.cs").read_text(encoding="utf-8")
v98_order_rows = (root / "src/Aegis.Cafeteria.Infrastructure/Persistence/Models/CoreRows.cs").read_text(encoding="utf-8")
v98_projection = (root / "src/Aegis.Cafeteria.Application/Integration/ExternalLedgerProjectionService.cs").read_text(encoding="utf-8")
if "WHERE e.EligibleForLunchTicket = 1" not in v98_gateway:
    raise SystemExit("v98 must load eligible FFP employees only")
for forbidden in ("IProductCatalogSource", "IProductPriceMaintenanceGateway", "GetProductsAsync", "UpdateProductAsync"):
    if forbidden in v98_gateway:
        raise SystemExit(f"v98 FFP gateway still owns products: {forbidden}")
if "IProductCatalogMaintenance, EfProductCatalogMaintenance" not in v98_di:
    raise SystemExit("v98 local POS product maintenance DI missing")
if ".Where(x => x.IsEligibleForCredit)" not in v98_identification or "x.IsEligibleForCredit && x.NormalizedName.Contains" not in v98_identification:
    raise SystemExit("v98 customer browse/search must exclude stale ineligible employee rows")
for required in ("ProductName", "UnitPrice", "Quantity"):
    if required not in v98_order_rows:
        raise SystemExit(f"v98 immutable order-line snapshot missing: {required}")
if "@ {x.UnitPrice:N2}" not in v98_projection:
    raise SystemExit("v98 FFP ledger purchase description must preserve transaction unit price")
print("Static audit passed: v98 loads eligible employees only, owns products in POS SQLite, and preserves product descriptions/prices in immutable transaction/ledger records.")

# v99: shared Visual Studio multi-project development launch profile.
v99_solution_launch = root / "Aegis.Cafeteria.slnLaunch"
if not v99_solution_launch.exists():
    raise SystemExit("v99 shared Visual Studio solution launch profile is missing")
v99_profiles = json.loads(v99_solution_launch.read_text(encoding="utf-8"))
v99_combined = next((profile for profile in v99_profiles if profile.get("Name") == "Development - Services + Windows POS"), None)
if v99_combined is None:
    raise SystemExit("v99 combined Services + Windows POS launch profile is missing")
v99_projects = v99_combined.get("Projects", [])
expected_v99_projects = [
    ("src\\Aegis.Cafeteria.Services\\Aegis.Cafeteria.Services.csproj", "Start"),
    ("src\\Aegis.Cafeteria.UI\\Aegis.Cafeteria.UI.csproj", "Start")
]
actual_v99_projects = [(project.get("Path"), project.get("Action")) for project in v99_projects]
if actual_v99_projects != expected_v99_projects:
    raise SystemExit(f"v99 combined launch profile must start Services then Windows POS: {actual_v99_projects}")
for launch_file in (
    root / "src/Aegis.Cafeteria.Services/Properties/launchSettings.json",
    root / "src/Aegis.Cafeteria.UI/Properties/launchSettings.json"
):
    launch_data = json.loads(launch_file.read_text(encoding="utf-8"))
    if not any(profile.get("environmentVariables", {}).get("DOTNET_ENVIRONMENT") == "Development" for profile in launch_data.get("profiles", {}).values()):
        raise SystemExit(f"v99 Development environment missing from {launch_file}")
print("Static audit passed: v99 provides a shared Visual Studio F5 profile that starts Services then Windows POS, both in Development.")

# v100: Development-only Swagger/OpenAPI with Visual Studio browser launch.
v100_program = (root / "src/Aegis.Cafeteria.Services/Program.cs").read_text(encoding="utf-8")
v100_project = (root / "src/Aegis.Cafeteria.Services/Aegis.Cafeteria.Services.csproj").read_text(encoding="utf-8")
v100_launch = json.loads((root / "src/Aegis.Cafeteria.Services/Properties/launchSettings.json").read_text(encoding="utf-8"))
for required in (
    "if (builder.Environment.IsDevelopment())",
    "builder.Services.AddSwaggerGen();",
    "if (app.Environment.IsDevelopment())",
    "app.UseSwagger();",
    "app.UseSwaggerUI();"
):
    if required not in v100_program:
        raise SystemExit(f"v100 Development Swagger configuration missing: {required}")
if 'Swashbuckle.AspNetCore" Version="10.2.3"' not in v100_project:
    raise SystemExit("v100 Swashbuckle.AspNetCore package reference missing")
v100_service_profile = v100_launch.get("profiles", {}).get("Cafeteria Services Development", {})
if v100_service_profile.get("launchBrowser") is not True or v100_service_profile.get("launchUrl") != "swagger":
    raise SystemExit("v100 Services Development profile must launch Swagger automatically")
if v100_service_profile.get("applicationUrl") != "http://localhost:5088":
    raise SystemExit("v100 Services Development URL changed unexpectedly")
print("Static audit passed: v100 enables Swagger only in Development and opens it automatically from the Visual Studio Services profile.")


# Reset v1 cosmetic/package baseline guards.
v1_ui_project = (root / "src/Aegis.Cafeteria.UI/Aegis.Cafeteria.UI.csproj").read_text(encoding="utf-8")
v1_main_form = (root / "src/Aegis.Cafeteria.UI/MainForm.cs").read_text(encoding="utf-8")
v1_login_form = (root / "src/Aegis.Cafeteria.UI/OperatorLoginForm.cs").read_text(encoding="utf-8")
if "<ApplicationIcon>Assets\\FFP.ico</ApplicationIcon>" not in v1_ui_project:
    raise SystemExit("v1 must embed the FFP icon as the Windows application icon")
if "Icon = Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath);" not in v1_main_form:
    raise SystemExit("v1 main form must use the executable FFP icon")
if "Icon = Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath);" not in v1_login_form:
    raise SystemExit("v1 login form must use the executable FFP icon")
if "TopMost = true;" not in v1_login_form:
    raise SystemExit("v1 login form must remain always on top")
print("Static audit passed: reset v1 embeds the FFP application icon and keeps the login form always on top.")

# Reset v2: audited safe recalculation of an already-opened month.
v2_contracts = (root / "src/Aegis.Cafeteria.Domain/Services/CafeteriaServiceContracts.cs").read_text(encoding="utf-8")
v2_program = (root / "src/Aegis.Cafeteria.Services/Program.cs").read_text(encoding="utf-8")
v2_worker = (root / "src/Aegis.Cafeteria.Services/Workers/MonthOpeningWorker.cs").read_text(encoding="utf-8")
v2_gateway = (root / "src/Aegis.Cafeteria.Infrastructure/Ffp/FfpSqlApplicationGateway.cs").read_text(encoding="utf-8")
v2_publisher = (root / "src/Aegis.Cafeteria.Services/Workers/ServiceFfpPublisherWorker.cs").read_text(encoding="utf-8")
v2_sync = (root / "src/Aegis.Cafeteria.Infrastructure/Services/ManagementEventSyncWorker.cs").read_text(encoding="utf-8")
v2_payroll = (root / "src/Aegis.Cafeteria.Services/Email/PayrollDeductionReportSource.cs").read_text(encoding="utf-8")
if "MonthlyAllocationRecalculation = 4" not in v2_contracts:
    raise SystemExit("v2 monthly-allocation recalculation event contract missing")
if 'app.MapPost("/api/month-opening/recalculate"' not in v2_program or '.RequireAuthorization("CafeteriaManagement")' not in v2_program:
    raise SystemExit("v2 protected month recalculation endpoint missing")
for required in (
    "recalculationGuard.CheckAsync",
    "The eligible employee set has changed since Month Opening",
    "no longer has the untouched Month Opening balance",
    "DeterministicRecalculationOperationId"
):
    if required not in v2_worker:
        raise SystemExit(f"v2 recalculation safety/idempotency guard missing: {required}")
for required in (
    "Cafeteria Purchase",
    "Cafeteria Refund",
    "Customer Payment",
    "Additional Credit",
    "Stipend Card Reduction"
):
    if required not in v2_gateway:
        raise SystemExit(f"v2 FFP activity guard missing: {required}")
if '"Monthly Allocation Recalculation"' not in v2_publisher or "ExternalLedgerEntryKind.Adjustment" not in v2_publisher:
    raise SystemExit("v2 FFP recalculation ledger projection missing")
if "case CafeteriaServiceEventKind.MonthlyAllocationRecalculation" not in v2_sync or "ReplaceMonthlyCreditAllocationAsync" not in v2_sync:
    raise SystemExit("v2 POS replacement allocation synchronization missing")
if "MonthlyAllocationRecalculationServiceEvent" not in v2_payroll:
    raise SystemExit("v2 payroll reporting does not resolve recalculated allocations")
print("Static audit passed: reset v2 safely recalculates untouched month openings, preserves audit history, republishes FFP balances, and updates POS/payroll state.")

# Reset v3: coordinated Development-only reset across Services, POS and FFP test projection.
v3_contracts = (root / "src/Aegis.Cafeteria.Domain/Services/CafeteriaServiceContracts.cs").read_text(encoding="utf-8")
v3_program = (root / "src/Aegis.Cafeteria.Services/Program.cs").read_text(encoding="utf-8")
v3_reset = (root / "src/Aegis.Cafeteria.Services/Development/DevelopmentDataResetService.cs").read_text(encoding="utf-8")
v3_gateway = (root / "src/Aegis.Cafeteria.Infrastructure/Ffp/FfpSqlApplicationGateway.cs").read_text(encoding="utf-8")
v3_sync = (root / "src/Aegis.Cafeteria.Infrastructure/Services/ManagementEventSyncWorker.cs").read_text(encoding="utf-8")
if "DevelopmentReset = 5" not in v3_contracts or "DevelopmentResetServiceEvent" not in v3_contracts:
    raise SystemExit("v3 Development reset event contract missing")
if 'if (app.Environment.IsDevelopment())' not in v3_program or 'app.MapPost("/api/development/reset"' not in v3_program:
    raise SystemExit("v3 Development-only reset endpoint missing")
if 'RequireAuthorization("CafeteriaManagement")' not in v3_program:
    raise SystemExit("v3 reset endpoint must retain Cafeteria management authorization")
for required in ("DeletedFfpLedgerEntries", "DeletedServiceEvents", "preserveProducts", "preserveFundingPolicy"):
    if required not in v3_reset:
        raise SystemExit(f"v3 coordinated reset result/semantics missing: {required}")
for required in ("DELETE FROM dbo.EmployeeIDScans", "CreditLimit = 0", "AvailableBalance = 0", "IDevelopmentDataResetGateway"):
    if required not in v3_gateway:
        raise SystemExit(f"v3 FFP test reset missing: {required}")
for required in ("case CafeteriaServiceEventKind.DevelopmentReset", "database.FfpLedgerOutbox.ExecuteDeleteAsync", "if (!preserveProducts)"):
    if required not in v3_sync:
        raise SystemExit(f"v3 POS reset synchronization missing: {required}")
if "Entry predates the latest Development reset." not in v3_program:
    raise SystemExit("v3 stale pre-reset POS publication guard missing")
print("Static audit passed: reset v3 coordinates Development reset across Services, POS and FFP while preserving products/funding policy and blocking stale outbox replay.")

# Reset v4: close the FFP reset reader before committing the SQL transaction.
v4_gateway = (root / "src/Aegis.Cafeteria.Infrastructure/Ffp/FfpSqlApplicationGateway.cs").read_text(encoding="utf-8")
v4_reset_start = v4_gateway.index("public async Task<DevelopmentDataResetResult> ResetAsync")
v4_reset_end = v4_gateway.index("public async Task<IReadOnlyList<ExternalAccountSummary>>", v4_reset_start)
v4_reset_method = v4_gateway[v4_reset_start:v4_reset_end]
reader_scope = "await using (SqlCommand command = new(sql, connection, transaction))"
commit = "await transaction.CommitAsync(cancellationToken);"
if reader_scope not in v4_reset_method or commit not in v4_reset_method:
    raise SystemExit("v4 FFP reset reader/transaction pattern missing")
if v4_reset_method.index(reader_scope) > v4_reset_method.index(commit):
    raise SystemExit("v4 FFP reset must dispose the reader/command before committing")
reader_close_brace = v4_reset_method.index("            }\n\n            await transaction.CommitAsync(cancellationToken);", v4_reset_method.index(reader_scope))
if reader_close_brace < 0:
    raise SystemExit("v4 FFP reset reader scope does not close before transaction commit")
print("Static audit passed: reset v4 disposes the FFP SQL reader before committing the development-reset transaction.")
