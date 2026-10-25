// Preloads the route specific stylesheet of a link the visitor is about to open.
//
// The route sheet is injected into <head> by the RouteStylesheet component, which only
// happens once the new page is rendered. Without a preload the browser paints the next
// page before that sheet is applied, so the page appears completely unstyled on the first
// navigation. Preloading on intent keeps the "only download the CSS the page needs"
// behaviour while removing that flash.
(function () {
    'use strict';

    var mapElement = document.getElementById('route-style-map');
    if (!mapElement) return;

    var routes;
    try {
        routes = JSON.parse(mapElement.textContent || '[]');
    } catch (error) {
        return;
    }
    if (!routes || !routes.length) return;

    var preloaded = Object.create(null);

    function filesFor(path) {
        var normalized = (path || '/').split('?')[0].replace(/\/+$/, '').toLowerCase();
        if (!normalized) normalized = '/';

        for (var i = 0; i < routes.length; i++) {
            var route = routes[i];
            if (route.route === '/') {
                if (normalized === '/') return route.files;
                continue;
            }
            if (normalized.indexOf(route.route) === 0) return route.files;
        }
        return [];
    }

    function preload(path) {
        var files = filesFor(path);
        if (!files.length) return;

        for (var i = 0; i < files.length; i++) {
            var href = files[i];
            if (!href || preloaded[href]) continue;
            preloaded[href] = true;

            var link = document.createElement('link');
            link.rel = 'preload';
            link.as = 'style';
            link.href = href;
            document.head.appendChild(link);
        }
    }

    function internalHref(target) {
        if (!target || !target.closest) return null;
        var link = target.closest('a[href]');
        if (!link) return null;
        var href = link.getAttribute('href');
        if (!href || href.charAt(0) !== '/') return null;
        return href;
    }

    function ensureStyle(path) {
        var files = filesFor(path);
        if (!files.length) return;

        for (var i = 0; i < files.length; i++) {
            var href = files[i];
            if (!href) continue;

            // Check if stylesheet is already attached
            var existing = document.querySelector('link[rel="stylesheet"][href="' + href + '"]');
            if (existing) continue;

            var link = document.createElement('link');
            link.rel = 'stylesheet';
            link.href = href;
            document.head.appendChild(link);
        }
    }

    // Intercept clicks on internal links to immediately attach the route stylesheet
    document.addEventListener('click', function (event) {
        var href = internalHref(event.target);
        if (href) ensureStyle(href);
    }, true);

    // Also ensure styles on popstate (browser back/forward navigation)
    window.addEventListener('popstate', function () {
        ensureStyle(window.location.pathname);
    });

    document.addEventListener('mouseover', function (event) {
        var href = internalHref(event.target);
        if (href) preload(href);
    }, true);

    document.addEventListener('focusin', function (event) {
        var href = internalHref(event.target);
        if (href) preload(href);
    }, true);

    // Preload the sheet of the route that is currently displayed, so the very first paint
    // already has it in the cache.
    preload(location.pathname);
})();
