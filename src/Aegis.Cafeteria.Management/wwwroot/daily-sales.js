(() => {
    const panel = document.querySelector('[data-panel="email"] .card');
    if (!panel) return;

    const section = document.createElement('div');
    section.className = 'card';
    section.style.marginTop = '18px';
    section.innerHTML = `
        <div class="section-title">
          <div>
            <span class="section-icon">DS</span>
            <div>
              <h2>Daily Sales Report</h2>
              <p>Send a Daily Sales Report on demand to the configured Manager email address.</p>
            </div>
          </div>
        </div>
        <div class="form-grid">
          <label>Report date<input id="dailySalesReportDate" type="date" /></label>
          <div class="button-field"><button id="sendDailySalesReport">Send Daily Sales Report</button></div>
        </div>
        <p class="hint">The report always includes Report Status and Last Successful Sync. If source data is still pending, it is sent as Report Status: Data Incomplete.</p>`;
    panel.insertAdjacentElement('afterend', section);

    const dateInput = document.getElementById('dailySalesReportDate');
    const now = new Date();
    const localDate = new Date(now.getTime() - now.getTimezoneOffset() * 60000).toISOString().slice(0, 10);
    dateInput.value = localDate;
    dateInput.max = localDate;

    const button = document.getElementById('sendDailySalesReport');
    button.addEventListener('click', async () => {
        button.disabled = true;
        try {
            const date = dateInput.value;
            if (!date) throw new Error('Select a report date.');

            const configuredEmail = document.getElementById('managerEmail')?.value?.trim();
            if (!configuredEmail) {
                throw new Error('Configure and save the Manager email address before sending the Daily Sales Report.');
            }

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
