# 8Bitiz Redirect Manager – Landing page

Static landing page for the plugin. Open `index.html` in a browser or host on any static host (e.g. GitHub Pages).

## Local preview

```bash
cd landing
# macOS
open index.html
# Or use a simple server (e.g. Python)
python3 -m http.server 8080
# Then open http://localhost:8080
```

## GitHub Pages

1. In repo **Settings → Pages**, set source to **Deploy from a branch**.
2. Branch: `main`, folder: **/ (root)** or **/landing** (if you want the site at `https://username.github.io/RedirectManager/landing/`).
3. If root: move `landing/index.html` to `docs/index.html` and set Pages to **docs** folder; or add a redirect from root to `landing/index.html`.

For **Project site** at `https://username.github.io/RedirectManager/`, either put `index.html` in a `docs/` folder and enable Pages from `docs/`, or use a custom 404 redirect to `landing/index.html` (GitHub Pages supports this).
