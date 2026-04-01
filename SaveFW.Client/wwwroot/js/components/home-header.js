window.HomeHeader = (() => {
    let observer = null;
    let resizeHandler = null;
    let scrollHandler = null;
    let lastScrollY = 0;
    let isStuck = false;
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
    }

    function init() {
        destroy();

        const hero = document.getElementById("home-hero-gradient");
        const header = document.getElementById("home-sticky-header");
        const spacer = document.getElementById("home-sticky-header-spacer");

        if (!hero || !header || !spacer) {
            return;
        }

        const syncHeight = () => {
            spacer.style.setProperty("--home-header-height", `${header.offsetHeight}px`);
        };

        const setHeaderVisibility = (isVisible) => {
            header.classList.toggle("is-hidden", !isVisible);
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
