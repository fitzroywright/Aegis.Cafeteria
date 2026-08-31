let servicesBaseUrl = 'http://localhost:5088';
let selectedCustomerId = null;
let selectedCustomerName = null;

const result = document.getElementById('result');
const serviceStatus = document.getElementById('serviceStatus');
const selectedEmployee = document.getElementById('selectedEmployee');

function showResult(title, value) {
    const body = typeof value === 'string' ? value : JSON.stringify(value, null, 2);
    result.textContent = `${title}\n\n${body}`;
}

async function api(path, options = {}) {
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

    if (!response.ok) {
        const message = typeof value === 'string' ? value : (value.error || value.detail || JSON.stringify(value));
        throw new Error(`${response.status} ${response.statusText}: ${message}`);
    }

    return value;
}

function requireEmployee() {
    if (!selectedCustomerId) {
        throw new Error('Select an employee first.');
    }
    return selectedCustomerId;
}

function wire(id, handler) {
    document.getElementById(id).addEventListener('click', async () => {
        try {
            await handler();
        } catch (error) {
            showResult('Error', error.message || String(error));
        }
    });
}

async function searchEmployees() {
    const search = document.getElementById('employeeSearch').value.trim();
    if (search.length < 2) {
        throw new Error('Enter at least two characters.');
    }

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
        const info = document.createElement('div');
        const identity = [employee.code, employee.trn].filter(Boolean).join(' • ');
        info.innerHTML = `<strong>${employee.name}</strong><small>${identity || `Employee ${employee.customerId}`}</small>`;
        const select = document.createElement('button');
        select.textContent = 'Select';
        select.addEventListener('click', () => {
            selectedCustomerId = employee.customerId;
            selectedCustomerName = employee.name;
            selectedEmployee.textContent = `${employee.name} (#${employee.customerId})`;
            showResult('Employee selected', employee);
        });
        row.append(info, select);
        host.appendChild(row);
    });
}

async function initialize() {
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
    } catch (error) {
        serviceStatus.textContent = 'Services Unavailable';
        serviceStatus.style.background = '#a93434';
        showResult('Services check failed', error.message);
    }
}

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
    const response = await api(`/api/management/customers/${customerId}/credit-policy`, {
        method: 'PUT',
        body: JSON.stringify({ additionalCreditLimit })
    });
    showResult('Additional Credit Limit saved', response);
});
wire('approveAdditionalCredit', async () => {
    const customerId = requireEmployee();
    const amount = Number(document.getElementById('additionalCreditAmount').value);
    const reason = document.getElementById('additionalCreditReason').value.trim() || null;
    const response = await api('/api/management/additional-credit', {
        method: 'POST',
        body: JSON.stringify({ customerId, amount, reason, operationId: crypto.randomUUID() })
    });
    showResult(`Additional Credit approved for ${selectedCustomerName}`, response);
});
wire('processStipend', async () => {
    const customerId = requireEmployee();
    const cashAmount = Number(document.getElementById('stipendCash').value);
    const cardReductionAmount = Number(document.getElementById('stipendReduction').value);
    const paymentMode = Number(document.getElementById('stipendMode').value);
    const expenseReportReference = document.getElementById('stipendReference').value.trim() || null;
    const notes = document.getElementById('stipendNotes').value.trim() || null;
    const response = await api('/api/management/stipends', {
        method: 'POST',
        body: JSON.stringify({ customerId, cashAmount, cardReductionAmount, paymentMode, expenseReportReference, notes, operationId: crypto.randomUUID() })
    });
    showResult(`Stipend processed for ${selectedCustomerName}`, response);
});
wire('loadFunding', async () => {
    const policy = await api('/api/management/funding-policy');
    document.getElementById('dailyCreditRate').value = policy.dailyCreditLimitRate ?? '';
    document.getElementById('dailySubsidyRate').value = policy.dailyCompanySubsidyRate ?? '';
    showResult('Funding Policy', policy);
});
wire('saveFunding', async () => {
    const dailyCreditLimitRate = Number(document.getElementById('dailyCreditRate').value);
    const dailyCompanySubsidyRate = Number(document.getElementById('dailySubsidyRate').value);
    const response = await api('/api/management/funding-policy', {
        method: 'PUT',
        body: JSON.stringify({ dailyCreditLimitRate, dailyCompanySubsidyRate })
    });
    showResult('Funding Policy saved', response);
});
wire('runMonthOpening', async () => {
    const year = Number(document.getElementById('monthYear').value);
    const month = Number(document.getElementById('monthNumber').value);
    const response = await api(`/api/month-opening/run?year=${year}&month=${month}`, { method: 'POST', body: '{}' });
    showResult('Month Opening completed', response);
});
wire('recalculateMonth', async () => {
    const year = Number(document.getElementById('monthYear').value);
    const month = Number(document.getElementById('monthNumber').value);
    const response = await api(`/api/month-opening/recalculate?year=${year}&month=${month}`, { method: 'POST', body: '{}' });
    showResult('Month recalculation completed', response);
});
wire('loadPayrollReport', async () => {
    const year = Number(document.getElementById('reportYear').value);
    const month = Number(document.getElementById('reportMonth').value);
    const report = await api(`/api/management/payroll-report?year=${year}&month=${month}`);
    document.getElementById('payrollReport').textContent = JSON.stringify(report, null, 2);
    showResult('Employee Amount Due report loaded', { year, month });
});
wire('resetDevelopment', async () => {
    if (!confirm('Reset Development cafeteria financial/service data?')) return;
    const response = await api('/api/development/reset', { method: 'POST', body: '{}' });
    selectedCustomerId = null;
    selectedCustomerName = null;
    selectedEmployee.textContent = 'No employee selected';
    document.getElementById('employeeResults').innerHTML = '';
    showResult('Development data reset', response);
});
wire('diagnosticsButton', async () => {
    window.open(`${servicesBaseUrl}/engineering/diagnostics`, '_blank', 'noopener');
});
wire('clearResult', async () => {
    result.textContent = 'Ready.';
});

initialize();
