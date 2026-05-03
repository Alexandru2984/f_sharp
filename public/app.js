const escapeHTML = str => str ? str.replace(/[&<>'"]/g, tag => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;'
}[tag])) : str;

const app = {
    state: {
        stats: null,
        anomalies: [],
        expenses: [],
        categories: [],
        trends: [],
        budgets: []
    },

    async init() {
        try {
            await this.fetchAPI('/api/me');
            this.showPage('dashboard');
        } catch (e) {
            this.showPage('login');
        }
    },

    async fetchAPI(url, options = {}) {
        try {
            const res = await fetch(url, options);
            if (res.status === 401 && url !== '/api/login') {
                this.showPage('login');
                throw new Error("Unauthorized");
            }
            if (!res.ok) throw new Error(await res.text());
            return await res.json();
        } catch (e) {
            console.error(e);
            if (e.message !== "Unauthorized") {
                alert(`Error: ${e.message}`);
            }
            throw e;
        }
    },

    async loadDashboardData() {
        this.state.stats = await this.fetchAPI('/api/stats');
        this.state.anomalies = await this.fetchAPI('/api/anomalies');
        this.state.expenses = await this.fetchAPI('/api/expenses');
        this.state.categories = await this.fetchAPI('/api/categories');
        this.state.trends = await this.fetchAPI('/api/trends');
        this.state.budgets = await this.fetchAPI('/api/budgets');
    },

    async runAnomalyEngine() {
        await this.fetchAPI('/api/anomalies/run', { method: 'POST' });
        await this.showPage('dashboard');
    },

    async resolveAnomaly(id) {
        await this.fetchAPI(`/api/anomalies/${id}/resolve`, { method: 'PATCH' });
        await this.showPage('dashboard');
    },

    async showPage(page) {
        const content = document.getElementById('app-content');
        content.innerHTML = '<p>Loading...</p>';

        if (page === 'dashboard') {
            await this.loadDashboardData();
            this.renderDashboard(content);
            document.querySelector('.links').style.display = 'block';
        } else if (page === 'import') {
            this.renderImportPage(content);
            document.querySelector('.links').style.display = 'block';
        } else if (page === 'budgets') {
            await this.loadDashboardData();
            this.renderBudgetsPage(content);
            document.querySelector('.links').style.display = 'block';
        } else if (page === 'login') {
            this.renderLoginPage(content);
            document.querySelector('.links').style.display = 'none';
        }
    },

    renderDashboard(el) {
        const { stats, anomalies, expenses, categories, trends, budgets } = this.state;
        
        let html = `
            <div class="flex-between">
                <h2>Dashboard Overview</h2>
                <div style="display:flex; gap: 1rem;">
                    <button class="btn btn-secondary" style="background-color: var(--surface-color); color: var(--text-primary); border: 1px solid var(--border-color);" onclick="app.showPage('budgets')">Budgets</button>
                    <button class="btn" onclick="app.runAnomalyEngine()">Run Engine</button>
                </div>
            </div>
            
            <div class="grid-3">
                <div class="card"><h3>Total Expenses</h3><div class="value">${stats.totalExpenses.toFixed(2)}</div></div>
                <div class="card"><h3>Current Month</h3><div class="value">${stats.currentMonthSpending.toFixed(2)}</div></div>
                <div class="card"><h3>Avg Monthly</h3><div class="value">${stats.averageMonthlySpending.toFixed(2)}</div></div>
                <div class="card"><h3>Anomalies</h3><div class="value">${stats.anomalyCount}</div></div>
                <div class="card"><h3>Highest Risk Category</h3><div class="value">${stats.highestRiskCategory}</div></div>
            </div>

            ${budgets.length > 0 ? `
            <h3>Budget Status (Current Month)</h3>
            <div class="grid-3" style="margin-bottom: 2rem;">
                ${budgets.map(b => `
                    <div class="card">
                        <div class="flex-between">
                            <h3 style="margin-bottom:0">${b.category}</h3>
                            <span style="font-size:0.8rem; color:var(--text-secondary)">${b.spent.toFixed(0)} / ${b.limit.toFixed(0)}</span>
                        </div>
                        <div class="progress-container">
                            <div class="progress-bar ${b.percentage > 100 ? 'over' : ''}" style="width: ${Math.min(100, b.percentage)}%"></div>
                        </div>
                    </div>
                `).join('')}
            </div>
            ` : ''}
            
            <div class="grid-3" style="grid-template-columns: 2fr 1fr;">
                <div class="chart-container"><canvas id="trendChart"></canvas></div>
                <div class="chart-container"><canvas id="catChart"></canvas></div>
            </div>

            <h3>Recent Anomalies</h3>
            <div class="table-container">
                <table>
                    <thead><tr><th>Date Detected</th><th>Score</th><th>Severity</th><th>Reason</th><th>Recommendation</th><th>Action</th></tr></thead>
                    <tbody>
                        ${anomalies.map(a => `
                            <tr>
                                <td>${new Date(a.detectedAt).toLocaleString()}</td>
                                <td>${a.score}</td>
                                <td><span class="badge ${a.severity.toLowerCase()}">${escapeHTML(a.severity)}</span></td>
                                <td>${escapeHTML(a.reason)}</td>
                                <td>${escapeHTML(a.recommendation)}</td>
                                <td><button class="btn btn-small" onclick="app.resolveAnomaly(${a.id})">Dismiss</button></td>
                            </tr>
                        `).join('')}
                        ${anomalies.length === 0 ? '<tr><td colspan="6">No anomalies detected.</td></tr>' : ''}
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
                                <td>${escapeHTML(e.merchant)}</td>
                                <td>${escapeHTML(e.category)}</td>
                                <td>${e.amount.toFixed(2)}</td>
                                <td>${escapeHTML(e.currency)}</td>
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
            amount: parseFloat(document.getElementById('m_amount').value),
            currency: document.getElementById('m_currency').value,
            category: document.getElementById('m_category').value,
            merchant: document.getElementById('m_merchant').value,
            description: document.getElementById('m_desc').value,
            date: document.getElementById('m_date').value
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
    },

    renderBudgetsPage(el) {
        const { budgets, categories } = this.state;
        el.innerHTML = `
            <h2>Manage Category Budgets</h2>
            <div class="card" style="margin-top:2rem; max-width: 600px;">
                <form onsubmit="app.saveBudget(event)">
                    <div class="form-group">
                        <label>Category</label>
                        <select id="b_category" style="width:100%; padding:0.75rem; background:#121212; color:white; border:1px solid #333;" required>
                            <option value="">Select a category</option>
                            ${categories.map(c => `<option value="${c.category}">${c.category}</option>`).join('')}
                        </select>
                    </div>
                    <div class="form-group">
                        <label>Monthly Limit</label>
                        <input type="number" step="0.01" id="b_limit" required>
                    </div>
                    <button type="submit" class="btn">Save Budget</button>
                    <button type="button" class="btn" style="background:transparent; color:white; border:1px solid #333; margin-left:1rem;" onclick="app.showPage('dashboard')">Back</button>
                </form>
            </div>

            <h3 style="margin-top:2rem;">Active Budgets</h3>
            <div class="table-container" style="margin-top:1rem;">
                <table>
                    <thead><tr><th>Category</th><th>Limit</th></tr></thead>
                    <tbody>
                        ${budgets.map(b => `
                            <tr>
                                <td>${b.category}</td>
                                <td>${b.limit.toFixed(2)}</td>
                            </tr>
                        `).join('')}
                        ${budgets.length === 0 ? '<tr><td colspan="2">No budgets set.</td></tr>' : ''}
                    </tbody>
                </table>
            </div>
        `;
    },

    async saveBudget(e) {
        e.preventDefault();
        const data = {
            category: document.getElementById('b_category').value,
            limitAmount: parseFloat(document.getElementById('b_limit').value)
        };
        await this.fetchAPI('/api/budgets', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(data)
        });
        alert('Budget saved!');
        this.showPage('budgets');
    },

    renderLoginPage(el) {
        const isRegistering = this.state.isRegistering || false;
        const title = isRegistering ? "Create Account" : "Sign In";
        const action = isRegistering ? "Register" : "Login";
        const toggleText = isRegistering ? "Already have an account? Sign In" : "Need an account? Register";
        const onSubmit = isRegistering ? "app.submitRegister(event)" : "app.submitLogin(event)";

        el.innerHTML = `
            <div style="display:flex; justify-content:center; margin-top: 4rem;">
                <div class="card" style="width: 100%; max-width: 400px; padding: 2rem;">
                    <h2 style="text-align:center; margin-bottom:1.5rem; color:var(--primary-color)">${title}</h2>
                    <form onsubmit="${onSubmit}">
                        <div class="form-group">
                            <label>Username</label>
                            <input type="text" id="l_username" required>
                        </div>
                        <div class="form-group">
                            <label>Password</label>
                            <input type="password" id="l_password" required>
                        </div>
                        <button type="submit" class="btn" style="width:100%; margin-top:1rem;">${action}</button>
                        <div style="text-align:center; margin-top:1rem;">
                            <a href="#" onclick="app.toggleRegister()" style="color:var(--text-secondary); font-size:0.875rem;">${toggleText}</a>
                        </div>
                    </form>
                </div>
            </div>
        `;
    },

    toggleRegister() {
        this.state.isRegistering = !this.state.isRegistering;
        this.showPage('login');
    },

    async submitRegister(e) {
        e.preventDefault();
        const data = {
            username: document.getElementById('l_username').value,
            password: document.getElementById('l_password').value
        };
        try {
            const r = await fetch('/api/register', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data)
            });
            if(!r.ok) {
                const txt = await r.text();
                throw new Error(txt);
            }
            this.state.isRegistering = false;
            await this.init();
        } catch (err) {
            alert('Registration failed: ' + err.message);
        }
    },

    async submitLogin(e) {
        e.preventDefault();
        const data = {
            username: document.getElementById('l_username').value,
            password: document.getElementById('l_password').value
        };
        try {
            await fetch('/api/login', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data)
            }).then(async r => {
                if(!r.ok) throw new Error(await r.text());
            });
            await this.init();
        } catch (err) {
            alert('Login failed. Please check credentials.');
        }
    },

    async logout() {
        await fetch('/api/logout', { method: 'POST' });
        this.showPage('login');
    }
};

window.onload = () => app.init();
