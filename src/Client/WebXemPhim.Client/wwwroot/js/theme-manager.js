/**
 * CinemaFlow Theme Manager
 * Persistent Light / Dark Mode with instant pre-rendering support.
 */
window.cinemaTheme = {
    getStoredTheme: function () {
        try {
            var stored = localStorage.getItem('cinemaflow_theme');
            if (stored === 'light' || stored === 'dark') {
                return stored;
            }
            return 'dark';
        } catch (e) {
            return 'dark';
        }
    },
    setTheme: function (theme) {
        try {
            var mode = theme === 'light' ? 'light' : 'dark';
            document.documentElement.setAttribute('data-theme', mode);
            if (document.body) {
                document.body.setAttribute('data-theme', mode);
            }
            localStorage.setItem('cinemaflow_theme', mode);

            var metaTheme = document.querySelector('meta[name="theme-color"]');
            if (metaTheme) {
                metaTheme.setAttribute('content', mode === 'light' ? '#f8f7f4' : '#0b0c10');
            }
        } catch (e) {
            // LocalStorage or DOM security restrictions
        }
    }
};

// Immediately apply stored theme on script load
(function () {
    try {
        var theme = localStorage.getItem('cinemaflow_theme') || 'dark';
        document.documentElement.setAttribute('data-theme', theme);
        if (document.body) {
            document.body.setAttribute('data-theme', theme);
        } else {
            document.addEventListener('DOMContentLoaded', function () {
                if (document.body) {
                    document.body.setAttribute('data-theme', theme);
                }
            });
            // Also listen to readystatechange as backup
            document.addEventListener('readystatechange', function () {
                if (document.body && !document.body.getAttribute('data-theme')) {
                    document.body.setAttribute('data-theme', theme);
                }
            });
        }
    } catch (e) {}
})();
