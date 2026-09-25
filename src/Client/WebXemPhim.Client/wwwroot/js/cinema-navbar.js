/**
 * CinemaFlow - Smart Navbar Scroll Hide/Reveal Animation
 * Hides navbar by sliding up when scrolling down past a threshold.
 * Reveals navbar by sliding down when scrolling up.
 */
(function () {
    let lastScrollY = window.scrollY || 0;
    let ticking = false;
    const SCROLL_THRESHOLD = 90; // Scroll distance in px before hide animation can trigger
    const TOLERANCE = 6;         // Scroll delta threshold to avoid jitter

    function updateNavbar() {
        const navbar = document.querySelector('.cinema-navbar');
        if (!navbar) {
            ticking = false;
            return;
        }

        const currentScrollY = Math.max(0, window.scrollY || window.pageYOffset || 0);

        // 1. Subtle glass shadow on scroll
        if (currentScrollY > 20) {
            navbar.classList.add('navbar-scrolled');
        } else {
            navbar.classList.remove('navbar-scrolled');
        }

        // 2. Prevent hiding if user is interacting with search or popovers
        const hasOpenPopover = navbar.querySelector('.nav-popover-dismiss') !== null ||
                              navbar.classList.contains('has-open-popover') ||
                              navbar.contains(document.activeElement);

        if (hasOpenPopover) {
            navbar.classList.remove('navbar-hidden');
            lastScrollY = currentScrollY;
            ticking = false;
            return;
        }

        // 3. Near top of page: always keep visible
        if (currentScrollY <= SCROLL_THRESHOLD) {
            navbar.classList.remove('navbar-hidden');
            lastScrollY = currentScrollY;
            ticking = false;
            return;
        }

        // 4. Directional scroll with tolerance
        const diff = currentScrollY - lastScrollY;
        if (Math.abs(diff) > TOLERANCE) {
            if (diff > 0) {
                // Scrolling down past threshold -> push up & hide
                navbar.classList.add('navbar-hidden');
            } else {
                // Scrolling up -> slide down into view
                navbar.classList.remove('navbar-hidden');
            }
            lastScrollY = currentScrollY;
        }

        ticking = false;
    }

    function onScroll() {
        if (!ticking) {
            window.requestAnimationFrame(updateNavbar);
            ticking = true;
        }
    }

    function initNavbar() {
        window.removeEventListener('scroll', onScroll);
        window.addEventListener('scroll', onScroll, { passive: true });
        lastScrollY = Math.max(0, window.scrollY || 0);
        updateNavbar();
    }

    // Initialize on script load or DOM ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initNavbar);
    } else {
        initNavbar();
    }

    // Reset when Blazor navigates
    window.addEventListener('popstate', function () {
        const navbar = document.querySelector('.cinema-navbar');
        if (navbar) {
            navbar.classList.remove('navbar-hidden');
        }
    });

    window.cinemaNavbar = {
        init: initNavbar,
        reset: function () {
            const navbar = document.querySelector('.cinema-navbar');
            if (navbar) {
                navbar.classList.remove('navbar-hidden');
            }
            lastScrollY = 0;
        }
    };
})();
