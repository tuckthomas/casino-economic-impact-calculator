window.HomeHeader = (() => {
    let observer = null;
    let resizeHandler = null;
    let scrollHandler = null;
    let animationFrameId = null;
    let lastActiveLink = null;
    const desktopBreakpoint = window.matchMedia("(min-width: 1024px)");

    function destroy() {
        if (observer) {
            observer.disconnect();
            observer = null;
        }

        if (resizeHandler) {
            window.removeEventListener("resize", resizeHandler);
            resizeHandler = null;
        }

        if (scrollHandler) {
            window.removeEventListener("scroll", scrollHandler);
            scrollHandler = null;
        }

        if (animationFrameId !== null) {
            cancelAnimationFrame(animationFrameId);
            animationFrameId = null;
        }

        document.documentElement.style.scrollPaddingTop = "";
        lastActiveLink = null;
    }

    function init() {
        destroy();

        const hero = document.getElementById("home-hero-gradient");
        const header = document.getElementById("home-sticky-header");

        if (!hero || !header) {
            return;
        }

        const sectionLinks = Array.from(header.querySelectorAll('.home-sticky-link[href^="#"]'));
        const linksViewport = header.querySelector(".home-sticky-header-links");
        const sectionTargets = sectionLinks
            .map(link => {
                const selector = String(link.getAttribute("href") || "").trim();
                if (!selector || selector === "#") return null;
                const target = document.querySelector(selector);
                return target ? { link, target } : null;
            })
            .filter(Boolean);

        const syncScrollPadding = () => {
            document.documentElement.style.scrollPaddingTop = `${header.offsetHeight + 12}px`;
        };

        const ensureActiveLinkVisible = (link, instant = false) => {
            if (!link || !linksViewport || desktopBreakpoint.matches) return;

            const maxScrollLeft = Math.max(linksViewport.scrollWidth - linksViewport.clientWidth, 0);
            if (maxScrollLeft <= 0) return;

            const viewportRect = linksViewport.getBoundingClientRect();
            const linkRect = link.getBoundingClientRect();
            const padding = 24;
            const currentLeft = linksViewport.scrollLeft;

            const isOutOfLeftBounds = linkRect.left < (viewportRect.left + padding);
            const isOutOfRightBounds = linkRect.right > (viewportRect.right - padding);
            if (!isOutOfLeftBounds && !isOutOfRightBounds) return;

            const viewportCenter = viewportRect.left + (viewportRect.width / 2);
            const linkCenter = linkRect.left + (linkRect.width / 2);
            let targetLeft = currentLeft + (linkCenter - viewportCenter);

            targetLeft = Math.min(Math.max(targetLeft, 0), maxScrollLeft);
            if (Math.abs(targetLeft - currentLeft) < 1) return;

            linksViewport.scrollTo({
                left: targetLeft,
                behavior: instant ? "auto" : "smooth"
            });
        };

        const updateActiveSectionLink = (instantScroll = false) => {
            if (!sectionTargets.length) return;

            const scrollAnchor = (window.scrollY || window.pageYOffset || 0) + header.offsetHeight + 24;
            let active = sectionTargets[0];
            let bestTop = Number.NEGATIVE_INFINITY;
            const currentScrollY = window.scrollY || window.pageYOffset || 0;

            for (const entry of sectionTargets) {
                const top = entry.target.getBoundingClientRect().top + currentScrollY;
                if (top <= scrollAnchor && top >= bestTop) {
                    bestTop = top;
                    active = entry;
                }
            }

            sectionTargets.forEach(entry => {
                entry.link.classList.toggle("is-active", entry === active);
            });

            if (!active) return;

            const shouldUpdate = instantScroll || active.link !== lastActiveLink;
            lastActiveLink = active.link;

            if (shouldUpdate) {
                ensureActiveLinkVisible(active.link, instantScroll);
            }
        };

        const scheduleScrollUpdate = () => {
            if (animationFrameId !== null) return;

            animationFrameId = requestAnimationFrame(() => {
                animationFrameId = null;
                updateActiveSectionLink();
            });
        };

        syncScrollPadding();
        updateActiveSectionLink(true);

        // Native CSS sticky positioning owns the header geometry. The observer only
        // adds a shadow once the hero has left the viewport; it never changes layout.
        observer = new IntersectionObserver(
            ([entry]) => {
                header.classList.toggle("is-stuck", !entry.isIntersecting);
            },
            { threshold: 0 }
        );
        observer.observe(hero);

        resizeHandler = () => {
            syncScrollPadding();
            updateActiveSectionLink(true);
        };

        scrollHandler = () => {
            scheduleScrollUpdate();
        };

        window.addEventListener("resize", resizeHandler);
        window.addEventListener("scroll", scrollHandler, { passive: true });
    }

    return { init };
})();
