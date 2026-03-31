window.HomeHeader = (() => {
    let observer = null;
    let resizeHandler = null;

    function destroy() {
        if (observer) {
            observer.disconnect();
            observer = null;
        }

        if (resizeHandler) {
            window.removeEventListener("resize", resizeHandler);
            resizeHandler = null;
        }
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

        const setStickyState = (isStuck) => {
            header.classList.toggle("is-stuck", isStuck);
            spacer.classList.toggle("is-active", isStuck);
            syncHeight();
        };

        syncHeight();

        observer = new IntersectionObserver(
            ([entry]) => {
                setStickyState(!entry.isIntersecting);
            },
            { threshold: 0 }
        );

        observer.observe(hero);

        resizeHandler = () => {
            syncHeight();
        };

        window.addEventListener("resize", resizeHandler);
    }

    return { init };
})();
