from docx import Document
from docx.shared import Inches, Pt, RGBColor
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.enum.table import WD_TABLE_ALIGNMENT, WD_CELL_VERTICAL_ALIGNMENT
from docx.enum.section import WD_SECTION
from docx.enum.style import WD_STYLE_TYPE
from docx.oxml import OxmlElement
from docx.oxml.ns import qn
from docx.enum.text import WD_BREAK
from pathlib import Path

OUT=Path('docs')
OUT.mkdir(exist_ok=True)
NAVY='17365D'; BLUE='2F75B5'; PALE='EAF2F8'; GRAY='F3F5F7'; DARK='243447'; GREEN='E2F0D9'; WARN='FFF2CC'; RED='FCE4D6'

def shade(cell, fill):
    tcPr=cell._tc.get_or_add_tcPr(); shd=tcPr.find(qn('w:shd'))
    if shd is None:
        shd=OxmlElement('w:shd'); tcPr.append(shd)
    shd.set(qn('w:fill'), fill)

def margins(cell, top=80, start=100, bottom=80, end=100):
    tc=cell._tc; tcPr=tc.get_or_add_tcPr(); tcMar=tcPr.first_child_found_in('w:tcMar')
    if tcMar is None:
        tcMar=OxmlElement('w:tcMar'); tcPr.append(tcMar)
    for tag,val in [('top',top),('start',start),('bottom',bottom),('end',end)]:
        node=tcMar.find(qn('w:'+tag))
        if node is None: node=OxmlElement('w:'+tag); tcMar.append(node)
        node.set(qn('w:w'), str(val)); node.set(qn('w:type'),'dxa')

def set_repeat_table_header(row):
    trPr=row._tr.get_or_add_trPr(); tblHeader=OxmlElement('w:tblHeader'); tblHeader.set(qn('w:val'),'true'); trPr.append(tblHeader)

def base_doc(title, subtitle):
    d=Document(); sec=d.sections[0]; sec.page_width=Inches(8.5); sec.page_height=Inches(11); sec.top_margin=Inches(.72); sec.bottom_margin=Inches(.68); sec.left_margin=Inches(.78); sec.right_margin=Inches(.78)
    styles=d.styles
    styles['Normal'].font.name='Aptos'; styles['Normal'].font.size=Pt(10.5); styles['Normal'].font.color.rgb=RGBColor.from_string(DARK)
    styles['Normal'].paragraph_format.space_after=Pt(5.5); styles['Normal'].paragraph_format.line_spacing=1.08
    for s,size,color in [('Title',26,NAVY),('Heading 1',17,NAVY),('Heading 2',13,BLUE),('Heading 3',11,DARK)]:
        st=styles[s]; st.font.name='Aptos Display' if s!='Normal' else 'Aptos'; st.font.size=Pt(size); st.font.color.rgb=RGBColor.from_string(color); st.font.bold=True
    styles['Heading 1'].paragraph_format.space_before=Pt(14); styles['Heading 1'].paragraph_format.space_after=Pt(6)
    styles['Heading 2'].paragraph_format.space_before=Pt(9); styles['Heading 2'].paragraph_format.space_after=Pt(4)
    if 'Code' not in styles:
        st=styles.add_style('Code', WD_STYLE_TYPE.PARAGRAPH); st.font.name='Cascadia Mono'; st.font.size=Pt(9); st.font.color.rgb=RGBColor.from_string(DARK); st.paragraph_format.left_indent=Inches(.15); st.paragraph_format.right_indent=Inches(.1); st.paragraph_format.space_before=Pt(3); st.paragraph_format.space_after=Pt(5)
    p=d.add_paragraph(); p.alignment=WD_ALIGN_PARAGRAPH.CENTER; r=p.add_run('AEGIS.CAFETERIA'); r.bold=True; r.font.name='Aptos Display'; r.font.size=Pt(13); r.font.color.rgb=RGBColor.from_string(BLUE)
    p=d.add_paragraph(style='Title'); p.alignment=WD_ALIGN_PARAGRAPH.CENTER; p.add_run(title)
    p=d.add_paragraph(); p.alignment=WD_ALIGN_PARAGRAPH.CENTER; r=p.add_run(subtitle); r.font.size=Pt(12); r.font.color.rgb=RGBColor.from_string(BLUE)
    p=d.add_paragraph(); p.alignment=WD_ALIGN_PARAGRAPH.CENTER; r=p.add_run('Version 11  •  22 August 2026'); r.bold=True; r.font.size=Pt(10); r.font.color.rgb=RGBColor.from_string(DARK)
    t=d.add_table(rows=1, cols=1); t.alignment=WD_TABLE_ALIGNMENT.CENTER; t.autofit=False; t.columns[0].width=Inches(6.5); c=t.cell(0,0); shade(c, PALE); margins(c,150,180,150,180); c.vertical_alignment=WD_CELL_VERTICAL_ALIGNMENT.CENTER
    pp=c.paragraphs[0]; pp.alignment=WD_ALIGN_PARAGRAPH.CENTER; rr=pp.add_run('Local-first POS  •  EF Core  •  SQLite  •  SQL Server/PostgreSQL  •  SignalR  •  Bitwarden Secrets Manager'); rr.bold=True; rr.font.size=Pt(10); rr.font.color.rgb=RGBColor.from_string(NAVY)
    d.add_paragraph('')
    return d

def h(d,text,level=1): d.add_heading(text, level=level)
def p(d,text,bold_prefix=None):
    para=d.add_paragraph()
    if bold_prefix and text.startswith(bold_prefix):
        a=text[:len(bold_prefix)]; b=text[len(bold_prefix):]; para.add_run(a).bold=True; para.add_run(b)
    else: para.add_run(text)
    return para

def bullets(d,items):
    for x in items: d.add_paragraph(x, style='List Bullet')
def nums(d,items):
    for x in items: d.add_paragraph(x, style='List Number')
def code(d,text):
    for line in text.strip('\n').splitlines(): d.add_paragraph(line, style='Code')
def callout(d,title,text,fill=PALE):
    t=d.add_table(rows=1,cols=1); t.alignment=WD_TABLE_ALIGNMENT.CENTER; c=t.cell(0,0); shade(c,fill); margins(c,120,140,120,140); pp=c.paragraphs[0]; rr=pp.add_run(title+'  '); rr.bold=True; rr.font.color.rgb=RGBColor.from_string(NAVY); pp.add_run(text); d.add_paragraph('')
def table(d, headers, rows, widths=None):
    t=d.add_table(rows=1,cols=len(headers)); t.alignment=WD_TABLE_ALIGNMENT.CENTER; t.style='Table Grid'; t.autofit=False
    for i,hdr in enumerate(headers):
        c=t.rows[0].cells[i]; shade(c,NAVY); margins(c); c.vertical_alignment=WD_CELL_VERTICAL_ALIGNMENT.CENTER; r=c.paragraphs[0].add_run(str(hdr)); r.bold=True; r.font.color.rgb=RGBColor(255,255,255); r.font.size=Pt(9)
        if widths: c.width=Inches(widths[i])
    set_repeat_table_header(t.rows[0])
    for ri,row in enumerate(rows):
        cells=t.add_row().cells
        for i,val in enumerate(row):
            c=cells[i]; margins(c); c.vertical_alignment=WD_CELL_VERTICAL_ALIGNMENT.CENTER
            if ri%2: shade(c,GRAY)
            r=c.paragraphs[0].add_run(str(val)); r.font.size=Pt(9)
            if widths: c.width=Inches(widths[i])
    d.add_paragraph(''); return t

def footer(d):
    for sec in d.sections:
        pp=sec.footer.paragraphs[0]; pp.alignment=WD_ALIGN_PARAGRAPH.CENTER; rr=pp.add_run('Aegis.Cafeteria v11  •  Internal Technical Documentation'); rr.font.size=Pt(8); rr.font.color.rgb=RGBColor.from_string(BLUE)

def save(d,name): footer(d); d.save(OUT/name)

# 1 Architecture
D=base_doc('Architecture & Design Review','Production architecture updated for Bitwarden Secrets Manager and Windows-laptop validation')
h(D,'Executive summary')
p(D,'v11 preserves the offline-first POS architecture and removes application secret values from ordinary configuration. The Windows and Linux POS clients resolve required secrets from Bitwarden Secrets Manager through a provider-neutral interface. A development-only Windows Test Host now makes it possible to validate central persistence and SignalR locally before integrating the administration module into the FFP Application.')
callout(D,'Primary design rule','The POS transaction path remains local SQLite first. Bitwarden, SQL Server/PostgreSQL, SignalR and Graylog must never be in the cashier transaction commit path.',GREEN)
h(D,'System roles')
table(D,['Role','Persistence','Responsibilities'],[
('Windows POS','Local SQLite + synchronized central','Customer identification, account view, orders, split tender, offline operation.'),
('Linux POS','Local SQLite + synchronized central','Same application/domain services through Avalonia.'),
('FFP cafeteria module','Central SQL Server/PostgreSQL','Reference data, eligibility, rates, month opening, additional credit, audit/admin workflows.'),
('Windows Test Host','Central SQL Server/PostgreSQL','Development-only stand-in for the FFP module; SignalR hub and deterministic test endpoints.'),
('Bitwarden Secrets Manager','Bitwarden cloud or self-hosted Enterprise','Machine-account controlled source for database credentials and future protected service credentials.')], [1.35,1.65,3.55])
h(D,'Secrets architecture')
code(D,'''
appsettings.json
  -> non-secret settings + Bitwarden secret UUIDs only
BWS_ACCESS_TOKEN
  -> bootstrap machine-account token (environment only)
bws secret get <UUID>
  -> secret value resolved into process memory
Runtime options
  -> EF Core central connection / optional SignalR bearer token
''')
bullets(D,[
'No central database password or full central connection string is stored in appsettings.json.',
'The access token is not passed on the bws command line; the child process receives it through BWS_ACCESS_TOKEN.',
'Secret values are cached in process memory after successful resolution and are not persisted by the provider.',
'Errors intentionally identify the logical secret but never include the resolved value or access token.',
'Cloud Bitwarden uses the default bws environment; self-hosting uses the configurable --server-url base URL.'
])
h(D,'Threat and failure review')
table(D,['Risk','Control'],[
('Secret committed to source control','Static audit rejects Persistence/Cafeteria CentralConnectionString values in JSON; appsettings contains UUIDs only.'),
('Access token visible in process list','Provider sets BWS_ACCESS_TOKEN in child environment rather than --access-token argument.'),
('Bitwarden unavailable after startup','Already-resolved secrets remain in memory; POS local operations continue.'),
('Bitwarden unavailable before startup','v11 fails closed because the central connection secret cannot be resolved. For production HA, self-hosted Secrets Manager should be highly available; an OS-protected offline bootstrap cache is a possible future enhancement if required.'),
('SignalR unavailable','POS remains operational; ChangeJournal catch-up occurs on reconnect, rescan, startup, network-restoration or manual trigger.'),
('Central DB unavailable','POS writes remain local SQLite + Outbox; sync resumes later.'),
('Graylog unavailable','Local rolling logs continue; business operation is unaffected.')],[2.1,4.45])
h(D,'Design patterns retained')
table(D,['Pattern','Use'],[
('Strategy','Monthly credit/debt/account calculation policies and provider-neutral database behavior.'),
('Adapter','Bitwarden bws provider; FFP holiday-table adapter; SignalR boundary.'),
('Observer','SignalR and local account-change notifications.'),
('Decorator','Structured logging around persistence operations.'),
('Factory','Account-period and DbContext creation.'),
('Transactional Outbox','Not a GoF pattern, but critical for durable local-first synchronization.')],[1.45,5.1])
h(D,'Remaining production integration items')
bullets(D,[
'Implement the actual FFP holiday-table IHolidayProvider adapter after the real table/schema is supplied.',
'Generate and validate provider-specific EF Core migrations.',
'Integrate central administration services and SignalR hub into the FFP Application.',
'Choose production Bitwarden cloud versus self-hosted Enterprise and create production machine accounts/project scopes.',
'Run the complete Windows laptop gate, then repeat central-provider integration tests against the selected production database.',
'Perform deployment, backup/restore and recovery drills.'
])
save(D,'Aegis.Cafeteria.Architecture.Design.Review.v11.docx')

# 2 Programmer
D=base_doc('Programmer Guide','Domain, application, persistence, UI and Secrets Manager integration')
h(D,'Solution layout')
code(D,'''
src/
  Aegis.Cafeteria.Domain
  Aegis.Cafeteria.Application
  Aegis.Cafeteria.Infrastructure
  Aegis.Cafeteria.UI              # Windows WinForms
  Aegis.Cafeteria.UI.Linux        # Avalonia
  Aegis.Cafeteria.TestHost        # development-only central/SignalR host
tests/
  Aegis.Cafeteria.UnitTests
  Aegis.Cafeteria.PersistenceTests
''')
h(D,'Secrets API')
code(D,'''
IApplicationSecretProvider
  GetSecretAsync(logicalName)
  GetRequiredSecretAsync(logicalName)

ApplicationSecretNames
  CentralDatabaseConnectionString
  SignalRBearerToken
''')
p(D,'The application layer owns only the abstraction and logical secret names. The infrastructure project owns the Bitwarden implementation. This keeps business logic independent of Bitwarden and makes unit testing straightforward.')
h(D,'Bitwarden implementation')
p(D,'BwsSecretsManagerProvider maps logical names to UUIDs in SecretsManager:SecretIds. It executes bws with JSON output, parses only the value property, and caches successful results in memory. BwsCliExecutor uses ProcessStartInfo.ArgumentList and injects BWS_ACCESS_TOKEN into the child environment rather than the command line.')
code(D,'''
"SecretsManager": {
  "CliPath": "bws",
  "ServerUrl": "",
  "SecretIds": {
    "CentralDatabaseConnectionString": "<uuid>",
    "SignalRBearerToken": ""
  }
}
''')
callout(D,'Do not add a fallback password','Tests may use fake IApplicationSecretProvider implementations. Production/runtime code must not fall back to appsettings, user-secrets, hard-coded connection strings, or command-line passwords.',WARN)
h(D,'Runtime configuration resolution')
p(D,'CafeteriaRuntimeOptionsResolver combines non-secret JSON/environment configuration with Secrets Manager values. The result contains CafeteriaPersistenceOptions, CafeteriaRealtimeOptions and CafeteriaLoggingOptions. Environment variables are loaded after appsettings so laptop/CI overrides can use normal .NET double-underscore keys.')
h(D,'POS startup sequence')
nums(D,[
'Load appsettings plus environment-variable overrides.',
'Read Bitwarden non-secret options and secret UUID mappings.',
'Create BwsSecretsManagerProvider.',
'Resolve the central database connection string and optional SignalR bearer token.',
'Create Serilog and register POS composition root.',
'Initialize local SQLite.',
'Start background SignalR/network-trigger services.',
'Open the UI.'
])
h(D,'Windows Test Host')
p(D,'Aegis.Cafeteria.TestHost is intentionally not a production service. It uses AddCafeteriaAdministration and AddCafeteriaRealtimeServer, initializes the central schema with EnsureCreated only when explicitly enabled for development, and exposes deterministic test endpoints.')
table(D,['Endpoint','Purpose'],[
('GET /health','Verify central host, secrets resolution and provider selection.'),
('POST /test/seed','Create test customer, identifiers, products and current rate schedule.'),
('POST /test/open-current-month','Generate immutable monthly allocation snapshots.'),
('POST /test/additional-credit/{customerId}/{amount}','Create central additional credit and emit a SignalR wake-up event.'),
('/hubs/cafeteria-changes','SignalR change notification hub.')],[2.6,3.95])
h(D,'Coding rules')
bullets(D,[
'Domain and Application projects must not reference EF Core, Bitwarden, WinForms/Avalonia, Serilog or SignalR packages.',
'UI code calls application services; it must not write EF entities directly.',
'Financial balances remain derived from period snapshots/events, not mutable customer balance columns.',
'All central writes that affect POS state must create ChangeJournal entries before notification.',
'Notifications are post-commit best effort; notification failure never reverses a committed financial operation.',
'Do not log customer identifier values, secret values, BWS_ACCESS_TOKEN, database connection strings or serialized payloads.'
])
h(D,'Test workflow')
code(D,'''
.\\scripts\\windows\\Test-WindowsLaptop.ps1
''')
p(D,'This performs prerequisite verification, restore, Release build, unit tests, persistence/secrets tests, central TestHost startup, health check, test data seed and month-opening allocation generation. Manual UI scenarios are documented in the Windows Test Guide.')
save(D,'Aegis.Cafeteria.Programmer.Guide.v11.docx')

# 3 User operations
D=base_doc('User & Operations Guide','Windows cafeteria POS operating workflow')
h(D,'Normal cashier workflow')
nums(D,[
'Identify the customer by NFC tag, barcode, TRN, or manual search.',
'Confirm the displayed name and photograph.',
'Review financial status when necessary: credit limit, subsidy remaining, non-subsidy remaining, available amount, carried debt and liability.',
'Add products to the order.',
'Enter cafeteria-account and/or cash tender.',
'Complete the transaction. The sale commits locally first and the screen can return immediately to the next customer.'
])
h(D,'What the customer screen shows')
table(D,['Area','Information'],[
('Identity','Name, photograph and current account period.'),
('Financial status','Monthly credit limit, subsidy/non-subsidy remaining, additional credit, carried debt, liability, total available.'),
('History','Current-month transactions and corrections.'),
('Connectivity','Online/offline and pending synchronization status.')],[1.55,5.0])
h(D,'Monthly credit rules')
bullets(D,[
'At the beginning of the month, the system calculates the allocation once using eligible working days and the configured stipend/subsidy daily rates.',
'The full calculated monthly allocation is immediately available.',
'Customers with IsEligibleForCredit=false receive no monthly cafeteria allocation.',
'Unused positive credit expires at month end and is not carried forward.',
'Unpaid customer debt carries forward repeatedly until fully cleared.',
'Additional credit is separate, manager-authorized and does not increase the company subsidy.'
])
h(D,'Offline operation')
p(D,'The cashier can identify cached customers, view cached account information, select cached products and complete local transactions while the central database is unavailable. The POS records pending synchronization locally. When a natural synchronization trigger occurs after connectivity returns, pending work is sent automatically.')
callout(D,'No polling','The POS does not continually ask the server whether data changed. SignalR, reconnect, startup, network restoration, local writes and customer re-identification are the synchronization triggers.',PALE)
h(D,'If additional credit was just approved')
p(D,'When SignalR is connected, the customer account can refresh immediately. If the live notification was missed, identify/rescan the customer or reconnect/start the application to trigger catch-up synchronization. A cashier should not manually alter balances.')
h(D,'Operational warning indicators')
table(D,['Indicator','Action'],[
('Offline','Continue local operation if appropriate. Be aware very recent administrative changes may not yet be present.'),
('Pending sync','No cashier action normally required. If it persists after network recovery, notify IT.'),
('Secrets Manager startup failure','Application cannot load central credentials. IT should check bws, BWS_ACCESS_TOKEN, secret UUID permissions and Bitwarden availability.'),
('Customer not found','Try another identifier or manual search; if still missing, ask administration to verify the central customer/identifier record.')],[2.2,4.35])
h(D,'Support information')
p(D,'Structured application logs are written to the local rolling log and, when reachable, to Graylog at 10.10.0.3:12201. Do not send screenshots or copied logs containing customer photos or confidential information outside authorized support channels.')
save(D,'Aegis.Cafeteria.User.Operations.Guide.v11.docx')

# 4 Persistence sync
D=base_doc('Persistence & Synchronization Guide','Local-first SQLite, central EF Core and event-driven synchronization')
h(D,'Persistence topology')
code(D,'''
Cashier UI
   -> local SQLite transaction
       -> business record(s)
       -> Outbox record
   COMMIT

Event-driven sync
   -> central SQL Server or PostgreSQL
       -> Inbox/idempotency
       -> business data
       -> ChangeJournal

Central admin change
   -> central transaction + ChangeJournal
   -> best-effort SignalR wake-up
   -> POS pull by sequence into SQLite cache
''')
h(D,'SQLite rules')
bullets(D,['WAL journal mode.','synchronous=FULL.','Foreign keys enabled.','5-second busy timeout.','Business write and Outbox entry commit atomically.','Identification/account reads never require the network.'])
h(D,'Central provider')
p(D,'CentralDatabaseProvider is selectable between SqlServer and PostgreSql. The actual central connection string comes from Bitwarden Secrets Manager. Provider choice, batch sizes and timeouts remain ordinary non-secret configuration.')
h(D,'Synchronization triggers')
table(D,['Trigger','Behavior'],[
('Application startup','Drain pending local work and pull central changes after startup initialization.'),
('Local POS write','Wake sync worker immediately.'),
('SignalR notification','Pull authoritative ChangeJournal data; notification payload is not financial truth.'),
('SignalR reconnect','Catch up from last sequence after missed notifications.'),
('Network restoration','Wake sync after connectivity returns.'),
('Customer identification/rescan','Opportunity for immediate catch-up without background polling.'),
('Manual trigger','Operational/testing recovery mechanism.')],[2.0,4.55])
h(D,'Ownership model')
table(D,['Data','Owner'],[
('Customer, identifiers, products','Central/FFP admin; cached read-only at POS.'),
('Holiday source, rates','FFP/central; synchronized cache at POS.'),
('Monthly allocation snapshot','Central authoritative immutable snapshot; local cache.'),
('POS orders/payments/refunds','POS operational writer; synchronized centrally.'),
('Additional credit approvals','Central administrative writer.'),
('Carried debt / financial history','Auditable period/account events; not mutable customer fields.')],[2.15,4.4])
h(D,'Secrets interaction')
p(D,'The synchronization layer never reads JSON credentials. At process startup, the runtime resolver gets the central connection string from IApplicationSecretProvider. EF Core receives the resolved value in memory. Synchronization logging must never write DbConnection.ConnectionString or exception data that includes credential-bearing connection strings.')
h(D,'Failure semantics')
bullets(D,[
'Central unavailable: local write succeeds and remains pending.',
'Duplicate push: central Inbox/operation ID prevents double application.',
'SignalR notification lost: ChangeJournal sequence catch-up recovers it.',
'Admin commit succeeds but SignalR send fails: commit remains successful and failure is logged.',
'Synchronization poison entry: retry count is bounded and the failure remains visible for intervention.',
'Unused monthly positive balances do not become sync carry-forward state; unpaid liabilities do.'
])
save(D,'Aegis.Cafeteria.Persistence.Synchronization.Guide.v11.docx')

# 5 deployment logging
D=base_doc('Deployment & Logging Guide','Windows POS deployment, Graylog and secret-safe configuration')
h(D,'Windows POS prerequisites')
bullets(D,['.NET 10 desktop/runtime appropriate to deployment packaging.','Writable application-data directory for SQLite and rolling logs.','Bitwarden Secrets Manager bws executable available on PATH or configured by absolute path.','Machine-account BWS_ACCESS_TOKEN supplied securely to the process/service account.','Network routes to the central database, SignalR host, Bitwarden endpoint and Graylog as applicable.'])
h(D,'Non-secret configuration')
p(D,'Keep only endpoints, provider type, secret UUIDs, local paths, batch sizes and logging settings in appsettings.json. Do not place connection strings containing credentials, passwords, access tokens or API keys in JSON.')
h(D,'Bitwarden cloud vs self-hosted')
table(D,['Deployment','SecretsManager:ServerUrl'],[
('Bitwarden.com US default','Leave blank; bws uses its default cloud service.'),
('Bitwarden EU','Set the required Bitwarden EU base URL or configure bws consistently.'),
('Self-hosted Enterprise','Set the self-hosted Bitwarden base URL, for example https://bitwarden.internal.example.org.')],[2.0,4.55])
h(D,'Graylog')
p(D,'Default destination is GELF UDP 10.10.0.3:12201. A local rolling file is always configured first so Graylog/network failure does not remove the diagnostic trail.')
table(D,['Log category','Examples'],[
('Lifecycle','Startup, shutdown, database initialization, SignalR connection/reconnect.'),
('Synchronization','Pending counts, successful drain, retry/poison events, sequence changes.'),
('Financial audit events','Order IDs/totals, additional-credit IDs/amounts, period-close events—without customer names or identifier values.'),
('Security-safe errors','Logical secret name and failure class, never token/value/connection string.')],[1.8,4.75])
h(D,'Forbidden log/config data')
bullets(D,['BWS_ACCESS_TOKEN or any machine-account access token.','Resolved Bitwarden secret values.','Database passwords or full credential-bearing connection strings.','Customer names/photos in Graylog business-event properties.','NFC, barcode or TRN values.','Serialized synchronization payloads containing sensitive content.'])
h(D,'Deployment checklist')
nums(D,['Create a Bitwarden Secrets Manager project and least-privilege machine account.','Create production secrets and record only their UUIDs in deployment configuration.','Provide BWS_ACCESS_TOKEN through the approved host credential/environment mechanism.','Deploy central EF migrations.','Deploy/configure SignalR within the FFP Application.','Install/publish the POS.','Verify local SQLite, Graylog and Bitwarden connectivity.','Run online/offline/reconnect acceptance tests.','Document backup, recovery and credential-rotation procedures.'])
save(D,'Aegis.Cafeteria.Deployment.Logging.Guide.v11.docx')

# 6 secrets/windows test
D=base_doc('Secrets Manager & Windows Laptop Test Guide','Step-by-step setup for Bitwarden Secrets Manager and complete local validation')
h(D,'What this guide tests')
bullets(D,['Bitwarden machine-account authentication and secret retrieval.','.NET 10 restore/build.','All unit and persistence test projects.','Central SQL Server/LocalDB initialization.','SignalR server/client connection.','Central reference-data seed and monthly allocation creation.','POS customer identification and account display.','Local-first sale entry and synchronization.','Live additional-credit refresh.','Offline/reconnect behavior.','Graylog/local logging.'])
h(D,'1. Install prerequisites')
nums(D,['Install .NET 10 SDK.','Install JetBrains Rider or Visual Studio if desired.','Install SQL Server LocalDB/Express or identify another SQL Server instance available from the laptop.','Install Bitwarden Secrets Manager CLI (bws) and ensure bws.exe is on PATH.'])
code(D,'bws --version\ndotnet --version')
h(D,'2. Prepare Bitwarden Secrets Manager')
nums(D,['Create or select the Secrets Manager organization.','Create a project named Aegis.Cafeteria.Test (or equivalent).','Create a machine account such as Aegis-Cafeteria-Laptop-Test.','Give the machine account read-only access to the test project.','Create an access token and copy it securely; Bitwarden does not allow retrieval of the token later.','Create a secret named Aegis.Cafeteria.Test.CentralDatabaseConnectionString.'])
callout(D,'Example LocalDB secret value','Server=(localdb)\\MSSQLLocalDB;Database=AegisCafeteriaTest;Integrated Security=true;TrustServerCertificate=true',GRAY)
p(D,'The example uses Windows Integrated Security, but the value is still managed in Secrets Manager so the configuration model stays identical when production later uses a credential-bearing SQL Server or PostgreSQL connection string.')
h(D,'3. Set the bootstrap token for this PowerShell session')
code(D,'$env:BWS_ACCESS_TOKEN = "<machine-account-access-token>"')
callout(D,'Avoid setx for the test token','Use a process/session environment variable while testing. Do not paste the token into appsettings.json, source code, screenshots or logs.',WARN)
h(D,'4. Supply the secret UUID')
p(D,'Copy the UUID of the central connection-string secret. The cleanest laptop override is:')
code(D,'$env:SecretsManager__SecretIds__CentralDatabaseConnectionString = "<secret-uuid>"')
p(D,'This environment override is inherited by the Test Host and POS and avoids editing two appsettings files.')
h(D,'5. Verify prerequisites and automated tests')
code(D,'.\\scripts\\windows\\Test-Prerequisites.ps1\n.\\scripts\\windows\\Test-WindowsLaptop.ps1')
p(D,'The second command restores, builds Release, runs unit and persistence/secrets tests, starts the central Test Host, checks /health, seeds deterministic central data, and creates current-month allocation snapshots.')
h(D,'6. Manual end-to-end POS test')
p(D,'Open PowerShell A:')
code(D,'.\\scripts\\windows\\Start-TestHost.ps1')
p(D,'Open PowerShell B:')
code(D,'Invoke-RestMethod http://localhost:5080/test/seed -Method Post\nInvoke-RestMethod http://localhost:5080/test/open-current-month -Method Post\n.\\scripts\\windows\\Start-POS.ps1')
p(D,'In the POS, identify the test customer with any of:')
table(D,['Method','Test value'],[('NFC','NFC-1001'),('Barcode','BC-1001'),('TRN','123456789'),('Search','Aegis Test Customer')],[1.65,4.9])
h(D,'7. Sale and split-tender test')
nums(D,['Select Test Breakfast and/or Test Lunch.','Complete a cafeteria-account purchase within the displayed available amount.','Repeat with a split cafeteria-account + cash tender.','Verify the transaction appears in current-month history and the available amount changes correctly.','Check the local rolling log; confirm no secret values are present.'])
h(D,'8. Live additional-credit test')
p(D,'Keep customer 1001 visible in the POS. From PowerShell A or another shell:')
code(D,'Invoke-RestMethod "http://localhost:5080/test/additional-credit/1001/1000" -Method Post')
p(D,'Expected result: the central approval commits, SignalR wakes the POS, SQLite synchronizes, and the displayed account refreshes without restarting the POS.')
h(D,'9. Offline/reconnect test')
nums(D,['Stop the Test Host while leaving the POS open.','Complete a permitted POS sale. It must commit to SQLite and show pending synchronization rather than fail because central storage is absent.','Restart the Test Host.','SignalR reconnect/network restoration or customer rescan should trigger synchronization without timer polling.','Verify pending sync clears and the central side receives the operation exactly once.'])
h(D,'10. Graylog test')
bullets(D,['If 10.10.0.3 is reachable, verify Aegis.Cafeteria messages arrive on the configured GELF input.','Temporarily make Graylog unavailable and verify POS operations continue and local rolling logs remain available.','Search logs for Password=, BWS_ACCESS_TOKEN, access-token-like strings, TRN/NFC values and full connection strings; none should be present.'])
h(D,'11. Self-hosted Secrets Manager test later')
p(D,'When Enterprise self-hosting is available, set SecretsManager:ServerUrl to the self-hosted Bitwarden base URL and use a machine-account token/secret UUID from that instance. No business, persistence or UI code changes are required.')
save(D,'Aegis.Cafeteria.SecretsManager.Windows.Test.Guide.v11.docx')

# 7 test report
D=base_doc('Test Verification Report','v11 test inventory, generation-time verification and Windows release gate')
h(D,'Verification status')
callout(D,'Important','The artifact-generation environment does not contain the .NET SDK. Therefore restore/build/xUnit execution has not been performed here. Static structural checks pass; the supplied Windows laptop script is the required executable gate.',WARN)
h(D,'Automated test inventory')
table(D,['Area','Coverage'],[
('Domain/accounting','Monthly allocation, subsidy/non-subsidy calculations, carried debt, payments, stipend, refund, period-close behavior.'),
('Application services','Additional credit authorization, checkout, lifecycle, idempotency, month opening, debt carry-forward, statements.'),
('SQLite persistence','Atomic local write + Outbox, customer query/identification, provider selection.'),
('Synchronization','Push idempotency, pull sequencing, event-trigger worker semantics.'),
('Secrets Manager','Secret JSON parsing, UUID mapping, self-host server argument, bootstrap-token failure, in-memory cache, runtime options resolution.'),
('Logging configuration','Graylog/local logging defaults and safe configuration boundaries.')],[1.8,4.75])
h(D,'Windows laptop automated gate')
code(D,'.\\scripts\\windows\\Test-WindowsLaptop.ps1')
nums(D,['Checks .NET 10 and bws.','Checks BWS_ACCESS_TOKEN without displaying it.','Retrieves the configured central database secret from Bitwarden without displaying the value.','Restores the solution.','Builds the full solution in Release.','Runs UnitTests with coverage collection.','Runs PersistenceTests with coverage collection.','Starts TestHost.','Checks health.','Seeds test data.','Generates current-month allocation snapshots.'])
h(D,'Manual acceptance scenarios')
table(D,['Scenario','Expected result'],[
('NFC/barcode/TRN/search','Same customer account view loads quickly from local SQLite.'),
('Normal sale','Local commit succeeds; central sync follows asynchronously.'),
('Split tender','Card/cash allocation equals order total and financial status updates correctly.'),
('Central outage','POS continues local operation; pending sync is visible.'),
('Reconnect','Backlog synchronizes exactly once without polling.'),
('Additional credit from TestHost','SignalR triggers local refresh while customer screen is open.'),
('Bitwarden unavailable after startup','POS continues with already-resolved in-memory secret and local-first operation.'),
('Graylog unavailable','Local logs continue and POS remains operational.')],[2.25,4.3])
h(D,'Production release gate')
bullets(D,['All automated Windows laptop checks pass.','Manual scenarios above pass.','No secret values appear in repository/config/logs.','SQL Server and/or PostgreSQL provider-specific migrations are generated and tested.','Real FFP holiday adapter is implemented and tested against the existing holiday table.','FFP cafeteria-admin module hosts central services and SignalR with production authorization.','Production Bitwarden machine accounts use least privilege and defined token rotation/revocation procedures.','Backup/restore and disaster-recovery tests are completed.'])
h(D,'Generation-time checks completed')
bullets(D,['All csproj/slnx XML parsed.','All JSON configuration parsed.','Static audit confirms no plaintext central connection string in JSON configuration.','No NotImplementedException placeholders.','Obsolete timer-polling tokens absent.','Documentation generated for v11 and visually rendered before delivery.'])
save(D,'Aegis.Cafeteria.Test.Verification.Report.v11.docx')
