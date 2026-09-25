(() => {
    const tabs = document.querySelector('.tabs');
    const emailTab = document.querySelector('.tab[data-tab="email"]');
    const auditPanel = document.querySelector('.tab-panel[data-panel="audit"]');
    if (!tabs || !emailTab || !auditPanel) return;

    const menuButton = document.createElement('button');
    menuButton.className = 'tab';
    menuButton.dataset.tab = 'daily-sales';
    menuButton.textContent = 'Daily Sales Report';
    emailTab.insertAdjacentElement('afterend', menuButton);

    const panel = document.createElement('section');
    panel.className = 'tab-panel';
    panel.dataset.panel = 'daily-sales';
    panel.innerHTML = `
      <div class="card">
        <div class="section-title">
          <div>
            <span class="section-icon">DS</span>
            <div>
              <h2>Daily Sales Report</h2>
              <p>Send a Daily Sales Report on demand to the Manager email configured under Email & Reports.</p>
            </div>
          </div>
        </div>
        <div class="form-grid">
          <label>Report date<input id="dailySalesReportDate" type="date" /></label>
          <label>Configured recipient<input id="dailySalesRecipient" type="email" readonly /></label>
        </div>
        <div class="actions"><button id="sendDailySalesReport">Send Daily Sales Report</button></div>
        <p class="hint">The report always includes Report Status: Complete or Report Status: Data Incomplete, plus Last Successful Sync. Data Incomplete does not prevent the report from being sent.</p>
      </div>`;
    auditPanel.insertAdjacentElement('beforebegin', panel);

    menuButton.addEventListener('click', async () => {
        selectTab('daily-sales');
        try {
            const settings = await loadReportSettings(false);
            document.getElementById('dailySalesRecipient').value = settings.emailAddress ?? '';
        } catch (error) {
            showResult('Daily Sales Report settings error', error.message || String(error));
        }
    });

    const dateInput = document.getElementById('dailySalesReportDate');
    const now = new Date();
    const localDate = new Date(now.getTime() - now.getTimezoneOffset() * 60000).toISOString().slice(0, 10);
    dateInput.value = localDate;
    dateInput.max = localDate;

    const configuredManagerEmail = document.getElementById('managerEmail')?.value?.trim() ?? '';
    document.getElementById('dailySalesRecipient').value = configuredManagerEmail;

    const button = document.getElementById('sendDailySalesReport');
    button.addEventListener('click', async () => {
        button.disabled = true;
        try {
            const date = dateInput.value;
            if (!date) throw new Error('Select a report date.');

            const settings = await loadReportSettings(false);
            const configuredEmail = settings.emailAddress?.trim();
            if (!configuredEmail) {
                throw new Error('Configure and save the Manager email address under Email & Reports before sending the Daily Sales Report.');
            }
            document.getElementById('dailySalesRecipient').value = configuredEmail;

            const response = await api('/api/management/reports/daily-sales/send', {
                method: 'POST',
                body: JSON.stringify({ date })
            });

            const syncText = response.lastSuccessfulSync
                ? formatDateTime(response.lastSuccessfulSync)
                : 'Not yet available';

            showResult('Daily Sales Report queued', {
                date: response.date,
                recipient: response.recipient,
                reportStatus: response.reportStatus,
                lastSuccessfulSync: syncText,
                pendingSyncOperations: response.pendingSyncOperations,
                failedSyncOperations: response.failedSyncOperations
            });
        } catch (error) {
            showResult('Daily Sales Report error', error.message || String(error));
        } finally {
            button.disabled = false;
        }
    });
})();
