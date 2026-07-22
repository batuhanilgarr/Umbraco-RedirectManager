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
        missedLoading: { type: Boolean },
        stats: { type: Object },
        statsLoading: { type: Boolean },
        telemetryEnabled: { type: Boolean },
        telemetryLoading: { type: Boolean },
        telemetryDecided: { type: Boolean },
        showTelemetryPrompt: { type: Boolean },
        updateAvailable: { type: Boolean },
        currentVersion: { type: String },
        latestVersion: { type: String },
        redirectsSort: { type: Object },
        missedSort: { type: Object },
        topRedirectsSort: { type: Object },
        staleRedirectsSort: { type: Object }
    };

    static styles = css`
        :host {
            display: block;
            padding: 20px;
            min-width: 0;
            max-width: 100%;
            overflow: hidden;
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
        }

        /* ── page header ── */
        .page-header {
            display: flex;
            justify-content: space-between;
            align-items: flex-start;
            margin-bottom: 14px;
            gap: 16px;
        }

        .page-header h1 {
            margin: 0;
            font-size: 20px;
            font-weight: 600;
            color: #1b264f;
            letter-spacing: -0.01em;
        }

        .page-header p {
            margin: 3px 0 0;
            font-size: 13px;
            color: #888;
            line-height: 1.5;
        }

        /* ── buttons ── */
        .btn {
            display: inline-flex;
            align-items: center;
            gap: 5px;
            padding: 7px 13px;
            border: 1px solid #e0e0e0;
            border-radius: 6px;
            cursor: pointer;
            font-size: 12px;
            font-weight: 500;
            background: #fff;
            color: #555;
            white-space: nowrap;
            transition: background 0.1s, border-color 0.1s;
            line-height: 1.4;
        }

        .btn:hover { background: #f5f5f5; border-color: #ccc; }
        .btn:disabled { opacity: 0.5; cursor: not-allowed; }

        .btn-primary {
            background: #2bc37b;
            color: #fff;
            border-color: transparent;
            padding: 8px 16px;
            font-weight: 600;
        }

        .btn-primary:hover { background: #25a866; border-color: transparent; }

        .btn-apply {
            background: #3544b1;
            color: #fff;
            border-color: transparent;
        }

        .btn-apply:hover { background: #2d3a9e; border-color: transparent; }

        .btn-sm { padding: 4px 9px; font-size: 11px; border-radius: 5px; }

        .btn-danger { color: #d42054; border-color: #f5c0cc; background: #fff; }
        .btn-danger:hover { background: #fef0f3; border-color: #e8a0b0; }

        .btn-info { color: #3544b1; border-color: #c5caee; background: #fff; }
        .btn-info:hover { background: #eef0fb; border-color: #aab0e0; }

        .btn-success-sm { color: #065f46; border-color: #a7f3d0; background: #f0fdf4; }
        .btn-success-sm:hover { background: #dcfce7; }

        /* ── status legend ── */
        .status-legend {
            display: flex;
            gap: 14px;
            flex-wrap: wrap;
            align-items: center;
            padding: 10px 0 14px;
            border-bottom: 1px solid #f0f0f0;
            margin-bottom: 12px;
        }

        .legend-item {
            display: flex;
            align-items: center;
            gap: 6px;
        }

        .legend-lbl {
            font-size: 12px;
            color: #777;
        }

        /* ── status badges ── */
        .status-badge {
            display: inline-flex;
            align-items: center;
            justify-content: center;
            padding: 2px 7px;
            border-radius: 4px;
            font-weight: 600;
            font-size: 11px;
            line-height: 1.5;
        }

        .status-301 { background: #d1fae5; color: #065f46; }
        .status-302 { background: #fef3c7; color: #92400e; }
        .status-404 { background: #fee2e2; color: #991b1b; }
        .status-410 { background: #e5e7eb; color: #374151; }

        /* ── toolbar ── */
        .toolbar {
            display: flex;
            align-items: center;
            gap: 7px;
            flex-wrap: wrap;
            padding: 10px 12px;
            background: #fafafa;
            border: 1px solid #efefef;
            border-radius: 8px;
            margin-bottom: 12px;
        }

        .search-wrap {
            position: relative;
            flex: 1;
            min-width: 180px;
        }

        .search-icon {
            position: absolute;
            left: 8px;
            top: 50%;
            transform: translateY(-50%);
            color: #bbb;
            pointer-events: none;
            display: flex;
            align-items: center;
        }

        .toolbar input[type="text"] {
            box-sizing: border-box;
            width: 100%;
            padding: 7px 10px 7px 28px;
            border: 1px solid #e0e0e0;
            border-radius: 6px;
            font-size: 12px;
            background: #fff;
            color: #333;
            line-height: 1.4;
        }

        .toolbar input[type="text"]:focus {
            outline: none;
            border-color: #3544b1;
            box-shadow: 0 0 0 3px rgba(53,68,177,0.1);
        }

        .toolbar select {
            padding: 7px 10px;
            border: 1px solid #e0e0e0;
            border-radius: 6px;
            background: #fff;
            font-size: 12px;
            color: #555;
            cursor: pointer;
            line-height: 1.4;
        }

        .toolbar select:focus { outline: none; border-color: #3544b1; }

        .toolbar-sep {
            width: 1px;
            height: 20px;
            background: #e0e0e0;
            margin: 0 2px;
            flex-shrink: 0;
        }

        /* ── notification ── */
        .notif {
            padding: 10px 14px;
            border-radius: 6px;
            font-size: 12px;
            margin-bottom: 10px;
            border: 1px solid;
        }

        .notif-success { background: #f0fdf4; border-color: #bbf7d0; color: #166534; }
        .notif-error   { background: #fef2f2; border-color: #fecaca; color: #991b1b; }
        .notif-info    { background: #eff6ff; border-color: #bfdbfe; color: #1e40af; }
        .notif-warning { background: #fffbeb; border-color: #fde68a; color: #92400e; }

        .update-banner {
            display: flex;
            align-items: center;
            flex-wrap: wrap;
            gap: 8px;
            padding: 10px 14px;
            margin-bottom: 14px;
            border-radius: 6px;
            border: 1px solid #c7d2fe;
            background: #eef2ff;
            color: #3730a3;
            font-size: 12px;
        }

        .update-banner code {
            padding: 2px 6px;
            background: #fff;
            border: 1px solid #e0e7ff;
            border-radius: 4px;
            font-size: 11px;
            font-family: 'Monaco', 'Courier New', monospace;
        }

        .update-banner a {
            color: #3730a3;
            font-weight: 600;
            text-decoration: underline;
        }

        /* ── bulk bar ── */
        .bulk-bar {
            display: flex;
            align-items: center;
            gap: 8px;
            flex-wrap: wrap;
            padding: 8px 12px;
            background: #eef0fb;
            border: 1px solid #c5caee;
            border-radius: 6px;
            margin-bottom: 10px;
        }

        .bulk-bar strong {
            font-size: 12px;
            color: #3544b1;
            margin-right: 4px;
        }

        /* ── tabs ── */
        .tabs {
            display: flex;
            border-bottom: 1px solid #e9e9e9;
        }

        .tab-btn {
            display: inline-flex;
            align-items: center;
            gap: 6px;
            padding: 10px 16px;
            font-size: 13px;
            font-weight: 500;
            color: #888;
            cursor: pointer;
            border: none;
            background: none;
            border-bottom: 2px solid transparent;
            margin-bottom: -1px;
            transition: color 0.1s;
        }

        .tab-btn.active {
            color: #3544b1;
            border-bottom-color: #3544b1;
        }

        .tab-count {
            display: inline-flex;
            align-items: center;
            justify-content: center;
            min-width: 18px;
            height: 16px;
            padding: 0 5px;
            border-radius: 8px;
            font-size: 10px;
            font-weight: 600;
            background: #eef0fb;
            color: #3544b1;
        }

        .tab-count.danger {
            background: #fee2e2;
            color: #991b1b;
        }

        /* ── overview tab ── */
        .stat-cards {
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(140px, 1fr));
            gap: 12px;
            margin-bottom: 22px;
        }

        .stat-card {
            display: flex;
            flex-direction: column;
            gap: 4px;
            padding: 14px 16px;
            border: 1px solid #e9e9e9;
            border-radius: 8px;
            background: #fafbff;
        }

        .stat-value {
            font-size: 24px;
            font-weight: 700;
            color: #1b264f;
            line-height: 1.1;
        }

        .stat-label {
            font-size: 12px;
            color: #888;
        }

        .stat-section {
            margin-bottom: 24px;
        }

        .stat-section h3 {
            margin: 0 0 4px;
            font-size: 14px;
            font-weight: 600;
            color: #1b264f;
        }

        .stat-section-hint {
            margin: 0 0 10px;
            font-size: 12px;
            color: #888;
        }

        /* ── table ── */
        .table-wrapper {
            overflow-x: auto;
            -webkit-overflow-scrolling: touch;
            border: 1px solid #e9e9e9;
            border-radius: 8px;
            margin-top: 1px;
        }

        table {
            width: 100%;
            min-width: 900px;
            border-collapse: separate;
            border-spacing: 0;
            background: white;
        }

        th {
            padding: 9px 14px;
            text-align: left;
            font-size: 11px;
            font-weight: 600;
            color: #888;
            background: #fafafa;
            border-bottom: 1px solid #e9e9e9;
            white-space: nowrap;
            text-transform: uppercase;
            letter-spacing: 0.04em;
            position: sticky;
            top: 0;
            z-index: 1;
        }

        th.sortable {
            cursor: pointer;
            user-select: none;
        }

        th.sortable:hover {
            color: #555;
        }

        .sort-indicator {
            display: inline-block;
            width: 10px;
            margin-left: 2px;
            font-size: 9px;
            color: #4a6fdc;
        }

        th.center, td.center { text-align: center; }

        td {
            padding: 10px 14px;
            border-bottom: 1px solid #f0f0f0;
            vertical-align: middle;
            font-size: 12px;
            color: #333;
        }

        tbody tr:last-child td { border-bottom: none; }
        tbody tr:hover { background: #fafbff; }
        tbody tr.row-selected { background: #f0f2fc; }

        .url-cell { max-width: 200px; }

        .url-val {
            display: block;
            font-family: 'Monaco', 'Courier New', monospace;
            font-size: 11px;
            color: #333;
            white-space: nowrap;
            overflow: hidden;
            text-overflow: ellipsis;
            max-width: 200px;
        }

        .url-copy-wrap {
            display: flex;
            flex-direction: column;
            align-items: flex-start;
            gap: 3px;
        }

        .notes-val {
            display: block;
            max-width: 160px;
            white-space: nowrap;
            overflow: hidden;
            text-overflow: ellipsis;
            font-size: 11px;
            color: #888;
        }

        .domain-pill {
            display: inline-flex;
            align-items: center;
            gap: 4px;
            padding: 2px 8px;
            border-radius: 10px;
            background: #f5f5f5;
            border: 1px solid #e8e8e8;
            font-size: 11px;
            color: #555;
            white-space: nowrap;
        }

        .domain-pill.all-domains {
            background: transparent;
            border: none;
            color: #bbb;
            font-style: italic;
            padding-left: 0;
        }

        .type-pill {
            display: inline-block;
            padding: 2px 7px;
            border-radius: 4px;
            font-size: 10px;
            font-weight: 600;
            background: #f5f5f5;
            border: 1px solid #e0e0e0;
            color: #666;
        }

        .type-pill.regex {
            background: #f3f0fe;
            border-color: #d0c8f7;
            color: #5b21b6;
        }

        .type-pill.wildcard {
            background: #fef9c3;
            border-color: #fde047;
            color: #854d0e;
        }

        .type-pill.ab-pill {
            background: #fff7ed;
            border-color: #fed7aa;
            color: #9a3412;
            margin-left: 4px;
        }

        .active-indicator {
            display: inline-flex;
            align-items: center;
            gap: 5px;
            font-size: 12px;
        }

        .status-dot {
            width: 6px;
            height: 6px;
            border-radius: 50%;
            flex-shrink: 0;
        }

        .status-dot.active   { background: #2bc37b; }
        .status-dot.inactive { background: #d42054; }

        .schedule-badge {
            display: inline-block;
            margin-left: 5px;
            padding: 1px 6px;
            border-radius: 4px;
            font-size: 10px;
            font-weight: 600;
        }

        .schedule-badge.scheduled { background: #dbeafe; color: #1e40af; }
        .schedule-badge.expired   { background: #f3f4f6; color: #6b7280; }

        .hit-count {
            font-size: 12px;
            color: #ccc;
        }

        .hit-count.has-hits {
            color: #333;
            font-weight: 600;
        }

        .act-group {
            display: flex;
            align-items: center;
            gap: 4px;
            justify-content: flex-end;
        }

        /* ── empty / loading ── */
        .empty, .loading {
            padding: 48px 20px;
            text-align: center;
            color: #aaa;
            font-size: 13px;
        }

        input[type="checkbox"] {
            width: 14px;
            height: 14px;
            accent-color: #3544b1;
            cursor: pointer;
        }
    `;

    static get modalInlineStyles() {
        return `
            .redirect-manager-modal-root .modal-overlay {
                position: fixed; top: 0; left: 0; right: 0; bottom: 0;
                background: rgba(0,0,0,0.45); display: flex; align-items: center; justify-content: center;
                z-index: 1000; padding: 20px; overflow-y: auto;
            }
            .redirect-manager-modal-root .modal {
                background: #fff; border-radius: 12px; width: 560px;
                max-width: calc(100vw - 40px); max-height: calc(100vh - 40px); overflow-y: auto;
                box-shadow: 0 16px 48px rgba(0,0,0,0.14); margin: auto;
                display: flex; flex-direction: column;
                font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            }
            .redirect-manager-modal-root .modal-header {
                display: flex; align-items: center; justify-content: space-between;
                padding: 18px 22px 16px; border-bottom: 1px solid #f0f0f0;
            }
            .redirect-manager-modal-root .modal-header h2 {
                margin: 0; font-size: 15px; font-weight: 600; color: #1b264f; letter-spacing: -0.01em;
            }
            .redirect-manager-modal-root .btn-close {
                background: none; border: none; color: #aaa; cursor: pointer; font-size: 15px;
                padding: 4px 6px; border-radius: 4px; line-height: 1; display: flex; align-items: center;
            }
            .redirect-manager-modal-root .btn-close:hover { background: #f5f5f5; color: #555; }
            .redirect-manager-modal-root .form-body {
                padding: 20px 22px; display: flex; flex-direction: column; gap: 0;
            }
            .redirect-manager-modal-root .form-group { margin-bottom: 16px; }
            .redirect-manager-modal-root .form-group:last-child { margin-bottom: 0; }
            .redirect-manager-modal-root .form-group label {
                display: block; margin-bottom: 6px; font-size: 10px; font-weight: 600;
                color: #888; text-transform: uppercase; letter-spacing: 0.06em;
            }
            .redirect-manager-modal-root .form-group .lbl-opt {
                font-weight: 400; text-transform: none; letter-spacing: 0; color: #bbb; font-size: 10px;
            }
            .redirect-manager-modal-root .form-group .req { color: #d42054; }
            .redirect-manager-modal-root .form-group input[type="text"],
            .redirect-manager-modal-root .form-group input[type="datetime-local"],
            .redirect-manager-modal-root .form-group select,
            .redirect-manager-modal-root .form-group textarea {
                width: 100%; padding: 9px 11px; border: 1px solid #e0e0e0; border-radius: 6px;
                font-size: 13px; font-family: inherit; box-sizing: border-box; color: #333;
                transition: border-color 0.15s ease, box-shadow 0.15s ease; background: #fff;
            }
            .redirect-manager-modal-root .form-group input[type="text"]:focus,
            .redirect-manager-modal-root .form-group input[type="datetime-local"]:focus,
            .redirect-manager-modal-root .form-group select:focus,
            .redirect-manager-modal-root .form-group textarea:focus {
                outline: none; border-color: #3544b1; box-shadow: 0 0 0 3px rgba(53,68,177,0.12); background: #fafbff;
            }
            .redirect-manager-modal-root .form-group input[type="text"]::placeholder,
            .redirect-manager-modal-root .form-group textarea::placeholder { color: #bbb; }
            .redirect-manager-modal-root .form-group input[type="datetime-local"] {
                cursor: text; color-scheme: light;
            }
            .redirect-manager-modal-root .form-group input[type="datetime-local"]::-webkit-calendar-picker-indicator {
                cursor: pointer; opacity: 0.5; border-radius: 4px; padding: 3px; margin-left: 4px;
                transition: opacity 0.15s ease, background-color 0.15s ease;
            }
            .redirect-manager-modal-root .form-group input[type="datetime-local"]::-webkit-calendar-picker-indicator:hover {
                opacity: 1; background-color: rgba(53,68,177,0.1);
            }
            .redirect-manager-modal-root .form-group select {
                cursor: pointer; appearance: none; -webkit-appearance: none; -moz-appearance: none;
                background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 20 20' fill='none' stroke='%23888888' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'%3E%3Cpath d='M5 7l5 5 5-5'/%3E%3C/svg%3E");
                background-repeat: no-repeat; background-position: right 12px center; background-size: 12px;
            }
            .redirect-manager-modal-root .form-group select:focus {
                background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 20 20' fill='none' stroke='%233544b1' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'%3E%3Cpath d='M5 7l5 5 5-5'/%3E%3C/svg%3E");
            }
            .redirect-manager-modal-root .form-group textarea { resize: vertical; min-height: 72px; }
            .redirect-manager-modal-root .form-group small {
                display: block; margin-top: 5px; color: #999; font-size: 11px; line-height: 1.5;
            }
            .redirect-manager-modal-root .form-group small code {
                padding: 1px 5px; background: #f3f4f6; border-radius: 3px;
                font-size: 10px; color: #374151; font-family: 'Monaco','Courier New',monospace;
            }
            .redirect-manager-modal-root .form-row {
                display: flex; gap: 14px;
            }
            .redirect-manager-modal-root .form-row .form-group { flex: 1; }
            @media (max-width: 540px) {
                .redirect-manager-modal-root .form-row { flex-direction: column; }
            }
            .redirect-manager-modal-root .status-select-wrap { position: relative; }
            .redirect-manager-modal-root .status-select-wrap select {
                padding-right: 112px; background-position: right 86px center;
            }
            .redirect-manager-modal-root .status-float-badge {
                position: absolute; right: 10px; top: 50%; transform: translateY(-50%);
                padding: 2px 8px; border-radius: 4px; font-size: 11px; font-weight: 600;
                pointer-events: none;
            }
            .redirect-manager-modal-root .status-float-badge.status-301 { background: #d1fae5; color: #065f46; }
            .redirect-manager-modal-root .status-float-badge.status-302 { background: #fef3c7; color: #92400e; }
            .redirect-manager-modal-root .status-float-badge.status-404 { background: #fee2e2; color: #991b1b; }
            .redirect-manager-modal-root .status-float-badge.status-410 { background: #e5e7eb; color: #374151; }
            .redirect-manager-modal-root .toggle-row {
                display: flex; align-items: center; justify-content: space-between; gap: 16px;
                padding: 10px 12px; background: #fafafa; border-radius: 8px; border: 1px solid #efefef;
            }
            .redirect-manager-modal-root .toggle-label {
                flex: 1; margin: 0; cursor: pointer; font-size: 12px; font-weight: 500; color: #333;
                text-transform: none; letter-spacing: 0;
            }
            .redirect-manager-modal-root .toggle-hint {
                font-weight: 400; color: #999; font-size: 11px;
            }
            .redirect-manager-modal-root .toggle-switch {
                position: relative; display: inline-block; width: 40px; height: 22px; flex-shrink: 0;
            }
            .redirect-manager-modal-root .toggle-switch input { opacity: 0; width: 0; height: 0; }
            .redirect-manager-modal-root .toggle-slider {
                position: absolute; cursor: pointer; top: 0; left: 0; right: 0; bottom: 0;
                background-color: #d1d5db; transition: 0.25s; border-radius: 22px;
            }
            .redirect-manager-modal-root .toggle-slider:before {
                position: absolute; content: ""; height: 16px; width: 16px; left: 3px; bottom: 3px;
                background-color: white; transition: 0.25s; border-radius: 50%; box-shadow: 0 1px 3px rgba(0,0,0,.2);
            }
            .redirect-manager-modal-root .toggle-switch input:checked + .toggle-slider { background-color: #2bc37b; }
            .redirect-manager-modal-root .toggle-switch input:checked + .toggle-slider:before { transform: translateX(18px); }
            .redirect-manager-modal-root .modal-footer {
                display: flex; justify-content: flex-end; gap: 8px;
                padding: 14px 22px; border-top: 1px solid #f0f0f0;
            }
            .redirect-manager-modal-root .btn-cancel {
                padding: 8px 16px; border: 1px solid #e0e0e0; border-radius: 6px;
                background: #fff; color: #666; font-size: 13px; font-weight: 500; cursor: pointer;
            }
            .redirect-manager-modal-root .btn-cancel:hover { background: #f5f5f5; }
            .redirect-manager-modal-root .btn-save {
                padding: 8px 20px; border: none; border-radius: 6px;
                background: #2bc37b; color: #fff; font-size: 13px; font-weight: 600; cursor: pointer;
            }
            .redirect-manager-modal-root .btn-save:hover { background: #25a866; }
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
        this.telemetryEnabled = false;
        this.telemetryLoading = false;
        this.telemetryDecided = true;
        this.showTelemetryPrompt = false;
        this.updateAvailable = false;
        this.currentVersion = '';
        this.latestVersion = '';
        this.redirectsSort = { column: null, direction: 'asc', type: 'string' };
        this.missedSort = { column: null, direction: 'asc', type: 'string' };
        this.topRedirectsSort = { column: null, direction: 'asc', type: 'string' };
        this.staleRedirectsSort = { column: null, direction: 'asc', type: 'string' };
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

    sortRows(rows, column, direction, type) {
        const sign = direction === 'asc' ? 1 : -1;
        return [...rows].sort((a, b) => {
            let av = a[column];
            let bv = b[column];
            if (type === 'date') {
                av = av ? new Date(av).getTime() : 0;
                bv = bv ? new Date(bv).getTime() : 0;
                return sign * (av - bv);
            }
            if (type === 'number') {
                av = Number(av) || 0;
                bv = Number(bv) || 0;
                return sign * (av - bv);
            }
            av = (av ?? '').toString().toLowerCase();
            bv = (bv ?? '').toString().toLowerCase();
            return sign * (av < bv ? -1 : (av > bv ? 1 : 0));
        });
    }

    onSortClick(stateProp, column, type) {
        const state = this[stateProp];
        const direction = (state.column === column && state.direction === 'asc') ? 'desc' : 'asc';
        this[stateProp] = { column, direction, type };
    }

    sortIndicator(stateProp, column) {
        const state = this[stateProp];
        if (state.column !== column) {
            return '';
        }
        return state.direction === 'asc' ? '▲' : '▼';
    }

    get sortedRedirects() {
        const { column, direction, type } = this.redirectsSort;
        return column ? this.sortRows(this.redirects, column, direction, type) : this.redirects;
    }

    get sortedMissedRequests() {
        const { column, direction, type } = this.missedSort;
        return column ? this.sortRows(this.missedRequests, column, direction, type) : this.missedRequests;
    }

    get sortedTopRedirects() {
        const { column, direction, type } = this.topRedirectsSort;
        const rows = this.stats?.topRedirects || [];
        return column ? this.sortRows(rows, column, direction, type) : rows;
    }

    get sortedStaleRedirects() {
        const { column, direction, type } = this.staleRedirectsSort;
        const rows = this.stats?.staleRedirects || [];
        return column ? this.sortRows(rows, column, direction, type) : rows;
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
        this.loadStats();
        this.loadTelemetryStatus();
        this.pingTelemetry();
        this.loadUpdateStatus();
    }

    /** Opt-in usage ping (no-op if telemetry is disabled/unconfigured server-side); never blocks dashboard load. */
    pingTelemetry() {
        this.authFetch('/umbraco/api/redirectmanager/telemetry/ping', { method: 'POST' }).catch(() => {});
    }

    /** Always-on update-availability check (no opt-in — no site data is sent, only a public NuGet.org listing is read). */
    async loadUpdateStatus() {
        try {
            const response = await this.authFetch('/umbraco/api/redirectmanager/update-status');
            if (response.ok) {
                const result = await response.json();
                this.updateAvailable = !!result.updateAvailable;
                this.currentVersion = result.currentVersion || '';
                this.latestVersion = result.latestVersion || '';
            }
        } catch (error) {
            console.error('Failed to load update status:', error);
        }
    }

    async loadTelemetryStatus() {
        try {
            const response = await this.authFetch('/umbraco/api/redirectmanager/telemetry/status');
            if (response.ok) {
                const result = await response.json();
                this.telemetryEnabled = !!result.enabled;
                this.telemetryDecided = !!result.decided;
                this.showTelemetryPrompt = !this.telemetryDecided;
            }
        } catch (error) {
            console.error('Failed to load telemetry status:', error);
        }
    }

    async setTelemetryEnabled(enabled) {
        this.telemetryLoading = true;
        try {
            const response = await this.authFetch(
                `/umbraco/api/redirectmanager/telemetry/${enabled ? 'enable' : 'disable'}`,
                { method: 'POST' }
            );
            if (response.ok) {
                this.telemetryEnabled = enabled;
                this.telemetryDecided = true;
                this.showTelemetryPrompt = false;
            } else {
                this.showMessage('Failed to update telemetry setting', 'error');
            }
        } catch (error) {
            this.showMessage('Failed to update telemetry setting', 'error');
        }
        this.telemetryLoading = false;
    }

    toggleTelemetryEnabled(e) {
        this.setTelemetryEnabled(e.target.checked);
    }

    acceptTelemetryPrompt() {
        this.setTelemetryEnabled(true);
    }

    declineTelemetryPrompt() {
        this.setTelemetryEnabled(false);
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
            culture: '',
            description: '',
            statusCode: 301,
            isActive: true,
            isRegex: false,
            abTestEnabled: false,
            variantBUrl: '',
            variantBWeight: 50,
            preserveQueryString: false,
            validFrom: '',
            validUntil: ''
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

    async loadStats() {
        this.statsLoading = true;
        try {
            const response = await this.authFetch('/umbraco/api/redirectmanager/stats');
            if (response.ok) {
                this.stats = await response.json();
            }
        } catch (error) {
            console.error('Failed to load stats:', error);
        }
        this.statsLoading = false;
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
            culture: redirect.culture || '',
            description: redirect.description || '',
            statusCode: redirect.statusCode,
            isActive: redirect.isActive,
            isRegex: !!redirect.isRegex,
            abTestEnabled: !!redirect.variantBUrl,
            variantBUrl: redirect.variantBUrl || '',
            variantBWeight: redirect.variantBWeight ?? 50,
            preserveQueryString: !!redirect.preserveQueryString,
            validFrom: this.toDatetimeLocalValue(redirect.validFrom),
            validUntil: this.toDatetimeLocalValue(redirect.validUntil)
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
        const isIntField = name === 'statusCode' || name === 'variantBWeight';
        this.formData = {
            ...this.formData,
            [name]: type === 'checkbox' ? checked : (isIntField ? parseInt(value, 10) : value)
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
                // Reload from the server rather than optimistically patching isActive
                // client-side: the bulk endpoint also stamps ModifiedBy/UpdatedDate on
                // every affected row, and the client has no way to fabricate those
                // values (it doesn't know the current user's display name), so an
                // optimistic patch would leave the audit tooltip showing stale data
                // until the next manual reload.
                await this.loadRedirects();
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

    // A plain navigation (window.location.href) can't carry an Authorization
    // header, so the file has to be fetched (with the bearer token attached
    // via authFetch) and then handed to the browser as a Blob download.
    async downloadFile(url, fileName, errorMessage) {
        try {
            const response = await this.authFetch(url);
            if (!response.ok) {
                this.showMessage(errorMessage, 'error');
                return;
            }

            const blob = await response.blob();
            const objectUrl = URL.createObjectURL(blob);
            const link = document.createElement('a');
            link.href = objectUrl;
            link.download = fileName;
            // Umbraco's backoffice router installs a global click listener that
            // turns same-origin anchor clicks into SPA navigations via
            // history.pushState — which throws a SecurityError for blob: URLs
            // (they have an opaque origin and can't be used as a history entry),
            // aborting the click and silently killing the download. data-router-slot
            //="disabled" is the router's own documented escape hatch for exactly
            // this case (see packages/core/router/router-slot/util/anchor.ts in
            // Umbraco-CMS): it makes the router skip this anchor entirely so the
            // browser handles the click as a normal download instead.
            link.dataset.routerSlot = 'disabled';
            document.body.appendChild(link);
            link.click();
            document.body.removeChild(link);
            URL.revokeObjectURL(objectUrl);
        } catch (error) {
            console.error(errorMessage, error);
            this.showMessage(errorMessage, 'error');
        }
    }

    async exportCsv() {
        await this.downloadFile(
            `/umbraco/api/redirectmanager/export${this.buildQueryParams()}`,
            'redirects.csv',
            'Failed to export CSV');
    }

    async exportStats() {
        await this.downloadFile('/umbraco/api/redirectmanager/stats/export', 'redirect-overview.csv', 'Failed to export overview');
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

        if (!this.formData.isRegex && (this.formData.oldUrl.match(/\*/g) || []).length > 1) {
            this.showMessage('Old URL can only contain one wildcard (*)', 'error');
            return;
        }

        if ((this.formData.statusCode === 301 || this.formData.statusCode === 302) && !this.formData.newUrl) {
            this.showMessage('New URL is required for redirect status codes', 'error');
            return;
        }

        if (!this.formData.isRegex && (this.formData.newUrl.match(/\*/g) || []).length > 1) {
            this.showMessage('New URL can only contain one wildcard (*)', 'error');
            return;
        }

        if (this.formData.abTestEnabled && !this.formData.variantBUrl) {
            this.showMessage('Variant B URL is required when A/B test is enabled', 'error');
            return;
        }

        if (this.formData.validFrom && this.formData.validUntil && new Date(this.formData.validUntil) < new Date(this.formData.validFrom)) {
            this.showMessage('Valid until must be after Valid from', 'error');
            return;
        }

        const payload = {
            ...this.formData,
            variantBUrl: this.formData.abTestEnabled ? this.formData.variantBUrl : null,
            variantBWeight: this.formData.abTestEnabled ? this.formData.variantBWeight : null,
            validFrom: this.fromDatetimeLocalValue(this.formData.validFrom),
            validUntil: this.fromDatetimeLocalValue(this.formData.validUntil)
        };

        try {
            let response;
            if (this.editingRedirect) {
                response = await this.authFetch(`/umbraco/api/redirectmanager/update/${this.editingRedirect.id}`, {
                    method: 'PUT',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(payload)
                });
            } else {
                response = await this.authFetch('/umbraco/api/redirectmanager/create', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(payload)
                });
            }

            if (response.ok) {
                const saved = await response.json();
                const overlapNote = (saved.overlapWarnings && saved.overlapWarnings.length > 0)
                    ? ` Heads up: this rule also matches existing active rule(s): ${saved.overlapWarnings.join(', ')}`
                    : '';

                if (this.editingRedirect) {
                    this.redirects = this.redirects.map(r => r.id === saved.id ? saved : r);
                    this.showMessage(`Redirect updated.${overlapNote}`, overlapNote ? 'warning' : 'success');
                } else {
                    this.redirects = [saved, ...this.redirects];
                    this.showMessage(`Redirect created.${overlapNote}`, overlapNote ? 'warning' : 'success');
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

    getStatusBadgeLabel(code) {
        const map = { 301: 'Permanent', 302: 'Temporary', 404: 'Not found', 410: 'Gone' };
        return map[code] || String(code);
    }

    getLastHitTitle(redirect) {
        return redirect.lastHitDate
            ? `Last hit: ${new Date(redirect.lastHitDate).toLocaleString()}`
            : 'Never hit';
    }

    getAuditTitle(redirect) {
        const created = redirect.createdDate ? new Date(redirect.createdDate).toLocaleString() : null;
        const modified = redirect.updatedDate ? new Date(redirect.updatedDate).toLocaleString() : null;

        const createdPart = created ? `Created${redirect.createdBy ? ` by ${redirect.createdBy}` : ''} on ${created}` : '';
        const modifiedPart = modified ? `Last modified${redirect.modifiedBy ? ` by ${redirect.modifiedBy}` : ''} on ${modified}` : '';

        return [createdPart, modifiedPart].filter(Boolean).join(' · ');
    }

    // Converts a stored UTC ISO string (or null) into the local-time string
    // an <input type="datetime-local"> expects (no timezone designator,
    // minute precision). Returns '' for null/invalid input, which the input
    // renders as empty (no date selected).
    toDatetimeLocalValue(isoString) {
        if (!isoString) return '';
        const d = new Date(isoString);
        if (Number.isNaN(d.getTime())) return '';
        const pad = (n) => String(n).padStart(2, '0');
        return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
    }

    // Converts a <input type="datetime-local"> value (interpreted by the
    // browser/JS Date constructor as local time, since it has no timezone
    // designator) into a UTC ISO string for the API. Returns null for
    // blank/invalid input.
    fromDatetimeLocalValue(localValue) {
        if (!localValue) return null;
        const d = new Date(localValue);
        if (Number.isNaN(d.getTime())) return null;
        return d.toISOString();
    }

    getScheduleBadge(redirect) {
        const now = new Date();
        if (redirect.validFrom && new Date(redirect.validFrom) > now) return 'Scheduled';
        if (redirect.validUntil && new Date(redirect.validUntil) < now) return 'Expired';
        return null;
    }

    getMatchTypeLabel(redirect) {
        if (redirect.isRegex) return 'Regex';
        if (redirect.oldUrl && redirect.oldUrl.includes('*')) return 'Wildcard';
        return 'Exact';
    }

    getMissedRequestTitle(item) {
        return `First seen: ${new Date(item.firstSeenDate).toLocaleString()}`;
    }

    render() {
        return html`
            <!-- Page header -->
            <div class="page-header">
                <div>
                    <h1>Redirect Manager</h1>
                    <p>Centrally manage URL redirects for your site.</p>
                </div>
                <button class="btn btn-primary" @click=${() => this.openAddModal()}>
                    + Add redirect
                </button>
            </div>

            <!-- Update-available banner: unconditional while outdated, no dismiss/close affordance -->
            ${this.updateAvailable ? html`
                <div class="update-banner">
                    A new version is available: <strong>${this.latestVersion}</strong>
                    (you're currently on ${this.currentVersion}).
                    <code>dotnet add package BT.RedirectManager --version ${this.latestVersion}</code>
                    <a href="https://www.nuget.org/packages/BT.RedirectManager" target="_blank" rel="noopener">View on NuGet</a>
                </div>
            ` : ''}

            <!-- Status legend -->
            <div class="status-legend">
                <div class="legend-item">
                    <span class="status-badge status-301">301</span>
                    <span class="legend-lbl">Permanent</span>
                </div>
                <div class="legend-item">
                    <span class="status-badge status-302">302</span>
                    <span class="legend-lbl">Temporary</span>
                </div>
                <div class="legend-item">
                    <span class="status-badge status-404">404</span>
                    <span class="legend-lbl">Not found</span>
                </div>
                <div class="legend-item">
                    <span class="status-badge status-410">410</span>
                    <span class="legend-lbl">Gone</span>
                </div>
            </div>

            <!-- Notification -->
            ${this.messageText ? html`
                <div class="notif notif-${this.messageType}">${this.messageText}</div>
            ` : ''}

            <!-- Toolbar -->
            <div class="toolbar">
                <div class="search-wrap">
                    <span class="search-icon">
                        <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
                            <circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/>
                        </svg>
                    </span>
                    <input type="text"
                           placeholder="Search old/new URL"
                           .value=${this.query}
                           @input=${(e) => { this.query = e.target.value; }} />
                </div>
                <select .value=${this.statusFilter} @change=${(e) => { this.statusFilter = e.target.value; }}>
                    <option value="">All status</option>
                    <option value="301">301</option>
                    <option value="302">302</option>
                    <option value="404">404</option>
                    <option value="410">410</option>
                </select>
                <select .value=${this.activeFilter} @change=${(e) => { this.activeFilter = e.target.value; }}>
                    <option value="">Active &amp; inactive</option>
                    <option value="true">Active only</option>
                    <option value="false">Inactive only</option>
                </select>
                <select .value=${this.regexFilter} @change=${(e) => { this.regexFilter = e.target.value; }}>
                    <option value="">All types</option>
                    <option value="false">Exact</option>
                    <option value="true">Regex</option>
                </select>
                <button class="btn btn-apply" @click=${this.applyFilters}>Apply</button>
                <button class="btn" @click=${this.clearFilters}>Clear</button>
                <span class="toolbar-sep"></span>
                <button class="btn" @click=${this.exportCsv}>Export CSV</button>
                <button class="btn" ?disabled=${this.importInProgress} @click=${this.triggerImport}>
                    ${this.importInProgress ? 'Importing...' : 'Import CSV'}
                </button>
                <input id="importFileInput" type="file" accept=".csv,text/csv" style="display:none" @change=${this.handleImportFile} />
            </div>

            <!-- Bulk action bar (only when items are selected) -->
            ${this.anySelected ? html`
                <div class="bulk-bar">
                    <strong>${this.selectedIds.length} selected</strong>
                    <button class="btn btn-sm" @click=${() => this.bulkSetActiveSelected(true)}>Activate</button>
                    <button class="btn btn-sm" @click=${() => this.bulkSetActiveSelected(false)}>Deactivate</button>
                    <button class="btn btn-sm btn-danger" @click=${this.bulkDeleteSelected}>Delete</button>
                </div>
            ` : ''}

            <!-- Tabs -->
            <div class="tabs">
                <button class="tab-btn ${this.activeTab === 'redirects' ? 'active' : ''}"
                        @click=${() => { this.activeTab = 'redirects'; }}>
                    Redirects
                    <span class="tab-count">${this.redirects.length}</span>
                </button>
                <button class="tab-btn ${this.activeTab === 'missed' ? 'active' : ''}"
                        @click=${() => { this.activeTab = 'missed'; }}>
                    404 log
                    ${this.missedRequests.length > 0 ? html`
                        <span class="tab-count danger">${this.missedRequests.length}</span>
                    ` : ''}
                </button>
                <button class="tab-btn ${this.activeTab === 'stats' ? 'active' : ''}"
                        @click=${() => { this.activeTab = 'stats'; }}>
                    Overview
                </button>
            </div>

            <!-- Redirects tab -->
            ${this.activeTab === 'redirects' ? html`
                ${this.loading ? html`
                    <div class="loading">Loading redirects...</div>
                ` : this.redirects.length === 0 ? html`
                    <div class="empty">No redirects found. Click "Add redirect" to create one.</div>
                ` : html`
                    <div class="table-wrapper">
                        <table>
                            <thead>
                                <tr>
                                    <th style="width:36px;">
                                        <input type="checkbox" .checked=${this.allSelected} @change=${this.toggleSelectAll} />
                                    </th>
                                    <th style="width:60px;" class="center">Status</th>
                                    <th>Old URL</th>
                                    <th>New URL</th>
                                    <th>Domain</th>
                                    <th>Culture</th>
                                    <th>Notes</th>
                                    <th class="center">Match</th>
                                    <th class="center">Active</th>
                                    <th class="center" title="Hit count">Hits</th>
                                    <th class="center" title="Hits in the last 7 days">7d</th>
                                    <th class="center" title="Hits in the last 30 days">30d</th>
                                    <th></th>
                                </tr>
                            </thead>
                            <tbody>
                                ${this.redirects.map(redirect => html`
                                    <tr class="${this.selectedIds.includes(redirect.id) ? 'row-selected' : ''}" title="${this.getAuditTitle(redirect)}">
                                        <td>
                                            <input type="checkbox"
                                                   .checked=${this.selectedIds.includes(redirect.id)}
                                                   @change=${(e) => this.toggleSelectId(redirect.id, e.target.checked)} />
                                        </td>
                                        <td class="center">
                                            <span class="status-badge status-${redirect.statusCode}">
                                                ${redirect.statusCode}
                                            </span>
                                        </td>
                                        <td class="url-cell" title="${redirect.oldUrl}">
                                            <div class="url-copy-wrap">
                                                <span class="url-val">${redirect.oldUrl}</span>
                                                <button class="btn btn-sm" @click=${() => this.copyToClipboard(redirect.oldUrl)}>Copy</button>
                                            </div>
                                        </td>
                                        <td class="url-cell" title="${redirect.newUrl || ''}">
                                            ${redirect.newUrl ? html`
                                                <div class="url-copy-wrap">
                                                    <span class="url-val">${redirect.newUrl}</span>
                                                    <button class="btn btn-sm" @click=${() => this.copyToClipboard(redirect.newUrl)}>Copy</button>
                                                </div>
                                            ` : html`<span style="color:#ccc;">—</span>`}
                                        </td>
                                        <td>
                                            ${redirect.domain
                                                ? html`<span class="domain-pill">${redirect.domain}</span>`
                                                : html`<span class="domain-pill all-domains">All domains</span>`}
                                        </td>
                                        <td>
                                            ${redirect.culture
                                                ? html`<span class="domain-pill">${redirect.culture}</span>`
                                                : html`<span class="domain-pill all-domains">All cultures</span>`}
                                        </td>
                                        <td title="${redirect.description || ''}">
                                            <span class="notes-val">${redirect.description || '—'}</span>
                                        </td>
                                        <td class="center">
                                            <span class="type-pill ${redirect.isRegex ? 'regex' : (redirect.oldUrl && redirect.oldUrl.includes('*') ? 'wildcard' : '')}">
                                                ${this.getMatchTypeLabel(redirect)}
                                            </span>
                                            ${redirect.variantBUrl ? html`
                                                <span class="type-pill ab-pill"
                                                      title="A/B test — A: ${(redirect.hitCount || 0).toLocaleString()} hits, B: ${(redirect.variantBHitCount || 0).toLocaleString()} hits (${redirect.variantBWeight}% to B)">
                                                    A/B
                                                </span>
                                            ` : ''}
                                        </td>
                                        <td class="center">
                                            <span class="active-indicator">
                                                <span class="status-dot ${redirect.isActive ? 'active' : 'inactive'}"></span>
                                                ${redirect.isActive ? 'Yes' : 'No'}
                                            </span>
                                            ${this.getScheduleBadge(redirect) ? html`
                                                <span class="schedule-badge ${this.getScheduleBadge(redirect) === 'Scheduled' ? 'scheduled' : 'expired'}">
                                                    ${this.getScheduleBadge(redirect)}
                                                </span>
                                            ` : ''}
                                        </td>
                                        <td class="center" title="${this.getLastHitTitle(redirect)}">
                                            <span class="hit-count ${(redirect.hitCount || 0) > 0 ? 'has-hits' : ''}">
                                                ${(redirect.hitCount || 0).toLocaleString()}
                                            </span>
                                        </td>
                                        <td class="center">
                                            <span class="hit-count ${(redirect.hits7d || 0) > 0 ? 'has-hits' : ''}">
                                                ${(redirect.hits7d || 0).toLocaleString()}
                                            </span>
                                        </td>
                                        <td class="center">
                                            <span class="hit-count ${(redirect.hits30d || 0) > 0 ? 'has-hits' : ''}">
                                                ${(redirect.hits30d || 0).toLocaleString()}
                                            </span>
                                        </td>
                                        <td>
                                            <div class="act-group">
                                                <button class="btn btn-sm" @click=${() => this.testRedirect(redirect.oldUrl)}>
                                                    Test
                                                </button>
                                                <button class="btn btn-sm btn-info" @click=${() => this.openEditModal(redirect)}>
                                                    Edit
                                                </button>
                                                <button class="btn btn-sm btn-danger" @click=${() => this.deleteRedirect(redirect)}>
                                                    Delete
                                                </button>
                                            </div>
                                        </td>
                                    </tr>
                                `)}
                            </tbody>
                        </table>
                    </div>
                `}
            ` : ''}

            <!-- 404 log tab -->
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
                                    <th>Path</th>
                                    <th class="center">Hits</th>
                                    <th class="center">First seen</th>
                                    <th class="center">Last seen</th>
                                    <th></th>
                                </tr>
                            </thead>
                            <tbody>
                                ${this.missedRequests.map(item => html`
                                    <tr>
                                        <td class="url-cell" title="${this.getMissedRequestTitle(item)}">
                                            <span class="url-val">${item.path}</span>
                                        </td>
                                        <td class="center">
                                            <span class="hit-count ${item.hitCount > 0 ? 'has-hits' : ''}">${item.hitCount}</span>
                                        </td>
                                        <td class="center" style="font-size:11px;color:#888;">
                                            ${new Date(item.firstSeenDate).toLocaleDateString()}
                                        </td>
                                        <td class="center" style="font-size:11px;color:#888;">
                                            ${new Date(item.lastSeenDate).toLocaleDateString()}
                                        </td>
                                        <td>
                                            <div class="act-group">
                                                <button class="btn btn-sm btn-success-sm" @click=${() => this.createRedirectFromMissed(item)}>
                                                    Create redirect
                                                </button>
                                                <button class="btn btn-sm btn-danger" @click=${() => this.dismissMissedRequest(item)}>
                                                    Dismiss
                                                </button>
                                            </div>
                                        </td>
                                    </tr>
                                `)}
                            </tbody>
                        </table>
                    </div>
                `}
            ` : ''}

            <!-- Overview tab -->
            ${this.activeTab === 'stats' ? html`
                ${this.statsLoading || !this.stats ? html`
                    <div class="loading">Loading overview...</div>
                ` : html`
                    <div style="display:flex; justify-content:flex-end; margin-bottom:12px;">
                        <button class="btn" @click=${this.exportStats}>Export overview (CSV)</button>
                    </div>
                    <div class="stat-cards">
                        <div class="stat-card">
                            <span class="stat-value">${this.stats.total}</span>
                            <span class="stat-label">Total redirects</span>
                        </div>
                        <div class="stat-card">
                            <span class="stat-value">${this.stats.active}</span>
                            <span class="stat-label">Active</span>
                        </div>
                        <div class="stat-card">
                            <span class="stat-value">${this.stats.inactive}</span>
                            <span class="stat-label">Inactive</span>
                        </div>
                        <div class="stat-card">
                            <span class="stat-value">${this.stats.staleRedirects.length}</span>
                            <span class="stat-label">0 hits in last 30d</span>
                        </div>
                    </div>

                    <div class="stat-section">
                        <h3>Top 10 most-used redirects</h3>
                        ${this.stats.topRedirects.length === 0 ? html`
                            <div class="empty">No redirects have any hits yet.</div>
                        ` : html`
                            <div class="table-wrapper">
                                <table>
                                    <thead>
                                        <tr>
                                            <th>Old URL</th>
                                            <th>New URL</th>
                                            <th class="center">Hits</th>
                                        </tr>
                                    </thead>
                                    <tbody>
                                        ${this.stats.topRedirects.map(r => html`
                                            <tr>
                                                <td class="url-cell" title="${r.oldUrl}"><span class="url-val">${r.oldUrl}</span></td>
                                                <td class="url-cell" title="${r.newUrl || ''}"><span class="url-val">${r.newUrl || '—'}</span></td>
                                                <td class="center"><span class="hit-count has-hits">${r.hitCount.toLocaleString()}</span></td>
                                            </tr>
                                        `)}
                                    </tbody>
                                </table>
                            </div>
                        `}
                    </div>

                    <div class="stat-section">
                        <h3>Active, but 0 hits in the last 30 days</h3>
                        <p class="stat-section-hint">Candidates for cleanup — or a sign a rule isn't firing when it should.</p>
                        ${this.stats.staleRedirects.length === 0 ? html`
                            <div class="empty">Every active redirect has been hit in the last 30 days.</div>
                        ` : html`
                            <div class="table-wrapper">
                                <table>
                                    <thead>
                                        <tr>
                                            <th>Old URL</th>
                                            <th>New URL</th>
                                            <th class="center">All-time hits</th>
                                            <th class="center">Last hit</th>
                                        </tr>
                                    </thead>
                                    <tbody>
                                        ${this.stats.staleRedirects.map(r => html`
                                            <tr>
                                                <td class="url-cell" title="${r.oldUrl}"><span class="url-val">${r.oldUrl}</span></td>
                                                <td class="url-cell" title="${r.newUrl || ''}"><span class="url-val">${r.newUrl || '—'}</span></td>
                                                <td class="center"><span class="hit-count">${r.hitCount.toLocaleString()}</span></td>
                                                <td class="center" style="font-size:11px;color:#888;">
                                                    ${r.lastHitDate ? new Date(r.lastHitDate).toLocaleDateString() : 'Never'}
                                                </td>
                                            </tr>
                                        `)}
                                    </tbody>
                                </table>
                            </div>
                        `}
                    </div>

                    <div class="stat-section">
                        <h3>Telemetry</h3>
                        <p class="stat-section-hint">
                            Opt-in. When enabled, sends a small anonymous ping (a random site ID,
                            this site's domain, and the plugin/Umbraco version — no redirect data,
                            no traffic, no IPs) so the plugin author can see it's still installed.
                            Off by default.
                        </p>
                        <label style="display:flex; align-items:center; gap:8px; cursor:pointer;">
                            <input type="checkbox"
                                   .checked=${this.telemetryEnabled}
                                   ?disabled=${this.telemetryLoading}
                                   @change=${this.toggleTelemetryEnabled} />
                            <span>Send anonymous usage data</span>
                        </label>
                    </div>
                `}
            ` : ''}

            <!-- Telemetry consent prompt (first dashboard load only, until accepted or declined) -->
            ${this.showTelemetryPrompt ? html`
                <div class="redirect-manager-modal-root">
                    <style>${RedirectManagerDashboard.modalInlineStyles}</style>
                    <div class="modal-overlay">
                        <div class="modal" style="max-width:420px;">
                            <div class="modal-header">
                                <h2>Help improve Redirect Manager?</h2>
                            </div>
                            <div class="form-body">
                                <p style="margin:0 0 16px;">
                                    Send a small anonymous ping (a random site ID, this site's domain,
                                    and the plugin/Umbraco version — no redirect data, no traffic, no
                                    IPs) so the author can see the plugin is still installed. You can
                                    change this anytime from the Overview tab.
                                </p>
                                <div style="display:flex; justify-content:flex-end; gap:8px;">
                                    <button class="btn" ?disabled=${this.telemetryLoading} @click=${this.declineTelemetryPrompt}>No thanks</button>
                                    <button class="btn btn-primary" ?disabled=${this.telemetryLoading} @click=${this.acceptTelemetryPrompt}>Yes, send anonymous data</button>
                                </div>
                            </div>
                        </div>
                    </div>
                </div>
            ` : ''}

            <!-- Modal -->
            ${this.showModal ? html`
                <div class="redirect-manager-modal-root">
                    <style>${RedirectManagerDashboard.modalInlineStyles}</style>
                    <div class="modal-overlay" @click=${(e) => e.target === e.currentTarget && this.closeModal()}>
                        <div class="modal" @click=${(e) => e.stopPropagation()}>
                            <div class="modal-header">
                                <h2>${this.editingRedirect ? 'Edit redirect' : 'Add redirect'}</h2>
                                <button class="btn-close" @click=${this.closeModal} aria-label="Close">&#x2715;</button>
                            </div>

                            <div class="form-body">
                                <!-- Status code -->
                                <div class="form-group">
                                    <label>Status code</label>
                                    <div class="status-select-wrap">
                                        <select name="statusCode" .value=${String(this.formData.statusCode)} @change=${this.handleInputChange}>
                                            <option value="301">301 — Permanent redirect</option>
                                            <option value="302">302 — Temporary redirect</option>
                                            <option value="404">404 — Not found</option>
                                            <option value="410">410 — Gone</option>
                                        </select>
                                        <span class="status-float-badge status-${this.formData.statusCode}">
                                            ${this.getStatusBadgeLabel(this.formData.statusCode)}
                                        </span>
                                    </div>
                                    <small>Tells browsers and search engines which type of redirect this is.</small>
                                </div>

                                <!-- Old URL + New URL side by side -->
                                <div class="form-row">
                                    <div class="form-group">
                                        <label>Old URL <span class="req">*</span></label>
                                        <input type="text"
                                               name="oldUrl"
                                               .value=${this.formData.oldUrl}
                                               @input=${this.handleInputChange}
                                               placeholder="/old-page" />
                                        <small>The path to redirect from. Tip: use * to match anything (e.g. /blog/*).</small>
                                    </div>
                                    ${this.formData.statusCode === 301 || this.formData.statusCode === 302 ? html`
                                        <div class="form-group">
                                            <label>New URL <span class="req">*</span></label>
                                            <input type="text"
                                                   name="newUrl"
                                                   .value=${this.formData.newUrl}
                                                   @input=${this.handleInputChange}
                                                   placeholder="/new-page" />
                                            <small>The path to redirect to. Use * to reuse the matched value (e.g. /articles/*).</small>
                                        </div>
                                    ` : ''}
                                </div>

                                <!-- A/B test -->
                                ${(this.formData.statusCode === 301 || this.formData.statusCode === 302) && !this.formData.isRegex ? html`
                                    <div class="form-group">
                                        <div class="toggle-row">
                                            <label class="toggle-label" for="modal-abTestEnabled">
                                                A/B test
                                                <span class="toggle-hint"> — split traffic between New URL and a second URL</span>
                                            </label>
                                            <label class="toggle-switch">
                                                <input type="checkbox"
                                                       name="abTestEnabled"
                                                       id="modal-abTestEnabled"
                                                       .checked=${this.formData.abTestEnabled}
                                                       @change=${this.handleInputChange} />
                                                <span class="toggle-slider"></span>
                                            </label>
                                        </div>
                                    </div>
                                    ${this.formData.abTestEnabled ? html`
                                        <div class="form-group">
                                            <label>Variant B URL <span class="req">*</span></label>
                                            <input type="text"
                                                   name="variantBUrl"
                                                   .value=${this.formData.variantBUrl}
                                                   @input=${this.handleInputChange}
                                                   placeholder="/new-page-variant-b" />
                                            <small>Visitors assigned to Variant B go here instead of New URL.</small>
                                        </div>
                                        <div class="form-group">
                                            <label>Variant B weight — % of visitors sent to B</label>
                                            <input type="number"
                                                   name="variantBWeight"
                                                   min="0" max="100"
                                                   .value=${String(this.formData.variantBWeight)}
                                                   @input=${this.handleInputChange} />
                                            <small>A visitor is assigned once (cookie) and always sees the same variant afterward.</small>
                                        </div>
                                    ` : ''}
                                ` : ''}

                                <!-- Preserve query string -->
                                ${this.formData.statusCode === 301 || this.formData.statusCode === 302 ? html`
                                    <div class="form-group">
                                        <div class="toggle-row">
                                            <label class="toggle-label" for="modal-preserveQueryString">
                                                Preserve query string
                                                <span class="toggle-hint"> — append the incoming request's query string to New URL</span>
                                            </label>
                                            <label class="toggle-switch">
                                                <input type="checkbox"
                                                       name="preserveQueryString"
                                                       id="modal-preserveQueryString"
                                                       .checked=${this.formData.preserveQueryString}
                                                       @change=${this.handleInputChange} />
                                                <span class="toggle-slider"></span>
                                            </label>
                                        </div>
                                    </div>
                                ` : ''}

                                <!-- Domain -->
                                <div class="form-group">
                                    <label>Domain <span class="lbl-opt">(optional)</span></label>
                                    <input type="text"
                                           name="domain"
                                           .value=${this.formData.domain}
                                           @input=${this.handleInputChange}
                                           placeholder="e.g. shop.example.com" />
                                    <small>Leave blank to apply to all domains. Domain-specific rules take precedence.</small>
                                </div>

                                <!-- Culture -->
                                <div class="form-group">
                                    <label>Culture <span class="lbl-opt">(optional)</span></label>
                                    <input type="text"
                                           name="culture"
                                           .value=${this.formData.culture}
                                           @input=${this.handleInputChange}
                                           placeholder="e.g. tr-TR" />
                                    <small>Leave blank to apply to all cultures. Resolved from Umbraco's Culture and Hostnames configuration for the request's domain.</small>
                                </div>

                                <!-- Valid from / Valid until -->
                                <div class="form-row">
                                    <div class="form-group">
                                        <label>Valid from <span class="lbl-opt">(optional)</span></label>
                                        <input type="datetime-local"
                                               name="validFrom"
                                               .value=${this.formData.validFrom}
                                               @input=${this.handleInputChange} />
                                        <small>Leave blank to make this redirect active immediately.</small>
                                    </div>
                                    <div class="form-group">
                                        <label>Valid until <span class="lbl-opt">(optional)</span></label>
                                        <input type="datetime-local"
                                               name="validUntil"
                                               .value=${this.formData.validUntil}
                                               @input=${this.handleInputChange} />
                                        <small>Leave blank to keep this redirect active indefinitely.</small>
                                    </div>
                                </div>

                                <!-- Notes -->
                                <div class="form-group">
                                    <label>Notes</label>
                                    <textarea name="description"
                                              rows="3"
                                              .value=${this.formData.description}
                                              @input=${this.handleInputChange}
                                              placeholder="Optional description…"></textarea>
                                </div>

                                <!-- Active toggle -->
                                <div class="form-group">
                                    <div class="toggle-row">
                                        <label class="toggle-label" for="modal-isActive">Active</label>
                                        <label class="toggle-switch">
                                            <input type="checkbox"
                                                   name="isActive"
                                                   id="modal-isActive"
                                                   .checked=${this.formData.isActive}
                                                   @change=${this.handleInputChange} />
                                            <span class="toggle-slider"></span>
                                        </label>
                                    </div>
                                    <small>Enable or disable this redirect.</small>
                                </div>

                                <!-- Regex toggle -->
                                <div class="form-group">
                                    <div class="toggle-row">
                                        <label class="toggle-label" for="modal-isRegex">
                                            Regex match
                                            <span class="toggle-hint"> — use <code>$1</code> capture groups in new URL</span>
                                        </label>
                                        <label class="toggle-switch">
                                            <input type="checkbox"
                                                   name="isRegex"
                                                   id="modal-isRegex"
                                                   .checked=${this.formData.isRegex}
                                                   @change=${this.handleInputChange} />
                                            <span class="toggle-slider"></span>
                                        </label>
                                    </div>
                                </div>
                            </div>

                            <div class="modal-footer">
                                <button class="btn-cancel" @click=${this.closeModal}>Cancel</button>
                                <button class="btn-save" @click=${this.saveRedirect}>Save redirect</button>
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
