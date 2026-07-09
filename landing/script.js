(function () {
    'use strict';

    // Smooth scroll for anchor links (already handled by CSS scroll-behavior: smooth; fallback for older browsers)
    document.querySelectorAll('a[href^="#"]').forEach(function (anchor) {
        anchor.addEventListener('click', function (e) {
            var id = this.getAttribute('href');
            if (id === '#') return;
            var target = document.querySelector(id);
            if (target) {
                e.preventDefault();
                target.scrollIntoView({ behavior: 'smooth', block: 'start' });
            }
        });
    });
})();

(function () {
    'use strict';

    // Internal-use only page: pulls live opt-in usage data from the
    // telemetry service to show who's running the plugin. Not meant for
    // public distribution — the API key below is a write/read secret for
    // that service.
    var TELEMETRY_ENDPOINT = 'https://redirectmanager-telemetry.azurewebsites.net/api/pings';
    var TELEMETRY_API_KEY = '62db2a1ff665cab5c5fb2458b3d3dbd719eba57ebda92d2f431a6fc0d094d095';

    function formatDate(iso) {
        if (!iso) return '—';
        var d = new Date(iso);
        if (isNaN(d.getTime())) return iso;
        return d.toLocaleString();
    }

    function renderUsers(pings) {
        var loadingEl = document.getElementById('users-loading');
        var errorEl = document.getElementById('users-error');
        var tableWrap = document.getElementById('users-table-wrap');
        var tbody = document.getElementById('users-table-body');
        if (!loadingEl || !errorEl || !tableWrap || !tbody) return;

        loadingEl.hidden = true;

        if (!pings || pings.length === 0) {
            errorEl.textContent = 'No pings recorded yet.';
            errorEl.hidden = false;
            return;
        }

        tbody.innerHTML = '';
        pings.forEach(function (p) {
            var tr = document.createElement('tr');
            tr.innerHTML =
                '<td>' + (p.domain || '—') + '</td>' +
                '<td>' + (p.pluginVersion || '—') + '</td>' +
                '<td>' + (p.umbracoVersion || '—') + '</td>' +
                '<td>' + (p.pingCount != null ? p.pingCount : '—') + '</td>' +
                '<td>' + formatDate(p.lastSeenUtc) + '</td>';
            tbody.appendChild(tr);
        });
        tableWrap.hidden = false;
    }

    function showError() {
        var loadingEl = document.getElementById('users-loading');
        var errorEl = document.getElementById('users-error');
        if (loadingEl) loadingEl.hidden = true;
        if (errorEl) errorEl.hidden = false;
    }

    var usersSection = document.getElementById('users');
    if (!usersSection) return;

    fetch(TELEMETRY_ENDPOINT, { headers: { 'X-Api-Key': TELEMETRY_API_KEY } })
        .then(function (response) {
            if (!response.ok) throw new Error('Request failed: ' + response.status);
            return response.json();
        })
        .then(renderUsers)
        .catch(showError);
})();
