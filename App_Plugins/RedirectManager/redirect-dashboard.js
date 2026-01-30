import { LitElement, html, css } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';

class RedirectManagerDashboard extends UmbLitElement {
    static properties = {
        redirects: { type: Array },
        loading: { type: Boolean },
        showModal: { type: Boolean },
        editingRedirect: { type: Object },
        formData: { type: Object },
        query: { type: String },
        statusFilter: { type: String },
        activeFilter: { type: String },
        regexFilter: { type: String },
        selectedIds: { type: Array },
        importInProgress: { type: Boolean }
    };

    static styles = css`
        :host {
            display: block;
            padding: 20px;
        }

        .header {
            display: flex;
            justify-content: space-between;
            align-items: center;
            margin-bottom: 20px;
        }

        h1 {
            margin: 0;
            font-size: 24px;
            color: #1b264f;
        }

        .btn {
            padding: 10px 20px;
            border: none;
            border-radius: 4px;
            cursor: pointer;
            font-size: 14px;
            font-weight: 500;
        }

        .btn-primary {
            background-color: #3544b1;
            color: white;
        }

        .btn-primary:hover {
            background-color: #2d3a9e;
        }

        .btn-danger {
            background-color: #d42054;
            color: white;
        }

        .btn-secondary {
            background-color: #6c757d;
            color: white;
        }

        .btn-sm {
            padding: 6px 12px;
            font-size: 12px;
        }

        table {
            width: 100%;
            border-collapse: collapse;
            background: white;
            border-radius: 8px;
            overflow: hidden;
            box-shadow: 0 1px 3px rgba(0,0,0,0.1);
        }

        th, td {
            padding: 12px 16px;
            text-align: left;
            border-bottom: 1px solid #e9e9e9;
        }

        th {
            background-color: #f5f5f5;
            font-weight: 600;
            color: #333;
        }

        tbody tr:hover {
            background-color: #f9f9f9;
        }

        .url-cell {
            font-family: monospace;
            font-size: 13px;
            max-width: 250px;
            overflow: hidden;
            text-overflow: ellipsis;
            white-space: nowrap;
        }

        .status-badge {
            display: inline-block;
            padding: 4px 8px;
            border-radius: 4px;
            font-weight: 600;
            font-size: 12px;
            color: white;
        }

        .status-301 { background-color: #2bc37b; }
        .status-302 { background-color: #f5c520; color: #333; }
        .status-404 { background-color: #d42054; }
        .status-410 { background-color: #6c757d; }

        .active-yes { color: #2bc37b; font-weight: 600; }
        .active-no { color: #d42054; font-weight: 600; }

        .actions {
            display: flex;
            gap: 8px;
        }

        .loading, .empty {
            padding: 40px;
            text-align: center;
            color: #666;
        }

        .modal-overlay {
            position: fixed;
            top: 0;
            left: 0;
            right: 0;
            bottom: 0;
            background: rgba(0,0,0,0.5);
            display: flex;
            align-items: center;
            justify-content: center;
            z-index: 1000;
        }

        .modal {
            background: white;
            border-radius: 8px;
            padding: 24px;
            width: 500px;
            max-width: 90%;
            box-shadow: 0 4px 20px rgba(0,0,0,0.2);
        }

        .modal h2 {
            margin: 0 0 20px 0;
            color: #1b264f;
        }

        .form-group {
            margin-bottom: 16px;
        }

        .form-group label {
            display: block;
            margin-bottom: 6px;
            font-weight: 500;
            color: #333;
        }

        .form-group input,
        .form-group select {
            width: 100%;
            padding: 10px;
            border: 1px solid #d8d7d9;
            border-radius: 4px;
            font-size: 14px;
            box-sizing: border-box;
        }

        .form-group small {
            display: block;
            margin-top: 4px;
            color: #666;
            font-size: 12px;
        }

        .checkbox-group {
            display: flex;
            align-items: center;
            gap: 8px;
        }

        .checkbox-group input {
            width: auto;
        }

        .modal-actions {
            display: flex;
            justify-content: flex-end;
            gap: 10px;
            margin-top: 20px;
        }
    `;

    constructor() {
        super();
        this.redirects = [];
        this.loading = true;
        this.showModal = false;
        this.editingRedirect = null;
        this.formData = this.getEmptyFormData();
        this.query = '';
        this.statusFilter = '';
        this.activeFilter = '';
        this.regexFilter = '';
        this.selectedIds = [];
        this.importInProgress = false;
    }

    connectedCallback() {
        super.connectedCallback();
        this.loadRedirects();
    }

    getEmptyFormData() {
        return {
            oldUrl: '',
            newUrl: '',
            statusCode: 301,
            isActive: true,
            isRegex: false
        };
    }

    buildQueryParams() {
        const params = new URLSearchParams();
        if (this.query && this.query.trim().length > 0) params.set('q', this.query.trim());
        if (this.statusFilter) params.set('statusCode', this.statusFilter);
        if (this.activeFilter) params.set('isActive', this.activeFilter);
        if (this.regexFilter) params.set('isRegex', this.regexFilter);
        const qs = params.toString();
        return qs.length > 0 ? `?${qs}` : '';
    }

    async loadRedirects() {
        this.loading = true;
        try {
            const response = await fetch(`/umbraco/api/redirectmanager/getall${this.buildQueryParams()}`);
            if (response.ok) {
                this.redirects = await response.json();
                this.selectedIds = [];
            }
        } catch (error) {
            console.error('Failed to load redirects:', error);
        }
        this.loading = false;
    }

    applyFilters() {
        this.loadRedirects();
    }

    clearFilters() {
        this.query = '';
        this.statusFilter = '';
        this.activeFilter = '';
        this.regexFilter = '';
        this.loadRedirects();
    }

    openAddModal() {
        this.editingRedirect = null;
        this.formData = this.getEmptyFormData();
        this.showModal = true;
    }

    openEditModal(redirect) {
        this.editingRedirect = redirect;
        this.formData = {
            oldUrl: redirect.oldUrl,
            newUrl: redirect.newUrl || '',
            statusCode: redirect.statusCode,
            isActive: redirect.isActive,
            isRegex: !!redirect.isRegex
        };
        this.showModal = true;
    }

    closeModal() {
        this.showModal = false;
        this.editingRedirect = null;
        this.formData = this.getEmptyFormData();
    }

    handleInputChange(e) {
        const { name, value, type, checked } = e.target;
        this.formData = {
            ...this.formData,
            [name]: type === 'checkbox' ? checked : (name === 'statusCode' ? parseInt(value) : value)
        };
    }

    toggleSelectAll(e) {
        const checked = e.target.checked;
        if (checked) {
            this.selectedIds = this.redirects.map(r => r.id);
        } else {
            this.selectedIds = [];
        }
    }

    toggleSelectId(id, checked) {
        if (checked) {
            if (!this.selectedIds.includes(id)) {
                this.selectedIds = [...this.selectedIds, id];
            }
        } else {
            this.selectedIds = this.selectedIds.filter(x => x !== id);
        }
    }

    get allSelected() {
        return this.redirects.length > 0 && this.selectedIds.length === this.redirects.length;
    }

    get anySelected() {
        return this.selectedIds.length > 0;
    }

    async bulkDeleteSelected() {
        if (!this.anySelected) return;
        if (!confirm(`Delete ${this.selectedIds.length} redirect(s)?`)) return;

        try {
            const response = await fetch('/umbraco/api/redirectmanager/bulk/delete', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ ids: this.selectedIds })
            });
            if (response.ok) {
                this.loadRedirects();
            } else {
                alert('Failed to delete selected redirects');
            }
        } catch (error) {
            console.error('Failed to bulk delete:', error);
            alert('Failed to delete selected redirects');
        }
    }

    async bulkSetActiveSelected(isActive) {
        if (!this.anySelected) return;

        try {
            const response = await fetch(isActive ? '/umbraco/api/redirectmanager/bulk/activate' : '/umbraco/api/redirectmanager/bulk/deactivate', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ ids: this.selectedIds })
            });
            if (response.ok) {
                this.loadRedirects();
            } else {
                alert('Failed to update selected redirects');
            }
        } catch (error) {
            console.error('Failed to bulk update:', error);
            alert('Failed to update selected redirects');
        }
    }

    exportCsv() {
        const url = `/umbraco/api/redirectmanager/export${this.buildQueryParams()}`;
        window.location.href = url;
    }

    triggerImport() {
        const input = this.renderRoot?.querySelector('#importFileInput');
        if (input) input.click();
    }

    async handleImportFile(e) {
        const file = e.target.files && e.target.files[0];
        if (!file) return;

        this.importInProgress = true;
        try {
            const formData = new FormData();
            formData.append('file', file);

            const response = await fetch('/umbraco/api/redirectmanager/import', {
                method: 'POST',
                body: formData
            });

            if (response.ok) {
                const result = await response.json();
                alert(`Imported. Created: ${result.created}, Updated: ${result.updated}, Skipped: ${result.skipped}`);
                this.loadRedirects();
            } else {
                const error = await response.text();
                alert(error || 'Import failed');
            }
        } catch (error) {
            console.error('Import failed:', error);
            alert('Import failed');
        } finally {
            this.importInProgress = false;
            e.target.value = '';
        }
    }

    async saveRedirect() {
        if (!this.formData.oldUrl) {
            alert('Old URL is required');
            return;
        }

        if ((this.formData.statusCode === 301 || this.formData.statusCode === 302) && !this.formData.newUrl) {
            alert('New URL is required for redirect status codes');
            return;
        }

        try {
            let response;
            if (this.editingRedirect) {
                response = await fetch(`/umbraco/api/redirectmanager/update/${this.editingRedirect.id}`, {
                    method: 'PUT',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(this.formData)
                });
            } else {
                response = await fetch('/umbraco/api/redirectmanager/create', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(this.formData)
                });
            }

            if (response.ok) {
                this.closeModal();
                this.loadRedirects();
            } else {
                const error = await response.text();
                alert(error || 'Failed to save redirect');
            }
        } catch (error) {
            console.error('Failed to save redirect:', error);
            alert('Failed to save redirect');
        }
    }

    async deleteRedirect(redirect) {
        if (!confirm(`Are you sure you want to delete the redirect for "${redirect.oldUrl}"?`)) {
            return;
        }

        try {
            const response = await fetch(`/umbraco/api/redirectmanager/delete/${redirect.id}`, {
                method: 'DELETE'
            });

            if (response.ok) {
                this.loadRedirects();
            } else {
                alert('Failed to delete redirect');
            }
        } catch (error) {
            console.error('Failed to delete redirect:', error);
            alert('Failed to delete redirect');
        }
    }

    getStatusLabel(code) {
        const labels = {
            301: '301 - Permanent',
            302: '302 - Temporary',
            404: '404 - Not Found',
            410: '410 - Gone'
        };
        return labels[code] || code;
    }

    render() {
        return html`
            <div class="header">
                <div>
                    <h1>8Bitiz Redirect Manager</h1>
                    <div style="margin-top: 10px; display: flex; gap: 10px; flex-wrap: wrap; align-items: center;">
                        <input type="text" placeholder="Search old/new URL" style="padding: 8px 10px; border: 1px solid #ddd; border-radius: 4px; min-width: 220px;" .value=${this.query} @input=${(e) => { this.query = e.target.value; }} />
                        <select style="padding: 8px 10px; border: 1px solid #ddd; border-radius: 4px;" .value=${this.statusFilter} @change=${(e) => { this.statusFilter = e.target.value; }}>
                            <option value="">All Status</option>
                            <option value="301">301</option>
                            <option value="302">302</option>
                            <option value="404">404</option>
                            <option value="410">410</option>
                        </select>
                        <select style="padding: 8px 10px; border: 1px solid #ddd; border-radius: 4px;" .value=${this.activeFilter} @change=${(e) => { this.activeFilter = e.target.value; }}>
                            <option value="">All</option>
                            <option value="true">Active</option>
                            <option value="false">Inactive</option>
                        </select>
                        <select style="padding: 8px 10px; border: 1px solid #ddd; border-radius: 4px;" .value=${this.regexFilter} @change=${(e) => { this.regexFilter = e.target.value; }}>
                            <option value="">All Types</option>
                            <option value="false">Exact</option>
                            <option value="true">Regex</option>
                        </select>
                        <button class="btn btn-secondary" @click=${this.applyFilters}>Apply</button>
                        <button class="btn btn-secondary" @click=${this.clearFilters}>Clear</button>
                        <button class="btn btn-secondary" @click=${this.exportCsv}>Export CSV</button>
                        <button class="btn btn-secondary" ?disabled=${this.importInProgress} @click=${this.triggerImport}>
                            ${this.importInProgress ? 'Importing...' : 'Import CSV'}
                        </button>
                        <input id="importFileInput" type="file" accept=".csv,text/csv" style="display:none" @change=${this.handleImportFile} />
                    </div>

                    ${this.anySelected ? html`
                        <div style="margin-top: 10px; display:flex; gap:10px; align-items:center; flex-wrap:wrap;">
                            <strong>${this.selectedIds.length} selected</strong>
                            <button class="btn btn-secondary btn-sm" @click=${() => this.bulkSetActiveSelected(true)}>Activate</button>
                            <button class="btn btn-secondary btn-sm" @click=${() => this.bulkSetActiveSelected(false)}>Deactivate</button>
                            <button class="btn btn-danger btn-sm" @click=${this.bulkDeleteSelected}>Delete Selected</button>
                        </div>
                    ` : ''}
                </div>
                <div>
                    <button class="btn btn-primary" @click=${this.openAddModal}>
                        Add New Redirect
                    </button>
                </div>
            </div>

            ${this.loading ? html`
                <div class="loading">Loading redirects...</div>
            ` : this.redirects.length === 0 ? html`
                <div class="empty">No redirects found. Click "Add New Redirect" to create one.</div>
            ` : html`
                <table>
                    <thead>
                        <tr>
                            <th style="width: 40px;">
                                <input type="checkbox" .checked=${this.allSelected} @change=${this.toggleSelectAll} />
                            </th>
                            <th>Status</th>
                            <th>Old URL</th>
                            <th>New URL</th>
                            <th>Type</th>
                            <th>Match</th>
                            <th>Active</th>
                            <th>Actions</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${this.redirects.map(redirect => html`
                            <tr>
                                <td>
                                    <input type="checkbox" .checked=${this.selectedIds.includes(redirect.id)} @change=${(e) => this.toggleSelectId(redirect.id, e.target.checked)} />
                                </td>
                                <td>
                                    <span class="status-badge status-${redirect.statusCode}">
                                        ${redirect.statusCode}
                                    </span>
                                </td>
                                <td class="url-cell" title="${redirect.oldUrl}">${redirect.oldUrl}</td>
                                <td class="url-cell" title="${redirect.newUrl || ''}">${redirect.newUrl || '-'}</td>
                                <td>${this.getStatusLabel(redirect.statusCode)}</td>
                                <td>${redirect.isRegex ? 'Regex' : 'Exact'}</td>
                                <td>
                                    <span class="${redirect.isActive ? 'active-yes' : 'active-no'}">
                                        ${redirect.isActive ? 'Yes' : 'No'}
                                    </span>
                                </td>
                                <td class="actions">
                                    <button class="btn btn-secondary btn-sm" @click=${() => this.openEditModal(redirect)}>
                                        Edit
                                    </button>
                                    <button class="btn btn-danger btn-sm" @click=${() => this.deleteRedirect(redirect)}>
                                        Delete
                                    </button>
                                </td>
                            </tr>
                        `)}
                    </tbody>
                </table>
            `}

            ${this.showModal ? html`
                <div class="modal-overlay" @click=${(e) => e.target === e.currentTarget && this.closeModal()}>
                    <div class="modal">
                        <h2>${this.editingRedirect ? 'Edit Redirect' : 'Add New Redirect'}</h2>
                        
                        <div class="form-group">
                            <label>Status Code</label>
                            <select name="statusCode" .value=${this.formData.statusCode} @change=${this.handleInputChange}>
                                <option value="301">301 - Permanent Redirect</option>
                                <option value="302">302 - Temporary Redirect</option>
                                <option value="404">404 - Not Found</option>
                                <option value="410">410 - Gone</option>
                            </select>
                            <small>Select the redirect type</small>
                        </div>

                        <div class="form-group">
                            <label>Old URL *</label>
                            <input type="text" 
                                   name="oldUrl" 
                                   .value=${this.formData.oldUrl} 
                                   @input=${this.handleInputChange}
                                   placeholder="/old-page">
                            <small>The URL path to redirect from</small>
                        </div>

                        ${this.formData.statusCode === 301 || this.formData.statusCode === 302 ? html`
                            <div class="form-group">
                                <label>New URL *</label>
                                <input type="text" 
                                       name="newUrl" 
                                       .value=${this.formData.newUrl} 
                                       @input=${this.handleInputChange}
                                       placeholder="/new-page">
                                <small>The URL path to redirect to</small>
                            </div>
                        ` : ''}

                        <div class="form-group">
                            <div class="checkbox-group">
                                <input type="checkbox" 
                                       name="isActive" 
                                       id="isActive"
                                       .checked=${this.formData.isActive} 
                                       @change=${this.handleInputChange}>
                                <label for="isActive">Active</label>
                            </div>
                            <small>Enable or disable this redirect</small>
                        </div>

                        <div class="form-group">
                            <div class="checkbox-group">
                                <input type="checkbox" 
                                       name="isRegex" 
                                       id="isRegex"
                                       .checked=${this.formData.isRegex} 
                                       @change=${this.handleInputChange}>
                                <label for="isRegex">Regex match</label>
                            </div>
                            <small>
                                If enabled, Old URL is treated as a regex pattern. For 301/302 you can use capture groups in New URL (e.g. <code>$1</code>).
                            </small>
                        </div>

                        <div class="modal-actions">
                            <button class="btn btn-secondary" @click=${this.closeModal}>Cancel</button>
                            <button class="btn btn-primary" @click=${this.saveRedirect}>Save</button>
                        </div>
                    </div>
                </div>
            ` : ''}
        `;
    }
}

customElements.define('redirect-manager-dashboard', RedirectManagerDashboard);

export { RedirectManagerDashboard as default };
