window.HomeHeader = (() => {
    let observer = null;
    let resizeHandler = null;
    let scrollHandler = null;
    let lastScrollY = 0;
    let isStuck = false;
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

        lastScrollY = 0;
        isStuck = false;
        lastActiveLink = null;
    }

    function init() {
        destroy();

        const hero = document.getElementById("home-hero-gradient");
        const header = document.getElementById("home-sticky-header");
        const spacer = document.getElementById("home-sticky-header-spacer");

        if (!hero || !header || !spacer) {
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

        const syncHeight = () => {
            spacer.style.setProperty("--home-header-height", `${header.offsetHeight}px`);
        };

        const setHeaderVisibility = (isVisible) => {
            header.classList.toggle("is-hidden", !isVisible);
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

            for (const entry of sectionTargets) {
                if ((entry.target.offsetTop || 0) <= scrollAnchor) {
                    active = entry;
                } else {
                    break;
                }
            }

            sectionTargets.forEach(entry => {
                entry.link.classList.toggle("is-active", entry === active);
            });

            if (!active) return;

            const shouldScrollIntoView = instantScroll || active.link !== lastActiveLink;
            lastActiveLink = active.link;

            if (shouldScrollIntoView) {
                ensureActiveLinkVisible(active.link, instantScroll);
            }
        };

        const setStickyState = (nextIsStuck) => {
            isStuck = nextIsStuck;
            header.classList.toggle("is-stuck", nextIsStuck);
            spacer.classList.toggle("is-active", nextIsStuck);

            if (!nextIsStuck) {
                setHeaderVisibility(true);
            }

            syncHeight();
        };

        const updateOnScroll = () => {
            const currentScrollY = Math.max(window.scrollY || window.pageYOffset || 0, 0);
            const scrollDelta = currentScrollY - lastScrollY;
            const revealThreshold = header.offsetHeight * 0.5;
            const isDesktop = desktopBreakpoint.matches;

            if (!isStuck) {
                setHeaderVisibility(true);
            } else if (isDesktop) {
                setHeaderVisibility(true);
            } else if (currentScrollY <= hero.offsetHeight) {
                setHeaderVisibility(true);
            } else if (scrollDelta > 6 && currentScrollY > header.offsetHeight * 2) {
                setHeaderVisibility(false);
            } else if (scrollDelta < -4 || currentScrollY <= hero.offsetHeight + revealThreshold) {
                setHeaderVisibility(true);
            }

            updateActiveSectionLink();
            lastScrollY = currentScrollY;
        };

        syncHeight();
        lastScrollY = Math.max(window.scrollY || window.pageYOffset || 0, 0);

        observer = new IntersectionObserver(
            ([entry]) => {
                setStickyState(!entry.isIntersecting);
                updateOnScroll();
            },
            { threshold: 0 }
        );

        observer.observe(hero);

        resizeHandler = () => {
            syncHeight();
            updateActiveSectionLink(true);
            updateOnScroll();
        };

        scrollHandler = () => {
            updateOnScroll();
        };

        window.addEventListener("resize", resizeHandler);
        window.addEventListener("scroll", scrollHandler, { passive: true });
        updateOnScroll();
    }

    return { init };
})();
