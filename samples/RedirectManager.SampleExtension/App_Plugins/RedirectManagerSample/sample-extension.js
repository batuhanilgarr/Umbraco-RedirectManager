// Sample extension elements. The dashboard's extension host assigns `api` (and, for row
// panels, `item`) after creating the element. Row elements MUST load lazily: a 404 log can
// hold thousands of rows, so never fetch on connect — wait for a user action.
class RmSampleTab extends HTMLElement {
    set api(value) {
        this._api = value;
        this.render();
    }

    async render() {
        if (!this._api || this._rendered) return;
        this._rendered = true;
        const response = await this._api.authFetch('/umbraco/api/redirectmanager/capabilities');
        const caps = await response.json();
        this.innerHTML = `<div id="rm-sample-tab-body" style="padding:16px">
            <h3>Sample extension tab</h3>
            <p>Edition: <strong>${caps.edition}</strong> · extensions: <strong>${caps.extensions.length}</strong></p>
        </div>`;
    }
}
customElements.define('rm-sample-tab', RmSampleTab);

class RmSampleRow extends HTMLElement {
    set api(value) {
        this._api = value;
        this.draw();
    }

    set item(value) {
        this._item = value;
        this.draw();
    }

    draw() {
        if (!this._api || !this._item || this._drawn) return;
        this._drawn = true;
        const button = document.createElement('button');
        button.textContent = 'Suggest';
        button.className = 'rm-sample-suggest';
        button.addEventListener('click', async () => {
            const url = `/umbraco/api/redirectmanager/suggestions?path=${encodeURIComponent(this._item.path)}`;
            const response = await this._api.authFetch(url);
            const list = await response.json();
            const summary = list.map(s => `${s.targetUrl} (${s.confidence})`).join(', ') || 'none';
            const length = this._item.extensions && this._item.extensions['sample.pathLength'];
            button.insertAdjacentHTML('afterend',
                `<span class="rm-sample-result"> ${summary} · pathLength=${length}</span>`);
        });
        this.appendChild(button);
    }
}
customElements.define('rm-sample-row', RmSampleRow);
