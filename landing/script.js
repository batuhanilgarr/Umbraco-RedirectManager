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
