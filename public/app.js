const app = {
    state: {
        stats: null,
        anomalies: [],
        expenses: [],
        categories: [],
        trends: []
    },

    async init() {
        this.showPage('dashboard');
    },

    async fetchAPI(url, options = {}) {
        try {
            const res = await fetch(url, options);
            if (!res.ok) throw new Error(await res.text());
            return await res.json();
        } catch (e) {
            console.error(e);
            alert(`Error: ${e.message}`);
            throw e;
        }
    },

    async loadDashboardData() {
        this.state.stats = await this.fetchAPI('/api/stats');
        this.state.anomalies = await this.fetchAPI('/api/anomalies');
        this.state.expenses = await this.fetchAPI('/api/expenses');
        this.state.categories = await this.fetchAPI('/api/categories');
        this.state.trends = await this.fetchAPI('/api/trends');
    },

    async runAnomalyEngine() {
        await this.fetchAPI('/api/anomalies/run', { method: 'POST' });
        await this.showPage('dashboard');
    },

    async showPage(page) {
        const content = document.getElementById('app-content');
        content.innerHTML = '<p>Loading...</p>';

        if (page === 'dashboard') {
            await this.loadDashboardData();
            this.renderDashboard(content);
        } else if (page === 'import') {
            this.renderImportPage(content);
        }
    },

    renderDashboard(el) {
        const { stats, anomalies, expenses, categories, trends } = this.state;
        
        let html = `
            <div class="flex-between">
                <h2>Dashboard Overview</h2>
                <button class="btn" onclick="app.runAnomalyEngine()">Run Detection Engine</button>
            </div>
            
            <div class="grid-3">
                <div class="card"><h3>Total Expenses</h3><div class="value">${stats.totalExpenses.toFixed(2)}</div></div>
                <div class="card"><h3>Current Month</h3><div class="value">${stats.currentMonthSpending.toFixed(2)}</div></div>
                <div class="card"><h3>Avg Monthly</h3><div class="value">${stats.averageMonthlySpending.toFixed(2)}</div></div>
                <div class="card"><h3>Anomalies</h3><div class="value">${stats.anomalyCount}</div></div>
                <div class="card"><h3>Highest Risk Category</h3><div class="value">${stats.highestRiskCategory}</div></div>
            </div>
            
            <div class="grid-3" style="grid-template-columns: 2fr 1fr;">
                <div class="chart-container"><canvas id="trendChart"></canvas></div>
                <div class="chart-container"><canvas id="catChart"></canvas></div>
            </div>

            <h3>Recent Anomalies</h3>
            <div class="table-container">
                <table>
                    <thead><tr><th>Date Detected</th><th>Score</th><th>Severity</th><th>Reason</th><th>Recommendation</th></tr></thead>
                    <tbody>
                        ${anomalies.map(a => `
                            <tr>
                                <td>${new Date(a.detectedAt).toLocaleString()}</td>
                                <td>${a.score}</td>
                                <td><span class="badge ${a.severity.toLowerCase()}">${a.severity}</span></td>
                                <td>${a.reason}</td>
                                <td>${a.recommendation}</td>
                            </tr>
                        `).join('')}
                        ${anomalies.length === 0 ? '<tr><td colspan="5">No anomalies detected.</td></tr>' : ''}
                    </tbody>
                </table>
            </div>

            <h3>Recent Expenses</h3>
            <div class="table-container">
                <table>
                    <thead><tr><th>Date</th><th>Merchant</th><th>Category</th><th>Amount</th><th>Currency</th></tr></thead>
                    <tbody>
                        ${expenses.slice(0, 10).map(e => `
                            <tr>
                                <td>${new Date(e.date).toLocaleDateString()}</td>
                                <td>${e.merchant}</td>
                                <td>${e.category}</td>
                                <td>${e.amount.toFixed(2)}</td>
                                <td>${e.currency}</td>
                            </tr>
                        `).join('')}
                    </tbody>
                </table>
            </div>
        `;
        el.innerHTML = html;

        setTimeout(() => {
            if (trends.length > 0) {
                new Chart(document.getElementById('trendChart'), {
                    type: 'line',
                    data: {
                        labels: trends.map(t => t.month),
                        datasets: [{
                            label: 'Monthly Spending',
                            data: trends.map(t => t.total),
                            borderColor: '#bb86fc',
                            tension: 0.1,
                            fill: false
                        }]
                    },
                    options: { responsive: true, plugins: { title: { display: true, text: 'Spending Trends', color: '#fff' }, legend: { labels: { color: '#fff'} } }, scales: { y: { ticks: { color: '#fff' } }, x: { ticks: { color: '#fff' } } } }
                });
            }

            if (categories.length > 0) {
                new Chart(document.getElementById('catChart'), {
                    type: 'doughnut',
                    data: {
                        labels: categories.map(c => c.category),
                        datasets: [{
                            data: categories.map(c => c.total),
                            backgroundColor: ['#bb86fc', '#03dac6', '#cf6679', '#f2c94c', '#4a148c', '#3700b3']
                        }]
                    },
                    options: { responsive: true, plugins: { title: { display: true, text: 'Expenses by Category', color: '#fff' }, legend: { labels: { color: '#fff'} } } }
                });
            }
        }, 100);
    },

    renderImportPage(el) {
        el.innerHTML = `
            <h2>Add Data</h2>
            <div style="display:flex; gap: 2rem; margin-top:2rem;">
                
                <div class="card" style="flex:1;">
                    <h3 style="margin-bottom:1rem;">Manual Entry</h3>
                    <form id="manualForm" onsubmit="app.submitManual(event)">
                        <div class="form-group"><label>Amount</label><input type="number" step="0.01" id="m_amount" required></div>
                        <div class="form-group"><label>Currency</label><input type="text" id="m_currency" value="USD" required></div>
                        <div class="form-group"><label>Category</label><input type="text" id="m_category" required></div>
                        <div class="form-group"><label>Merchant</label><input type="text" id="m_merchant" required></div>
                        <div class="form-group"><label>Description</label><input type="text" id="m_desc"></div>
                        <div class="form-group"><label>Date</label><input type="datetime-local" id="m_date" required></div>
                        <button type="submit" class="btn">Save Expense</button>
                    </form>
                </div>

                <div class="card" style="flex:1;">
                    <h3 style="margin-bottom:1rem;">CSV Import</h3>
                    <p style="color:var(--text-secondary); margin-bottom:1rem;">CSV must have headers: Date, Amount, Currency, Category, Merchant, Description.</p>
                    <form id="csvForm" onsubmit="app.submitCsv(event)">
                        <div class="form-group">
                            <input type="file" id="csvFile" accept=".csv" required>
                        </div>
                        <button type="submit" class="btn">Upload CSV</button>
                    </form>
                    <div id="csvResult" style="margin-top: 1rem;"></div>
                </div>

            </div>
        `;
    },

    async submitManual(e) {
        e.preventDefault();
        const data = {
            Amount: parseFloat(document.getElementById('m_amount').value),
            Currency: document.getElementById('m_currency').value,
            Category: document.getElementById('m_category').value,
            Merchant: document.getElementById('m_merchant').value,
            Description: document.getElementById('m_desc').value,
            Date: document.getElementById('m_date').value
        };
        await this.fetchAPI('/api/expenses', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(data)
        });
        alert('Expense saved!');
        document.getElementById('manualForm').reset();
    },

    async submitCsv(e) {
        e.preventDefault();
        const file = document.getElementById('csvFile').files[0];
        const formData = new FormData();
        formData.append('file', file);
        
        try {
            const res = await this.fetchAPI('/api/expenses/import-csv', {
                method: 'POST',
                body: formData
            });
            document.getElementById('csvResult').innerHTML = `
                <p style="color:var(--success-color)">Imported: ${res.importedRows}</p>
                <p style="color:var(--error-color)">Skipped: ${res.skippedRows}</p>
            `;
        } catch (err) {
            document.getElementById('csvResult').innerText = "Import failed.";
        }
    }
};

window.onload = () => app.init();
