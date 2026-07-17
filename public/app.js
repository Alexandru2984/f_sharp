'use strict';

const escapeHTML = str => (str === null || str === undefined) ? '' : String(str).replace(/[&<>'"]/g, tag => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;'
}[tag]));

const fmtMoney = v => Number(v).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });

const RULE_LABELS = {
    CAT_OUTLIER: 'Category outlier',
    MERCHANT_SPIKE: 'Merchant spike',
    CAT_CHANGE: 'Category change',
    DUPLICATE: 'Duplicate charge',
    SUB_HIKE: 'Subscription hike',
    NIGHT: 'Night spending',
    LEGACY: 'Legacy'
};

const app = {
    state: {
        stats: null,
        anomalies: [],
        expensePage: { items: [], total: 0, page: 1, pageSize: 10 },
        categories: [],
        trends: [],
        budgets: [],
        recurring: [],
        report: null,
        reportMonth: new Date().toISOString().slice(0, 7),
        filters: { search: '', category: '', from: '', to: '' },
        isRegistering: false,
        username: null
    },
    charts: [],

    // ---- infrastructure ----

    toast(message, type = 'success') {
        const container = document.getElementById('toast-container');
        const el = document.createElement('div');
        el.className = `toast ${type}`;
        el.textContent = message;
        container.appendChild(el);
        setTimeout(() => el.classList.add('visible'), 10);
        setTimeout(() => { el.classList.remove('visible'); setTimeout(() => el.remove(), 300); }, 3500);
    },

    async fetchAPI(url, options = {}) {
        const res = await fetch(url, options);
        if (res.status === 401 && url !== '/api/login') {
            this.showPage('login');
            throw new Error('Unauthorized');
        }
        if (!res.ok) {
            let message = `Request failed (${res.status})`;
            try {
                const body = await res.json();
                if (body.error) message = body.error;
                else if (body.errors) message = body.errors.join(' ');
            } catch { /* non-JSON body */ }
            throw new Error(message);
        }
        return res.json();
    },

    async guard(fn) {
        try {
            return await fn();
        } catch (e) {
            if (e.message !== 'Unauthorized') this.toast(e.message, 'error');
            throw e;
        }
    },

    openModal(html) {
        document.getElementById('modal').innerHTML = html;
        document.getElementById('modal-backdrop').classList.remove('hidden');
    },

    closeModal() {
        document.getElementById('modal-backdrop').classList.add('hidden');
        document.getElementById('modal').innerHTML = '';
    },

    destroyCharts() {
        this.charts.forEach(c => c.destroy());
        this.charts = [];
    },

    setNavVisible(visible) {
        document.querySelector('.links').style.display = visible ? 'block' : 'none';
    },

    setActiveNav(page) {
        document.querySelectorAll('.links a[data-page]').forEach(a => {
            a.classList.toggle('active', a.dataset.page === page);
        });
    },

    // ---- init & routing ----

    async init() {
        try {
            const me = await this.fetchAPI('/api/me');
            this.state.username = me.username;
            this.showPage('dashboard');
        } catch {
            this.showPage('login');
        }
    },

    async showPage(page) {
        const content = document.getElementById('app-content');
        this.destroyCharts();
        this.closeModal();
        content.innerHTML = '<p>Loading...</p>';
        this.setNavVisible(page !== 'login');
        this.setActiveNav(page);

        try {
            if (page === 'dashboard') {
                await this.loadDashboardData();
                this.renderDashboard(content);
            } else if (page === 'expenses') {
                await this.loadExpenses();
                this.renderExpensesPage(content);
            } else if (page === 'import') {
                this.renderImportPage(content);
            } else if (page === 'budgets') {
                const [budgets, categories] = await Promise.all([
                    this.fetchAPI('/api/budgets'), this.fetchAPI('/api/categories')
                ]);
                this.state.budgets = budgets;
                this.state.categories = categories;
                this.renderBudgetsPage(content);
            } else if (page === 'reports') {
                await this.loadReport();
                this.renderReportsPage(content);
            } else if (page === 'account') {
                this.renderAccountPage(content);
            } else if (page === 'login') {
                this.renderLoginPage(content);
            }
        } catch (e) {
            if (e.message !== 'Unauthorized') {
                content.innerHTML = `<p style="color:var(--error-color)">Failed to load page: ${escapeHTML(e.message)}</p>`;
            }
        }
    },

    // ---- data loading ----

    async loadDashboardData() {
        const [stats, anomalies, expensePage, categories, trends, budgets] = await Promise.all([
            this.fetchAPI('/api/stats'),
            this.fetchAPI('/api/anomalies'),
            this.fetchAPI('/api/expenses?page=1&pageSize=10'),
            this.fetchAPI('/api/categories'),
            this.fetchAPI('/api/trends'),
            this.fetchAPI('/api/budgets')
        ]);
        Object.assign(this.state, { stats, anomalies, expensePage, categories, trends, budgets });
    },

    async loadExpenses() {
        const f = this.state.filters;
        const params = new URLSearchParams({ page: this.state.expensePage.page, pageSize: 20 });
        if (f.search) params.set('search', f.search);
        if (f.category) params.set('category', f.category);
        if (f.from) params.set('from', f.from);
        if (f.to) params.set('to', f.to);
        const [expensePage, categories] = await Promise.all([
            this.fetchAPI('/api/expenses?' + params.toString()),
            this.fetchAPI('/api/categories')
        ]);
        this.state.expensePage = expensePage;
        this.state.categories = categories;
    },

    async loadReport() {
        const [report, recurring] = await Promise.all([
            this.fetchAPI('/api/reports/monthly?month=' + encodeURIComponent(this.state.reportMonth)),
            this.fetchAPI('/api/recurring')
        ]);
        this.state.report = report;
        this.state.recurring = recurring;
    },

    // ---- actions ----

    async runAnomalyEngine() {
        await this.guard(async () => {
            const res = await this.fetchAPI('/api/anomalies/run', { method: 'POST' });
            this.toast(res.message);
            await this.showPage('dashboard');
        });
    },

    async resolveAnomaly(id) {
        await this.guard(async () => {
            await this.fetchAPI(`/api/anomalies/${id}/resolve`, { method: 'PATCH' });
            this.toast('Anomaly dismissed.');
            await this.showPage('dashboard');
        });
    },

    async deleteExpense(id) {
        if (!confirm('Delete this expense? Linked anomalies are removed too.')) return;
        await this.guard(async () => {
            await this.fetchAPI(`/api/expenses/${id}`, { method: 'DELETE' });
            this.toast('Expense deleted.');
            await this.showPage('expenses');
        });
    },

    async deleteBudget(category) {
        if (!confirm(`Remove the budget for "${category}"?`)) return;
        await this.guard(async () => {
            await this.fetchAPI(`/api/budgets/${encodeURIComponent(category)}`, { method: 'DELETE' });
            this.toast('Budget removed.');
            await this.showPage('budgets');
        });
    },

    openEditModal(id) {
        const e = this.state.expensePage.items.find(x => x.id === id);
        if (!e) return;
        this.openModal(`
            <h3>Edit Expense</h3>
            <form data-form="edit-expense" data-id="${e.id}">
                <div class="form-group"><label>Amount</label><input type="number" step="0.01" name="amount" value="${e.amount}" required></div>
                <div class="form-group"><label>Currency</label><input type="text" name="currency" value="${escapeHTML(e.currency)}" required></div>
                <div class="form-group"><label>Category</label><input type="text" name="category" value="${escapeHTML(e.category)}" required></div>
                <div class="form-group"><label>Merchant</label><input type="text" name="merchant" value="${escapeHTML(e.merchant)}" required></div>
                <div class="form-group"><label>Description</label><input type="text" name="description" value="${escapeHTML(e.description)}"></div>
                <div class="form-group"><label>Date</label><input type="datetime-local" name="date" value="${e.date.slice(0, 16)}" required></div>
                <div style="display:flex; gap:1rem;">
                    <button type="submit" class="btn">Save</button>
                    <button type="button" class="btn btn-outline" data-action="close-modal">Cancel</button>
                </div>
            </form>
        `);
    },

    async submitEditExpense(form) {
        const id = form.dataset.id;
        const data = {
            amount: parseFloat(form.amount.value),
            currency: form.currency.value,
            category: form.category.value,
            merchant: form.merchant.value,
            description: form.description.value,
            date: form.date.value
        };
        await this.guard(async () => {
            await this.fetchAPI(`/api/expenses/${id}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data)
            });
            this.toast('Expense updated.');
            this.closeModal();
            await this.showPage('expenses');
        });
    },

    async submitManual(form) {
        const data = {
            amount: parseFloat(form.amount.value),
            currency: form.currency.value,
            category: form.category.value,
            merchant: form.merchant.value,
            description: form.description.value,
            date: form.date.value
        };
        await this.guard(async () => {
            await this.fetchAPI('/api/expenses', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data)
            });
            this.toast('Expense saved.');
            form.reset();
        });
    },

    async submitCsv(form) {
        const file = form.querySelector('input[type=file]').files[0];
        if (!file) return;
        const formData = new FormData();
        formData.append('file', file);
        const resultEl = document.getElementById('csvResult');
        try {
            const res = await this.fetchAPI('/api/expenses/import-csv', { method: 'POST', body: formData });
            resultEl.innerHTML = `
                <p style="color:var(--success-color)">Imported: ${res.importedRows}</p>
                <p style="color:var(--error-color)">Skipped: ${res.skippedRows}</p>
                ${res.validationErrors.slice(0, 5).map(e => `<p style="color:var(--text-secondary); font-size:0.8rem;">${escapeHTML(e)}</p>`).join('')}
            `;
        } catch (err) {
            resultEl.textContent = 'Import failed: ' + err.message;
        }
    },

    async saveBudget(form) {
        const data = {
            userId: 0,
            category: form.category.value,
            limitAmount: parseFloat(form.limit.value)
        };
        await this.guard(async () => {
            await this.fetchAPI('/api/budgets', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data)
            });
            this.toast('Budget saved.');
            await this.showPage('budgets');
        });
    },

    async submitPasswordChange(form) {
        if (form.newPassword.value !== form.confirmPassword.value) {
            this.toast('New passwords do not match.', 'error');
            return;
        }
        await this.guard(async () => {
            await this.fetchAPI('/api/account/change-password', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ oldPassword: form.oldPassword.value, newPassword: form.newPassword.value })
            });
            this.toast('Password changed.');
            form.reset();
        });
    },

    async submitLogin(form) {
        try {
            const res = await fetch('/api/login', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ username: form.username.value, password: form.password.value })
            });
            if (!res.ok) throw new Error(res.status === 429 ? 'Too many attempts; wait a minute.' : 'Invalid credentials.');
            await this.init();
        } catch (err) {
            this.toast('Login failed. ' + err.message, 'error');
        }
    },

    async submitRegister(form) {
        try {
            const res = await fetch('/api/register', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ username: form.username.value, password: form.password.value })
            });
            if (!res.ok) {
                const body = await res.json().catch(() => ({}));
                throw new Error(body.error || (body.errors || []).join(' ') || 'Registration failed.');
            }
            this.state.isRegistering = false;
            await this.init();
        } catch (err) {
            this.toast(err.message, 'error');
        }
    },

    async logout() {
        await fetch('/api/logout', { method: 'POST' });
        this.state.username = null;
        this.showPage('login');
    },

    // ---- rendering ----

    renderDashboard(el) {
        const { stats, anomalies, expensePage, categories, trends, budgets } = this.state;

        el.innerHTML = `
            <div class="flex-between">
                <h2>Dashboard Overview</h2>
                <div style="display:flex; gap: 1rem;">
                    <button class="btn btn-outline" data-page="budgets">Budgets</button>
                    <button class="btn" data-action="run-engine">Run Engine</button>
                </div>
            </div>

            <div class="grid-3">
                <div class="card"><h3>Total Expenses</h3><div class="value">${fmtMoney(stats.totalExpenses)}</div></div>
                <div class="card"><h3>Current Month</h3><div class="value">${fmtMoney(stats.currentMonthSpending)}</div></div>
                <div class="card"><h3>Avg Monthly</h3><div class="value">${fmtMoney(stats.averageMonthlySpending)}</div></div>
                <div class="card"><h3>Anomalies</h3><div class="value">${stats.anomalyCount}</div></div>
                <div class="card"><h3>Highest Risk Category</h3><div class="value">${escapeHTML(stats.highestRiskCategory)}</div></div>
            </div>

            ${budgets.length > 0 ? `
            <h3>Budget Status (Current Month)</h3>
            <div class="grid-3" style="margin-bottom: 2rem;">
                ${budgets.map(b => `
                    <div class="card">
                        <div class="flex-between">
                            <h3 style="margin-bottom:0">${escapeHTML(b.category)}</h3>
                            <span style="font-size:0.8rem; color:var(--text-secondary)">${fmtMoney(b.spent)} / ${fmtMoney(b.limit)}</span>
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

            <h3>Open Anomalies</h3>
            <div class="table-container">
                <table>
                    <thead><tr><th>Detected</th><th>Rule</th><th>Score</th><th>Severity</th><th>Reason</th><th>Recommendation</th><th></th></tr></thead>
                    <tbody>
                        ${anomalies.map(a => `
                            <tr>
                                <td>${new Date(a.detectedAt).toLocaleString()}</td>
                                <td><span class="badge rule">${escapeHTML(RULE_LABELS[a.ruleCode] || a.ruleCode)}</span></td>
                                <td>${a.score}</td>
                                <td><span class="badge ${escapeHTML(a.severity.toLowerCase())}">${escapeHTML(a.severity)}</span></td>
                                <td>${escapeHTML(a.reason)}</td>
                                <td>${escapeHTML(a.recommendation)}</td>
                                <td><button class="btn btn-small" data-action="resolve-anomaly" data-id="${a.id}">Dismiss</button></td>
                            </tr>
                        `).join('')}
                        ${anomalies.length === 0 ? '<tr><td colspan="7">No anomalies detected.</td></tr>' : ''}
                    </tbody>
                </table>
            </div>

            <div class="flex-between">
                <h3>Recent Expenses</h3>
                <a href="#" data-page="expenses">View all →</a>
            </div>
            <div class="table-container">
                <table>
                    <thead><tr><th>Date</th><th>Merchant</th><th>Category</th><th>Amount</th><th>Currency</th></tr></thead>
                    <tbody>
                        ${expensePage.items.map(e => `
                            <tr>
                                <td>${new Date(e.date).toLocaleDateString()}</td>
                                <td>${escapeHTML(e.merchant)}</td>
                                <td>${escapeHTML(e.category)}</td>
                                <td>${fmtMoney(e.amount)}</td>
                                <td>${escapeHTML(e.currency)}</td>
                            </tr>
                        `).join('')}
                        ${expensePage.items.length === 0 ? '<tr><td colspan="5">No expenses yet. <a href="#" data-page="import">Add some data</a>.</td></tr>' : ''}
                    </tbody>
                </table>
            </div>
        `;

        this.renderCharts(trends, categories);
    },

    renderCharts(trends, categories) {
        if (trends.length > 0) {
            this.charts.push(new Chart(document.getElementById('trendChart'), {
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
                options: { responsive: true, plugins: { title: { display: true, text: 'Spending Trends', color: '#fff' }, legend: { labels: { color: '#fff' } } }, scales: { y: { ticks: { color: '#fff' } }, x: { ticks: { color: '#fff' } } } }
            }));
        }
        if (categories.length > 0) {
            this.charts.push(new Chart(document.getElementById('catChart'), {
                type: 'doughnut',
                data: {
                    labels: categories.map(c => c.category),
                    datasets: [{
                        data: categories.map(c => c.total),
                        backgroundColor: ['#bb86fc', '#03dac6', '#cf6679', '#f2c94c', '#4a148c', '#3700b3']
                    }]
                },
                options: { responsive: true, plugins: { title: { display: true, text: 'Expenses by Category', color: '#fff' }, legend: { labels: { color: '#fff' } } } }
            }));
        }
    },

    renderExpensesPage(el) {
        const { expensePage, categories, filters } = this.state;
        const totalPages = Math.max(1, Math.ceil(expensePage.total / expensePage.pageSize));

        el.innerHTML = `
            <div class="flex-between">
                <h2>Expenses <span style="color:var(--text-secondary); font-size:1rem;">(${expensePage.total} total)</span></h2>
                <a class="btn btn-outline" href="/api/expenses/export">Export CSV</a>
            </div>

            <form class="card filter-bar" data-form="expense-filters">
                <input type="text" name="search" placeholder="Search merchant or description" value="${escapeHTML(filters.search)}">
                <select name="category">
                    <option value="">All categories</option>
                    ${categories.map(c => `<option value="${escapeHTML(c.category)}" ${c.category === filters.category ? 'selected' : ''}>${escapeHTML(c.category)}</option>`).join('')}
                </select>
                <input type="date" name="from" value="${escapeHTML(filters.from)}">
                <input type="date" name="to" value="${escapeHTML(filters.to)}">
                <button type="submit" class="btn">Filter</button>
                <button type="button" class="btn btn-outline" data-action="clear-filters">Clear</button>
            </form>

            <div class="table-container">
                <table>
                    <thead><tr><th>Date</th><th>Merchant</th><th>Category</th><th>Description</th><th>Amount</th><th>Currency</th><th></th></tr></thead>
                    <tbody>
                        ${expensePage.items.map(e => `
                            <tr>
                                <td>${new Date(e.date).toLocaleString()}</td>
                                <td>${escapeHTML(e.merchant)}</td>
                                <td>${escapeHTML(e.category)}</td>
                                <td>${escapeHTML(e.description)}</td>
                                <td>${fmtMoney(e.amount)}</td>
                                <td>${escapeHTML(e.currency)}</td>
                                <td style="white-space:nowrap;">
                                    <button class="btn btn-small btn-outline" data-action="edit-expense" data-id="${e.id}">Edit</button>
                                    <button class="btn btn-small btn-danger" data-action="delete-expense" data-id="${e.id}">Delete</button>
                                </td>
                            </tr>
                        `).join('')}
                        ${expensePage.items.length === 0 ? '<tr><td colspan="7">No expenses match.</td></tr>' : ''}
                    </tbody>
                </table>
            </div>

            <div class="pagination">
                <button class="btn btn-small btn-outline" data-action="prev-page" ${expensePage.page <= 1 ? 'disabled' : ''}>← Prev</button>
                <span>Page ${expensePage.page} / ${totalPages}</span>
                <button class="btn btn-small btn-outline" data-action="next-page" ${expensePage.page >= totalPages ? 'disabled' : ''}>Next →</button>
            </div>
        `;
    },

    renderImportPage(el) {
        el.innerHTML = `
            <h2>Add Data</h2>
            <div class="two-col">
                <div class="card">
                    <h3 style="margin-bottom:1rem;">Manual Entry</h3>
                    <form data-form="manual-expense">
                        <div class="form-group"><label>Amount</label><input type="number" step="0.01" name="amount" required></div>
                        <div class="form-group"><label>Currency</label><input type="text" name="currency" value="USD" required></div>
                        <div class="form-group"><label>Category</label><input type="text" name="category" required></div>
                        <div class="form-group"><label>Merchant</label><input type="text" name="merchant" required></div>
                        <div class="form-group"><label>Description</label><input type="text" name="description"></div>
                        <div class="form-group"><label>Date</label><input type="datetime-local" name="date" required></div>
                        <button type="submit" class="btn">Save Expense</button>
                    </form>
                </div>
                <div class="card">
                    <h3 style="margin-bottom:1rem;">CSV Import</h3>
                    <p style="color:var(--text-secondary); margin-bottom:1rem;">CSV must have headers: Date, Amount, Currency, Category, Merchant, Description. Max 5 MB / 10,000 rows.</p>
                    <form data-form="csv-import">
                        <div class="form-group">
                            <input type="file" name="file" accept=".csv" required>
                        </div>
                        <button type="submit" class="btn">Upload CSV</button>
                    </form>
                    <div id="csvResult" style="margin-top: 1rem;"></div>
                </div>
            </div>
        `;
    },

    renderBudgetsPage(el) {
        const { budgets, categories } = this.state;
        el.innerHTML = `
            <h2>Manage Category Budgets</h2>
            <div class="card" style="margin-top:2rem; max-width: 600px;">
                <form data-form="budget">
                    <div class="form-group">
                        <label>Category</label>
                        <select name="category" required>
                            <option value="">Select a category</option>
                            ${categories.map(c => `<option value="${escapeHTML(c.category)}">${escapeHTML(c.category)}</option>`).join('')}
                        </select>
                    </div>
                    <div class="form-group">
                        <label>Monthly Limit</label>
                        <input type="number" step="0.01" name="limit" required>
                    </div>
                    <button type="submit" class="btn">Save Budget</button>
                    <button type="button" class="btn btn-outline" data-page="dashboard" style="margin-left:1rem;">Back</button>
                </form>
            </div>

            <h3 style="margin-top:2rem;">Active Budgets</h3>
            <div class="table-container" style="margin-top:1rem;">
                <table>
                    <thead><tr><th>Category</th><th>Monthly Limit</th><th>Spent</th><th></th></tr></thead>
                    <tbody>
                        ${budgets.map(b => `
                            <tr>
                                <td>${escapeHTML(b.category)}</td>
                                <td>${fmtMoney(b.limit)}</td>
                                <td>${fmtMoney(b.spent)} (${b.percentage}%)</td>
                                <td><button class="btn btn-small btn-danger" data-action="delete-budget" data-category="${escapeHTML(b.category)}">Remove</button></td>
                            </tr>
                        `).join('')}
                        ${budgets.length === 0 ? '<tr><td colspan="4">No budgets set.</td></tr>' : ''}
                    </tbody>
                </table>
            </div>
        `;
    },

    renderReportsPage(el) {
        const { report, recurring, reportMonth } = this.state;
        const changeColor = report.changePercent > 0 ? 'var(--error-color)' : 'var(--success-color)';
        const changeSign = report.changePercent > 0 ? '+' : '';

        el.innerHTML = `
            <div class="flex-between">
                <h2>Monthly Report</h2>
                <form data-form="report-month" style="display:flex; gap:0.5rem; align-items:center;">
                    <input type="month" name="month" value="${escapeHTML(reportMonth)}">
                    <button type="submit" class="btn btn-small">Load</button>
                </form>
            </div>

            <div class="grid-3">
                <div class="card"><h3>Total (${escapeHTML(report.month)})</h3><div class="value">${fmtMoney(report.total)}</div></div>
                <div class="card"><h3>Transactions</h3><div class="value">${report.expenseCount}</div></div>
                <div class="card"><h3>vs Previous Month</h3><div class="value" style="color:${changeColor}">${report.previousMonthTotal > 0 ? changeSign + report.changePercent + '%' : 'n/a'}</div></div>
                <div class="card"><h3>Anomalies</h3><div class="value">${report.anomalyCount}</div></div>
            </div>

            <div class="two-col">
                <div>
                    <h3>Spending by Category</h3>
                    <div class="table-container">
                        <table>
                            <thead><tr><th>Category</th><th>Total</th><th>Share</th></tr></thead>
                            <tbody>
                                ${report.byCategory.map(c => `
                                    <tr>
                                        <td>${escapeHTML(c.category)}</td>
                                        <td>${fmtMoney(c.total)}</td>
                                        <td>
                                            <div style="display:flex; align-items:center; gap:0.5rem;">
                                                <div class="progress-container" style="margin:0; flex:1;"><div class="progress-bar" style="width:${Math.min(100, c.share)}%"></div></div>
                                                <span style="font-size:0.8rem;">${c.share}%</span>
                                            </div>
                                        </td>
                                    </tr>
                                `).join('')}
                                ${report.byCategory.length === 0 ? '<tr><td colspan="3">No expenses this month.</td></tr>' : ''}
                            </tbody>
                        </table>
                    </div>
                </div>
                <div>
                    <h3>Top Merchants</h3>
                    <div class="table-container">
                        <table>
                            <thead><tr><th>Merchant</th><th>Charges</th><th>Total</th></tr></thead>
                            <tbody>
                                ${report.topMerchants.map(m => `
                                    <tr><td>${escapeHTML(m.merchant)}</td><td>${m.count}</td><td>${fmtMoney(m.total)}</td></tr>
                                `).join('')}
                                ${report.topMerchants.length === 0 ? '<tr><td colspan="3">No merchants this month.</td></tr>' : ''}
                            </tbody>
                        </table>
                    </div>
                </div>
            </div>

            <h3>Detected Recurring Charges</h3>
            <p style="color:var(--text-secondary); margin-bottom:1rem;">Stable charges detected across 3+ months — your subscriptions, rent and utilities.</p>
            <div class="table-container">
                <table>
                    <thead><tr><th>Merchant</th><th>Category</th><th>Avg Amount</th><th>Charges</th><th>Months</th><th>Last Seen</th></tr></thead>
                    <tbody>
                        ${recurring.map(r => `
                            <tr>
                                <td>${escapeHTML(r.merchant)}</td>
                                <td>${escapeHTML(r.category)}</td>
                                <td>${fmtMoney(r.averageAmount)}</td>
                                <td>${r.occurrences}</td>
                                <td>${r.monthsActive}</td>
                                <td>${new Date(r.lastDate).toLocaleDateString()}</td>
                            </tr>
                        `).join('')}
                        ${recurring.length === 0 ? '<tr><td colspan="6">No recurring charges detected yet.</td></tr>' : ''}
                    </tbody>
                </table>
            </div>
        `;
    },

    renderAccountPage(el) {
        el.innerHTML = `
            <h2>Account</h2>
            <div class="card" style="margin-top:2rem; max-width:500px;">
                <h3 style="margin-bottom:1rem;">Signed in as</h3>
                <p style="font-size:1.25rem; font-weight:bold;">${escapeHTML(this.state.username)}</p>
            </div>
            <div class="card" style="margin-top:2rem; max-width:500px;">
                <h3 style="margin-bottom:1rem;">Change Password</h3>
                <form data-form="change-password">
                    <div class="form-group"><label>Current password</label><input type="password" name="oldPassword" autocomplete="current-password" required></div>
                    <div class="form-group"><label>New password (min 8 chars)</label><input type="password" name="newPassword" autocomplete="new-password" minlength="8" required></div>
                    <div class="form-group"><label>Confirm new password</label><input type="password" name="confirmPassword" autocomplete="new-password" minlength="8" required></div>
                    <button type="submit" class="btn">Change Password</button>
                </form>
            </div>
        `;
    },

    renderLoginPage(el) {
        const isRegistering = this.state.isRegistering;
        el.innerHTML = `
            <div style="display:flex; justify-content:center; margin-top: 4rem;">
                <div class="card" style="width: 100%; max-width: 400px; padding: 2rem;">
                    <h2 style="text-align:center; margin-bottom:1.5rem; color:var(--primary-color)">${isRegistering ? 'Create Account' : 'Sign In'}</h2>
                    <form data-form="${isRegistering ? 'register' : 'login'}">
                        <div class="form-group">
                            <label>Username</label>
                            <input type="text" name="username" autocomplete="username" required>
                        </div>
                        <div class="form-group">
                            <label>Password</label>
                            <input type="password" name="password" autocomplete="${isRegistering ? 'new-password' : 'current-password'}" required>
                        </div>
                        <button type="submit" class="btn" style="width:100%; margin-top:1rem;">${isRegistering ? 'Register' : 'Login'}</button>
                        <div style="text-align:center; margin-top:1rem;">
                            <a href="#" data-action="toggle-register" style="color:var(--text-secondary); font-size:0.875rem;">
                                ${isRegistering ? 'Already have an account? Sign In' : 'Need an account? Register'}
                            </a>
                        </div>
                    </form>
                </div>
            </div>
        `;
    }
};

// ---- event delegation ----

document.addEventListener('click', e => {
    const pageLink = e.target.closest('[data-page]');
    if (pageLink) {
        e.preventDefault();
        if (pageLink.dataset.page === 'expenses') app.state.expensePage.page = 1;
        app.showPage(pageLink.dataset.page);
        return;
    }
    const actionEl = e.target.closest('[data-action]');
    if (!actionEl) return;
    e.preventDefault();
    const id = parseInt(actionEl.dataset.id, 10);
    switch (actionEl.dataset.action) {
        case 'logout': app.logout(); break;
        case 'run-engine': app.runAnomalyEngine(); break;
        case 'resolve-anomaly': app.resolveAnomaly(id); break;
        case 'edit-expense': app.openEditModal(id); break;
        case 'delete-expense': app.deleteExpense(id); break;
        case 'delete-budget': app.deleteBudget(actionEl.dataset.category); break;
        case 'close-modal': app.closeModal(); break;
        case 'toggle-register':
            app.state.isRegistering = !app.state.isRegistering;
            app.showPage('login');
            break;
        case 'clear-filters':
            app.state.filters = { search: '', category: '', from: '', to: '' };
            app.state.expensePage.page = 1;
            app.showPage('expenses');
            break;
        case 'prev-page':
            app.state.expensePage.page = Math.max(1, app.state.expensePage.page - 1);
            app.showPage('expenses');
            break;
        case 'next-page':
            app.state.expensePage.page += 1;
            app.showPage('expenses');
            break;
    }
});

document.addEventListener('submit', e => {
    const form = e.target.closest('[data-form]');
    if (!form) return;
    e.preventDefault();
    switch (form.dataset.form) {
        case 'login': app.submitLogin(form); break;
        case 'register': app.submitRegister(form); break;
        case 'manual-expense': app.submitManual(form); break;
        case 'csv-import': app.submitCsv(form); break;
        case 'budget': app.saveBudget(form); break;
        case 'edit-expense': app.submitEditExpense(form); break;
        case 'change-password': app.submitPasswordChange(form); break;
        case 'expense-filters':
            app.state.filters = {
                search: form.search.value.trim(),
                category: form.category.value,
                from: form.from.value,
                to: form.to.value
            };
            app.state.expensePage.page = 1;
            app.showPage('expenses');
            break;
        case 'report-month':
            app.state.reportMonth = form.month.value || app.state.reportMonth;
            app.showPage('reports');
            break;
    }
});

document.getElementById('modal-backdrop').addEventListener('click', e => {
    if (e.target.id === 'modal-backdrop') app.closeModal();
});

window.addEventListener('load', () => app.init());
