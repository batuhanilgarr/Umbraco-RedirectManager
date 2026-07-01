import { LitElement, html, css } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { UMB_AUTH_CONTEXT } from '@umbraco-cms/backoffice/auth';

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
        importInProgress: { type: Boolean },
        messageText: { type: String },
        messageType: { type: String },
        activeTab: { type: String },
        missedRequests: { type: Array },
        missedLoading: { type: Boolean }
    };

    static styles = css`
        :host {
            display: block;
            padding: 20px;
            /* Umbraco backoffice flex container içinde scroll'un tabloda olması için */
            min-width: 0;
            max-width: 100%;
            overflow: hidden;
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

        .table-wrapper {
            overflow-x: auto;
            -webkit-overflow-scrolling: touch;
            margin-top: 10px;
            border-radius: 8px;
            box-shadow: 0 1px 3px rgba(0,0,0,0.1);
            /* Flex içinde scroll için wrapper'ın genişliği sınırlı olmalı */
            min-width: 0;
            width: 100%;
        }

        table {
            width: 100%;
            min-width: 1280px;
            border-collapse: collapse;
            background: white;
        }

        th, td {
            padding: 12px 16px;
            text-align: left;
            border-bottom: 1px solid #e9e9e9;
            vertical-align: middle;
        }

        th {
            background-color: #f5f5f5;
            font-weight: 600;
            color: #333;
            white-space: nowrap;
        }

        tbody tr:hover {
            background-color: #f9f9f9;
        }

        .url-cell {
            font-family: monospace;
            font-size: 13px;
            min-width: 200px;
            max-width: 250px;
            overflow: hidden;
            text-overflow: ellipsis;
            white-space: nowrap;
        }

        .url-cell > div {
            min-width: 0;
        }

        .url-cell span {
            display: inline-block;
            max-width: 100%;
            overflow: hidden;
            text-overflow: ellipsis;
            vertical-align: middle;
        }

        .url-cell > div span {
            flex: 1;
            min-width: 0;
        }

        .notes-cell {
            min-width: 140px;
            max-width: 280px;
            overflow: hidden;
            text-overflow: ellipsis;
            white-space: nowrap;
        }

        td.actions-cell {
            min-width: 140px;
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
            flex-wrap: nowrap;
            gap: 8px;
            flex-direction: column;
        }

        .actions .btn {
            flex-shrink: 0;
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
            padding: 20px;
            overflow-y: auto;
        }

        .modal {
            background: white;
            border-radius: 12px;
            padding: 28px 32px;
            width: 520px;
            max-width: calc(100vw - 40px);
            max-height: calc(100vh - 40px);
            overflow-y: auto;
            box-shadow: 0 8px 32px rgba(0,0,0,0.2);
            margin: auto;
        }

        .modal h2 {
            margin: 0 0 24px 0;
            font-size: 1.35rem;
            font-weight: 600;
            color: #1b264f;
            padding-bottom: 12px;
            border-bottom: 1px solid #e9e9e9;
        }

        .form-group {
            margin-bottom: 20px;
        }

        .form-group:last-of-type {
            margin-bottom: 0;
        }

        .form-group label {
            display: block;
            margin-bottom: 8px;
            font-weight: 600;
            font-size: 14px;
            color: #333;
        }

        .form-group input[type="text"],
        .form-group select,
        .form-group textarea {
            width: 100%;
            padding: 10px 12px;
            border: 1px solid #d8d7d9;
            border-radius: 6px;
            font-size: 14px;
            font-family: inherit;
            box-sizing: border-box;
            transition: border-color 0.15s ease, box-shadow 0.15s ease;
        }

        .form-group input[type="text"]:focus,
        .form-group select:focus,
        .form-group textarea:focus {
            outline: none;
            border-color: #3544b1;
            box-shadow: 0 0 0 3px rgba(53, 68, 177, 0.15);
        }

        .form-group input[type="text"]::placeholder,
        .form-group textarea::placeholder {
            color: #999;
        }

        .form-group select {
            cursor: pointer;
            appearance: auto;
        }

        .form-group textarea {
            resize: vertical;
            min-height: 80px;
        }

        .form-group small {
            display: block;
            margin-top: 6px;
            color: #666;
            font-size: 12px;
            line-height: 1.4;
        }

        .form-group small code {
            padding: 2px 6px;
            background: #f0f0f0;
            border-radius: 4px;
            font-size: 11px;
            color: #333;
        }

        .checkbox-group {
            display: flex;
            align-items: center;
            gap: 10px;
        }

        .checkbox-group input[type="checkbox"] {
            width: 18px;
            height: 18px;
            margin: 0;
            cursor: pointer;
            accent-color: #3544b1;
        }

        .checkbox-group label {
            margin-bottom: 0;
            cursor: pointer;
            font-weight: 500;
        }

        .modal-actions {
            display: flex;
            justify-content: flex-end;
            gap: 12px;
            margin-top: 24px;
            padding-top: 20px;
            border-top: 1px solid #e9e9e9;
        }

        .modal-actions .btn {
            min-width: 100px;
        }
    `;

    /** Inline modal styles so they apply even when global redirect.css is not loaded or modal is in shadow DOM */
    static get modalInlineStyles() {
        return `
            .redirect-manager-modal-root .modal-overlay {
                position: fixed; top: 0; left: 0; right: 0; bottom: 0;
                background: rgba(0,0,0,0.5); display: flex; align-items: center; justify-content: center;
                z-index: 1000; padding: 20px; overflow-y: auto;
            }
            .redirect-manager-modal-root .modal {
                background: #fff; border-radius: 16px; padding: 36px 40px; width: 600px;
                max-width: calc(100vw - 40px); max-height: calc(100vh - 40px); overflow-y: auto;
                box-shadow: 0 20px 60px rgba(0,0,0,0.15); margin: auto;
                display: flex; flex-direction: column;
            }
            .redirect-manager-modal-root .modal h2 {
                margin: 0 0 32px 0; font-size: 1.5rem; font-weight: 700; color: #1b264f;
                padding-bottom: 16px; border-bottom: 2px solid #f0f0f0; letter-spacing: -0.01em;
            }
            .redirect-manager-modal-root .modal .form-group { 
                margin-bottom: 32px; 
            }
            .redirect-manager-modal-root .modal .form-group:last-of-type {
                margin-bottom: 0; 
            }
            .redirect-manager-modal-root .modal .form-group label {
                display: block; margin-bottom: 10px; font-weight: 600; font-size: 14px; color: #1b264f; letter-spacing: 0.01em;
            }
            .redirect-manager-modal-root .modal .form-group input[type="text"],
            .redirect-manager-modal-root .modal .form-group select,
            .redirect-manager-modal-root .modal .form-group textarea {
                width: 100%; padding: 14px 16px; border: 2px solid #e5e7eb; border-radius: 10px;
                font-size: 14px; font-family: inherit; box-sizing: border-box; color: #1f2937;
                transition: all 0.25s cubic-bezier(0.4, 0, 0.2, 1); background-color: #fff;
            }
            .redirect-manager-modal-root .modal .form-group input[type="text"]:hover,
            .redirect-manager-modal-root .modal .form-group select:hover,
            .redirect-manager-modal-root .modal .form-group textarea:hover {
                border-color: #d1d5db;
            }
            .redirect-manager-modal-root .modal .form-group input[type="text"]:focus,
            .redirect-manager-modal-root .modal .form-group select:focus,
            .redirect-manager-modal-root .modal .form-group textarea:focus {
                outline: none; border-color: #3544b1; box-shadow: 0 0 0 4px rgba(53,68,177,0.12); background-color: #fafbff;
            }
            .redirect-manager-modal-root .modal .form-group select {
                cursor: pointer; background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='12' height='12' viewBox='0 0 12 12'%3E%3Cpath fill='%23333' d='M6 9L1 4h10z'/%3E%3C/svg%3E");
                background-repeat: no-repeat; background-position: right 16px center; background-size: 12px;
                padding-right: 40px; appearance: none; -webkit-appearance: none; -moz-appearance: none;
            }
            .redirect-manager-modal-root .modal .form-group input[type="text"]::placeholder,
            .redirect-manager-modal-root .modal .form-group textarea::placeholder {
                color: #9ca3af; opacity: 1;
            }
            .redirect-manager-modal-root .modal .form-group textarea { resize: vertical; min-height: 100px; }
            .redirect-manager-modal-root .modal .form-group small {
                display: block; margin-top: 8px; color: #6b7280; font-size: 12px; line-height: 1.5;
            }
            .redirect-manager-modal-root .modal .form-group small code {
                padding: 2px 6px; background: #f3f4f6; border-radius: 4px; font-size: 11px; color: #374151; font-family: 'Monaco', 'Courier New', monospace;
            }
            .redirect-manager-modal-root .modal .status-code-select-wrapper {
                position: relative;
            }
            .redirect-manager-modal-root .modal .status-code-badge {
                position: absolute; right: 16px; top: 50%; transform: translateY(-50%);
                padding: 4px 10px; border-radius: 6px; font-size: 11px; font-weight: 700;
                pointer-events: none; margin-top: 0;
            }
            .redirect-manager-modal-root .modal .status-code-badge.status-301 {
                background: #d1fae5; color: #065f46;
            }
            .redirect-manager-modal-root .modal .status-code-badge.status-302 {
                background: #fef3c7; color: #92400e;
            }
            .redirect-manager-modal-root .modal .status-code-badge.status-404 {
                background: #fee2e2; color: #991b1b;
            }
            .redirect-manager-modal-root .modal .status-code-badge.status-410 {
                background: #e5e7eb; color: #374151;
            }
            .redirect-manager-modal-root .modal .status-code-select-wrapper select {
                padding-right: 100px;
            }
            .redirect-manager-modal-root .modal .form-row {
                display: flex; gap: 16px; justify-content: space-between;
            }
            .redirect-manager-modal-root .modal .form-row .form-group {
                flex: 1;
            }
            .redirect-manager-modal-root .modal .form-row .form-group + .form-group {
                margin-left: 8px;
            }
            @media (max-width: 768px) {
                .redirect-manager-modal-root .modal {
                    width: 100%;
                    padding: 24px 20px;
                }
                .redirect-manager-modal-root .modal h2 {
                    font-size: 1.3rem; margin-bottom: 24px;
                }
                .redirect-manager-modal-root .modal .form-group {
                    margin-bottom: 24px;
                }
                .redirect-manager-modal-root .modal .form-row {
                    flex-direction: column;
                }
                .redirect-manager-modal-root .modal .form-row .form-group + .form-group {
                    margin-left: 0;
                }
                .redirect-manager-modal-root .modal .status-code-badge {
                    display: none;
                }
                .redirect-manager-modal-root .modal .status-code-select-wrapper select {
                    padding-right: 40px;
                }
            }
            .redirect-manager-modal-root .modal .toggle-group {
                display: flex; align-items: center; justify-content: space-between; gap: 16px;
                padding: 16px; background: #f9fafb; border-radius: 10px; border: 1px solid #e5e7eb;
            }
            .redirect-manager-modal-root .modal .toggle-group .toggle-label {
                flex: 1; margin-bottom: 0; cursor: pointer; font-weight: 500; color: #374151;
            }
            .redirect-manager-modal-root .modal .toggle-switch {
                position: relative; display: inline-block; width: 52px; height: 28px;
                flex-shrink: 0;
            }
            .redirect-manager-modal-root .modal .toggle-switch input {
                opacity: 0; width: 0; height: 0;
            }
            .redirect-manager-modal-root .modal .toggle-slider {
                position: absolute; cursor: pointer; top: 0; left: 0; right: 0; bottom: 0;
                background-color: #d1d5db; transition: 0.3s; border-radius: 28px;
            }
            .redirect-manager-modal-root .modal .toggle-slider:before {
                position: absolute; content: ""; height: 20px; width: 20px; left: 4px; bottom: 4px;
                background-color: white; transition: 0.3s; border-radius: 50%; box-shadow: 0 2px 4px rgba(0,0,0,0.2);
            }
            .redirect-manager-modal-root .modal .toggle-switch input:checked + .toggle-slider {
                background-color: #2bc37b;
            }
            .redirect-manager-modal-root .modal .toggle-switch input:focus + .toggle-slider {
                box-shadow: 0 0 0 3px rgba(43,195,123,0.2);
            }
            .redirect-manager-modal-root .modal .toggle-switch input:checked + .toggle-slider:before {
                transform: translateX(24px);
            }
            .redirect-manager-modal-root .modal .modal-actions {
                display: flex; justify-content: flex-end; gap: 12px; margin-top: 40px;
                padding-top: 24px; border-top: 2px solid #f0f0f0;
            }
            .redirect-manager-modal-root .modal .modal-actions .btn {
                min-width: 120px; padding: 12px 28px; border: none; border-radius: 10px;
                font-size: 14px; font-weight: 600; cursor: pointer; transition: all 0.25s cubic-bezier(0.4, 0, 0.2, 1);
            }
            .redirect-manager-modal-root .modal .modal-actions .btn.btn-primary {
                background: #2bc37b; color: #fff; box-shadow: 0 2px 8px rgba(43,195,123,0.25);
            }
            .redirect-manager-modal-root .modal .modal-actions .btn.btn-primary:hover {
                background: #25a866; transform: translateY(-2px); box-shadow: 0 6px 16px rgba(43,195,123,0.35);
            }
            .redirect-manager-modal-root .modal .modal-actions .btn.btn-primary:active {
                transform: translateY(0); box-shadow: 0 2px 8px rgba(43,195,123,0.25);
            }
            .redirect-manager-modal-root .modal .modal-actions .btn.btn-secondary {
                background: transparent; color: #6b7280; border: 2px solid #e5e7eb;
            }
            .redirect-manager-modal-root .modal .modal-actions .btn.btn-secondary:hover {
                background: #f9fafb; color: #374151; border-color: #d1d5db;
            }
        `;
    }

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
        this.messageText = '';
        this.messageType = 'info';
        this.activeTab = 'redirects';
        this.missedRequests = [];
        this.missedLoading = false;
    }

    showMessage(text, type = 'info') {
        this.messageText = text;
        this.messageType = type;

        window.clearTimeout(this._messageTimeout);
        this._messageTimeout = window.setTimeout(() => {
            this.messageText = '';
        }, 4500);
    }

    async copyToClipboard(text) {
        try {
            if (navigator.clipboard && navigator.clipboard.writeText) {
                await navigator.clipboard.writeText(text);
            } else {
                const input = document.createElement('input');
                input.value = text;
                document.body.appendChild(input);
                input.select();
                document.execCommand('copy');
                document.body.removeChild(input);
            }
            this.showMessage('Copied to clipboard', 'success');
        } catch (e) {
            this.showMessage('Copy failed', 'error');
        }
    }

    /**
     * Wraps the platform fetch() to attach the current backoffice user's
     * bearer token. Umbraco 17+/18's backoffice API controllers (including
     * custom ones secured with AuthorizationPolicies.BackOfficeAccess) are
     * authenticated via OpenIddict, not the classic backoffice cookie, so a
     * same-origin fetch() with no Authorization header is rejected with 401
     * even when the user has an active backoffice session in the browser.
     */
    async authFetch(url, options = {}) {
        const authContext = await this.getContext(UMB_AUTH_CONTEXT);
        const token = await authContext?.getLatestToken();
        const headers = new Headers(options.headers || {});
        if (token) {
            headers.set('Authorization', `Bearer ${token}`);
        }
        return fetch(url, { ...options, headers, credentials: 'include' });
    }

    async testRedirect(path) {
        try {
            const response = await this.authFetch(`/umbraco/api/redirectmanager/test?path=${encodeURIComponent(path)}`);
            if (!response.ok) {
                const error = await response.text();
                this.showMessage(error || 'Test failed', 'error');
                return;
            }

            const result = await response.json();
            if (!result.matched) {
                this.showMessage('No redirect matched', 'info');
                return;
            }

            const code = result.redirect?.statusCode;
            const to = result.computedNewUrl || '-';
            this.showMessage(`Matched ${result.matchType} (${code}) -> ${to}`, 'success');
        } catch (e) {
            this.showMessage('Test failed', 'error');
        }
    }

    connectedCallback() {
        super.connectedCallback();
        this.ensureModalStylesLoaded();
        this.loadRedirects();
        this.loadMissedRequests();
    }

    /** Ensure redirect.css is in document so modal styles apply if modal is in light DOM */
    ensureModalStylesLoaded() {
        const id = 'redirect-manager-styles';
        if (document.getElementById(id)) return;
        const link = document.createElement('link');
        link.id = id;
        link.rel = 'stylesheet';
        link.href = '/App_Plugins/RedirectManager/redirect.css';
        document.head.appendChild(link);
    }

    getEmptyFormData() {
        return {
            oldUrl: '',
            newUrl: '',
            domain: '',
            description: '',
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
            const response = await this.authFetch(`/umbraco/api/redirectmanager/getall${this.buildQueryParams()}`);
            if (response.ok) {
                this.redirects = await response.json();
                this.selectedIds = [];
            }
        } catch (error) {
            console.error('Failed to load redirects:', error);
        }
        this.loading = false;
    }

    async loadMissedRequests() {
        this.missedLoading = true;
        try {
            const response = await this.authFetch('/umbraco/api/redirectmanager/missed');
            if (response.ok) {
                this.missedRequests = await response.json();
            }
        } catch (error) {
            console.error('Failed to load missed requests:', error);
        }
        this.missedLoading = false;
    }

    async dismissMissedRequest(item) {
        try {
            const response = await this.authFetch(`/umbraco/api/redirectmanager/missed/${item.id}`, { method: 'DELETE' });
            if (response.ok) {
                this.missedRequests = this.missedRequests.filter(m => m.id !== item.id);
            } else {
                this.showMessage('Failed to dismiss entry', 'error');
            }
        } catch (error) {
            console.error('Failed to dismiss missed request:', error);
            this.showMessage('Failed to dismiss entry', 'error');
        }
    }

    createRedirectFromMissed(item) {
        this.openAddModal(item.path);
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

    openAddModal(prefillOldUrl = '') {
        this.editingRedirect = null;
        this.formData = { ...this.getEmptyFormData(), oldUrl: prefillOldUrl };
        this.showModal = true;
    }

    openEditModal(redirect) {
        this.editingRedirect = redirect;
        this.formData = {
            oldUrl: redirect.oldUrl,
            newUrl: redirect.newUrl || '',
            domain: redirect.domain || '',
            description: redirect.description || '',
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
            const response = await this.authFetch('/umbraco/api/redirectmanager/bulk/delete', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ ids: this.selectedIds })
            });
            if (response.ok) {
                const selected = new Set(this.selectedIds);
                this.redirects = this.redirects.filter(r => !selected.has(r.id));
                this.selectedIds = [];
                this.showMessage('Selected redirects deleted', 'success');
            } else {
                const error = await response.text();
                this.showMessage(error || 'Failed to delete selected redirects', 'error');
            }
        } catch (error) {
            console.error('Failed to bulk delete:', error);
            this.showMessage('Failed to delete selected redirects', 'error');
        }
    }

    async bulkSetActiveSelected(isActive) {
        if (!this.anySelected) return;

        try {
            const response = await this.authFetch(isActive ? '/umbraco/api/redirectmanager/bulk/activate' : '/umbraco/api/redirectmanager/bulk/deactivate', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ ids: this.selectedIds })
            });
            if (response.ok) {
                const selected = new Set(this.selectedIds);
                this.redirects = this.redirects.map(r => selected.has(r.id) ? { ...r, isActive } : r);
                this.showMessage('Selected redirects updated', 'success');
            } else {
                const error = await response.text();
                this.showMessage(error || 'Failed to update selected redirects', 'error');
            }
        } catch (error) {
            console.error('Failed to bulk update:', error);
            this.showMessage('Failed to update selected redirects', 'error');
        }
    }

    async exportCsv() {
        // A plain navigation (window.location.href) can't carry an Authorization
        // header, so the file has to be fetched (with the bearer token attached
        // via authFetch) and then handed to the browser as a Blob download.
        const url = `/umbraco/api/redirectmanager/export${this.buildQueryParams()}`;
        try {
            const response = await this.authFetch(url);
            if (!response.ok) {
                this.showMessage('Failed to export CSV', 'error');
                return;
            }

            const blob = await response.blob();
            const objectUrl = URL.createObjectURL(blob);
            const link = document.createElement('a');
            link.href = objectUrl;
            link.download = 'redirects.csv';
            document.body.appendChild(link);
            link.click();
            document.body.removeChild(link);
            URL.revokeObjectURL(objectUrl);
        } catch (error) {
            console.error('Failed to export CSV:', error);
            this.showMessage('Failed to export CSV', 'error');
        }
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

            const response = await this.authFetch('/umbraco/api/redirectmanager/import', {
                method: 'POST',
                body: formData
            });

            if (response.ok) {
                const result = await response.json();
                this.showMessage(`Imported. Created: ${result.created}, Updated: ${result.updated}, Skipped: ${result.skipped}`, 'success');
                this.loadRedirects();
            } else {
                const error = await response.text();
                this.showMessage(error || 'Import failed', 'error');
            }
        } catch (error) {
            console.error('Import failed:', error);
            this.showMessage('Import failed', 'error');
        } finally {
            this.importInProgress = false;
            e.target.value = '';
        }
    }

    async saveRedirect() {
        if (!this.formData.oldUrl) {
            this.showMessage('Old URL is required', 'error');
            return;
        }

        if ((this.formData.statusCode === 301 || this.formData.statusCode === 302) && !this.formData.newUrl) {
            this.showMessage('New URL is required for redirect status codes', 'error');
            return;
        }

        try {
            let response;
            if (this.editingRedirect) {
                response = await this.authFetch(`/umbraco/api/redirectmanager/update/${this.editingRedirect.id}`, {
                    method: 'PUT',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(this.formData)
                });
            } else {
                response = await this.authFetch('/umbraco/api/redirectmanager/create', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(this.formData)
                });
            }

            if (response.ok) {
                const saved = await response.json();

                if (this.editingRedirect) {
                    this.redirects = this.redirects.map(r => r.id === saved.id ? saved : r);
                    this.showMessage('Redirect updated', 'success');
                } else {
                    this.redirects = [saved, ...this.redirects];
                    this.showMessage('Redirect created', 'success');
                }

                this.closeModal();
            } else {
                const error = await response.text();
                this.showMessage(error || 'Failed to save redirect', 'error');
            }
        } catch (error) {
            console.error('Failed to save redirect:', error);
            this.showMessage('Failed to save redirect', 'error');
        }
    }

    async deleteRedirect(redirect) {
        if (!confirm(`Are you sure you want to delete the redirect for "${redirect.oldUrl}"?`)) {
            return;
        }

        try {
            const response = await this.authFetch(`/umbraco/api/redirectmanager/delete/${redirect.id}`, {
                method: 'DELETE'
            });

            if (response.ok) {
                this.redirects = this.redirects.filter(r => r.id !== redirect.id);
                this.selectedIds = this.selectedIds.filter(id => id !== redirect.id);
                this.showMessage('Redirect deleted', 'success');
            } else {
                const error = await response.text();
                this.showMessage(error || 'Failed to delete redirect', 'error');
            }
        } catch (error) {
            console.error('Failed to delete redirect:', error);
            this.showMessage('Failed to delete redirect', 'error');
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

    getLastHitTitle(redirect) {
        return redirect.lastHitDate
            ? `Last hit: ${new Date(redirect.lastHitDate).toLocaleString()}`
            : 'Never hit';
    }

    getMissedRequestTitle(item) {
        return `First seen: ${new Date(item.firstSeenDate).toLocaleString()}`;
    }

    render() {
        return html`
            <div class="header">
                <div>
                    <h1>Bitiz Redirect Manager</h1>
                    <p style="margin-top: 8px; margin-bottom: 16px; color: #666; font-size: 14px; line-height: 1.5;">
                        This panel lets you centrally manage URL redirects for your site. You can add new redirects, update existing ones, or remove rules that are no longer needed.
                    </p>

                    <!-- Status Code Legend -->
                    <div class="redirect-status-legend" style="background: #f8f9fa; border: 1px solid #e9e9e9; border-radius: 8px; padding: 16px 20px; margin-bottom: 20px;">
                        <h3 style="margin: 0 0 12px 0; font-size: 14px; font-weight: 600; color: #333;">Status Code Descriptions:</h3>
                        <div style="display: flex; flex-wrap: wrap; gap: 16px;">
                            <div style="display: flex; align-items: center; gap: 8px; flex: 1 1 calc(50% - 8px); min-width: 250px;">
                                <span class="redirect-status redirect-status-301" style="display: inline-block; padding: 4px 8px; border-radius: 4px; font-weight: 600; font-size: 12px; color: #fff; background-color: #2bc37b;">301</span>
                                <span style="font-weight: 600; color: #333; font-size: 13px;">Permanent Redirect</span>
                                <span style="color: #666; font-size: 12px;">- Tells browsers and search engines to always use the new URL</span>
                            </div>
                            <div style="display: flex; align-items: center; gap: 8px; flex: 1 1 calc(50% - 8px); min-width: 250px;">
                                <span class="redirect-status redirect-status-302" style="display: inline-block; padding: 4px 8px; border-radius: 4px; font-weight: 600; font-size: 12px; color: #333; background-color: #f5c520;">302</span>
                                <span style="font-weight: 600; color: #333; font-size: 13px;">Temporary Redirect</span>
                                <span style="color: #666; font-size: 12px;">- Temporarily redirects traffic to another URL</span>
                            </div>
                            <div style="display: flex; align-items: center; gap: 8px; flex: 1 1 calc(50% - 8px); min-width: 250px;">
                                <span class="redirect-status redirect-status-404" style="display: inline-block; padding: 4px 8px; border-radius: 4px; font-weight: 600; font-size: 12px; color: #fff; background-color: #d42054;">404</span>
                                <span style="font-weight: 600; color: #333; font-size: 13px;">Not Found</span>
                                <span style="color: #666; font-size: 12px;">- Returns a standard 404 \"Page not found\" response</span>
                            </div>
                            <div style="display: flex; align-items: center; gap: 8px; flex: 1 1 calc(50% - 8px); min-width: 250px;">
                                <span class="redirect-status redirect-status-410" style="display: inline-block; padding: 4px 8px; border-radius: 4px; font-weight: 600; font-size: 12px; color: #fff; background-color: #6c757d;">410</span>
                                <span style="font-weight: 600; color: #333; font-size: 13px;">Gone</span>
                                <span style="color: #666; font-size: 12px;">- Indicates the page has been permanently removed</span>
                            </div>
                        </div>
                    </div>

                    ${this.messageText ? html`
                        <div style="margin-top: 10px; padding: 10px 12px; border-radius: 6px; border: 1px solid #e9e9e9; background: ${this.messageType === 'success' ? '#e8f5e9' : this.messageType === 'error' ? '#fdecea' : '#eef2ff'}; color: #1b264f;">
                            ${this.messageText}
                        </div>
                    ` : ''}

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
                        <button class="btn btn-primary" style="margin-left: auto;" @click=${() => this.openAddModal()}>
                            Add New Redirect
                        </button>
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
            </div>

            <div style="margin-top: 20px; margin-bottom: 4px; display:flex; gap: 8px; border-bottom: 1px solid #e9e9e9; padding-bottom: 12px;">
                <button class="btn ${this.activeTab === 'redirects' ? 'btn-primary' : 'btn-secondary'}" @click=${() => { this.activeTab = 'redirects'; }}>Redirects</button>
                <button class="btn ${this.activeTab === 'missed' ? 'btn-primary' : 'btn-secondary'}" @click=${() => { this.activeTab = 'missed'; }}>404 Log</button>
            </div>

            ${this.activeTab === 'redirects' ? html`
            ${this.loading ? html`
                <div class="loading">Loading redirects...</div>
            ` : this.redirects.length === 0 ? html`
                <div class="empty">No redirects found. Click "Add New Redirect" to create one.</div>
            ` : html`
                <div class="table-wrapper">
                    <table>
                        <thead>
                            <tr>
                                <th style="width: 40px;">
                                    <input type="checkbox" .checked=${this.allSelected} @change=${this.toggleSelectAll} />
                                </th>
                                <th style="text-align: center;">Status</th>
                                <th style="text-align: center;">Old URL</th>
                                <th style="text-align: center;">New URL</th>
                                <th style="text-align: center;">Domain</th>
                                <th style="text-align: center;">Notes</th>
                                <th style="text-align: center;">Type</th>
                                <th style="text-align: center;">Match</th>
                                <th style="text-align: center;">Active</th>
                                <th style="text-align: center;">Hits</th>
                                <th style="text-align: center;">Actions</th>
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
                                    <td class="url-cell" title="${redirect.oldUrl}">
                                        <div style="display:flex; gap:6px; align-items:center;flex-direction:column;">
                                            <span>${redirect.oldUrl}</span>
                                            <button class="btn btn-secondary btn-sm" @click=${() => this.copyToClipboard(redirect.oldUrl)}>Copy</button>
                                        </div>
                                    </td>
                                    <td class="url-cell" title="${redirect.newUrl || ''}">
                                        ${redirect.newUrl ? html`
                                            <div style="display:flex; gap:6px; align-items:center;flex-direction:column;">
                                                <span>${redirect.newUrl}</span>
                                                <button class="btn btn-secondary btn-sm" @click=${() => this.copyToClipboard(redirect.newUrl)}>Copy</button>
                                            </div>
                                        ` : '-'}
                                    </td>
                                    <td style="text-align: center;">${redirect.domain || 'All domains'}</td>
                                    <td class="notes-cell" title="${redirect.description || ''}">${redirect.description || '-'}</td>
                                    <td>${this.getStatusLabel(redirect.statusCode)}</td>
                                    <td>${redirect.isRegex ? 'Regex' : 'Exact'}</td>
                                    <td>
                                        <span class="${redirect.isActive ? 'active-yes' : 'active-no'}">
                                            ${redirect.isActive ? 'Yes' : 'No'}
                                        </span>
                                    </td>
                                    <td style="text-align: center;" title="${this.getLastHitTitle(redirect)}">
                                        ${redirect.hitCount || 0}
                                    </td>
                                    <td class="actions actions-cell">
                                        <button class="btn btn-secondary btn-sm" @click=${() => this.testRedirect(redirect.oldUrl)}>
                                            Test
                                        </button>
                                        <button class="btn btn-info btn-sm" @click=${() => this.openEditModal(redirect)}>
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
                </div>
            `}
            ` : ''}

            ${this.activeTab === 'missed' ? html`
                ${this.missedLoading ? html`
                    <div class="loading">Loading 404 log...</div>
                ` : this.missedRequests.length === 0 ? html`
                    <div class="empty">No missed requests logged yet.</div>
                ` : html`
                    <div class="table-wrapper">
                        <table>
                            <thead>
                                <tr>
                                    <th style="text-align: center;">Path</th>
                                    <th style="text-align: center;">Hits</th>
                                    <th style="text-align: center;">First Seen</th>
                                    <th style="text-align: center;">Last Seen</th>
                                    <th style="text-align: center;">Actions</th>
                                </tr>
                            </thead>
                            <tbody>
                                ${this.missedRequests.map(item => html`
                                    <tr>
                                        <td class="url-cell" title="${this.getMissedRequestTitle(item)}">${item.path}</td>
                                        <td style="text-align:center;">${item.hitCount}</td>
                                        <td style="text-align:center;">${new Date(item.firstSeenDate).toLocaleDateString()}</td>
                                        <td style="text-align:center;">${new Date(item.lastSeenDate).toLocaleDateString()}</td>
                                        <td class="actions actions-cell">
                                            <button class="btn btn-primary btn-sm" @click=${() => this.createRedirectFromMissed(item)}>Create Redirect</button>
                                            <button class="btn btn-danger btn-sm" @click=${() => this.dismissMissedRequest(item)}>Dismiss</button>
                                        </td>
                                    </tr>
                                `)}
                            </tbody>
                        </table>
                    </div>
                `}
            ` : ''}

            ${this.showModal ? html`
                <div class="redirect-manager-modal-root">
                    <style>${RedirectManagerDashboard.modalInlineStyles}</style>
                    <div class="modal-overlay" @click=${(e) => e.target === e.currentTarget && this.closeModal()}>
                        <div class="modal" @click=${(e) => e.stopPropagation()}>
                            <h2>${this.editingRedirect ? 'Edit Redirect' : 'Add New Redirect'}</h2>
                        
                        <div class="form-group">
                            <label>Status Code</label>
                            <div class="status-code-select-wrapper">
                                <select name="statusCode" .value=${this.formData.statusCode} @change=${this.handleInputChange}>
                                    <option value="301">301 - Permanent Redirect</option>
                                    <option value="302">302 - Temporary Redirect</option>
                                    <option value="404">404 - Not Found</option>
                                    <option value="410">410 - Gone</option>
                                </select>
                                <span class="status-code-badge status-${this.formData.statusCode}">
                                    ${this.formData.statusCode === 301 ? 'Kalıcı' : this.formData.statusCode === 302 ? 'Geçici' : this.formData.statusCode === 404 ? 'Bulunamadı' : 'Kaldırıldı'}
                                </span>
                            </div>
                            <small>Select the redirect type</small>
                        </div>

                        <div class="form-row">
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
                        </div>

                        <div class="form-group">
                            <label>Domain (optional)</label>
                            <input type="text"
                                   name="domain"
                                   .value=${this.formData.domain}
                                   @input=${this.handleInputChange}
                                   placeholder="example.com">
                            <small>Leave blank to apply this redirect to all domains. If both a domain-specific and an all-domains redirect exist for the same Old URL, the domain-specific one wins.</small>
                        </div>

                        <div class="form-group">
                            <label>Notes</label>
                            <textarea name="description"
                                      rows="3"
                                      .value=${this.formData.description}
                                      @input=${this.handleInputChange}
                                      placeholder="Optional notes..."></textarea>
                            <small>Optional description to help identify the purpose of this redirect</small>
                        </div>

                        <div class="form-group">
                            <div class="toggle-group">
                                <label class="toggle-label" for="isActive">Active</label>
                                <label class="toggle-switch">
                                    <input type="checkbox" 
                                           name="isActive" 
                                           id="isActive"
                                           .checked=${this.formData.isActive} 
                                           @change=${this.handleInputChange}>
                                    <span class="toggle-slider"></span>
                                </label>
                            </div>
                            <small>Enable or disable this redirect</small>
                        </div>

                        <div class="form-group">
                            <div class="toggle-group">
                                <label class="toggle-label" for="isRegex">Regex match</label>
                                <label class="toggle-switch">
                                    <input type="checkbox" 
                                           name="isRegex" 
                                           id="isRegex"
                                           .checked=${this.formData.isRegex} 
                                           @change=${this.handleInputChange}>
                                    <span class="toggle-slider"></span>
                                </label>
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
                </div>
            ` : ''}
        `;
    }
}

customElements.define('redirect-manager-dashboard', RedirectManagerDashboard);

export { RedirectManagerDashboard as default };
