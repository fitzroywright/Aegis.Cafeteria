let servicesBaseUrl = '/api/services';
let selectedCustomerId = null;
let selectedCustomerName = null;
let busyRequests = 0;

const result = document.getElementById('result');
const serviceStatus = document.getElementById('serviceStatus');
const selectedEmployee = document.getElementById('selectedEmployee');

function showResult(title, value) {
    const body = typeof value === 'string' ? value : JSON.stringify(value, null, 2);
    result.textContent = `${title}\n\n${body}`;
}

function setBusy(isBusy) {
    document.body.classList.toggle('busy', isBusy);
    document.body.setAttribute('aria-busy', isBusy ? 'true' : 'false');
}

function beginBusy() {
    busyRequests += 1;
    if (busyRequests === 1) setBusy(true);
}

function endBusy() {
    busyRequests = Math.max(0, busyRequests - 1);
    if (busyRequests === 0) setBusy(false);
}

async function api(path, options = {}) {
    beginBusy();
    try {
        const response = await fetch(`${servicesBaseUrl}${path}`, {
            credentials: 'include',
            ...options,
            headers: {
                'Content-Type': 'application/json',
                ...(options.headers || {})
            }
        });

        const text = await response.text();
        let value = text;
        if (text) {
            try { value = JSON.parse(text); } catch { }
        }

        if (response.status === 401) {
            window.location.assign('/login');
            throw new Error('Your management session has expired. Sign in again.');
        }

        if (!response.ok) {
            const message = typeof value === 'string' ? value : (value.error || value.detail || JSON.stringify(value));
            throw new Error(`${response.status} ${response.statusText}: ${message}`);
        }

        return value;
    } finally {
        endBusy();
    }
}

function requireEmployee() {
    if (!selectedCustomerId) throw new Error('Select an employee first.');
    return selectedCustomerId;
}

function wire(id, handler) {
    const button = document.getElementById(id);
    if (!button) throw new Error(`Management control '${id}' is missing from the page.`);
    button.addEventListener('click', async () => {
        button.disabled = true;
        try {
            await handler();
        } catch (error) {
            showResult('Error', error.message || String(error));
        } finally {
            button.disabled = false;
        }
    });
}

function selectTab(tabName) {
    document.querySelectorAll('.tab').forEach(tab => tab.classList.toggle('active', tab.dataset.tab === tabName));
    document.querySelectorAll('.tab-panel').forEach(panel => panel.classList.toggle('active', panel.dataset.panel === tabName));
}

function formatDateTime(value) {
    if (!value) return '';
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return String(value);
    const months = ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];
    const month = months[date.getMonth()];
    const day = String(date.getDate()).padStart(2, '0');
    const year = date.getFullYear();
    let hour = date.getHours();
    const suffix = hour >= 12 ? 'PM' : 'AM';
    hour = hour % 12 || 12;
    const minute = String(date.getMinutes()).padStart(2, '0');
    return `${month} ${day} ${year} ${String(hour).padStart(2, '0')}:${minute} ${suffix}`;
}

async function selectEmployee(employee) {
    selectedCustomerId = employee.customerId;
    selectedCustomerName = employee.name;
    selectedEmployee.textContent = `${employee.name} (#${employee.customerId})`;
    document.getElementById('employeeSearch').value = employee.name;
    document.getElementById('employeeResults').innerHTML = '';

    const policy = await api(`/api/management/customers/${employee.customerId}/credit-policy`);
    document.getElementById('creditLimit').value = policy.additionalCreditLimit ?? 0;
    document.getElementById('auditCustomerId').value = employee.customerId;
    showResult('Employee selected', { ...employee, additionalCreditLimit: policy.additionalCreditLimit ?? 0 });
}

async function searchEmployees() {
    const search = document.getElementById('employeeSearch').value.trim();
    if (search.length < 2) throw new Error('Enter at least two characters.');

    const employees = await api(`/api/management/customers?search=${encodeURIComponent(search)}&take=25`);
    const host = document.getElementById('employeeResults');
    host.innerHTML = '';

    if (!employees.length) {
        host.textContent = 'No eligible employees found.';
        return;
    }

    employees.forEach(employee => {
        const row = document.createElement('div');
        row.className = 'result-row';
        row.tabIndex = 0;
        row.setAttribute('role', 'button');
        row.setAttribute('aria-label', `Select ${employee.name}`);

        const info = document.createElement('div');
        const identity = [employee.code, employee.trn].filter(Boolean).join(' • ');
        const strong = document.createElement('strong');
        strong.textContent = employee.name;
        const small = document.createElement('small');
        small.textContent = identity || `Employee ${employee.customerId}`;
        info.append(strong, small);

        const select = document.createElement('button');
        select.type = 'button';
        select.textContent = 'Select';

        const choose = async event => {
            event?.stopPropagation();
            try { await selectEmployee(employee); }
            catch (error) { showResult('Error selecting employee', error.message || String(error)); }
        };

        select.addEventListener('click', choose);
        row.addEventListener('click', choose);
        row.addEventListener('keydown', event => {
            if (event.key === 'Enter' || event.key === ' ') {
                event.preventDefault();
                choose(event);
            }
        });

        row.append(info, select);
        host.appendChild(row);
    });
}

function setReportSettings(settings) {
    document.getElementById('managerWeeklyEnabled').checked = Boolean(settings.weeklySummaryEnabled);
    document.getElementById('managerEmail').value = settings.emailAddress ?? '';
    document.getElementById('managerWeeklyDay').value = String(settings.weeklySummaryDay ?? 1);
    document.getElementById('managerWeeklyHour').value = settings.weeklySummaryHourLocal ?? 8;
}

async function loadReportSettings(show = true) {
    const settings = await api('/api/management/report-settings');
    setReportSettings(settings);
    if (show) showResult('Manager report settings', settings);
    return settings;
}

function auditQuery() {
    const params = new URLSearchParams();
    const actor = document.getElementById('auditActor').value.trim();
    const action = document.getElementById('auditAction').value.trim();
    const customerId = document.getElementById('auditCustomerId').value.trim();
    const succeeded = document.getElementById('auditSucceeded').value;
    const from = document.getElementById('auditFrom').value;
    const to = document.getElementById('auditTo').value;
    const take = document.getElementById('auditTake').value || '100';
    if (actor) params.set('actor', actor);
    if (action) params.set('action', action);
    if (customerId) params.set('customerId', customerId);
    if (succeeded) params.set('succeeded', succeeded);
    if (from) params.set('from', new Date(from).toISOString());
    if (to) params.set('to', new Date(to).toISOString());
    params.set('take', take);
    return params.toString();
}

function renderAudit(rows) {
    const host = document.getElementById('auditTable');
    host.innerHTML = '';
    if (!rows.length) {
        host.innerHTML = '<div class="hint">No audit records matched the filters.</div>';
        return;
    }

    const table = document.createElement('table');
    table.className = 'audit-grid';
    const head = document.createElement('thead');
    head.innerHTML = '<tr><th>Date/Time</th><th>Actor</th><th>Action</th><th>Entity</th><th>Customer</th><th>Result</th><th>Status</th><th>Detail</th></tr>';
    table.appendChild(head);
    const body = document.createElement('tbody');

    rows.forEach(row => {
        const tr = document.createElement('tr');
        const values = [
            formatDateTime(row.occurredAt),
            row.actor ?? '',
            row.action ?? '',
            [row.entityType, row.entityId].filter(Boolean).join(' #'),
            row.customerId ?? '',
            row.succeeded ? 'Succeeded' : 'Failed',
            row.httpStatusCode ?? '',
            row.detail ?? ''
        ];
        values.forEach(value => {
            const td = document.createElement('td');
            td.textContent = String(value);
            tr.appendChild(td);
        });
        body.appendChild(tr);
    });
    table.appendChild(body);
    host.appendChild(table);
}

async function initialize() {
    const sessionResponse = await fetch('/api/session', { credentials: 'include' });
    if (!sessionResponse.ok) {
        window.location.assign('/login');
        return;
    }

    const configResponse = await fetch('/api/config', { credentials: 'include' });
    if (configResponse.ok) {
        const config = await configResponse.json();
        servicesBaseUrl = config.servicesBaseUrl || servicesBaseUrl;
    }

    const now = new Date();
    document.getElementById('monthYear').value = now.getFullYear();
    document.getElementById('monthNumber').value = now.getMonth() + 1;
    document.getElementById('reportYear').value = now.getFullYear();
    document.getElementById('reportMonth').value = now.getMonth() + 1;

    try {
        await api('/health', { headers: {} });
        serviceStatus.textContent = 'Services Online';
        serviceStatus.style.background = '#1a774e';
        await loadReportSettings(false);
    } catch (error) {
        serviceStatus.textContent = 'Services Unavailable';
        serviceStatus.style.background = '#a93434';
        showResult('Services check failed', error.message);
    }
}

document.querySelectorAll('.tab').forEach(tab => tab.addEventListener('click', () => selectTab(tab.dataset.tab)));
document.getElementById('employeeSearch').addEventListener('keydown', event => {
    if (event.key === 'Enter') {
        event.preventDefault();
        searchEmployees().catch(error => showResult('Error', error.message));
    }
});

wire('employeeSearchButton', searchEmployees);
wire('loadCreditPolicy', async () => {
    const customerId = requireEmployee();
    const policy = await api(`/api/management/customers/${customerId}/credit-policy`);
    document.getElementById('creditLimit').value = policy.additionalCreditLimit ?? 0;
    showResult('Additional Credit Limit', policy);
});
wire('saveCreditPolicy', async () => {
    const customerId = requireEmployee();
    const additionalCreditLimit = Number(document.getElementById('creditLimit').value);
    const response = await api(`/api/management/customers/${customerId}/credit-policy`, { method: 'PUT', body: JSON.stringify({ additionalCreditLimit }) });
    showResult('Additional Credit Limit saved', response);
});
wire('approveAdditionalCredit', async () => {
    const customerId = requireEmployee();
    const amount = Number(document.getElementById('additionalCreditAmount').value);
    const reason = document.getElementById('additionalCreditReason').value.trim() || null;
    const response = await api('/api/management/additional-credit', { method: 'POST', body: JSON.stringify({ customerId, amount, reason, operationId: crypto.randomUUID() }) });
    showResult(`Additional Credit approved for ${selectedCustomerName}`, response);
});
wire('processStipend', async () => {
    const customerId = requireEmployee();
    const cashAmount = Number(document.getElementById('stipendCash').value);
    const cardReductionAmount = Number(document.getElementById('stipendReduction').value);
    const paymentMode = Number(document.getElementById('stipendMode').value);
    const expenseReportReference = document.getElementById('stipendReference').value.trim() || null;
    const notes = document.getElementById('stipendNotes').value.trim() || null;
    const response = await api('/api/management/stipends', { method: 'POST', body: JSON.stringify({ customerId, cashAmount, cardReductionAmount, paymentMode, expenseReportReference, notes, operationId: crypto.randomUUID() }) });
    showResult(`Stipend processed for ${selectedCustomerName}`, response);
});
wire('loadFunding', async () => {
    const policy = await api('/api/management/funding-policy');
    document.getElementById('dailyCreditRate').value = policy.dailyCreditLimitRate ?? '';
    document.getElementById('dailySubsidyRate').value = policy.dailyCompanySubsidyRate ?? '';
    showResult('Rates', policy);
});
wire('saveFunding', async () => {
    const dailyCreditLimitRate = Number(document.getElementById('dailyCreditRate').value);
    const dailyCompanySubsidyRate = Number(document.getElementById('dailySubsidyRate').value);
    const response = await api('/api/management/funding-policy', { method: 'PUT', body: JSON.stringify({ dailyCreditLimitRate, dailyCompanySubsidyRate }) });
    showResult('Rates saved', response);
});
wire('runMonthOpening', async () => {
    const year = Number(document.getElementById('monthYear').value);
    const month = Number(document.getElementById('monthNumber').value);
    showResult('Month Opening completed', await api(`/api/month-opening/run?year=${year}&month=${month}`, { method: 'POST', body: '{}' }));
});
wire('recalculateMonth', async () => {
    const year = Number(document.getElementById('monthYear').value);
    const month = Number(document.getElementById('monthNumber').value);
    const response = await api(`/api/month-opening/recalculate?year=${year}&month=${month}`, { method: 'POST', body: '{}' });
    const eligibleWorkingDays = Number(response.eligibleWorkingDays ?? 0);
    const dailyCreditLimitRate = Number(response.dailyCreditLimitRate ?? 0);
    const dailyCompanySubsidyRate = Number(response.dailyCompanySubsidyRate ?? 0);
    const monthlyCreditLimit = eligibleWorkingDays * dailyCreditLimitRate;
    const monthlyCompanySubsidy = Math.min(monthlyCreditLimit, eligibleWorkingDays * dailyCompanySubsidyRate);
    showResult('Month recalculation completed', { ...response, monthlyCreditLimit, monthlyCompanySubsidy, employeeFundedAllocation: Math.max(0, monthlyCreditLimit - monthlyCompanySubsidy) });
});
wire('loadPayrollReport', async () => {
    const year = Number(document.getElementById('reportYear').value);
    const month = Number(document.getElementById('reportMonth').value);
    const report = await api(`/api/management/payroll-report?year=${year}&month=${month}`);
    document.getElementById('payrollReport').textContent = JSON.stringify(report, null, 2);
    showResult('Employee Amount Due report loaded', { year, month });
});
wire('sendPayrollReport', async () => {
    const year = Number(document.getElementById('reportYear').value);
    const month = Number(document.getElementById('reportMonth').value);
    const force = document.getElementById('forcePayrollReport').checked;
    const response = await api(`/api/management/payroll-report/send?year=${year}&month=${month}&force=${force}`, { method: 'POST', body: '{}' });
    showResult('Employee Amount Due report sent', response);
});
wire('loadReportSettings', () => loadReportSettings(true));
wire('saveReportSettings', async () => {
    const weeklySummaryEnabled = document.getElementById('managerWeeklyEnabled').checked;
    const emailAddress = document.getElementById('managerEmail').value.trim() || null;
    const weeklySummaryDay = Number(document.getElementById('managerWeeklyDay').value);
    const weeklySummaryHourLocal = Number(document.getElementById('managerWeeklyHour').value);
    const settings = await api('/api/management/report-settings', { method: 'PUT', body: JSON.stringify({ weeklySummaryEnabled, emailAddress, weeklySummaryDay, weeklySummaryHourLocal }) });
    setReportSettings(settings);
    showResult('Manager report settings saved', settings);
});
wire('sendTestEmail', async () => {
    const emailAddress = document.getElementById('managerEmail').value.trim();
    if (!emailAddress) throw new Error('Enter a manager email address first.');
    await api('/api/management/report-settings/test-email', { method: 'POST', body: JSON.stringify({ emailAddress }) });
    showResult('Test email queued', `A test message was queued for ${emailAddress}.`);
});
wire('loadAudit', async () => {
    const rows = await api(`/api/management/audit?${auditQuery()}`);
    renderAudit(rows);
    showResult('Audit Trail loaded', { records: rows.length });
});
wire('clearAuditFilters', async () => {
    ['auditActor','auditAction','auditCustomerId','auditFrom','auditTo'].forEach(id => document.getElementById(id).value = '');
    document.getElementById('auditSucceeded').value = '';
    document.getElementById('auditTake').value = '100';
    document.getElementById('auditTable').innerHTML = '<div class="hint">No audit records loaded.</div>';
});
wire('diagnosticsButton', async () => window.open('/engineering/diagnostics', '_blank', 'noopener'));
wire('logoutButton', async () => {
    const form = document.createElement('form');
    form.method = 'post';
    form.action = '/logout';
    document.body.appendChild(form);
    form.submit();
});
wire('clearResult', async () => { result.textContent = 'Ready.'; });

initialize();
